using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Foreground service that keeps the process, CPU and Wi-Fi awake so scripts keep farming with the
/// screen off or the app in the background.
/// </summary>
[Service(Name = "com.arglax.movibeskua.KeepAliveService", Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class KeepAliveService : Service
{
    private const string ChannelId = "movibeskua.running";
    private const int NotificationId = 4021;
    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;

    public static bool IsRunning { get; private set; }

    public static void Start(Context context)
    {
        Intent intent = new(context, typeof(KeepAliveService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void Stop(Context context)
    {
        context.StopService(new Intent(context, typeof(KeepAliveService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateChannel();
        Intent open = new(this, typeof(MainActivity));
        open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
        PendingIntent? pending = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        Notification notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("MoVibeSkua is running")
            .SetContentText("Keeping the game and scripts awake in the background.")
            .SetSmallIcon(Resource.Drawable.icon)
            .SetOngoing(true)
            .SetContentIntent(pending)
            .SetPriority(NotificationCompat.PriorityLow)
            .Build()!;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        AcquireLocks();
        IsRunning = true;
        return StartCommandResult.Sticky;
    }

    private void AcquireLocks()
    {
        if (_wakeLock is null && GetSystemService(PowerService) is PowerManager power)
        {
            _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "MoVibeSkua:bot");
            _wakeLock?.Acquire();
        }

        if (_wifiLock is null && ApplicationContext?.GetSystemService(WifiService) is WifiManager wifi)
        {
#pragma warning disable CA1422
            _wifiLock = wifi.CreateWifiLock(global::Android.Net.WifiMode.FullHighPerf, "MoVibeSkua:net");
#pragma warning restore CA1422
            _wifiLock?.Acquire();
        }
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;
        NotificationChannel channel = new(ChannelId, "Running in background", NotificationImportance.Low)
        {
            Description = "Shown while MoVibeSkua keeps the game running."
        };
        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        try { _wakeLock?.Release(); } catch { }
        try { _wifiLock?.Release(); } catch { }
        _wakeLock = null;
        _wifiLock = null;
        base.OnDestroy();
    }
}
