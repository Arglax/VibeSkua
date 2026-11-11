using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using MoVibeSkua.Android.ViewModels;
using MoVibeSkua.Android.Views;
using Skua.Core.Interfaces;
using Skua.Flash.Ruffle;
using Trace = System.Diagnostics.Trace;

namespace MoVibeSkua.Android.Services;

public enum HardwareAlertSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record HardwareAlertInfo(
    string Message,
    HardwareAlertSeverity Severity,
    Material.Icons.MaterialIconKind Icon,
    string BrushHex);

public sealed record MemoryMetrics(
    long UsedBytes,
    long AvailableBytes,
    long TotalBytes,
    bool IsLowMemory)
{
    public double UsedMb => UsedBytes / (1024.0 * 1024.0);
    public double AvailableMb => AvailableBytes / (1024.0 * 1024.0);
    public double TotalMb => TotalBytes / (1024.0 * 1024.0);

    public string FormattedStatus => $"{UsedMb:0} MB used / {AvailableMb:0} MB avail (Total: {TotalMb:0} MB)";
}

/// <summary>
/// Central Performance Guard subsystem for MoVibeSkua.Android.
/// Coordinates adaptive frame pacing, dynamic throttling, thermal stress monitoring,
/// and low-memory LMK prevention.
/// </summary>
public sealed class PerformanceGuard
{
    public static PerformanceGuard Instance { get; } = new();

    private const string PrefsName = "movibeskua.shell";
    private const string KeyEnabled = "perf.guard.enabled";
    private const string KeyTargetFps = "perf.guard.target_fps";
    private const string KeyAutoThrottle = "perf.guard.auto_throttle";
    private const string KeyHardwareAlerts = "perf.guard.hardware_alerts";

    private ISharedPreferences? _prefs;
    private PowerManager? _powerManager;
    private ActivityManager? _activityManager;
    private Handler? _mainHandler;

    private bool _enablePerformanceGuard = true;
    private int _targetFps = 30;
    private bool _autoThrottleOnOverheat = true;
    private bool _showHardwareAlerts = true;

    // Dynamic throttle state
    private int _dynamicPacedFps;
    private bool _isDynamicThrottled;
    private DateTime _lastDynamicThrottleTime = DateTime.MinValue;
    private DateTime _lastDynamicStrainTime = DateTime.MinValue;

    // Thermal state
    private int _currentThermalStatus = -1;
    private double _currentHeadroom = -1;
    private double _currentBatteryTemp = -1;
    private bool _isThermalThrottled;
    private int _isTrimMemoryRunning;

    public event Action<HardwareAlertInfo>? HardwareAlertTriggered;
    public event Action<int>? TargetFpsChanged;
    public event Action? MemoryPressurePurged;

    public static int EffectiveTargetFps => Instance.ComputeEffectiveTargetFps();

    public static bool IsDynamicThrottleEligible =>
        Instance._enablePerformanceGuard && Instance._targetFps > 30 && !Instance._isThermalThrottled;

    public bool EnablePerformanceGuard
    {
        get => _enablePerformanceGuard;
        set
        {
            if (_enablePerformanceGuard == value)
                return;
            _enablePerformanceGuard = value;
            SavePreferences();
            SyncSettingsService();
            ApplyFramePacing();
        }
    }

    public int TargetFps
    {
        get => _targetFps;
        set
        {
            if (_targetFps == value)
                return;
            _targetFps = value;
            _isDynamicThrottled = false;
            SavePreferences();
            SyncSettingsService();
            ApplyFramePacing();
        }
    }

    public bool AutoThrottleOnOverheat
    {
        get => _autoThrottleOnOverheat;
        set
        {
            if (_autoThrottleOnOverheat == value)
                return;
            _autoThrottleOnOverheat = value;
            SavePreferences();
            SyncSettingsService();
            EvaluateThermalThrottle();
        }
    }

    public bool ShowHardwareAlerts
    {
        get => _showHardwareAlerts;
        set
        {
            if (_showHardwareAlerts == value)
                return;
            _showHardwareAlerts = value;
            SavePreferences();
            SyncSettingsService();
        }
    }

    public int CurrentThermalStatus => _currentThermalStatus;
    public double CurrentHeadroom => _currentHeadroom;
    public double CurrentBatteryTemp => _currentBatteryTemp;
    public bool IsThermalThrottled => _isThermalThrottled;
    public bool IsDynamicThrottled => _isDynamicThrottled;

    public void Initialize(Context context)
    {
        _prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        _powerManager = context.GetSystemService(Context.PowerService) as PowerManager;
        _activityManager = context.GetSystemService(Context.ActivityService) as ActivityManager;
        _mainHandler = new Handler(Looper.MainLooper!);

        LoadPreferences();
        SyncSettingsService();

        if (_powerManager is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            try
            {
                _powerManager.AddThermalStatusListener(new ThermalStatusListener(this));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PerfGuard] AddThermalStatusListener failed: {ex.Message}");
            }
        }

