using System.Runtime.InteropServices;
using System.Text;

namespace iTerminal.Core.Services.Scanning.Twain;

/// <summary>
/// Windows: scanners through TWAIN, the standard the scanner drivers implement. The Data Source Manager
/// (<c>twaindsm.dll</c>, installed with the TWAIN DSM redistributable or by the scanner's own software) is
/// called directly through <c>DSM_Entry</c>; no wrapper package is needed.
///
/// <para>Every TWAIN call for one operation runs on one dedicated thread, because the DSM delivers scanner
/// events as window messages to a hidden message-only window that thread owns. The scan is driven as the
/// TWAIN 2.x state machine: open the DSM, open the source, set resolution, pixel type and feeder mode, enable
/// it with no vendor UI, pump messages until <c>MSG_XFERREADY</c>, take the image as a native DIB, end the
/// transfer, and — for the feeder — go on while the pending count says sheets remain. Then disable and close.
/// The DIB is wrapped in a BMP header by <see cref="ScanImageCodec"/>.</para>
///
/// <para>Native transfer is used, not memory transfer: every driver supports it, and it needs no buffer
/// negotiation. The vendor UI is switched off, so the scan dialog's preview is the only interface.</para>
/// </summary>
public sealed class TwainScannerBackend : IScannerBackend
{
    /// <summary>A scan (a page, or a whole feeder) that is not finished within this time is abandoned.</summary>
    internal static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(10);

    public Task<ScannerListing> ListAsync(CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    using var session = new TwainSession();
                    return new ScannerListing(true, session.ListSources(), null);
                }
                catch (TwainException exception)
                {
                    return new ScannerListing(true, [], exception.Message);
                }
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public Task<ScanResult> ScanAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    using var session = new TwainSession();
                    IReadOnlyList<byte[]> dibs = session.Scan(scanner.Id, options, cancellationToken, ScanTimeout);
                    var pages = dibs.Select(dib => new ScanPage(ScanImageCodec.WrapDibAsBmp(dib))).ToList();
                    return new ScanResult(pages, $"Scanned {pages.Count} page{(pages.Count == 1 ? string.Empty : "s")} from {scanner.Name}.");
                }
                catch (TwainException exception)
                {
                    return ScanResult.Failed(exception.Message);
                }
                catch (InvalidDataException exception)
                {
                    return ScanResult.Failed(exception.Message);
                }
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }
}

/// <summary>A TWAIN operation that failed, with the sentence the dialog shows.</summary>
internal sealed class TwainException(string message) : Exception(message);

/// <summary>What one wait for a picture ended with.</summary>
internal enum TransferOutcome
{
    /// <summary>A page arrived and the feeder still holds sheets: wait for the next one.</summary>
    MorePending,

    /// <summary>A page arrived (or the transfer failed) and nothing else is pending.</summary>
    NoMorePending,

    /// <summary>The driver asked to close the source before any page arrived: no page, and nothing more to wait for.</summary>
    SourceClosed,
}

/// <summary>
/// One connection to the Data Source Manager, used by a single thread. Create it, use it, dispose it: disposing
/// closes the DSM and destroys the message window whatever happened in between.
/// </summary>
internal sealed class TwainSession : IDisposable
{
    private const ushort TwrcSuccess = 0;
    private const ushort TwrcDsEvent = 4;
    private const ushort TwrcNotDsEvent = 5;
    private const ushort TwrcXferDone = 6;
    private const ushort TwrcEndOfList = 7;

    private const uint DgControl = 0x0001;
    private const uint DgImage = 0x0002;

    private const ushort DatEvent = 0x0002;
    private const ushort DatIdentity = 0x0003;
    private const ushort DatParent = 0x0004;
    private const ushort DatPendingXfers = 0x0005;
    private const ushort DatCapability = 0x0001;
    private const ushort DatUserInterface = 0x000B;
    private const ushort DatImageNativeXfer = 0x0104;

