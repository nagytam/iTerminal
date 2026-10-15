using System.Runtime.InteropServices;

namespace iConsole.Inputs.Posix.Interop;

/// <summary>Minimal libc tty + stdin interop for Linux (glibc-style <c>termios</c>).</summary>
internal static unsafe class LinuxStdin
{
    private const string Libc = "libc.so.6";

    public const int StdinFd = 0;
    public const int TcsaNow = 0;
    public const int TcsaFlush = 2;
    public const uint ICanon = 0x0002;
    public const uint Echo = 0x0008;
    public const uint ISig = 0x0001;
    public const int VMin = 6;
    public const int VTime = 5;
    public const int FGetFl = 3;
    public const int FSetFl = 4;
    public const int ONonBlock = 0x800;

    public const int Eagain = 11;

    [StructLayout(LayoutKind.Sequential)]
    public struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;
    }

    [DllImport(Libc, SetLastError = true, EntryPoint = "tcgetattr")]
    public static extern int TcGetAttr(int fd, ref Termios termios);

    [DllImport(Libc, SetLastError = true, EntryPoint = "tcsetattr")]
    public static extern int TcSetAttr(int fd, int optional_actions, ref Termios termios);

    [DllImport(Libc, SetLastError = true)]
    public static extern int read(int fd, byte* buf, nuint count);

    [DllImport(Libc, SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);
}
