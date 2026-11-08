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
    Label = "MVS",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    // The game is landscape; the shell's portrait layout stays for a later rotation option.
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    public static MainActivity? Current { get; private set; }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .LogToTrace(Avalonia.Logging.LogEventLevel.Error);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        Platform.Army.SeparateWebViewData();
        if (!System.Diagnostics.Trace.Listeners.OfType<Platform.LogcatTraceListener>().Any())
            System.Diagnostics.Trace.Listeners.Add(new Platform.LogcatTraceListener());
        Platform.LogcatLogMirror.Start();
        // The app's own log file and problem reports (crashes, MVS errors) for the MVS developer.
        Diagnostics.AppLog.Start();
        Diagnostics.ProblemReports.Install();

        Platform.AndroidBootstrap.Prepare(this);
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Services.PerformanceMode.Apply(this);
    }

    private bool _touchOnGame;
    private Handler? _handler;

    // Avalonia's view takes Android focus for every touch it handles, including touches on the game
    // WebView laid over it. The WebView losing focus makes Ruffle drop the focused Flash text field,
    // so the in-game password could not be typed after the username. Keep Avalonia's view
    // unfocusable while it handles a touch that started on the game.
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev?.ActionMasked == MotionEventActions.Down)
            _touchOnGame = Controls.GameView.Contains(ev.RawX, ev.RawY);
        if (!_touchOnGame && ev?.ActionMasked == MotionEventActions.Up)
            return DeferTouchUp(ev);
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

    // A tap clicks Avalonia's buttons on the touch-up, and a click that asks a question (a script's
    // start-up prompt, Fast Travel's Clear) waits in a nested message loop until it is answered. Run
    // inside the touch dispatch, that wait left the touch unfinished, and Android killed the app as
    // not responding after 5 seconds. The touch-up is finished now and handled right after instead.
    private bool DeferTouchUp(MotionEvent ev)
    {
        MotionEvent up = MotionEvent.Obtain(ev);
        // At the front of the queue: behind a game frame the tap would wait a frame longer (25 ms
        // on the phone) before the button reacts.
        (_handler ??= new Handler(Looper.MainLooper!)).PostAtFrontOfQueue(() =>
        {
            try
            {
                if (!IsDestroyed)
                    base.DispatchTouchEvent(up);
            }
            finally
            {
                up.Recycle();
            }
        });
        return true;
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
        Usage.UsageTracker.OnForeground();
    }

    // Out of sight (home screen, another app, screen off): the game keeps running but draws nothing.
    protected override void OnStop()
    {
        Platform.BotControl.SetInBackground(true);
        base.OnStop();
    }

    protected override void OnStart()
    {
        base.OnStart();
        Platform.BotControl.SetInBackground(false);
    }

    protected override void OnPause()
    {
        Usage.UsageTracker.OnBackground();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (Current == this)
            Current = null;
        // An army client swiped away from recent apps (or closed from the Army page) is done; its
        // process goes too. The first client keeps the old behaviour.
        if (IsFinishing && !Platform.Army.IsFirst)
            Platform.KeepAliveService.Release(this, Platform.Army.Slot);
        base.OnDestroy();
    }
}
