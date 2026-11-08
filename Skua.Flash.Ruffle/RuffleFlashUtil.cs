using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;

namespace Skua.Flash.Ruffle;

/// <summary>
/// <see cref="IFlashUtil"/> backed by Ruffle running in a web view, talking over <see cref="RuffleHost"/>.
/// </summary>
public class RuffleFlashUtil : IFlashUtil
{
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly ConcurrentDictionary<string, long> _lastErrorTrace = new();

    // Every call is a round trip into the game page, answered between game frames (10-30 ms on a
    // phone, against well under 1 ms for desktop Flash). The bot reads the same few values over and
    // over (Player.Playing, LoggedIn, target, state: 170-270 calls a second in a fight), so read-only
    // calls are answered from a short cache. Any other call (attack, skill, set, packet) may change the
    // game and empties it.
    private const long ReadCacheMs = 50;
    private static readonly HashSet<string> ReadOnlyCalls = new(StringComparer.Ordinal)
    {
        "isNull", "isTrue", "isLoggedIn", "isKicked", "getGameObject", "getGameObjectS", "getGameObjectKey",
        "getArrayObject", "getTargetMonster", "canUseSkill", "availableMonsters", "lnkGetValue", "inventoryBagUsedSlots",
        "inventoryMiscSlots", "inventoryMiscUsedSlots", "inventoryHasSpaceFor", "isFavoriteItem"
    };
    // Calls the bot repeats to keep an option applied (re-sent every second whether or not anything
    // changed); they change nothing the bot reads, so they leave the read cache as it is.
    private static readonly HashSet<string> CacheNeutralCalls = new(StringComparer.Ordinal)
    {
        "killLag", "magnetise", "infiniteRange", "skipCutscenes", "catchPackets"
    };

    // Game functions that only read (callGameFunction with these paths does not empty the cache).
    private static readonly HashSet<string> ReadOnlyGameFunctions = new(StringComparer.Ordinal)
    {
        "world.myAvatar.pMC.artLoaded"
    };
    // Reads fetched ahead for a read cycle stay good a little longer: the pass that uses them may
    // first wait on one slow call (a skill use) before reading the rest.
    private const long PrefetchMs = 200;
    private const int MaxPrefetch = 40;
    private readonly ConcurrentDictionary<string, (string? Raw, long At, long Ttl)> _readCache = new();

    private sealed class ReadCycle
    {
        public required string Name;
        public readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        public readonly List<(string Function, string ArgsJson)> Reads = new();
    }

    // Values that only change on a relogin or server switch, read on almost every game packet (the
    // player's name) or watch-loop pass (the server): kept for a few seconds, through other calls.
    private const long StickyMs = 5000;
    private static readonly HashSet<string> StickyReads = new(StringComparer.Ordinal)
    {
        "getGameObjectS|[\"loginInfo.strUsername\"]",
        "getGameObjectS|[\"serverIP\"]"
    };
    private readonly ConcurrentDictionary<string, (string? Raw, long At)> _sticky = new();

    private readonly AsyncLocal<ReadCycle?> _cycle = new();
    private readonly ConcurrentDictionary<string, List<(string Function, string ArgsJson)>> _cycleReads = new();
    private long _statBatches;
    private long _statCalls, _statHits, _statMs, _statStart = Environment.TickCount64;

    public RuffleFlashUtil(IMessenger messenger, Lazy<IScriptManager> manager, RuffleHost host)
    {
        _messenger = messenger;
        _lazyManager = manager;
        Host = host;
        Host.Control.ExternalCall += OnExternalCall;
    }

    public RuffleHost Host { get; }

    public event FlashCallHandler? FlashCall;

    /// <summary>
    /// Raised when the page has to be (re)loaded. The platform view navigates its web view to the URL.
    /// </summary>
    public event Action<string>? PageRequested;

    public void InitializeFlash()
    {
        Host.Start();
        if (!Host.Control.Post("{\"t\":\"reload\"}"))
            PageRequested?.Invoke(Host.PageUrl);
    }

    // MVS used to force four AQW settings on every load (smooth map background on, cached players and
    // crowd mode off, touch mode off) because stock Ruffle's WebGL renderer could not run
    // BitmapData.draw. MVS's Ruffle implements it, so the player's own choices in the game's Options
    // (including its touch controls) are left alone now.
    private void OnExternalCall(string name, string argsJson)
    {
        try
        {
            FlashCall?.Invoke(name, FlashValues.ExternalArgs(argsJson));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
        }
    }

