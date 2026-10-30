using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace MoVibeSkua.Android;

[Activity(
    Label = "MoVibeSkua",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    public static MainActivity? Current { get; private set; }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .LogToTrace();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        if (!System.Diagnostics.Trace.Listeners.OfType<Platform.LogcatTraceListener>().Any())
            System.Diagnostics.Trace.Listeners.Add(new Platform.LogcatTraceListener());

        Platform.AndroidBootstrap.Prepare(this);
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Controls.GameView.Resume();
    }

    protected override void OnDestroy()
    {
        if (Current == this)
            Current = null;
        base.OnDestroy();
    }
}
