using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iConsole.Controls.Charts;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace iTerminal.Core.Dialogs;

public class SystemInfoDialog : Dialog
{
    private readonly TextViewerControl _textViewerControl;

    public SystemInfoDialog(IThemeService themeService)
        : base(nameof(SystemInfoDialog), themeService)
    {
        Title = "System Information";
        _textViewerControl = new TextViewerControl(false, themeService);
        AddControl(_textViewerControl);
    }

    internal const string CollectingText = "  Collecting system information…";

    /// <summary>
    /// The collection the dialog last started. Nothing awaits it — it exists so a test can wait
    /// for the report instead of polling.
    /// </summary>
    internal Task InfoTask { get; private set; } = Task.CompletedTask;

    internal IReadOnlyList<string> LinesForTest => [.. _textViewerControl.TextModel.Lines.AsSnapshot()];

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        // Collected off the key handler: DriveInfo.IsReady on a disconnected network drive, and the
        // network-interface walk, can each take seconds — and the dialog used to show nothing at all
        // until both were done.
        _textViewerControl.TextModel.Lines = [CollectingText];
        InfoTask = Task.Run(() =>
        {
            _textViewerControl.TextModel.Lines = [.. BuildSystemInfo().TrimEnd().Split(Environment.NewLine)];
            StateHasChanged();
        });
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);

        base.OnInitializeLayout();
    }

    internal static string BuildSystemInfo()
    {
        var sb = new StringBuilder();

        // ── Operating System ──────────────────────────────────────────────
        sb.AppendLine("══ Operating System ══════════════════════════════════════════");
        sb.AppendLine($"  OS Description   : {RuntimeInformation.OSDescription}");
        sb.AppendLine($"  OS Architecture  : {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"  OS Version       : {Environment.OSVersion}");
        sb.AppendLine($"  Platform         : {Environment.OSVersion.Platform}");
        sb.AppendLine($"  64-bit OS        : {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"  System Directory : {Environment.SystemDirectory}");
        sb.AppendLine($"  System Uptime    : {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\d\\ hh\\:mm\\:ss}");

        // ── Hardware ──────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Hardware ══════════════════════════════════════════════════");
        sb.AppendLine($"  Machine Name     : {Environment.MachineName}");
        sb.AppendLine($"  Processor Count  : {Environment.ProcessorCount}");
        sb.AppendLine($"  Process Arch     : {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"  64-bit Process   : {Environment.Is64BitProcess}");

        var totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
        sb.AppendLine($"  Available Memory : {totalMemoryMb:N0} MB");

        // ── Runtime ───────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Runtime ═══════════════════════════════════════════════════");
        sb.AppendLine($"  Framework        : {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"  Runtime ID       : {RuntimeInformation.RuntimeIdentifier}");
        sb.AppendLine($"  CLR Version      : {Environment.Version}");

        // ── Current User ──────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Current User ══════════════════════════════════════════════");
        sb.AppendLine($"  User Name        : {Environment.UserName}");
        sb.AppendLine($"  User Domain      : {Environment.UserDomainName}");
        sb.AppendLine($"  Home Directory   : {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");

        // ── Drives ────────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Drives ════════════════════════════════════════════════════");
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    var totalGb  = drive.TotalSize           / 1024.0 / 1024 / 1024;
                    var freeGb   = drive.TotalFreeSpace      / 1024.0 / 1024 / 1024;
                    var usedGb   = (drive.TotalSize - drive.TotalFreeSpace) / 1024.0 / 1024 / 1024;
                    sb.AppendLine($"  {drive.Name,-6} {drive.DriveType,-10} {drive.DriveFormat,-8}  " +
                                  $"Total: {totalGb,8:N2} GB  Used: {usedGb,8:N2} GB  Free: {freeGb,8:N2} GB  " +
                                  $"{BarChart.BuildBar(drive.TotalSize == 0 ? 0 : (double)(drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize, 10).Replace(' ', '░')}  " +
                                  $"Label: {drive.VolumeLabel}");
                }
                else
                {
                    sb.AppendLine($"  {drive.Name,-6} {drive.DriveType,-10} (not ready)");
                }
            }
            catch
            {
                sb.AppendLine($"  {drive.Name,-6} (access denied)");
            }
        }

        // ── Network Interfaces ────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Network Interfaces ════════════════════════════════════════");
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            sb.AppendLine($"  {nic.Name,-30}  Type: {nic.NetworkInterfaceType,-18}  Status: {nic.OperationalStatus}");
            try
            {
                var ip = nic.GetIPProperties();
                foreach (var addr in ip.UnicastAddresses)
                {
                    sb.AppendLine($"    {"",30}  Address: {addr.Address}");
                }
            }
            catch { /* skip interfaces that deny access */ }
        }

        // ── Environment Variables ─────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("══ Environment Variables ═════════════════════════════════════");
        foreach (System.Collections.DictionaryEntry entry in
                 Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process)
                     .Cast<System.Collections.DictionaryEntry>()
                     .OrderBy(e => e.Key?.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  {entry.Key,-40} = {entry.Value}");
        }

        return sb.ToString();
    }
}
