using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.PopUpWindows;
using iConsole.Controls.ScrollBar;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Desktops;
using iConsole.Dialogs;
using iConsole.Diagnostics;
using iConsole.Drivers;
using iConsole.Jobs;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace iConsole.Application;

public static class iConsoleApplicationBuilderExtension
{
    public static IServiceCollection AddDefaultConsoleDriver(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSystemConsoleDriver();
        }
        else
        {
            services.AddVirtualSequenceConsoleDriver();
        }

        return services;
    }

    public static iConsoleApplicationBuilder UseScreen<TScreen>(this iConsoleApplicationBuilder builder)
        where TScreen : Screen
    {
        builder.Services.AddSingleton<IScreen, TScreen>();
        return builder;
    }

    public static iConsoleApplicationBuilder UseDesktop<TDesktop>(this iConsoleApplicationBuilder builder)
        where TDesktop : Desktop
    {
        builder.Services.AddSingleton<TDesktop>();
        builder.Services.AddSingleton<IDesktop>(services => services.GetRequiredService<TDesktop>());
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<TDesktop>());
        return builder;
    }

    public static IServiceCollection AddSystemConsoleDriver(this IServiceCollection services)
    {
        services.AddSingleton<IConsoleDriver, SystemConsoleDriver>();
        return services;
    }

    public static IServiceCollection AddVirtualSequenceConsoleDriver(this IServiceCollection services)
    {
        services.AddSingleton<IConsoleDriver, VirtualSequenceConsoleDriver>();
        return services;
    }

    /// <summary>Registers iConsole controls, buffer, render jobs, and services — without keyboard/mouse input jobs.</summary>
    public static IServiceCollection AddiConsoleCore(this IServiceCollection services)
    {
        // Every Job now reacts to cancellation within ~1s at worst (see Desktop.StopAsync /
        // per-job WaitHandle-based polling), so the generic host's 30s default ShutdownTimeout
        // is far more slack than needed. Cap it low so F10 (Quit) can never be held up for long
        // by a straggling background job — the process exits soon after cancellation either way.
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

        services.AddTransient<MenuBar>();
        services.AddTransient<StatusBar>();
        services.AddTransient<Date>();
        services.AddTransient<Window>();
        services.AddTransient<ScrollBar>();
        services.AddTransient<TextViewerControl>();
        services.AddTransient<TextEditorControl>();
        services.AddSingleton<TaskManager>();
        services.AddTransient<Controls.StatusBar.TaskStatus>();
        services.AddTransient<DatePopUpWindow>();
        services.AddSingleton<HelpDialog>();
        services.AddSingleton<IBuffer, Buffers.Buffer>();
        services.AddSingleton<ConsoleOwnership>();
        services.AddSingleton<IJob, ControlRendererJob>();
        services.AddSingleton<IJob, ConsoleBufferRendererJob>();
        services.AddSingleton<ProcessService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IBeepService, BeepService>();
        // The language the interface is shown in; the app adds its own resources and a store for the choice.
        services.AddSingleton<iConsole.Localization.ILocalizer, iConsole.Localization.Localizer>();
        services.AddSingleton<IErrorPromptService, ErrorPromptService>();
        // Notifications: the service holds them, the job counts them down, and a ToastHost on the
        // screen's root draws them. Posting needs only the service.
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IJob, NotificationJob>();
        // The main-loop watchdog times key handlers and render passes (the 200 ms budget). It is one instance, offered as a job
        // and to the loops it watches.
        services.AddSingleton<MainLoopWatchDog>();
        services.AddSingleton<IJob>(provider => provider.GetRequiredService<MainLoopWatchDog>());
        services.AddTransient<ToastHost>();
        // Tooltips: the input consumer reports the pointer to the service, and a ToolTipHost on the screen's root shows
        // the tip for the control under it. Time comes from ITimeProvider, so tests can move past the delay.
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddSingleton<IToolTipService, ToolTipService>();
        services.AddTransient<ToolTipHost>();
        // Popovers: one open at a time, placed on screen. Context menus open through it.
        services.AddSingleton<IPopoverService, PopoverService>();
        services.AddTransient<ContextMenu>();
        services.AddSingleton<IDateTimeProviderService, DateTimeProviderService>();
        services.AddSingleton<IFrequentExecutionGuardService, FrequentExecutionGuardService>();
        services.AddSingleton<IFileToTextConverterService, FileToTextConverterService>();
        services.AddSingleton<ICommandLogger>(NullCommandLogger.Instance);
        services.AddSingleton<IScreenLogger>(NullScreenLogger.Instance);
        return services;
    }

    /// <summary>Console.ReadKey polling keyboard — mutually exclusive with unified terminal input (Win32 / Linux TTY).</summary>
    public static IServiceCollection AddPollingConsoleKeyboard(this IServiceCollection services)
    {
        services.AddSingleton<IJob, KeyboardEventPublisherJob>();
        services.AddSingleton<IJob, KeyboardEventConsumerJob>();
        return services;
    }

    public static IServiceCollection AddiConsole(this IServiceCollection services)
    {
        services.AddiConsoleCore();
        services.AddPollingConsoleKeyboard();
        return services;
    }
}


