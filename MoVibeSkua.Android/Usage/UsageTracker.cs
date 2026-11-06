using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.Res;
using CommunityToolkit.Mvvm.Messaging;
using MoVibeSkua.Android.Platform;
using Skua.Core.Messaging;

namespace MoVibeSkua.Android.Usage;

/// <summary>
/// Anonymous usage counts: how often each function is used and how long each page stays open, with
/// the app version and a two-letter country. Never names, accounts, scripts, maps, typed text or
/// device details. Every entry point swallows its own failures, so counting can never break the app.
/// Each process (each army client) counts on its own, keeps its counts in a file of its own until
/// they are uploaded, and uploads them to the same install record.
/// </summary>
public static class UsageTracker
{
    private const int SaveEvery = 5;
    private static readonly TimeSpan FirstUpload = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UploadEvery = TimeSpan.FromMinutes(30);
    // Leaving the app uploads too, but switching between army clients should not upload every time.
    private static readonly TimeSpan BackgroundUploadGap = TimeSpan.FromMinutes(5);

    private static readonly SignalCounters Counters = new();
    private static readonly VisitClock Clock = new();
    // Time per day: on screen, process alive (army clients and screen-off botting too), botting.
    private static readonly DayClock Foreground = new("fg");
    private static readonly DayClock Alive = new("run");
    private static readonly DayClock Botting = new("bot");
    private static bool _scriptRunning;
    private static bool _autoRunning;
    private static readonly object FileGate = new();
    private static readonly SemaphoreSlim UploadGate = new(1, 1);
    private static readonly object Recipient = new();
    private static int _sinceSave;
    private static bool _enabled = true;
    private static bool _loaded;
    private static bool _started;
    private static Screen _screen = Screen.GAME;
    private static long _lastUpload;

    private static string PendingPath => Path.Combine(UsageSync.Folder, $"pending-{Army.Slot}.json");

    public static void Start(Context context)
    {
        try
        {
            if (_started)
                return;
            _started = true;
            if (!UsageSync.Configured)
            {
                _enabled = false;
                Counters.Clear();
                return;
            }

            Load();
            HookCrashes();
            HookMessages();
            HookAuto();
            Alive.Start();
            if (Army.IsFirst)
            {
                Track(Signal.SYS_SESSION_START);
            }
            else
            {
                Track(Signal.ARMY_CLIENT_START);
                Track(ArmySize(Army.RunningClients(context).Count));
            }
            Track(context.Resources?.Configuration?.Orientation == Orientation.Portrait ? Signal.SYS_LAYOUT_PORTRAIT : Signal.SYS_LAYOUT_LANDSCAPE);
            EnterScreen(_screen);
            _ = Task.Run(UploadLoop);
        }
        catch
        {
        }
    }

    public static void Track(Signal signal)
    {
        try
        {
            if (!_enabled)
                return;
            if (Counters.Track(signal.ToString()))
                Clock.Action();
            if (Interlocked.Increment(ref _sinceSave) >= SaveEvery)
                Save();
        }
        catch
        {
        }
    }

    public static void EnterScreen(Screen screen)
    {
        try
        {
            _screen = screen;
            if (_enabled)
                Clock.Enter(screen, Counters.AddRaw);
        }
        catch
        {
        }
    }

    public static void OnForeground()
    {
        try
        {
            Foreground.Start();
            Clock.Foreground();
            // Leaving the app ended the visit; coming back starts a new one on the same page.
            if (_enabled && _started && Clock.Current is null)
                Clock.Enter(_screen, Counters.AddRaw);
        }
        catch
        {
        }
    }

    public static void OnBackground()
    {
        try
        {
            Clock.Background();
            if (!_enabled || !_started)
            {
                Foreground.Stop((_, _) => { });
                return;
            }
            Foreground.Stop(Counters.AddRaw);
            FlushTime();
            Clock.Finish(Counters.AddRaw);
            Save();
            if (Environment.TickCount64 - _lastUpload > BackgroundUploadGap.TotalMilliseconds)
                _ = Task.Run(UploadAsync);
        }
        catch
        {
        }
    }

    private static Signal ArmySize(int clients) => clients switch
    {
        <= 2 => Signal.ARMY_SIZE_2,
        3 => Signal.ARMY_SIZE_3,
        4 => Signal.ARMY_SIZE_4,
        <= 7 => Signal.ARMY_SIZE_5_7,
        _ => Signal.ARMY_SIZE_8_PLUS
    };

    private static async Task UploadLoop()
    {
        await Task.Delay(FirstUpload);
        while (true)
        {
            await UploadAsync();
            await Task.Delay(UploadEvery);
        }
    }

