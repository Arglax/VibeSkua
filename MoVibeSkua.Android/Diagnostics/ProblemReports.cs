using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>
/// Problem reports for the MVS developer: app crashes, errors inside MVS, and scripts that stopped
/// because of MVS (not because of the script, see <see cref="ScriptProblems"/>). Each report is first
/// saved to files/reports (so one written during a crash survives it), then sent to the movibeskua
/// Firebase project (collection movibeskua_reports) when the phone is online, with account names,
/// passwords and tokens taken out. At most 20 a day, and the same problem only once a day.
/// Sent only when the player chose to (<see cref="Mode"/>, asked the first time a problem happens);
/// each carries a context block (script, run time, memory, heat, network, Ruffle build) above its log.
/// </summary>
public static class ProblemReports
{
    public const string Collection = "movibeskua_reports";
    private const int DailyLimit = 20;
    private const int MaxStack = 12000;
    private const int MaxLog = 24000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static int _uploading;

    private static Context Context => global::Android.App.Application.Context;

    private static string Folder => Path.Combine(Context.FilesDir!.AbsolutePath, "reports");

    private static string SentPath => Path.Combine(Folder, "sent.json");

    /// <summary>What the player chose for error reports. Until they choose, reports stay on the phone.</summary>
    public enum SendMode { NotAsked, Auto, Ask, Never }

    private static ISharedPreferences? Prefs => Context.GetSharedPreferences("movibeskua.shell", FileCreationMode.Private);

    public static SendMode Mode
    {
        get
        {
            ISharedPreferences? prefs = Prefs;
            string? mode = prefs?.GetString("reports.mode", null);
            if (mode is not null && Enum.TryParse(mode, out SendMode parsed))
                return parsed;
            // Before the choice existed reports were on unless switched off: an explicit "off" stays off,
            // everyone else is asked.
            return prefs is not null && prefs.Contains("reports.send") && !prefs.GetBoolean("reports.send", true) ? SendMode.Never : SendMode.NotAsked;
        }
        set
        {
            Prefs?.Edit()?.PutString("reports.mode", value.ToString())?.Apply();
            Trace.WriteLine($"[Report] sending: {value}");
            if (value == SendMode.Auto)
                _ = Task.Run(UploadPendingAsync);
        }
    }

    /// <summary>Reports are sent without asking (the Support page's switch).</summary>
    public static bool SendEnabled
    {
        get => Mode == SendMode.Auto;
        set => Mode = value ? SendMode.Auto : SendMode.Never;
    }

