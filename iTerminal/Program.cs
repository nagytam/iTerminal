using iConsole.Application;
using iConsole.Desktops;
using iConsole.Inputs.Posix.Extensions;
using iConsole.Inputs.Windows.Extensions;
using iTerminal.Core.Application;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Elevation;
using iTerminal.Core.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

// A copy restarted with elevated rights is started while the one it replaces is still shutting down.
// The settings are written back whole when that one exits, so wait for it before touching them.
var waitForParent = Array.IndexOf(args, ElevationService.WaitForParentArgument);
if (waitForParent >= 0 && waitForParent + 1 < args.Length && int.TryParse(args[waitForParent + 1], out var parentId))
{
    try
    {
        using var parent = System.Diagnostics.Process.GetProcessById(parentId);
        parent.WaitForExit(TimeSpan.FromSeconds(20));
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        // Already gone.
    }
}

var builder = iConsoleApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
builder.Services.AddDefaultConsoleDriver();
builder.Services.AddiConsoleCore();
if (OperatingSystem.IsLinux())
{
    builder.Services.AddLinuxTerminalInput();
}
else
{
    await builder.Services.AddInputWindowsAsync();
}

builder.Services.AddiTerminal(builder.Configuration);
builder.UseDesktop<Desktop>();
builder.UseScreen<TerminalScreen>();

var app = builder.Build();

// A remembered master password that has not expired unlocks the saved passwords here. Otherwise the
// start-up dialog asks for it once the screen opens.
app.Services.GetRequiredService<IPasswordManager>().UnlockRemembered();

var elevation = app.Services.GetRequiredService<IElevationService>();
if (elevation.IsElevated)
{
    try
    {
        Console.Title = "iTerminal (administrator)";
    }
    catch (IOException)
    {
        // No console title to set.
    }

    app.Services.GetService<iConsole.Services.INotificationService>()?.Notify(
        "Running as administrator",
        "Everything started from here has full rights.",
        iConsole.Services.NotificationSeverity.Warning);
}

await app.RunAsync();

// Restarting as root on Unix replaces this process once the host has stopped and given the terminal back.
PendingRelaunch.TryExecute();