        // Periodic recovery evaluation for dynamic throttling (1s interval)
        _mainHandler.PostDelayed(PeriodicCheck, 1000);
        FramePacingController.Instance.Start();
        ApplyFramePacing();
    }

    private void LoadPreferences()
    {
        if (_prefs is null)
            return;

        _enablePerformanceGuard = _prefs.GetBoolean(KeyEnabled, true);
        _targetFps = _prefs.GetInt(KeyTargetFps, 30);
        _autoThrottleOnOverheat = _prefs.GetBoolean(KeyAutoThrottle, true);
        _showHardwareAlerts = _prefs.GetBoolean(KeyHardwareAlerts, true);
    }

    private void SavePreferences()
    {
        _prefs?.Edit()?
            .PutBoolean(KeyEnabled, _enablePerformanceGuard)?
            .PutInt(KeyTargetFps, _targetFps)?
            .PutBoolean(KeyAutoThrottle, _autoThrottleOnOverheat)?
            .PutBoolean(KeyHardwareAlerts, _showHardwareAlerts)?
            .Apply();
    }

    private void SyncSettingsService()
    {
        try
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings is null)
                return;

            settings.Set("EnablePerformanceGuard", _enablePerformanceGuard);
            settings.Set("TargetFPS", _targetFps);
            settings.Set("AutoThrottleOnOverheat", _autoThrottleOnOverheat);
            settings.Set("ShowHardwareAlerts", _showHardwareAlerts);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PerfGuard] SettingsService sync error: {ex.Message}");
        }
    }

    private int ComputeEffectiveTargetFps()
    {
        if (!_enablePerformanceGuard)
            return 0; // Unlimited / Device Refresh

        if (_isThermalThrottled)
            return 30; // Lowest playable pace under heat

        if (_isDynamicThrottled && _dynamicPacedFps > 0)
            return _dynamicPacedFps;

        return _targetFps;
    }

    public static void NotifyFrameDropStrain() => Instance.HandleFrameDropStrain();

    public static void NotifyFrameStrain() => Instance.RecordFrameStrain();

    public void RecordFrameStrain()
    {
        _lastDynamicStrainTime = DateTime.UtcNow;
    }

    private void HandleFrameDropStrain()
    {
        if (!_enablePerformanceGuard || _isThermalThrottled || _targetFps <= 30)
            return;

        int currentPace = _isDynamicThrottled ? _dynamicPacedFps : _targetFps;
        int nextPace = currentPace switch
        {
            > 48 => 48,
            > 30 => 30,
            _ => 30
        };

        if (nextPace < currentPace)
        {
            _isDynamicThrottled = true;
            _dynamicPacedFps = nextPace;
            _lastDynamicThrottleTime = DateTime.UtcNow;
            _lastDynamicStrainTime = DateTime.UtcNow;

            Trace.WriteLine($"[PerfGuard] Frame drop detected ({FramePacingController.SlowFrameThrottleThreshold} consecutive slow frames). Dynamic throttle stepped FPS down to {_dynamicPacedFps}");
            ApplyFramePacing();

            if (_showHardwareAlerts)
            {
                PostAlert(new HardwareAlertInfo(
                    $"Frame Pace Throttled: Stepped down to {_dynamicPacedFps} FPS to prevent lag",
                    HardwareAlertSeverity.Warning,
                    Material.Icons.MaterialIconKind.SpeedometerSlow,
                    "#D97706"));
            }
        }
        else
        {
            _lastDynamicStrainTime = DateTime.UtcNow;
        }
    }

    private void PeriodicCheck()
    {
        try
        {
            // Evaluate recovery from dynamic frame throttle using 4-second stabilization window
            if (_isDynamicThrottled && !_isThermalThrottled)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastDynamicStrainTime).TotalSeconds >= FramePacingController.RecoveryStabilizationSeconds &&
                    (now - _lastDynamicThrottleTime).TotalSeconds >= FramePacingController.RecoveryStabilizationSeconds &&
                    FramePacingController.Instance.IsRecoveryStabilized())
                {
                    int currentPace = _dynamicPacedFps;
                    int nextPace = currentPace switch
                    {
                        <= 30 when _targetFps >= 48 => 48,
                        <= 48 when _targetFps >= 60 => 60,
                        _ => _targetFps
                    };

                    if (nextPace > currentPace)
                    {
                        _dynamicPacedFps = nextPace;
                        _lastDynamicThrottleTime = now;
                        _lastDynamicStrainTime = now;
                        if (_dynamicPacedFps >= _targetFps)
                            _isDynamicThrottled = false;

                        Trace.WriteLine($"[PerfGuard] Frame pacing recovered after {FramePacingController.RecoveryStabilizationSeconds:0}s stabilization: Stepped FPS up to {_dynamicPacedFps}");
                        ApplyFramePacing();

                        if (_showHardwareAlerts)
                        {
                            PostAlert(new HardwareAlertInfo(
                                $"Frame Pace Restored: Stepped up to {_dynamicPacedFps} FPS",
                                HardwareAlertSeverity.Info,
                                Material.Icons.MaterialIconKind.Speedometer,
                                "#2563EB"));
                        }
                    }
                }
            }

            // Update battery temperature fallback
            _currentBatteryTemp = ReadBatteryCelsius();
            EvaluateThermalThrottle();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PerfGuard] PeriodicCheck error: {ex.Message}");
        }

        _mainHandler?.PostDelayed(PeriodicCheck, 1000);
    }

    /// <summary>0 = normal, 1 = warm (moderate), 2 = hot (critical); between 42 and 44 C the last level holds.</summary>
    private int _thermalLevel;

    public void OnThermalStatusChanged(int status)
    {
        _currentThermalStatus = status;
        if (_powerManager is not null && OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            float h = _powerManager.GetThermalHeadroom(10);
            if (!float.IsNaN(h))
                _currentHeadroom = h;
        }
        _currentBatteryTemp = ReadBatteryCelsius();
        EvaluateThermalThrottle();
    }

    public void UpdateThermalMetrics(int status, double headroom, double batteryCelsius)
    {
        _currentThermalStatus = status;
        _currentHeadroom = headroom;
        _currentBatteryTemp = batteryCelsius;
        EvaluateThermalThrottle();
    }

    private void EvaluateThermalThrottle()
    {
        int status = _currentThermalStatus;
        double battery = _currentBatteryTemp;

        // Status thresholds:
        // 0: None, 1: Light, 2: Moderate, 3: Severe, 4: Critical, 5: Emergency, 6: Shutdown (-1: unknown)
        bool isCritical = status >= 4 || battery >= 47.0;
        bool isModerate = !isCritical && (status >= 2 || battery >= 44.0);

        // Called every second: act (log, alert, throttle) only when the level changes. Repeating it posted
        // the warning banner and a log line every second while the phone sat at 44 C (2026-11-12).
        int level = isCritical ? 2 : isModerate ? 1 : (status <= 1 && battery < 42.0) ? 0 : _thermalLevel;
        if (level == _thermalLevel)
            return;
        _thermalLevel = level;

        if (isCritical)
        {
            Trace.WriteLine($"[PerfGuard] CRITICAL Thermal Pressure: status {status}, battery {battery:0.0}°C");

            if (_enablePerformanceGuard && _autoThrottleOnOverheat && !_isThermalThrottled)
            {
                _isThermalThrottled = true;
                ApplyFramePacing();

                // Direct Ruffle to pause in-betweening
                Ioc.Default.GetService<RuffleFlashUtil>()?.PostCommand("{\"t\":\"botOnly\",\"v\":false}");

                if (_showHardwareAlerts)
                {
                    PostAlert(new HardwareAlertInfo(
                        $"High Heat Alert ({battery:0.0}°C)! Throttled to 30 FPS to prevent shutdown",
                        HardwareAlertSeverity.Critical,
                        Material.Icons.MaterialIconKind.Fire,
                        "#DC2626"));
                }
            }
        }
        else if (isModerate)
        {
            Trace.WriteLine($"[PerfGuard] Moderate Thermal Pressure: status {status}, battery {battery:0.0}°C");

            if (_showHardwareAlerts && !_isThermalThrottled)
            {
                PostAlert(new HardwareAlertInfo(
                    $"Device Warming Up ({battery:0.0}°C) - Moderate Thermal Status",
                    HardwareAlertSeverity.Warning,
                    Material.Icons.MaterialIconKind.ThermometerAlert,
                    "#D97706"));
            }
        }
        else if (status <= 1 && battery < 42.0)
        {
            // Fully cooled down
            if (_isThermalThrottled)
            {
                _isThermalThrottled = false;
                Trace.WriteLine($"[PerfGuard] Thermal pressure subsided ({battery:0.0}°C). Restoring normal pace.");
                ApplyFramePacing();

                if (_showHardwareAlerts)
                {
                    PostAlert(new HardwareAlertInfo(
                        $"Device Cooled Down ({battery:0.0}°C). Normal render pacing restored",
                        HardwareAlertSeverity.Info,
                        Material.Icons.MaterialIconKind.ThermometerCheck,
                        "#16A34A"));
                }
            }
        }
    }

    public static void OnTrimMemory(TrimMemory level) => Instance.HandleTrimMemory(level);

    public void HandleTrimMemory(TrimMemory level)
    {
        Trace.WriteLine($"[PerfGuard] OnTrimMemory received: {level} ({(int)level})");

        // Action on pressure:
        // TrimMemory.RunningLow = 10
        // TrimMemory.RunningCritical = 15
        // TrimMemory.Complete = 80
        bool isPressure = level is TrimMemory.RunningLow or TrimMemory.RunningCritical or TrimMemory.Complete
            || (int)level >= 10;

        if (isPressure)
        {
            if (Interlocked.CompareExchange(ref _isTrimMemoryRunning, 1, 0) != 0)
                return; // Already performing an asynchronous trim pass

            // Run asynchronously off the Android UI thread / main looper to prevent ANRs
            Task.Run(async () =>
            {
                try
                {
                    Trace.WriteLine($"[PerfGuard] Purging transient caches and running targeted async GC under memory pressure: {level}");

                    // 1. Purge transient UI/Page views on Avalonia UI thread and await completion
                    if (MobileShellView.Instance is { } shell)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            shell.PurgeTransientViews();
                        }, DispatcherPriority.Background);
                    }

                    // 2. Targeted Garbage Collection after UI visual tree references are safely unhooked
                    GC.Collect(2, GCCollectionMode.Forced, false, true);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[PerfGuard] Async GC collect error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _isTrimMemoryRunning, 0);
                }

                MemoryPressurePurged?.Invoke();

                if (_showHardwareAlerts)
                {
                    PostAlert(new HardwareAlertInfo(
                        $"Low Memory Pressure ({level}): Purged UI caches & freed RAM",
                        HardwareAlertSeverity.Warning,
                        Material.Icons.MaterialIconKind.Memory,
                        "#D97706"));
                }
            });
        }
    }

    public MemoryMetrics GetMemoryMetrics()
    {
        long availBytes = 0;
        long totalBytes = 0;
        bool lowMem = false;

        if (_activityManager is not null)
        {
            try
            {
                ActivityManager.MemoryInfo info = new();
                _activityManager.GetMemoryInfo(info);
                availBytes = info.AvailMem;
                totalBytes = info.TotalMem;
                lowMem = info.LowMemory;
            }
            catch { }
        }

        long managedBytes = GC.GetTotalMemory(false);
        long javaHeapBytes = 0;
        try
        {
            Java.Lang.Runtime runtime = Java.Lang.Runtime.GetRuntime();
            javaHeapBytes = runtime.TotalMemory() - runtime.FreeMemory();
        }
        catch { }

        long usedBytes = Math.Max(managedBytes + javaHeapBytes, totalBytes - availBytes);

        return new MemoryMetrics(usedBytes, availBytes, totalBytes, lowMem);
    }

    public void ApplyFramePacing()
    {
        int effective = ComputeEffectiveTargetFps();
        TargetFpsChanged?.Invoke(effective);
        FramePacingController.Instance.ApplyWindowRefreshRate();

        try
        {
            // Send target FPS to Ruffle bridge.js
            string json = string.Create(CultureInfo.InvariantCulture, $"{{\"t\":\"targetFps\",\"fps\":{effective}}}");
            Ioc.Default.GetService<RuffleFlashUtil>()?.PostCommand(json);

            // Set Flash stage.frameRate directly
            int flashRate = effective > 0 ? effective : 30;
            Ioc.Default.GetService<RuffleFlashUtil>()?.Call("setGameObject", "stage.frameRate", flashRate);

            Trace.WriteLine($"[PerfGuard] Frame pacing applied: effective={effective} (Target={_targetFps}, Guard={_enablePerformanceGuard}, HeatThrottled={_isThermalThrottled}, DynThrottled={_isDynamicThrottled})");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PerfGuard] ApplyFramePacing error: {ex.Message}");
        }
    }

    private void PostAlert(HardwareAlertInfo alert)
    {
        HardwareAlertTriggered?.Invoke(alert);
        try
        {
            Ioc.Default.GetService<ShellViewModel>()?.PostHardwareAlert(
                alert.Message,
                alert.Icon,
                alert.BrushHex);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PerfGuard] PostAlert error: {ex.Message}");
        }
    }

    private static double ReadBatteryCelsius()
    {
        try
        {
            using Intent? battery = global::Android.App.Application.Context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
            int tenths = battery?.GetIntExtra(BatteryManager.ExtraTemperature, -1) ?? -1;
            return tenths > 0 ? tenths / 10.0 : -1;
        }
        catch
        {
            return -1;
        }
    }

    private sealed class ThermalStatusListener : Java.Lang.Object, PowerManager.IOnThermalStatusChangedListener
    {
        private readonly PerformanceGuard _guard;

        public ThermalStatusListener(PerformanceGuard guard) => _guard = guard;

        public void OnThermalStatusChanged(ThermalStatus status)
        {
            _guard.OnThermalStatusChanged((int)status);
        }
    }
}
