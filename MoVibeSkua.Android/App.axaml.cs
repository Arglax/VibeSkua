using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using MoVibeSkua.Android.Controls;
using MoVibeSkua.Android.Platform;
using MoVibeSkua.Android.Services;
using MoVibeSkua.Android.ViewModels;
using MoVibeSkua.Android.Views;
using Skua.App.Avalonia.AppStartup;
using Skua.App.Avalonia.Services;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Flash.Ruffle;
using Skua.Shared.Avalonia.Controls.Theming;
using Skua.Shared.Avalonia.Services;

namespace MoVibeSkua.Android;

public partial class App : Application
{
    private static bool _servicesConfigured;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (!_servicesConfigured)
        {
            _servicesConfigured = true;
            Trace.WriteLine("[Startup] configuring services");
            ConfigureServices();
            Usage.UsageTracker.Start(global::Android.App.Application.Context);
            // Class grades in the game's own inventory (drawn locally by skua.swf).
            Skua.App.Avalonia.Services.ClassRatings.AttachToGame(Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IFlashUtil>());
        }

        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            Trace.WriteLine("[Startup] creating shell");
            single.MainView = new MobileShellView
            {
                DataContext = Ioc.Default.GetRequiredService<ShellViewModel>()
            };
            Trace.WriteLine("[Startup] starting game");
            StartGame();
            Trace.WriteLine("[Startup] game started");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Trace.WriteLine($"[Unhandled] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Trace.WriteLine($"[Unobserved] {e.Exception}");
            e.SetObserved();
        };

        ServiceCollection services = new();
        services.AddCommonServices();
        services.AddCompiler();
        services.AddScriptableObjects();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IClipboardService, AndroidClipboardService>();
        services.AddSingleton<IDialogService, MobileDialogService>();
        services.AddSingleton<IWindowService, MobileWindowService>();
        services.AddSingleton<IFileDialogService, MobileFileDialogService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton(HotKeys.CreateHotKeys);
        services.AddSingleton<IHotKeyService, HotKeyService>();
        services.AddSingleton<ISoundService, AndroidSoundService>();
        services.AddSingleton<IScreenshotService, AndroidScreenshotService>();
        services.AddSingleton<IProcessService, AndroidProcessService>();
        services.AddSingleton(_ => new RuffleHost(new RuffleHostOptions
        {
            // Native resolution with MSAA: at 1.5 the game drew at about half the screen's pixels and
            // looked blurry, while the phone's GPU held full frame rate at its native 2.75.
            Quality = "high",
            // The Graphics page's resolution when one is chosen; 3 keeps the screen's own ratio.
            MaxPixelRatio = ViewModels.GraphicsViewModel.StartupPixelRatio(),
            PreferredRenderer = "webgl"
        }));
        services.AddSingleton<RuffleFlashUtil>();
        services.AddSingleton<IFlashUtil>(s => s.GetRequiredService<RuffleFlashUtil>());
        services.AddSingleton<SkuaStartupHandler>();
        services.AddSingleton<ViewModels.GraphicsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<LoginViewModel>();
        Skua.App.Avalonia.AppStartup.Services.AddSkuaMainAppViewModels(services);

        // Skua's own Streamer Mode (Options) shows the same stand-in as the shell's eye button.
        Skua.Core.Scripts.ScriptOption.StreamerName = ShellViewModel.StreamerName;
        Skua.Core.Scripts.ScriptOption.StreamerGuild = $"< {ShellViewModel.StreamerGuild} >";

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
        // Plugins open their windows through PluginWindow, which can't create a window here.
        PluginWindow.PageHost = (MobileWindowService)Ioc.Default.GetRequiredService<IWindowService>();