    private const ushort MsgGet = 0x0001;
    private const ushort MsgSet = 0x0006;
    private const ushort MsgReset = 0x0007;
    private const ushort MsgGetFirst = 0x0004;
    private const ushort MsgGetNext = 0x0005;
    private const ushort MsgXferReady = 0x0101;
    private const ushort MsgCloseDsReq = 0x0102;
    private const ushort MsgOpenDsm = 0x0301;
    private const ushort MsgCloseDsm = 0x0302;
    private const ushort MsgOpenDs = 0x0401;
    private const ushort MsgCloseDs = 0x0402;
    private const ushort MsgDisableDs = 0x0501;
    private const ushort MsgEnableDs = 0x0502;
    private const ushort MsgProcessEvent = 0x0601;
    private const ushort MsgEndXfer = 0x0701;

    private const ushort CapPixelType = 0x0101;
    private const ushort CapXResolution = 0x1118;
    private const ushort CapYResolution = 0x1119;
    private const ushort CapFeederEnabled = 0x1002;
    private const ushort TwonOneValue = 5;
    private const ushort TwtyBool = 0x0005;
    private const ushort TwtyUInt16 = 0x0004;
    private const ushort TwtyFix32 = 0x0007;
    private const ushort TwptBw = 0;
    private const ushort TwptGray = 1;
    private const ushort TwptRgb = 2;

    private const uint GlobalMoveable = 0x0002;
    private const uint GlobalZeroInit = 0x0040;
    private const uint GhndFlags = GlobalMoveable | GlobalZeroInit;
    private const int PmRemove = 0x0001;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly IntPtr _appIdentity;
    private readonly IntPtr _hwnd;
    private bool _dsmOpen;

    /// <summary>Opens the DSM and the hidden window it reports events to. Throws <see cref="TwainException"/> when it cannot.</summary>
    public TwainSession()
    {
        try
        {
            _appIdentity = Marshal.AllocHGlobal(Marshal.SizeOf<TwIdentity>());
            Marshal.StructureToPtr(TwIdentity.ForApplication(), _appIdentity, false);
            _hwnd = CreateWindowExW(0, "STATIC", "iTerminal scanner", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_appIdentity);
                throw new TwainException("The scanner window could not be created.");
            }
        }
        catch (DllNotFoundException)
        {
            throw new TwainException("TWAIN is not installed: twaindsm.dll was not found. Install the scanner's software or the TWAIN DSM.");
        }
        catch (EntryPointNotFoundException)
        {
            throw new TwainException("TWAIN is not installed: twaindsm.dll does not export DSM_Entry.");
        }

