using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Drivers;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Test.Automation;

/// <summary>
/// A small but complete iConsole application for the driver's own tests: a text box, a hint line on
/// the last row, F2 opening a dialog that greets whatever was typed, F10 quitting, and F3 wired to a
/// handler that throws.
/// </summary>
public sealed class SampleScreen : iConsole.Screens.Screen
{
    public const string Hint = "F2 Greet  F10 Quit";

    private readonly IConsoleDriver _consoleDriver;
    private readonly Label _hint;

    public SampleScreen(IConsoleDriver consoleDriver, IThemeService themeService,
        IBeepService beepService, IDateTimeProviderService dateTimeProviderService)
        : base(consoleDriver, themeService)
    {
        _consoleDriver = consoleDriver;
        NameBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Name", Name = "NameBox" };
        _hint = new Label(Hint, themeService) { Name = "Hint" };
        _badge = new OverlayLabel(Badge, themeService) { Name = "Badge" };
        AddControl(NameBox);
        AddControl(_hint);
        AddControl(_badge);
    }

    public const string Badge = "[sample]";

    private readonly Label _badge;

    /// <summary>
    /// A label on the overlay layer, where dialogs are drawn — the way a status overlay or a pop-up
    /// list shares that layer in a real app. It is what keeps the layer alive after a dialog closes.
    /// </summary>
    private sealed class OverlayLabel(string text, IThemeService themeService) : Label(text, themeService)
    {
        public override void OnInitializeZOrder()
        {
            ZOrder = 1;
        }
    }

    public TextBox NameBox { get; }

    public override void OnInitializeCommands()
    {
        base.OnInitializeCommands();
        AddCommand(new GreetCommand());
        AddCommand(new QuitCommand());
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.F3)
        {
            throw new InvalidOperationException("F3 is broken on purpose.");
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnWindowSizeChanged(object? sender, Size size)
    {
        ViewSize = size;
        OnInitializeLayout();
    }

    public override void OnInitializeLayout()
    {
        Layout.Fill(_consoleDriver, this);
        NameBox.Position = new Position(2, 2);
        NameBox.ViewSize = new Size(40, 1);
        _hint.Position = new Position(0, Math.Max(0, ViewSize.Height - 1));
        _hint.ViewSize = new Size(ViewSize.Width, 1);
        _badge.Position = new Position(Math.Max(0, ViewSize.Width - Badge.Length), 0);
        _badge.ViewSize = new Size(Badge.Length, 1);
        base.OnInitializeLayout();
    }

    private sealed class GreetCommand : Command
    {
        public override string Title => "Greet";
        public override Key? Key { get; init; } = new(ConsoleKey.F2, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var screen = (SampleScreen)Control!;
            await screen.ShowDialogAsync(new GreetingDialog(screen.NameBox.Text, screen.ThemeService));
        }
    }
}

/// <summary>"Hello, &lt;name&gt;!" and an OK button that closes it.</summary>
public sealed class GreetingDialog : Dialog
{
    private readonly Label _message;
    private readonly Button _ok;

    public GreetingDialog(string name, IThemeService themeService)
        : base("Greeting", themeService)
    {
        Title = "Greeting";
        _message = new Label($"Hello, {name}!", themeService);
        _ok = new Button("OK", new CloseGreetingCommand(), themeService);
        AddControl(_message);
        AddControl(_ok);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent is not null)
        {
            CenterInParent(new Size(40, 7));
        }
        _message.Position = new Position(2, 2);
        _message.ViewSize = new Size(36, 1);
        _ok.Position = new Position(17, 4);
        _ok.ViewSize = new Size(6, 1);
    }

    private sealed class CloseGreetingCommand : Command
    {
        public override string Title => "OK";
        public override Task ExecuteAsync(object? args)
        {
            CloseContainingDialog();
            return Task.CompletedTask;
        }
    }
}
