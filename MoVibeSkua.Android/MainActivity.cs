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

    private bool _touchOnGame;

    // Avalonia's view takes Android focus for every touch it handles, including touches on the game
    // WebView laid over it. The WebView losing focus makes Ruffle drop the focused Flash text field,
    // so the in-game password could not be typed after the username. Keep Avalonia's view
    // unfocusable while it handles a touch that started on the game.
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev?.ActionMasked == MotionEventActions.Down)
            _touchOnGame = Controls.GameView.Contains(ev.RawX, ev.RawY);
        if (!_touchOnGame || Controls.GameView.WebView?.Parent is not View host || !host.Focusable)
            return base.DispatchTouchEvent(ev);

        bool focusableInTouchMode = host.FocusableInTouchMode;
        host.Focusable = false;
        try
        {
            return base.DispatchTouchEvent(ev);
        }
        finally
        {
            host.Focusable = true;
            host.FocusableInTouchMode = focusableInTouchMode;
        }
    }

    // Avalonia's view is in the key path above the game and keeps the key-downs it handles, so the
    // game only saw key-ups: Backspace and Enter from the keyboard never reached Flash. The game gets
    // keys first while it has focus.
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { IsSystem: false } && Controls.GameView.WebView is { HasFocus: true } game && game.DispatchKeyEvent(e))
            return true;
        return base.DispatchKeyEvent(e);
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
