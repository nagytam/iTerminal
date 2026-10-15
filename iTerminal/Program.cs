using iConsole.Application;
using iConsole.Desktops;
using iConsole.Inputs.Posix.Extensions;
using iConsole.Inputs.Windows.Extensions;
using iTerminal.Core.Application;
using iTerminal.Core.Services;
using iTerminal.Core.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

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

await app.RunAsync();
