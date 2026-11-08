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
    private const int RecentLines = 300;

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
            return $"{info.VersionName} ({info.LongVersionCode})";
        }
        catch
        {
            return "unknown";
        }
    }
}
