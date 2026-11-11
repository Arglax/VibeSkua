using System;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Flash.Ruffle;
using Activity = Android.App.Activity;
using Trace = System.Diagnostics.Trace;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Headless mode: the whole screen goes black and nothing is drawn (the rails, the shell and the game),
/// while the game and any running script keep going, as when the app is in the background. One tap
/// anywhere brings everything back.
///
/// The app's own views are hidden (an invisible SurfaceView/WebView stops redrawing), a plain black view
/// covers the window, and the game page gets the same "background" command as when the app leaves the
/// screen: Ruffle skips drawing and ticks from its background worker.
/// </summary>
public static class Headless
{
    private static FrameLayout? _cover;
    private static View? _hidden;

    public static bool Active => _cover is not null;

    public static void Enter()
    {
        Activity? activity = MainActivity.Current;
        if (activity is null || Active)
            return;
        activity.RunOnUiThread(() =>
        {
            try
            {
                if (_cover is not null)
                    return;
                ViewGroup? content = activity.FindViewById<ViewGroup>(global::Android.Resource.Id.Content);
                if (content is null)
                    return;
                _hidden = content.ChildCount > 0 ? content.GetChildAt(0) : null;

                FrameLayout cover = new(activity) { Clickable = true, Focusable = true };
                cover.SetBackgroundColor(Color.Black);
                // A short hint, then pure black.
                TextView hint = new(activity)
                {
                    Text = "Headless mode · tap anywhere to show the game",
                    Gravity = GravityFlags.Center
                };
                hint.SetTextColor(Color.Argb(150, 200, 184, 154));
                hint.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
                cover.AddView(hint, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                cover.Touch += (_, e) =>
                {
                    if (e.Event?.Action == MotionEventActions.Up)
                        Exit();
                    e.Handled = true;
                };
                activity.AddContentView(cover, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                _cover = cover;
                new Handler(Looper.MainLooper!).PostDelayed(() => hint.Visibility = ViewStates.Gone, 3000);

                if (_hidden is not null)
                    _hidden.Visibility = ViewStates.Invisible;
                SendBackground(true);
                Trace.WriteLine("[Headless] on");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Headless] enter failed: {ex.Message}");
                Exit();
            }
        });
    }

    public static void Exit()
    {
        Activity? activity = MainActivity.Current;
        void Restore()
        {
            try
            {
                if (_cover?.Parent is ViewGroup parent)
                    parent.RemoveView(_cover);
                _cover = null;
                if (_hidden is not null)
                    _hidden.Visibility = ViewStates.Visible;
                _hidden = null;
                SendBackground(false);
                Trace.WriteLine("[Headless] off");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Headless] exit failed: {ex.Message}");
            }
        }
        if (activity is null)
            Restore();
        else
            activity.RunOnUiThread(Restore);
    }

    /// <summary>The app came back on screen: it said "not in background", so headless says it again.</summary>
    public static void Reassert()
    {
        if (Active)
            SendBackground(true);
    }

    private static void SendBackground(bool on) =>
        Ioc.Default.GetService<RuffleFlashUtil>()?.PostCommand(on ? "{\"t\":\"background\",\"v\":true}" : "{\"t\":\"background\",\"v\":false}");
}