    private static async Task UploadAsync()
    {
        if (!await UploadGate.WaitAsync(0))
            return;
        try
        {
            FlushTime();
            Dictionary<string, long> snapshot = Counters.Snapshot();
            Dictionary<string, long> stale = snapshot.Where(c => !UsageIds.IsKnown(c.Key)).ToDictionary(c => c.Key, c => c.Value);
            if (stale.Count > 0)
            {
                Counters.Subtract(stale);
                foreach (string id in stale.Keys)
                    snapshot.Remove(id);
            }
            if (snapshot.Count == 0)
                return;

            if (await UsageSync.UploadAsync(snapshot))
            {
                Counters.Subtract(snapshot);
                Save();
            }
            _lastUpload = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            // Offline: the counts stay for the next try.
            System.Diagnostics.Trace.WriteLine($"[Usage] upload failed: {ex.Message}");
        }
        finally
        {
            UploadGate.Release();
        }
    }

    private static void Load()
    {
        lock (FileGate)
        {
            if (_loaded)
                return;
            try
            {
                if (File.Exists(PendingPath) && JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(PendingPath)) is { } saved)
                    Counters.Restore(saved);
            }
            catch
            {
            }
            _loaded = true;
        }
    }

    private static void Save()
    {
        lock (FileGate)
        {
            // Saving before the file was read would overwrite the counts in it.
            if (!_loaded)
                return;
            Interlocked.Exchange(ref _sinceSave, 0);
            try
            {
                Directory.CreateDirectory(UsageSync.Folder);
                string temp = PendingPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Counters.Snapshot()));
                File.Move(temp, PendingPath, true);
            }
            catch
            {
            }
        }
    }

    private static void HookCrashes()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, _) => OnCrash();
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, _) => OnCrash();
    }

    // The process is going down: count it and keep the counts for the next start.
    private static void OnCrash()
    {
        try
        {
            Counters.Track(nameof(Signal.SYS_CRASH));
            FlushTime();
            Clock.Finish(Counters.AddRaw);
            Save();
        }
        catch
        {
        }
    }

    // Adds the running time so far to today's counts; the clocks keep running.
    private static void FlushTime()
    {
        if (!_enabled)
            return;
        Foreground.Flush(Counters.AddRaw);
        Alive.Flush(Counters.AddRaw);
        Botting.Flush(Counters.AddRaw);
    }

    private static void UpdateBotting()
    {
        try
        {
            if (_scriptRunning || _autoRunning)
                Botting.Start();
            else if (_enabled)
                Botting.Stop(Counters.AddRaw);
        }
        catch
        {
        }
    }

    private static void HookAuto()
    {
        if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Skua.Core.Interfaces.IScriptAuto>() is not { } auto
            || auto is not System.ComponentModel.INotifyPropertyChanged notify)
            return;
        notify.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Skua.Core.Interfaces.IScriptAuto.IsRunning) || auto.IsRunning == _autoRunning)
                return;
            _autoRunning = auto.IsRunning;
            if (_autoRunning)
                Track(auto.IsHunting ? Signal.AUTO_HUNT_START : Signal.AUTO_ATTACK_START);
            UpdateBotting();
        };
    }

    private static void HookMessages()
    {
        IMessenger messenger = StrongReferenceMessenger.Default;
        int scripts = (int)MessageChannels.ScriptStatus;
        int game = (int)MessageChannels.GameEvents;
        messenger.Register<object, ScriptStartedMessage, int>(Recipient, scripts, (_, _) =>
        {
            Track(Signal.SCRIPT_STARTED);
            _scriptRunning = true;
            UpdateBotting();
        });
        messenger.Register<object, ScriptStoppedMessage, int>(Recipient, scripts, (_, _) =>
        {
            Track(Signal.SCRIPT_STOPPED);
            _scriptRunning = false;
            UpdateBotting();
        });
        messenger.Register<object, ScriptErrorMessage, int>(Recipient, scripts, (_, _) => Track(Signal.SCRIPT_ERROR));
        messenger.Register<object, LoginMessage, int>(Recipient, game, (_, _) => Track(Signal.LOGIN_DONE));
        messenger.Register<object, ReloginTriggeredMessage, int>(Recipient, game, (_, m) => Track(m.WasKicked ? Signal.RELOGIN_KICKED : Signal.RELOGIN));
    }
}

/// <summary>
/// Counts a use, then runs the wrapped command (the shell's rail buttons and menu entries).
/// </summary>
public sealed class CountedCommand : System.Windows.Input.ICommand
{
    private readonly Signal _signal;
    private readonly System.Windows.Input.ICommand _inner;

    public CountedCommand(Signal signal, System.Windows.Input.ICommand inner)
    {
        _signal = signal;
        _inner = inner;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => _inner.CanExecuteChanged += value;
        remove => _inner.CanExecuteChanged -= value;
    }

    public bool CanExecute(object? parameter) => _inner.CanExecute(parameter);

    public void Execute(object? parameter)
    {
        UsageTracker.Track(_signal);
        _inner.Execute(parameter);
    }
}