    public string? Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T? Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    public object? Call(string function, Type type, params object[] args)
    {
        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            string argsJson = FlashValues.SerializeArgs(args);
            string? raw;
            long now = Environment.TickCount64;
            if (IsReadOnly(function, args))
            {
                string key = function + "|" + argsJson;
                if (StickyReads.Contains(key))
                {
                    if (_sticky.TryGetValue(key, out var kept) && now - kept.At <= StickyMs)
                    {
                        Interlocked.Increment(ref _statHits);
                        return FlashValues.ConvertResult(FlashValues.ResultToString(kept.Raw), type);
                    }
                    string? fresh = TimedCall(function, argsJson);
                    // An empty answer (logged out, loading) is not kept.
                    if (!string.IsNullOrEmpty(fresh) && fresh != "null" && fresh != "\"\"")
                        _sticky[key] = (fresh, Environment.TickCount64);
                    return FlashValues.ConvertResult(FlashValues.ResultToString(fresh), type);
                }
                if (_cycle.Value is { } cycle && cycle.Reads.Count < MaxPrefetch && cycle.Seen.Add(key))
                    cycle.Reads.Add((function, argsJson));
                if (_readCache.TryGetValue(key, out var hit) && now - hit.At <= hit.Ttl)
                {
                    Interlocked.Increment(ref _statHits);
                    raw = hit.Raw;
                }
                else
                {
                    raw = TimedCall(function, argsJson);
                    _readCache[key] = (raw, Environment.TickCount64, ReadCacheMs);
                    if (_readCache.Count > 512)
                        _readCache.Clear();
                }
            }
            else
            {
                if (!CacheNeutralCalls.Contains(function))
                    _readCache.Clear();
                raw = TimedCall(function, argsJson);
            }
            ReportStats();
            return FlashValues.ConvertResult(FlashValues.ResultToString(raw), type);
        }
        catch (Exception e)
        {
            if (function != "isKicked")
            {
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
                TraceError(function, e);
            }
            return default;
        }
    }

    private static bool IsReadOnly(string function, object[] args) =>
        ReadOnlyCalls.Contains(function)
        || (function == "callGameFunction" && args.Length == 1 && args[0] is string path && ReadOnlyGameFunctions.Contains(path));

    public void BeginReadCycle(string name)
    {
        ReadCycle? last = _cycle.Value;
        if (last is not null && last.Reads.Count > 0)
            _cycleReads[last.Name] = last.Reads;
        _cycle.Value = new ReadCycle { Name = name };
        if (!_cycleReads.TryGetValue(name, out var reads) || reads.Count < 2)
            return;

        long now = Environment.TickCount64;
        List<(string Function, string ArgsJson)> wanted = new(reads.Count);
        foreach (var read in reads)
        {
            if (!(_readCache.TryGetValue(read.Function + "|" + read.ArgsJson, out var hit) && now - hit.At <= hit.Ttl))
                wanted.Add(read);
        }
        if (wanted.Count < 2)
            return;
        try
        {
            long start = Environment.TickCount64;
            var results = Host.Control.CallBatch(wanted);
            Interlocked.Increment(ref _statBatches);
            Interlocked.Increment(ref _statCalls);
            Interlocked.Add(ref _statMs, Environment.TickCount64 - start);
            if (results is null)
                return;
            long at = Environment.TickCount64;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (results[i].Ok)
                    _readCache[wanted[i].Function + "|" + wanted[i].ArgsJson] = (results[i].Raw, at, PrefetchMs);
            }
        }
        catch (Exception e)
        {
            TraceError("batch", e);
        }
    }

    private string? TimedCall(string function, string argsJson)
    {
        long start = Environment.TickCount64;
        try
        {
            return Host.Control.Call(function, argsJson);
        }
        finally
        {
            Interlocked.Increment(ref _statCalls);
            Interlocked.Add(ref _statMs, Environment.TickCount64 - start);
        }
    }

    // Every 30 s in the app log: how many calls reached the game, how long each took, and how many the cache answered.
    private void ReportStats()
    {
        long now = Environment.TickCount64, start = Interlocked.Read(ref _statStart);
        if (now - start < 30_000 || Interlocked.CompareExchange(ref _statStart, now, start) != start)
            return;
        long calls = Interlocked.Exchange(ref _statCalls, 0), hits = Interlocked.Exchange(ref _statHits, 0), ms = Interlocked.Exchange(ref _statMs, 0), batches = Interlocked.Exchange(ref _statBatches, 0);
        double secs = (now - start) / 1000.0;
        if (calls + hits > 0)
            Trace.WriteLine($"[Flash] {calls / secs:0} calls/s to the game, {(calls > 0 ? (double)ms / calls : 0):0.0} ms each, {hits / secs:0}/s from the cache ({batches / secs:0.0}/s read ahead)");
    }

    /// <summary>
    /// The Flash log only keeps the function name; trace why it failed, at most every 30s per function.
    /// </summary>
    private void TraceError(string function, Exception e)
    {
        long now = Environment.TickCount64;
        if (_lastErrorTrace.TryGetValue(function, out long last) && now - last < 30_000)
            return;
        _lastErrorTrace[function] = now;
        Trace.WriteLine($"[Flash] {function} failed: {e.GetType().Name}: {e.Message}");
    }

    public object FromFlashXml(XElement el) => FlashValues.FromFlashXml(el);

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    /// <summary>
    /// Runs JavaScript in the page (fire and forget). Used for player-level settings such as quality.
    /// </summary>
    public bool PostCommand(string json) => Host.Control.Post(json);

    public void Dispose()
    {
        Host.Control.ExternalCall -= OnExternalCall;
    }
}
