using System;
using System.Globalization;
using Android.Content;
using Android.OS;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// Watches how hot the phone is and tells the game page, which warns the player and lets the frame
/// governor lower graphics before Android throttles the phone. Uses Android's thermal status (API 29+),
/// its thermal headroom forecast (API 30+) and the battery temperature (all versions).
/// </summary>
public static class ThermalMonitor
{
    private static global::Android.OS.Handler? _handler;
    private static PowerManager? _power;
    private static string _lastJson = string.Empty;
    private const int PollMs = 10_000;

    public static void Start()
    {
        if (_handler is not null)
            return;
        Context context = global::Android.App.Application.Context;
        _power = context.GetSystemService(Context.PowerService) as PowerManager;
        _handler = new global::Android.OS.Handler(Looper.MainLooper!);
        if (_power is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
            _power.AddThermalStatusListener(new StatusListener());
        _handler.Post(Poll);
    }

    private static void Poll()
    {
        try
        {
            Report();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Thermal] {ex.Message}");
        }
        _handler?.PostDelayed(Poll, PollMs);
    }

    /// <summary>
    /// status: 0 none, 1 light, 2 moderate, 3 severe, 4 critical, 5 emergency, 6 shutdown (-1 unknown).
    /// headroom: Android's forecast for the next 10 s; 1.0 is where it starts throttling (-1 unknown).
    /// </summary>
    private static void Report()
    {
        int status = -1;
        double headroom = -1;
        if (_power is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
            status = (int)_power.CurrentThermalStatus;
        if (_power is not null && OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            float h = _power.GetThermalHeadroom(10);
            if (!float.IsNaN(h))
                headroom = h;
        }
        double battery = BatteryCelsius();
        PerformanceMode.OnTemperature(battery, status);
        PerformanceGuard.Instance.UpdateThermalMetrics(status, headroom, battery);
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":\"thermal\",\"status\":{status},\"headroom\":{headroom:0.00},\"battery\":{battery:0.0}}}");
        if (json == _lastJson)
            return;
        _lastJson = json;
        Ioc.Default.GetService<Skua.Flash.Ruffle.RuffleFlashUtil>()?.PostCommand(json);
        System.Diagnostics.Trace.WriteLine($"[Thermal] status {status}, headroom {headroom:0.00}, battery {battery:0.0} C");
    }

    private static double BatteryCelsius()
    {
        using Intent? battery = global::Android.App.Application.Context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        int tenths = battery?.GetIntExtra(BatteryManager.ExtraTemperature, -1) ?? -1;
        return tenths > 0 ? tenths / 10.0 : -1;
    }

    private sealed class StatusListener : Java.Lang.Object, PowerManager.IOnThermalStatusChangedListener
    {
        public void OnThermalStatusChanged(ThermalStatus status) => _handler?.Post(Report);
    }
}
