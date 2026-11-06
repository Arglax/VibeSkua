using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Flash.Ruffle;

namespace MoVibeSkua.Android.Services;

/// <summary>
/// The small script window drawn over the game (bridge.js "scriptStatus"): which script runs, for how
/// long, its status, where the player is and the script's latest log line. The page draws it because
/// app controls cannot cover the game's WebView; it can be collapsed to a pill or hidden there.
/// </summary>
public sealed class ScriptStatusOverlay
{
    private static ScriptStatusOverlay? _instance;

    // CoreBots' Logger writes "[12:34:56] (Caller) message".
    private static readonly Regex LoggerLine = new(@"^\[\d{1,2}:\d{2}:\d{2}\]\s*(?:\((?<caller>[^)]*)\)\s*)?(?<text>.*)$", RegexOptions.Compiled);

    private readonly IScriptManager _scripts;
    private readonly IScriptInterface _bot;
    private readonly RuffleFlashUtil _flash;
    private readonly Timer _timer;
    private bool _wasRunning;
    private DateTime _started;
    private DateTime? _ended;
    private TimeSpan _ranFor;
    private string _name = "";
    private volatile string _task = "";
    private int _runId;
    private string _lastSent = "";
    private int _sinceSent;

    private ScriptStatusOverlay()
    {
        _scripts = Ioc.Default.GetRequiredService<IScriptManager>();
        _bot = Ioc.Default.GetRequiredService<IScriptInterface>();
        _flash = Ioc.Default.GetRequiredService<RuffleFlashUtil>();
        WeakReferenceMessenger.Default.Register<ScriptStatusOverlay, AddLogMessage>(this, static (r, m) =>
        {
            if (m.LogType == LogType.Script && !string.IsNullOrWhiteSpace(m.Text))
                r._task = m.Text.Trim();
        });
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    public static void Start()
    {
        try
        {
            _instance ??= new ScriptStatusOverlay();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ScriptStatus] {ex.Message}");
        }
    }

    private void Tick()
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            bool running = _scripts.ScriptRunning;
            if (running && !_wasRunning)
            {
                _started = now;
                _ended = null;
                _runId++;
                _task = "";
                string path = _scripts.LoadedScript ?? "";
                _name = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) : "Script";
            }
            else if (!running && _wasRunning)
            {
                _ended = now;
                _ranFor = now - _started;
            }
            _wasRunning = running;

            // A finished run stays on screen for a while so its last line can be read.
            bool show = running || (_ended is { } end && now - end < TimeSpan.FromSeconds(30));
            string where = "";
            if (show)
            {
                try
                {
                    if (_bot.Player.LoggedIn)
                        where = $"{_bot.Map.Name} · {_bot.Player.Cell}";
                }
                catch
                {
                }
            }

            (string caller, string text) = SplitTask(_task);
            string status = running
                ? (_scripts.ShouldExit ? "Stopping" : _scripts.ScriptPaused ? "Paused" : "Running")
                : "Finished";
            int secs = (int)(running ? now - _started : _ranFor).TotalSeconds;
            string json = JsonSerializer.Serialize(new
            {
                t = "scriptStatus",
                show,
                run = running,
                id = _runId,
                name = _name,
                status,
                secs,
                caller,
                task = text,
                where
            });
            // Sent when something changed, and every few seconds so a reloaded page shows it again.
            if (json == _lastSent && ++_sinceSent < 5)
                return;
            _lastSent = json;
            _sinceSent = 0;
            _flash.PostCommand(json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ScriptStatus] {ex.Message}");
        }
    }

    private static (string caller, string text) SplitTask(string line)
    {
        if (line.Length == 0)
            return ("", "");
        Match m = LoggerLine.Match(line);
        return m.Success ? (m.Groups["caller"].Value, m.Groups["text"].Value) : ("", line);
    }
}
