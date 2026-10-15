using iConsole.Application;
using iConsole.Demo.Screens;
using iConsole.Desktops;
using iConsole.Inputs.Posix.Extensions;
using iConsole.Inputs.Windows.Extensions;
using Microsoft.FeatureManagement;

var builder = iConsoleApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
builder.Services.AddVirtualSequenceConsoleDriver();
builder.Services.AddiConsoleCore();
if (OperatingSystem.IsLinux())
{
    builder.Services.AddLinuxTerminalInput();
}
else
{
    await builder.Services.AddInputWindowsAsync();
}

builder.UseDesktop<Desktop>();
builder.UseScreen<DemoScreen>();

var app = builder.Build();

await app.RunAsync();