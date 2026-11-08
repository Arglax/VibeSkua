using System;
using System.Linq;
using Android.App;
using Android.OS;
using Android.Views;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// Asks Android for the most the phone can give the game while it is cool, and backs off when it is
/// hot. The game itself (Ruffle) runs on one browser thread, so what helps is that thread getting the
/// fastest cores at full clock speed and frames lining up with the screen:
///  - the screen's highest refresh rate (30 fps game frames and 60 fps in-betweens fall on whole refreshes);
///  - the game state "playing" for Android's game scheduling (Android 13+; the manifest marks MVS as a
///    game, which is also what MIUI Game Turbo and Android's Game Mode go by);
///  - above the cool-down temperature, Android's sustained-performance mode: steady, lower clocks that
///    can be held without the phone throttling hard (and stuttering) later. It stays off below it, since
///    it also caps the peak.
/// </summary>
public static class PerformanceMode
{
    /// <summary>Battery temperature (C) at which the phone switches to its cooler mode.</summary>
    public const double CoolDownAt = 47.0;

    /// <summary>... and back to full speed once it is below this.</summary>
    public const double FullSpeedBelow = 45.0;

    private static bool _sustained;

    public static void Apply(Activity activity)
    {
        try
        {
            PreferHighestRefreshRate(activity);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Perf] refresh rate: {ex.Message}");
        }

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && activity.GetSystemService(global::Android.Content.Context.GameService) is GameManager games)
            {
                games.SetGameState(new GameState(false, (int)GameStateMode.GameplayInterruptible));
                System.Diagnostics.Trace.WriteLine($"[Perf] game mode {games.GameMode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Perf] game state: {ex.Message}");
        }
    }

    /// <summary>
    /// Bot-only mode draws nothing, so the screen needs no high refresh rate: 60 Hz (the lowest rate of
    /// at least 60) means fewer compositor wake-ups. Off goes back to the highest rate.
    /// </summary>
    public static void SetLowRefresh(Activity activity, bool low)
    {
        try
        {
            if (low)
                PreferRefreshRate(activity, lowest: true);
            else
                PreferHighestRefreshRate(activity);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Perf] refresh rate: {ex.Message}");
        }
    }

    private static void PreferHighestRefreshRate(Activity activity) => PreferRefreshRate(activity, lowest: false);

    private static void PreferRefreshRate(Activity activity, bool lowest)
    {
        Display? display = OperatingSystem.IsAndroidVersionAtLeast(30) ? activity.Display : activity.WindowManager?.DefaultDisplay;
        Window? window = activity.Window;
        if (display is null || window is null)
            return;
        Display.Mode? current = display.GetMode();
        Display.Mode? best = display.GetSupportedModes()?
            .Where(m => current is null || (m.PhysicalWidth == current.PhysicalWidth && m.PhysicalHeight == current.PhysicalHeight))
            .Where(m => !lowest || m.RefreshRate >= 59f)
            .OrderBy(m => lowest ? m.RefreshRate : -m.RefreshRate)
            .FirstOrDefault();
        if (best is null)
            return;
        WindowManagerLayoutParams attrs = window.Attributes!;
        attrs.PreferredDisplayModeId = best.ModeId;
        attrs.PreferredRefreshRate = best.RefreshRate;
        window.Attributes = attrs;
        System.Diagnostics.Trace.WriteLine($"[Perf] display mode {best.ModeId}: {best.PhysicalWidth}x{best.PhysicalHeight} @ {best.RefreshRate:0} Hz");
    }

    /// <summary>Called with each temperature reading (ThermalMonitor).</summary>
    public static void OnTemperature(double batteryCelsius, int thermalStatus)
    {
        bool hot = batteryCelsius >= CoolDownAt || thermalStatus >= 3;
        bool cool = batteryCelsius < FullSpeedBelow && thermalStatus < 3;
        bool want = _sustained ? !cool : hot;
        if (want == _sustained)
            return;
        Activity? activity = MainActivity.Current;
        if (activity is null)
            return;
        activity.RunOnUiThread(() =>
        {
            try
            {
                if (activity.GetSystemService(global::Android.Content.Context.PowerService) is PowerManager power
                    && power.IsSustainedPerformanceModeSupported)
                {
                    activity.Window?.SetSustainedPerformanceMode(want);
                }
                _sustained = want;
                System.Diagnostics.Trace.WriteLine($"[Perf] {(want ? "cool-down" : "full speed")} mode at {batteryCelsius:0.0} C (status {thermalStatus})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Perf] sustained mode: {ex.Message}");
            }
        });
    }
}
