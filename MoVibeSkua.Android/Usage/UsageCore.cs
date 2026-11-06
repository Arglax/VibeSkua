using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MoVibeSkua.Android.Usage;

/// <summary>
/// Counts waiting for the next upload, by id. Repeat hits of one signal inside
/// <see cref="DebounceMs"/> count once, so a double tap is one use.
/// </summary>
public sealed class SignalCounters
{
    public const long DebounceMs = 250;
    public const long BatchCap = 1000;

    private readonly ConcurrentDictionary<string, long> _pending = new();
    private readonly ConcurrentDictionary<string, long> _lastCounted = new();
    private readonly Func<long> _now;

    public SignalCounters(Func<long>? now = null)
    {
        _now = now ?? (() => Environment.TickCount64);
    }

    public bool Track(string id)
    {
        long time = _now();
        if (_lastCounted.TryGetValue(id, out long previous) && time - previous < DebounceMs)
            return false;
        _lastCounted[id] = time;
        _pending.AddOrUpdate(id, 1, (_, count) => Math.Min(count + 1, BatchCap));
        return true;
    }

    public void AddRaw(string id, long delta)
    {
        if (delta > 0)
            _pending.AddOrUpdate(id, delta, (_, count) => count + delta);
    }

    public void Restore(IReadOnlyDictionary<string, long> values)
    {
        foreach ((string id, long count) in values)
            AddRaw(id, count);
    }

    public Dictionary<string, long> Snapshot() => new(_pending);

    public void Subtract(IReadOnlyDictionary<string, long> uploaded)
    {
        foreach ((string id, long count) in uploaded)
        {
            while (_pending.TryGetValue(id, out long current))
            {
                long left = current - count;
                if (left > 0 ? _pending.TryUpdate(id, left, current) : _pending.TryRemove(new KeyValuePair<string, long>(id, current)))
                    break;
            }
        }
    }

    public void Clear()
    {
        _pending.Clear();
        _lastCounted.Clear();
    }
}

/// <summary>
/// Times the screen on show while the app is in the foreground. A finished visit adds its count, its
/// time, a dwell bucket, and whether it was a quick exit or an idle visit (open a while with nothing
/// used), which point at pages people get stuck on.
/// </summary>
public sealed class VisitClock
{
    public const long MinVisitMs = 300;
    public const long QuickExitMs = 4_000;
    public const long IdleMs = 20_000;
    public const long MaxVisitMs = 30L * 60 * 1000;

    private readonly Func<long> _now;
    private readonly object _gate = new();
    private Screen? _screen;
    private bool _inForeground;
    private bool _running;
    private long _startedAt;
    private long _activeMs;
    private int _actions;

    public VisitClock(Func<long>? now = null)
    {
        _now = now ?? (() => Environment.TickCount64);
    }

    public Screen? Current
    {
        get
        {
            lock (_gate)
                return _screen;
        }
    }

    public void Enter(Screen next, Action<string, long> sink)
    {
        lock (_gate)
        {
            FinishLocked(sink);
            _screen = next;
            _activeMs = 0;
            _actions = 0;
            if (_inForeground)
            {
                _running = true;
                _startedAt = _now();
            }
        }
    }

    public void Foreground()
    {
        lock (_gate)
        {
            _inForeground = true;
            if (_screen is not null && !_running)
            {
                _running = true;
                _startedAt = _now();
            }
        }
    }

    public void Background()
    {
        lock (_gate)
        {
            _inForeground = false;
            if (_running)
            {
                _activeMs += _now() - _startedAt;
                _running = false;
            }
        }
    }

    public void Action()
    {
        lock (_gate)
            _actions++;
    }

    public void Finish(Action<string, long> sink)
    {
        lock (_gate)
            FinishLocked(sink);
    }

    private void FinishLocked(Action<string, long> sink)
    {
        if (_screen is not { } current)
            return;
        if (_running)
        {
            _activeMs += _now() - _startedAt;
            _running = false;
        }
        long ms = Math.Clamp(_activeMs, 0, MaxVisitMs);
        bool acted = _actions > 0;
        _screen = null;
        _activeMs = 0;
        _actions = 0;
        if (ms < MinVisitMs && !acted)
            return;

        string prefix = "scr." + current + ".";
        sink(prefix + "visits", 1);
        sink(prefix + "ms", ms);
        sink(prefix + (ms < 5_000 ? "d5" : ms < 15_000 ? "d15" : ms < 60_000 ? "d60" : ms < 180_000 ? "d180" : "dmax"), 1);
        if (!acted && ms < QuickExitMs)
            sink(prefix + "quick", 1);
        if (!acted && ms >= IdleMs)
            sink(prefix + "idle", 1);
    }
}

/// <summary>
/// Time per UTC day for one kind of time ("fg" = app on screen, "run" = process alive, "bot" = a script
/// or Auto running). Start begins counting; Flush adds the time so far to "day.dYYYYMMDD.kind" (split
/// at UTC midnight) and keeps counting; Stop flushes and stops. Uploads go to days.dYYYYMMDD.kind.
/// </summary>
public sealed class DayClock
{
    private readonly string _kind;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private DateTime? _since;

    public DayClock(string kind, Func<DateTime>? utcNow = null)
    {
        _kind = kind;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool Running
    {
        get
        {
            lock (_gate)
                return _since is not null;
        }
    }

    public static string Key(DateTime utcDay, string kind) => $"day.d{utcDay:yyyyMMdd}.{kind}";

    public void Start()
    {
        lock (_gate)
            _since ??= _utcNow();
    }

    public void Stop(Action<string, long> sink)
    {
        lock (_gate)
        {
            FlushLocked(sink);
            _since = null;
        }
    }

    public void Flush(Action<string, long> sink)
    {
        lock (_gate)
            FlushLocked(sink);
    }

    private void FlushLocked(Action<string, long> sink)
    {
        if (_since is not { } from)
            return;
        DateTime now = _utcNow();
        _since = now;
        // A clock set backwards adds nothing rather than a negative time.
        if (now <= from)
            return;
        while (from.Date < now.Date)
        {
            DateTime midnight = from.Date.AddDays(1);
            sink(Key(from.Date, _kind), (long)(midnight - from).TotalMilliseconds);
            from = midnight;
        }
        sink(Key(from.Date, _kind), (long)(now - from).TotalMilliseconds);
    }
}

/// <summary>
/// The ids the Firestore rules accept. Counts saved by an older version under a name that has since
/// gone would make the rules refuse every upload, so they are dropped instead.
/// </summary>
public static class UsageIds
{
    public static readonly string[] ScreenFields = { "visits", "ms", "quick", "idle", "d5", "d15", "d60", "d180", "dmax" };

    private static readonly HashSet<string> Known = Enum.GetNames<Signal>()
        .Concat(Enum.GetNames<Screen>().SelectMany(screen => ScreenFields.Select(field => $"scr.{screen}.{field}")))
        .ToHashSet(StringComparer.Ordinal);

    private static readonly System.Text.RegularExpressions.Regex DayId = new(@"^day\.d\d{8}\.(fg|run|bot)$");

    public static bool IsKnown(string id) => Known.Contains(id) || DayId.IsMatch(id);
}