        IntPtr hwndPointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(hwndPointer, _hwnd);
            ushort rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatParent, MsgOpenDsm, hwndPointer);
            if (rc != TwrcSuccess)
            {
                throw new TwainException("The TWAIN Data Source Manager would not open.");
            }

            _dsmOpen = true;
        }
        finally
        {
            Marshal.FreeHGlobal(hwndPointer);
        }
    }

    /// <summary>The sources the DSM reports, in the order it gives them. The product name is both the id and the label.</summary>
    public IReadOnlyList<ScannerDevice> ListSources()
    {
        var sources = new List<ScannerDevice>();
        IntPtr identity = Marshal.AllocHGlobal(Marshal.SizeOf<TwIdentity>());
        try
        {
            ushort rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgGetFirst, identity);
            while (rc == TwrcSuccess)
            {
                string name = TwIdentity.ReadName(identity);
                sources.Add(new ScannerDevice(name, name));
                rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgGetNext, identity);
            }

            if (rc != TwrcEndOfList)
            {
                throw new TwainException("The TWAIN Data Source Manager stopped listing the scanners.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(identity);
        }

        return sources;
    }

    /// <summary>
    /// Scans from the source and returns each page as a DIB (a BMP without its file header). A flatbed scan takes one
    /// page; the feeder takes every sheet until the driver reports nothing pending.
    /// </summary>
    public IReadOnlyList<byte[]> Scan(string sourceName, ScanOptions options, CancellationToken cancellationToken, TimeSpan timeout)
    {
        IntPtr source = FindSource(sourceName);
        ushort rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgOpenDs, source);
        if (rc != TwrcSuccess)
        {
            Marshal.FreeHGlobal(source);
            throw new TwainException($"{sourceName} could not be opened.");
        }

        bool enabled = false;
        var pages = new List<byte[]>();
        try
        {
            SetOneValue(source, CapXResolution, TwtyFix32, (uint)options.Dpi);
            SetOneValue(source, CapYResolution, TwtyFix32, (uint)options.Dpi);
            SetOneValue(source, CapPixelType, TwtyUInt16, PixelType(options.ColorMode));
            SetOneValue(source, CapFeederEnabled, TwtyBool, options.Source == ScanSource.Feeder ? 1u : 0u);

            var userInterface = new TwUserInterface { ShowUi = 0, ModalUi = 0, HParent = _hwnd };
            IntPtr uiPointer = Marshal.AllocHGlobal(Marshal.SizeOf<TwUserInterface>());
            try
            {
                Marshal.StructureToPtr(userInterface, uiPointer, false);
                rc = DsmEntry(_appIdentity, source, DgControl, DatUserInterface, MsgEnableDs, uiPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(uiPointer);
            }

            if (rc != TwrcSuccess)
            {
                throw new TwainException($"{sourceName} would not start a scan.");
            }

            enabled = true;
            var deadline = DateTime.UtcNow + timeout;
            bool feeder = options.Source == ScanSource.Feeder;
            while (true)
            {
                TransferOutcome outcome = PumpUntilImage(source, cancellationToken, deadline, out byte[]? dib);
                if (dib is not null)
                {
                    pages.Add(dib);
                }

                // Flatbed: one page and done. Feeder: go on while the driver says sheets are still pending.
                if (outcome == TransferOutcome.SourceClosed || !feeder || outcome == TransferOutcome.NoMorePending)
                {
                    break;
                }
            }

            if (pages.Count == 0)
            {
                throw new TwainException(feeder ? "The document feeder is empty." : $"{sourceName} did not return a page.");
            }

            return pages;
        }
        finally
        {
            if (enabled)
            {
                ResetPending(source);
                DsmEntry(_appIdentity, source, DgControl, DatUserInterface, MsgDisableDs, IntPtr.Zero);
            }

            DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgCloseDs, source);
            Marshal.FreeHGlobal(source);
        }
    }

    /// <summary>
    /// Message loop: hands every message to the DSM first, and takes the image when the driver says one is ready.
    /// The image (if any) is in <paramref name="dib"/>; the outcome says whether the feeder has more to give.
    /// </summary>
    private TransferOutcome PumpUntilImage(IntPtr source, CancellationToken cancellationToken, DateTime deadline, out byte[]? dib)
    {
        dib = null;
        IntPtr messagePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMsg>());
        IntPtr eventPointer = Marshal.AllocHGlobal(Marshal.SizeOf<TwEvent>());
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                {
                    throw new TwainException("The scanner did not finish in time.");
                }

                if (!PeekMessageW(out NativeMsg message, IntPtr.Zero, 0, 0, PmRemove))
                {
                    Thread.Sleep(10);
                    continue;
                }

                Marshal.StructureToPtr(message, messagePointer, false);
                var scanEvent = new TwEvent { PEvent = messagePointer, TwMessage = 0 };
                Marshal.StructureToPtr(scanEvent, eventPointer, false);
                ushort rc = DsmEntry(_appIdentity, source, DgControl, DatEvent, MsgProcessEvent, eventPointer);
                if (rc == TwrcNotDsEvent)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                    continue;
                }

                if (rc != TwrcDsEvent)
                {
                    continue;
                }

                scanEvent = Marshal.PtrToStructure<TwEvent>(eventPointer);
                if (scanEvent.TwMessage == MsgCloseDsReq)
                {
                    return TransferOutcome.SourceClosed;
                }

                if (scanEvent.TwMessage == MsgXferReady)
                {
                    dib = TransferNativeImage(source, out bool morePending);
                    return morePending ? TransferOutcome.MorePending : TransferOutcome.NoMorePending;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(eventPointer);
            Marshal.FreeHGlobal(messagePointer);
        }
    }

    /// <summary>
    /// Takes the DIB the driver allocated, copies it out, and frees the driver's memory. Ends the transfer and reports
    /// through <paramref name="morePending"/> whether the feeder holds another sheet.
    /// </summary>
    private byte[]? TransferNativeImage(IntPtr source, out bool morePending)
    {
        IntPtr handlePointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(handlePointer, IntPtr.Zero);
            ushort rc = DsmEntry(_appIdentity, source, DgImage, DatImageNativeXfer, MsgGet, handlePointer);
            IntPtr handle = rc == TwrcXferDone ? Marshal.ReadIntPtr(handlePointer) : IntPtr.Zero;
            morePending = EndTransfer(source) > 0;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                IntPtr locked = GlobalLock(handle);
                if (locked == IntPtr.Zero)
                {
                    throw new TwainException("The scanned page could not be read from the driver.");
                }

                try
                {
                    int size = checked((int)GlobalSize(handle));
                    var dib = new byte[size];
                    Marshal.Copy(locked, dib, 0, size);
                    return dib;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                GlobalFree(handle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(handlePointer);
        }
    }

    /// <summary>Ends the current transfer and returns how many pages the driver still holds.</summary>
    private ushort EndTransfer(IntPtr source)
    {
        IntPtr pending = Marshal.AllocHGlobal(Marshal.SizeOf<TwPendingXfers>());
        try
        {
            Marshal.StructureToPtr(new TwPendingXfers(), pending, false);
            DsmEntry(_appIdentity, source, DgControl, DatPendingXfers, MsgEndXfer, pending);
            return Marshal.PtrToStructure<TwPendingXfers>(pending).Count;
        }
        finally
        {
            Marshal.FreeHGlobal(pending);
        }
    }

    /// <summary>Drops any page the feeder still holds, so the next scan starts clean.</summary>
    private void ResetPending(IntPtr source)
    {
        IntPtr pending = Marshal.AllocHGlobal(Marshal.SizeOf<TwPendingXfers>());
        try
        {
            Marshal.StructureToPtr(new TwPendingXfers(), pending, false);
            DsmEntry(_appIdentity, source, DgControl, DatPendingXfers, MsgReset, pending);
        }
        finally
        {
            Marshal.FreeHGlobal(pending);
        }
    }

    /// <summary>
    /// Sets a capability to one value on the open source. A driver that does not offer the capability keeps its own
    /// setting, so a refusal is not an error: the scan goes ahead with the driver's default.
    /// </summary>
    private void SetOneValue(IntPtr source, ushort capability, ushort itemType, uint item)
    {
        IntPtr container = GlobalAlloc(GhndFlags, (UIntPtr)8);
        if (container == IntPtr.Zero)
        {
            return;
        }

        IntPtr capabilityPointer = Marshal.AllocHGlobal(Marshal.SizeOf<TwCapability>());
        try
        {
            IntPtr locked = GlobalLock(container);
            if (locked != IntPtr.Zero)
            {
                // TW_ONEVALUE: ItemType, then the item (a fix32 is its whole part and its fraction in one 32-bit word).
                Marshal.WriteInt16(locked, 0, (short)itemType);
                Marshal.WriteInt32(locked, 4, unchecked((int)item));
                GlobalUnlock(container);
            }

            Marshal.StructureToPtr(new TwCapability { Cap = capability, ConType = TwonOneValue, HContainer = container }, capabilityPointer, false);
            DsmEntry(_appIdentity, source, DgControl, DatCapability, MsgSet, capabilityPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(capabilityPointer);
            GlobalFree(container);
        }
    }

    private IntPtr FindSource(string sourceName)
    {
        IntPtr identity = Marshal.AllocHGlobal(Marshal.SizeOf<TwIdentity>());
        ushort rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgGetFirst, identity);
        while (rc == TwrcSuccess)
        {
            if (TwIdentity.ReadName(identity) == sourceName)
            {
                return identity;
            }

            rc = DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatIdentity, MsgGetNext, identity);
        }

        Marshal.FreeHGlobal(identity);
        throw new TwainException($"{sourceName} is no longer connected.");
    }

    private static ushort PixelType(ScanColorMode mode) => mode switch
    {
        ScanColorMode.BlackAndWhite => TwptBw,
        ScanColorMode.Grayscale => TwptGray,
        _ => TwptRgb,
    };

    public void Dispose()
    {
        if (_dsmOpen)
        {
            IntPtr hwndPointer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(hwndPointer, _hwnd);
                DsmEntry(_appIdentity, IntPtr.Zero, DgControl, DatParent, MsgCloseDsm, hwndPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(hwndPointer);
                _dsmOpen = false;
            }
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }

        Marshal.FreeHGlobal(_appIdentity);
    }

    #region Native calls and layouts

    [DllImport("twaindsm.dll", EntryPoint = "DSM_Entry", CallingConvention = CallingConvention.StdCall)]
    private static extern ushort DsmEntry(IntPtr origin, IntPtr destination, uint dg, ushort dat, ushort msg, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out NativeMsg message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMsg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref NativeMsg message);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwEvent
    {
        public IntPtr PEvent;
        public ushort TwMessage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwUserInterface
    {
        public ushort ShowUi;
        public ushort ModalUi;
        public IntPtr HParent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwCapability
    {
        public ushort Cap;
        public ushort ConType;
        public IntPtr HContainer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwPendingXfers
    {
        public ushort Count;
        public uint Eoj;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwVersion
    {
        public ushort MajorNum;
        public ushort MinorNum;
        public ushort Language;
        public ushort Country;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
        public byte[] Info;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TwIdentity
    {
        public uint Id;
        public TwVersion Version;
        public ushort ProtocolMajor;
        public ushort ProtocolMinor;
        public uint SupportedGroups;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
        public byte[] Manufacturer;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
        public byte[] ProductFamily;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
        public byte[] ProductName;

        /// <summary>This application's identity, sent with every DSM call. Protocol 2.3, image and control groups.</summary>
        public static TwIdentity ForApplication() => new()
        {
            Id = 0,
            Version = new TwVersion { MajorNum = 1, MinorNum = 0, Language = 0x0013, Country = 1, Info = Text("1.0") },
            ProtocolMajor = 2,
            ProtocolMinor = 3,
            SupportedGroups = DgImage | DgControl,
            Manufacturer = Text("iTerminal"),
            ProductFamily = Text("iTerminal"),
            ProductName = Text("iTerminal"),
        };

        /// <summary>The product name of the TW_IDENTITY at <paramref name="pointer"/>, which is what a scanner is listed and opened by.</summary>
        public static string ReadName(IntPtr pointer)
        {
            TwIdentity identity = Marshal.PtrToStructure<TwIdentity>(pointer);
            int length = Array.IndexOf(identity.ProductName, (byte)0);
            return Encoding.ASCII.GetString(identity.ProductName, 0, length < 0 ? identity.ProductName.Length : length);
        }

        private static byte[] Text(string value)
        {
            var bytes = new byte[34];
            Encoding.ASCII.GetBytes(value, 0, Math.Min(value.Length, 33), bytes, 0);
            return bytes;
        }
    }

    #endregion
}
