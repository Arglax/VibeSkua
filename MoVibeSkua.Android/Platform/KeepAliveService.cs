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
/// screen off or the app in the background. It runs in the first client's process and also holds the
/// army clients' processes: a client in the background is otherwise cached, and Android freezes
/// cached processes, which stops the game. A binding from this foreground service keeps them running.
/// </summary>
[Service(Name = "com.arglax.movibeskua.KeepAliveService", Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class KeepAliveService : Service
{
    private const string ChannelId = "movibeskua.running";
    private const int NotificationId = 4021;
    private const string ActionHold = "com.arglax.movibeskua.action.HOLD_CLIENT";
    private const string ActionRelease = "com.arglax.movibeskua.action.RELEASE_CLIENT";
    private const string SlotExtra = "slot";
    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;
    private readonly Dictionary<int, ClientConnection> _held = new();

    public static bool IsRunning { get; private set; }

    public static void Start(Context context) => StartForeground(context, new Intent(context, typeof(KeepAliveService)));

    /// <summary>Asks the first client's service to keep army client <paramref name="slot"/> running.</summary>
    public static void Hold(Context context, int slot)
    {
        Intent intent = new(context, typeof(KeepAliveService));
        intent.SetAction(ActionHold);
        intent.PutExtra(SlotExtra, slot);
        StartForeground(context, intent);
    }

    /// <summary>Lets army client <paramref name="slot"/> go and ends its process.</summary>
    public static void Release(Context context, int slot)
    {
        Intent intent = new(context, typeof(KeepAliveService));
        intent.SetAction(ActionRelease);
        intent.PutExtra(SlotExtra, slot);
        try
        {
            context.StartService(intent);
        }
        catch (Exception ex)
        {
            // Nothing is running that could hold the client.
            System.Diagnostics.Trace.WriteLine($"[KeepAlive] release {slot}: {ex.Message}");
        }
    }

    public static void Stop(Context context)
    {
        context.StopService(new Intent(context, typeof(KeepAliveService)));
    }

    private static void StartForeground(Context context, Intent intent)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        // Restarted after its process died: take back the army clients that are still running.
        foreach (int slot in Army.RunningClients(this).Keys)
        {
            if (slot > 1)
                HoldClient(slot);
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        int slot = intent?.GetIntExtra(SlotExtra, 0) ?? 0;
        if (intent?.Action == ActionRelease)
        {
            ReleaseClient(slot);
            if (IsRunning)
                return StartCommandResult.Sticky;
            // Started only to let a client go: there is nothing to keep running.
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        CreateChannel();
        Notification notification = BuildNotification();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        AcquireLocks();
        IsRunning = true;
        if (intent?.Action == ActionHold)
            HoldClient(slot);
        return StartCommandResult.Sticky;
    }

    private Notification BuildNotification()
    {
        Intent open = new(this, typeof(MainActivity));
        open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
        PendingIntent? pending = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("MVS is running")
            .SetContentText(_held.Count == 0
                ? "Keeping the game and scripts awake in the background."
                : $"Keeping {_held.Count + 1} game clients and their scripts awake.")
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetOngoing(true)
            .SetContentIntent(pending)
            .SetPriority(NotificationCompat.PriorityLow)
            .Build()!;
    }

    private void UpdateNotification()
    {
        if (IsRunning)
            (GetSystemService(NotificationService) as NotificationManager)?.Notify(NotificationId, BuildNotification());
    }

    private void HoldClient(int slot)
    {
        if (slot < 2 || slot > Army.MaxClients || _held.ContainsKey(slot))
            return;
        ClientConnection connection = new(this, slot);
        if (BindService(new Intent(this, Army.ServiceType(slot)), connection, Bind.AutoCreate | Bind.Important))
        {
            _held[slot] = connection;
            UpdateNotification();
        }
    }

    private void ReleaseClient(int slot)
    {
        if (_held.Remove(slot, out ClientConnection? connection))
        {
            UnbindService(connection);
            UpdateNotification();
        }

        // Its task is gone or going; give the activity a moment to finish, then end the process,
        // which would otherwise stay logged in to the game with nothing showing it.
        if (slot < 2 || !Army.RunningClients(this).TryGetValue(slot, out int pid))
            return;
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            if (!_held.ContainsKey(slot) && Army.RunningClients(this).TryGetValue(slot, out int now) && now == pid)
                global::Android.OS.Process.KillProcess(pid);
        }, 1500);
    }

    private sealed class ClientConnection : Java.Lang.Object, IServiceConnection
    {
        private readonly KeepAliveService _service;
        private readonly int _slot;

        public ClientConnection(KeepAliveService service, int slot)
        {
            _service = service;
            _slot = slot;
        }

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
        }

        // The client's process died. Let go of it, or the binding would start an empty process
        // again; a reopened client asks to be held again.
        public void OnServiceDisconnected(ComponentName? name)
        {
            if (_service._held.Remove(_slot))
            {
                _service.UnbindService(this);
                _service.UpdateNotification();
            }
        }
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
            Description = "Shown while MVS keeps the game running."
        };
        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        foreach (ClientConnection connection in _held.Values)
            UnbindService(connection);
        _held.Clear();
        try { _wakeLock?.Release(); } catch { }
        try { _wifiLock?.Release(); } catch { }
        _wakeLock = null;
        _wifiLock = null;
        base.OnDestroy();
    }
}