        Task.Run(() =>
        {
            try
            {
                IClientFilesService clientFiles = Ioc.Default.GetRequiredService<IClientFilesService>();
                clientFiles.CreateDirectories();
                clientFiles.CreateFiles();
                Ioc.Default.GetRequiredService<IDiagnosticsService>().Start();
                Ioc.Default.GetRequiredService<ISettingsService>().SetApplicationVersion();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Startup] {ex}");
            }
        });

        ColorSpectrumFix.Initialize();
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        themeService.ThemeChanged += _ => ApplyTheme(themeService);
        themeService.SchemeChanged += (_, _) => ApplyTheme(themeService);
        ApplyTheme(themeService);
    }

    private static void ApplyGraphics() => Ioc.Default.GetService<ViewModels.GraphicsViewModel>()?.Apply();

    private static void StartGame()
    {
        RuffleFlashUtil flash = Ioc.Default.GetRequiredService<RuffleFlashUtil>();
        flash.PageRequested -= GameView.Navigate;
        flash.PageRequested += GameView.Navigate;
        flash.Host.Control.KeyboardRequested -= GameView.ShowKeyboard;
        flash.Host.Control.KeyboardRequested += GameView.ShowKeyboard;
        // A (re)loaded game page starts with the defaults; send it the Graphics page's settings.
        flash.Host.Control.Connected -= ApplyGraphics;
        flash.Host.Control.Connected += ApplyGraphics;

        Ioc.Default.GetRequiredService<SkuaStartupHandler>().Execute();
        Trace.WriteLine("[Startup] initializing flash");
        flash.InitializeFlash();

        // Script interface hooks the flash events in its constructor.
        _ = Ioc.Default.GetRequiredService<IScriptInterface>();
        // The small "script running" window over the game.
        ScriptStatusOverlay.Start();

        // Started once the UI has settled: the service's start callback runs on the main thread and
        // Android kills apps whose foreground service does not come up within a few seconds.
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                if (Army.IsFirst)
                    KeepAliveService.Start(global::Android.App.Application.Context);
                else
                    KeepAliveService.Hold(global::Android.App.Application.Context, Army.Slot);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[KeepAlive] {ex.Message}");
            }
        }, TimeSpan.FromSeconds(5), DispatcherPriority.Background);

        _ = Task.Run(InitializeStartupServices);
    }

    private static async Task InitializeStartupServices()
    {
        await Task.Delay(500);
        try
        {
            Ioc.Default.GetRequiredService<IPluginManager>().Initialize();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Startup plugin init failed: {ex.Message}");
        }

        try
        {
            Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Startup hotkey init failed: {ex.Message}");
        }

        // The first client keeps the shared scripts folder up to date; army clients would download
        // over each other (and ask the same question once per client).
        if (!Army.IsFirst)
            return;

        try
        {
            IGetScriptsService getScripts = Ioc.Default.GetRequiredService<IGetScriptsService>();
            await getScripts.GetScriptsAsync(null, default);

            ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
            IDialogService dialogs = Ioc.Default.GetRequiredService<IDialogService>();
            bool firstRun = getScripts.Missing > 0 && getScripts.Downloaded == 0;
            if ((firstRun || (settings.Get<bool>("CheckBotScriptsUpdates") && (getScripts.Missing > 0 || getScripts.Outdated > 0)))
                && (settings.Get<bool>("AutoUpdateBotScripts")
                    || dialogs.ShowMessageBox(firstRun
                        ? "Download the community bot scripts now? (needed to run scripts)"
                        : "Would you like to update your scripts?", "Scripts", true) == true))
            {
                await Skua.Core.Services.ScriptStartupUpdater.DownloadMissingAndOutdatedAsync(getScripts, dialogs);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Script update check failed: {ex.Message}");
        }
    }

    private void ApplyTheme(IThemeService themeService)
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool isDark = themeService.IsDarkTheme;
            RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (themeService is AvaloniaThemeService ats)
            {
                ThemeResourceApplicator.ApplyAccentBrushes(this, ats.PrimaryColor, ats.PrimaryForegroundColor, ats.SecondaryColor, ats.SecondaryForegroundColor, isDarkTheme: isDark);
                return;
            }
            ThemeResourceApplicator.ApplyAccentBrushes(this, themeService.SelectedColor?.ToString(), isDarkTheme: isDark);
        });
    }
}
