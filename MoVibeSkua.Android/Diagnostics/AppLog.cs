using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>
/// The app's own log: everything MVS and the bot engine trace (start-up, the game page, Flash calls,
/// heat, scripts' log lines), written to files/logs/mvs-YYYYMMDD[-cN].log and kept for five days.
/// Nothing of it is shown on screen; problem reports (<see cref="ProblemReports"/>) attach its last lines.
/// </summary>
public sealed class AppLog : TraceListener
{
    private const int KeepDays = 5;
    private const long MaxFileBytes = 4 * 1024 * 1024;
    // About 10-20 minutes of a busy run: reports take the last few minutes from it (ReportLog).
    private const int RecentLines = 3000;

    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static StreamWriter? _writer;
    private static string _day = string.Empty;
    private static int _part;

    public static string Folder => Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "logs");

    public static void Start()
    {
        if (Trace.Listeners.OfType<AppLog>().Any())
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (string old in Directory.GetFiles(Folder, "mvs-*.log"))
            {
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-KeepDays))
                    File.Delete(old);
            }
        }
        catch
        {
            // The log is a help, never a reason to fail.
        }
        Trace.Listeners.Add(new AppLog());
        Trace.WriteLine($"[App] MVS {AppVersion()} on {(global::Android.OS.Build.Manufacturer)} {(global::Android.OS.Build.Model)}, Android {(global::Android.OS.Build.VERSION.Release)} (client {Platform.Army.Slot})");
    }

    /// <summary>A line from the bot's Logs (script and Flash lines), which do not go through Trace.</summary>
    public static void Add(string line) => Append(line);

    public override void Write(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Append(message);
    }

    public override void WriteLine(string? message) => Write(message);

    private static void Append(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Watch(message);
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > RecentLines)
                Recent.Dequeue();
            try
            {
                Writer()?.WriteLine(line);
            }
            catch
            {
                _writer = null;
            }
        }
    }

    private static StreamWriter? Writer()
    {
        string day = DateTime.Now.ToString("yyyyMMdd");
        if (_writer is not null && day == _day && _writer.BaseStream.Length < MaxFileBytes)
            return _writer;
        _writer?.Dispose();
        _part = day == _day ? _part + 1 : 0;
        _day = day;
        string client = Platform.Army.IsFirst ? string.Empty : $"-c{Platform.Army.Slot}";
        string part = _part > 0 ? $"-{_part}" : string.Empty;
        _writer = new StreamWriter(new FileStream(Path.Combine(Folder, $"mvs-{day}{client}{part}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true
        };
        return _writer;
    }

    // ---------------- Measurements and engine crashes ----------------

    private static readonly Dictionary<string, string> Latest = new();

    // The newest line of each kind of measurement, for a report's context block.
    private static readonly (string Prefix, string Key)[] Measured =
    {
        ("[Ruffle] memory:", "memory"),
        ("[Ruffle] governor:", "frame rate"),
        ("[Thermal]", "heat"),
        ("[Flash] ", "game calls"),
        ("[RuffleHost] proxy", "last download failure"),
        ("[RuffleHost] server list fetch failed", "last download failure"),
        ("[Ruffle] page error:", "last page error"),
        ("[WebView] ERROR: panicked", "last engine panic"),
        ("[Script] [Relogin]", "last re-login line"),
        ("[Page]", "last page"),
    };

    private static void Watch(string message)
    {
        foreach ((string prefix, string key) in Measured)
        {
            if (!message.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (key == "game calls" && !message.Contains("calls/s", StringComparison.Ordinal))
                continue;
            lock (Latest)
                Latest[key] = $"{DateTime.Now:HH:mm:ss} {(message.Length > 220 ? message[..220] : message)}";
            break;
        }
        // The game engine (Ruffle) crashed: bridge.js reloads the game, which used to leave no report.
        if (message.StartsWith("[Ruffle] game engine crashed", StringComparison.Ordinal))
        {
            string why;
            lock (Latest)
                why = Latest.TryGetValue("last engine panic", out string? panic) ? panic : Latest.GetValueOrDefault("last page error") ?? "";
            int at = why.IndexOf(' ');
            why = at > 0 ? why[(at + 1)..] : why;
            System.Threading.Tasks.Task.Run(() => ProblemReports.Save("crash", "mvs", null,
                "Game engine crashed: " + (why.Length > 0 ? why : message), RunInfo.ScriptName, fatal: false));
        }
    }

    /// <summary>The newest measurement lines (memory, frame rate, heat, game calls, last failures).</summary>
    public static string MeasurementsText()
    {
        lock (Latest)
            return string.Join("\n", Latest.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
    }

    // Lines a report leaves out: repeated polling with nothing in it, and chat.
    private static readonly Regex Noise = new(@"\[Flash\] \w+ Args\[\d*\]\s*$|isLoggedIn failed: FlashCallException|%xt%chatm|%xt%whisper|\bchatm\b", RegexOptions.Compiled);

    /// <summary>
    /// The log for a report: the last <paramref name="minutes"/> minutes (at least the last 80 lines),
    /// without polling noise or chat, runs of the same line folded into one, newest kept when over
    /// <paramref name="maxChars"/>, and redacted.
    /// </summary>
    public static string ReportLog(int minutes = 5, int maxChars = 22000)
    {
        string[] copy;
        lock (Gate)
            copy = Recent.ToArray();
        string since = DateTime.Now.AddMinutes(-minutes).ToString("HH:mm:ss.fff");
        bool sameDay = DateTime.Now.AddMinutes(-minutes).Date == DateTime.Now.Date;
        List<string> lines = new();
        string? last = null;
        int repeats = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            string line = copy[i];
            bool recent = copy.Length - i <= 80 || !sameDay || string.CompareOrdinal(line, since) >= 0;
            if (!recent)
                continue;
            string body = line.Length > 13 ? line[13..] : line;
            if (Noise.IsMatch(body))
                continue;
            if (body == last)
            {
                repeats++;
                continue;
            }
            if (repeats > 0)
                lines.Add($"             (same line {repeats} more time{(repeats == 1 ? "" : "s")})");
            repeats = 0;
            last = body;
            lines.Add(line);
        }
        if (repeats > 0)
            lines.Add($"             (same line {repeats} more time{(repeats == 1 ? "" : "s")})");
        string text = Redact(string.Join("\n", lines));
        return text.Length > maxChars ? "(older lines cut)\n" + text[^maxChars..] : text;
    }

    /// <summary>
    /// The last lines the previous run wrote to its log file: after a crash that killed the app before it
    /// could save anything (a native crash, Android closing it), the only record of what led up to it.
    /// </summary>
    public static string PreviousRunTail(DateTime diedAtUtc, int maxChars = 20000)
    {
        try
        {
            string? file = Directory.GetFiles(Folder, "mvs-*.log")
                .Where(f => File.GetLastWriteTimeUtc(f) <= diedAtUtc.AddMinutes(2))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (file is null)
                return string.Empty;
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - maxChars * 2);
            stream.Seek(start, SeekOrigin.Begin);
            using StreamReader reader = new(stream, Encoding.UTF8);
            string text = reader.ReadToEnd();
            string[] lines = text.Split('\n').Skip(start > 0 ? 1 : 0).Where(l => !Noise.IsMatch(l)).ToArray();
            string tail = Redact(string.Join("\n", lines));
            return tail.Length > maxChars ? tail[^maxChars..] : tail;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The last lines, with account names, passwords and tokens taken out.</summary>
    public static string RecentText(int lines = 150) => Redact(RecentRaw(lines));

    /// <summary>The last lines as written (redact before they leave the phone).</summary>
    public static string RecentRaw(int lines = 150)
    {
        string[] copy;
        lock (Gate)
            copy = Recent.Skip(Math.Max(0, Recent.Count - lines)).ToArray();
        return string.Join("\n", copy);
    }

    private static readonly Regex Secrets = new(
        @"(?i)(token|password|pass|pwd|key|auth|session|ccid)(["":=\s]+)[^\s,""&;}]+",
        RegexOptions.Compiled);

    private static readonly Regex Emails = new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);

    /// <summary>
    /// Takes out what could identify the player: saved account names and passwords, the logged-in
    /// name, e-mail addresses and anything that looks like a token or password.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        foreach (string secret in KnownSecrets())
        {
            if (secret.Length >= 3)
                text = text.Replace(secret, "<account>", StringComparison.OrdinalIgnoreCase);
        }
        text = Emails.Replace(text, "<email>");
        return Secrets.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + "<hidden>");
    }

    private static string? _lastName;

    /// <summary>Remembers the logged-in name so a report written during a crash can still hide it.</summary>
    public static void RememberName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _lastName = name.Trim();
    }

    private static IEnumerable<string> KnownSecrets()
    {
        if (_lastName is not null)
            yield return _lastName;
        List<Platform.SavedAccount> saved;
        try
        {
            saved = Platform.SavedAccounts.Load();
        }
        catch
        {
            yield break;
        }
        foreach (Platform.SavedAccount account in saved)
        {
            yield return account.Username;
            yield return account.Password;
        }
    }

    public static string AppVersion()
    {
        try
        {
            global::Android.Content.Context context = global::Android.App.Application.Context;
            var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
            long code = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
            return $"{info.VersionName} ({code})";
        }
        catch
        {
            return "unknown";
        }
    }
}
