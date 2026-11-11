using System;
using System.Diagnostics;
using Android.OS;
using Android.Views;
using Activity = Android.App.Activity;
using Trace = System.Diagnostics.Trace;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// Precision frame pacing and timing controller that synchronizes with Android's Choreographer vsync callbacks,
/// locks render and game surface execution to the configured target FPS (30, 48, 60 FPS or Unlimited),
/// eliminates jitter without busy-waiting, and manages dynamic stepping down under hardware strain.
/// </summary>
public sealed class FramePacingController : Java.Lang.Object, Choreographer.IFrameCallback
{
    public static FramePacingController Instance { get; } = new();

    /// <summary>
    /// Consecutive slow frames required before triggering dynamic throttle step-down.
    /// At 60 FPS, 45 frames corresponds to ~750ms of sustained hardware strain,
    /// preventing transient lag (such as room or asset loading) from triggering false step-downs.
    /// </summary>
    public const int SlowFrameThrottleThreshold = 45;

    /// <summary>
    /// Required stabilization time in seconds with zero slow frames before frame rate can recover back up.
    /// Prevents rapid oscillation/hunting back up to 60 FPS during transient loads.
    /// </summary>
    public const double RecoveryStabilizationSeconds = 4.0;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private Choreographer? _choreographer;
    private long _lastDispatchedNanos;
    private long _lastFrameNanos;
    private bool _running;
    private Handler? _mainHandler;

    // Rolling frame time metrics for dynamic throttle detection
    private int _consecutiveSlowFrames;
    private readonly double[] _recentFrameTimesMs = new double[16];
    private int _recentFrameIndex;
    private DateTime _lastSlowFrameTime = DateTime.MinValue;

    public DateTime LastSlowFrameTime => _lastSlowFrameTime;

    public bool IsRecoveryStabilized(double requiredSeconds = RecoveryStabilizationSeconds)
    {
        return (DateTime.UtcNow - _lastSlowFrameTime).TotalSeconds >= requiredSeconds;
    }

    public void Start()
    {
        if (_running || PerformanceGuard.PacingDisabled || PerformanceGuard.EffectiveTargetFps <= 0)
            return;

        _running = true;
        _mainHandler ??= new Handler(Looper.MainLooper!);
        _mainHandler.Post(() =>
        {
            try
            {
                _choreographer = Choreographer.Instance;
                _choreographer?.PostFrameCallback(this);
                ApplyWindowRefreshRate();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FramePacer] Choreographer init failed: {ex.Message}");
            }
        });
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _choreographer?.RemoveFrameCallback(this);
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }

    public void ApplyWindowRefreshRate()
    {
        Activity? activity = MainActivity.Current;
        if (activity is null || activity.Window is null)
            return;

        activity.RunOnUiThread(() =>
        {
            try
            {
                int targetFps = PerformanceGuard.EffectiveTargetFps;
                Window window = activity.Window;
                WindowManagerLayoutParams attrs = window.Attributes!;

                if (targetFps > 0)
                {
                    // Android API 23+: Request matching refresh rate
                    attrs.PreferredRefreshRate = targetFps;
                }
                else
                {
                    attrs.PreferredRefreshRate = 0; // System default / maximum
                }
                window.Attributes = attrs;
                Trace.WriteLine($"[FramePacer] Window PreferredRefreshRate set to {attrs.PreferredRefreshRate} Hz");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FramePacer] ApplyWindowRefreshRate error: {ex.Message}");
            }
        });
    }

    public void DoFrame(long frameTimeNanos)
    {
        if (!_running)
            return;

        // Schedule next Choreographer vsync callback
        _choreographer?.PostFrameCallback(this);

        int targetFps = PerformanceGuard.EffectiveTargetFps;
        long deltaNanos = _lastFrameNanos > 0 ? (frameTimeNanos - _lastFrameNanos) : 0;
        _lastFrameNanos = frameTimeNanos;

        if (deltaNanos > 0)
        {
            double deltaMs = deltaNanos / 1_000_000.0;
            _recentFrameTimesMs[_recentFrameIndex % _recentFrameTimesMs.Length] = deltaMs;
            _recentFrameIndex++;
        }

        if (targetFps > 0)
        {
            long targetIntervalNanos = 1_000_000_000L / targetFps;
            // 1.5ms tolerance margin prevents dropping a vsync frame due to tiny sub-millisecond scheduling variations
            const long toleranceMarginNanos = 1_500_000L;

            if (_lastDispatchedNanos > 0 && (frameTimeNanos - _lastDispatchedNanos) < (targetIntervalNanos - toleranceMarginNanos))
            {
                // Skip frame tick to lock to target FPS
                return;
            }

            // Dynamic throttle check: detect sustained frame drops under hardware strain (> 40% over target budget)
            // Debounce to 45 consecutive slow frames (~750ms at 60 FPS) to avoid false triggers during room/asset loads
            if (PerformanceGuard.IsDynamicThrottleEligible && deltaNanos > (targetIntervalNanos * 1.4))
            {
                _consecutiveSlowFrames++;
                _lastSlowFrameTime = DateTime.UtcNow;
                PerformanceGuard.NotifyFrameStrain();

                if (_consecutiveSlowFrames >= SlowFrameThrottleThreshold)
                {
                    _consecutiveSlowFrames = 0;
                    PerformanceGuard.NotifyFrameDropStrain();
                }
            }
            else if (_consecutiveSlowFrames > 0)
            {
                _consecutiveSlowFrames--;
            }
        }
        else
        {
            _consecutiveSlowFrames = 0;
        }

        _lastDispatchedNanos = frameTimeNanos;
    }

    public double GetAverageFrameTimeMs()
    {
        double sum = 0;
        int count = Math.Min(_recentFrameIndex, _recentFrameTimesMs.Length);
        if (count == 0)
            return 0;
        for (int i = 0; i < count; i++)
            sum += _recentFrameTimesMs[i];
        return sum / count;
    }

    public double GetCurrentEstimatedFps()
    {
        double avgMs = GetAverageFrameTimeMs();
        return avgMs > 0 ? (1000.0 / avgMs) : 0;
    }
}