    /// <summary>Reports saved on the phone and not sent yet.</summary>
    public static int PendingCount
    {
        get
        {
            try
            {
                return Directory.Exists(Folder) ? Directory.GetFiles(Folder, "2*.json").Length : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Catches crashes and unobserved errors; sends what an earlier run left behind.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Save("crash", "mvs", ex, null, null, fatal: true);
        };
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            Save("crash", "mvs", e.Exception, null, null, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Background work that failed and nobody waited for: an MVS error, not a crash.
            if (e.Exception.InnerExceptions.All(IsNoise))
                return;
            Save("error", "mvs", e.Exception.GetBaseException(), null, null, fatal: false);
        };
        _ = Task.Run(async () =>
        {
            CollectExitRecords();
            await Task.Delay(TimeSpan.FromSeconds(20));
            await UploadPendingAsync();
        });
    }

    /// <summary>
    /// Crashes the app could not report itself: a native crash (the .NET runtime or the WebView aborting
    /// kills the process before any handler runs) or Android ending a frozen app. Android keeps a record
    /// of why each run ended; on the next start each new one becomes a report with the last lines the
    /// dead run wrote to its log file.
    /// </summary>
    private static void CollectExitRecords()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return;
        try
        {
            ActivityManager? am = Context.GetSystemService(Context.ActivityService) as ActivityManager;
            var records = am?.GetHistoricalProcessExitReasons(Context.PackageName, 0, 8);
            if (records is null || records.Count == 0)
                return;
            long seen = Prefs?.GetLong("exit.seen", 0) ?? 0;
            long newest = seen;
            foreach (ApplicationExitInfo info in records)
            {
                if (info.Timestamp <= seen)
                    continue;
                newest = Math.Max(newest, info.Timestamp);
                if (seen == 0)
                    continue; // First run with this: only remember where to start.
                // Managed crashes report themselves; these can't.
                int reason = (int)info.Reason;
                string? what = reason == (int)ApplicationExitInfoReason.CrashNative ? $"native crash (signal {info.Status})"
                    : reason == (int)ApplicationExitInfoReason.Anr ? "app not responding: Android closed it"
                    : reason == (int)ApplicationExitInfoReason.LowMemory ? "closed by Android for memory"
                    : null;
                if (what is null)
                    continue;
                DateTime died = DateTimeOffset.FromUnixTimeMilliseconds(info.Timestamp).UtcDateTime;
                string clues = TraceClues(info);
                string summary = $"MVS closed unexpectedly: {what}" + (string.IsNullOrEmpty(info.Description) ? "" : $" ({info.Description})")
                                 + (clues.Length > 0 ? $": {clues.Split('\n')[0]}" : "");
                SaveRaw("crash", "mvs", summary, clues, AppLog.PreviousRunTail(died), null, died,
                    $"Previous run, process {info.Pid}, memory at the end {info.Pss / 1024} MB");
            }
            if (newest != seen)
                Prefs?.Edit()?.PutLong("exit.seen", newest)?.Apply();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Report] exit records: {ex.Message}");
        }
    }

    // A native crash's record holds the tombstone (binary); its readable lines (abort message,
    // signal, top frames) are what tell one crash from another.
    private static string TraceClues(ApplicationExitInfo info)
    {
        try
        {
            using Stream? stream = info.TraceInputStream;
            if (stream is null)
                return string.Empty;
            byte[] buffer = new byte[256 * 1024];
            int read = 0, n;
            while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
                read += n;
            List<string> found = new();
            StringBuilder run = new();
            for (int i = 0; i <= read; i++)
            {
                char c = i < read ? (char)buffer[i] : '\0';
                if (c >= ' ' && c < 127)
                {
                    run.Append(c);
                    continue;
                }
                string text = run.ToString().Trim();
                run.Clear();
                if (text.Length >= 12 && (text.Contains("Assertion", StringComparison.OrdinalIgnoreCase) || text.Contains("Abort", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("signal", StringComparison.OrdinalIgnoreCase) || text.Contains("Exception", StringComparison.Ordinal)
                    || text.Contains(".so", StringComparison.Ordinal) && found.Count < 30))
                    found.Add(text);
            }
            return Cut(string.Join("\n", found.Distinct().Take(40)), MaxStack);
        }
        catch
        {
            return string.Empty;
        }
    }

    // Network drops and cancelled waits are everyday events on a phone, not bugs.
    private static bool IsNoise(Exception ex) => ex is OperationCanceledException or TimeoutException or HttpRequestException
        or IOException or WebException or System.Net.Sockets.SocketException;

    /// <summary>
    /// Saves a report. <paramref name="category"/> says who should look at it: "mvs" (the app) or
    /// "script" (only kept locally: those go to the script makers). Returns the report id.
    /// </summary>
    public static string? Save(string kind, string category, Exception? ex, string? message, string? script, bool fatal)
    {
        try
        {
            if (!fatal)
                RememberPlayer();
            string summary = message ?? $"{ex?.GetType().Name}: {ex?.Message}";
            // A crash keeps the raw lines: reading saved accounts to hide names could fail mid-crash.
            string log = fatal ? RawRecent() : AppLog.ReportLog();
            return SaveRaw(kind, category, summary, ex?.ToString() ?? string.Empty, log, ex, DateTime.UtcNow, null, script, redacted: !fatal, upload: !fatal);
        }
        catch
        {
            return null;
        }
    }

    private static string? SaveRaw(string kind, string category, string summary, string stack, string log, Exception? ex, DateTime when,
        string? note, string? script = null, bool redacted = true, bool upload = true)
    {
        try
        {
            string signature = Signature(kind, ex, ex is null ? summary + "|" + stack.Split('\n')[0] : summary);
            string id = $"{when:yyyyMMdd-HHmmss}-{signature[..8]}";
            JsonObject report = new()
            {
                ["id"] = id,
                ["kind"] = kind,
                ["category"] = category,
                ["summary"] = Cut(summary, 500),
                ["exception"] = Cut(ex?.GetType().FullName ?? string.Empty, 200),
                ["stack"] = Cut(stack, MaxStack),
                // What was measured when it happened, above the log lines that led to it.
                ["log"] = Cut(ContextBlock(script, note) + "\n\n== Log ==\n" + log, MaxLog),
                ["redacted"] = redacted,
                ["script"] = Cut(script ?? RunInfo.ScriptName ?? string.Empty, 120),
                ["signature"] = signature,
                ["appVersion"] = AppLog.AppVersion(),
                ["device"] = Cut($"{(global::Android.OS.Build.Manufacturer)} {(global::Android.OS.Build.Model)}", 80),
                ["android"] = Cut(global::Android.OS.Build.VERSION.Release ?? string.Empty, 20),
                ["client"] = Platform.Army.Slot,
                ["time"] = when.ToString("o")
            };
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, id + ".json"), report.ToJsonString());
            Trace.WriteLine($"[Report] saved {kind}/{category} {id}: {Cut(summary, 160)}");
            LastSavedId = id;
            if (upload)
                _ = Task.Run(UploadPendingAsync);
            Saved?.Invoke(id);
            return id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The newest report saved in this run, and an event when one is saved (asks for the player's choice).</summary>
    public static string? LastSavedId { get; private set; }

    public static event Action<string>? Saved;

    // The measurements next to a report: what ran, for how long, and how the phone and game were doing.
    private static string ContextBlock(string? script, string? note)
    {
        StringBuilder sb = new("== Context ==\n");
        void Line(string name, Func<string?> value)
        {
            try
            {
                string? v = value();
                if (!string.IsNullOrWhiteSpace(v))
                    sb.Append(name).Append(": ").Append(v).Append('\n');
            }
            catch
            {
            }
        }
        Line("note", () => note);
        Line("script", () => script ?? RunInfo.ScriptName);
        Line("script ran", () => RunInfo.RunTime is { } t ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : null);
        Line("ruffle", RuffleBuild);
        Line("battery", () =>
        {
            Intent? battery = Context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
            if (battery is null) return null;
            return $"{battery.GetIntExtra(global::Android.OS.BatteryManager.ExtraTemperature, 0) / 10.0:0.0} C, {battery.GetIntExtra(global::Android.OS.BatteryManager.ExtraLevel, 0)}%, " +
                   (battery.GetIntExtra(global::Android.OS.BatteryManager.ExtraPlugged, 0) != 0 ? "charging" : "on battery");
        });
        Line("phone memory", () =>
        {
            ActivityManager.MemoryInfo info = new();
            (Context.GetSystemService(Context.ActivityService) as ActivityManager)?.GetMemoryInfo(info);
            return $"{info.AvailMem / 1048576} MB free of {info.TotalMem / 1048576} MB{(info.LowMemory ? ", low" : "")}";
        });
        Line("app memory", () => $"{GC.GetTotalMemory(false) / 1048576} MB .NET, {(global::Android.OS.Debug.NativeHeapAllocatedSize / 1048576)} MB native");
        Line("network", () =>
        {
            var cm = Context.GetSystemService(Context.ConnectivityService) as global::Android.Net.ConnectivityManager;
            var caps = cm?.GetNetworkCapabilities(cm.ActiveNetwork);
            if (caps is null) return "none";
            string type = caps.HasTransport(global::Android.Net.TransportType.Wifi) ? "Wi-Fi" : caps.HasTransport(global::Android.Net.TransportType.Cellular) ? "mobile data" : "other";
            return $"{type}, {caps.LinkDownstreamBandwidthKbps / 1000} Mbps down (estimate), {(caps.HasCapability(global::Android.Net.NetCapability.Validated) ? "internet ok" : "no internet")}";
        });
        Line("in background", () => Platform.BotControl.InBackground ? "yes (screen off or another app)" : "no");
        string measured = AppLog.MeasurementsText();
        if (measured.Length > 0)
            sb.Append(measured).Append('\n');
        return sb.ToString().TrimEnd();
    }

    private static string? _ruffleBuild;

    private static string? RuffleBuild()
    {
        if (_ruffleBuild is not null)
            return _ruffleBuild;
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "Skua.Flash.Ruffle")
                continue;
            string? version = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/').EndsWith("ruffle/VERSION", StringComparison.Ordinal));
            string? wasm = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".wasm", StringComparison.Ordinal));
            string text = "";
            if (version is not null)
            {
                using Stream stream = assembly.GetManifestResourceStream(version)!;
                text = new StreamReader(stream).ReadToEnd().Trim().Split('\n')[0];
            }
            return _ruffleBuild = $"{text} {Path.GetFileName(wasm?.Replace('\\', '/') ?? "")}".Trim();
        }
        return null;
    }

    // The logged-in name, so it can be hidden in this report and in a crash report later.
    private static void RememberPlayer()
    {
        try
        {
            AppLog.RememberName(CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Skua.Core.Interfaces.IScriptPlayer>()?.Username);
        }
        catch
        {
        }
    }

    private static string RawRecent()
    {
        try
        {
            return Cut(AppLog.RecentRaw(), MaxLog);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Signature(string kind, Exception? ex, string summary)
    {
        // The same exception type at the same place is the same problem, whatever its message says.
        string where = ex?.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains(" at ", StringComparison.Ordinal))?.Trim() ?? summary;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + ex?.GetType().FullName + "|" + where));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string Cut(string text, int max) => text.Length > max ? text[..max] : text;

    /// <summary>Sends the saved reports, oldest first.</summary>
    public static async Task UploadPendingAsync()
    {
        if (Mode != SendMode.Auto || Interlocked.Exchange(ref _uploading, 1) == 1)
            return;
        await SendFilesAsync(null);
    }

    /// <summary>Sends one report now (the player chose "Send report" for it, in "Ask each time" mode).</summary>
    public static async Task<bool> SendOneAsync(string id)
    {
        if (Interlocked.Exchange(ref _uploading, 1) == 1)
            return false;
        return await SendFilesAsync(id);
    }

    private static async Task<bool> SendFilesAsync(string? onlyId)
    {
        bool sentOne = false;
        try
        {
            if (!Directory.Exists(Folder))
                return false;
            string[] files = Directory.GetFiles(Folder, "2*.json").OrderBy(f => f, StringComparer.Ordinal)
                .Where(f => onlyId is null || Path.GetFileNameWithoutExtension(f) == onlyId).ToArray();
            if (files.Length == 0)
                return false;
            var auth = await Usage.UsageSync.SignedInAsync();
            if (auth is null)
                return false;
            Dictionary<string, string> sent = ReadSent();
            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            foreach (string file in files)
            {
                JsonObject? report = JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject;
                string signature = report?["signature"]?.GetValue<string>() ?? string.Empty;
                // Script problems are sent too (marked "script"), so Arglax can pass them to the script team.
                bool duplicate = sent.TryGetValue(signature, out string? day) && day == today;
                int sentToday = sent.Values.Count(d => d == today);
                if (report is null || duplicate || sentToday >= DailyLimit)
                {
                    File.Delete(file);
                    continue;
                }
                bool? result = await SendAsync(auth.Value, report);
                // null: the network or the server failed, try again later. false: refused for good (rules),
                // dropped so it can't hold up every report behind it.
                if (result is null)
                    break;
                if (result == true)
                {
                    sent[signature] = today;
                    WriteSent(sent);
                    sentOne = true;
                }
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Report] upload failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _uploading, 0);
        }
        return sentOne;
    }

    private static async Task<bool?> SendAsync((string ProjectId, string Uid, string IdToken) auth, JsonObject report)
    {
        string log = report["log"]?.GetValue<string>() ?? string.Empty;
        string stack = report["stack"]?.GetValue<string>() ?? string.Empty;
        if (report["redacted"]?.GetValue<bool>() != true)
            log = AppLog.Redact(log);
        stack = AppLog.Redact(stack);

        JsonObject S(string v) => new() { ["stringValue"] = v };
        JsonObject fields = new()
        {
            ["uid"] = S(auth.Uid),
            ["kind"] = S(report["kind"]!.GetValue<string>()),
            ["category"] = S(report["category"]!.GetValue<string>()),
            ["summary"] = S(AppLog.Redact(report["summary"]!.GetValue<string>())),
            ["exception"] = S(report["exception"]!.GetValue<string>()),
            ["stack"] = S(Cut(stack, MaxStack)),
            ["log"] = S(Cut(log, MaxLog)),
            ["script"] = S(report["script"]!.GetValue<string>()),
            ["signature"] = S(report["signature"]!.GetValue<string>()),
            ["appVersion"] = S(report["appVersion"]!.GetValue<string>()),
            ["device"] = S(report["device"]!.GetValue<string>()),
            ["android"] = S(report["android"]!.GetValue<string>()),
            ["client"] = new JsonObject { ["integerValue"] = report["client"]!.GetValue<int>().ToString() },
            ["time"] = new JsonObject { ["timestampValue"] = report["time"]!.GetValue<string>() }
        };
        string id = report["id"]!.GetValue<string>() + "-" + auth.Uid[..6];
        using HttpRequestMessage request = new(HttpMethod.Post,
            $"https://firestore.googleapis.com/v1/projects/{auth.ProjectId}/databases/(default)/documents/{Collection}?documentId={id}")
        {
            Content = new StringContent(new JsonObject { ["fields"] = fields }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.IdToken);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Trace.WriteLine($"[Report] not sent now: {ex.Message}");
            return null;
        }
        using HttpResponseMessage _ = response;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Usage.UsageSync.TokenRefused();
            return null;
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
            return true; // Already there.
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            Trace.WriteLine($"[Report] refused: {(int)response.StatusCode} {Cut(error, 300)}");
            return (int)response.StatusCode >= 500 || response.StatusCode == (HttpStatusCode)429 ? null : false;
        }
        Trace.WriteLine($"[Report] sent {id}");
        return true;
    }

    private static Dictionary<string, string> ReadSent()
    {
        try
        {
            return File.Exists(SentPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SentPath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private static void WriteSent(Dictionary<string, string> sent)
    {
        // Only the last week matters for the daily limit.
        string cutoff = DateTime.UtcNow.AddDays(-7).ToString("yyyyMMdd");
        Dictionary<string, string> kept = sent.Where(p => string.CompareOrdinal(p.Value, cutoff) >= 0).ToDictionary(p => p.Key, p => p.Value);
        File.WriteAllText(SentPath, JsonSerializer.Serialize(kept));
    }
}
