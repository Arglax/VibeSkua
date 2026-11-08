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
using Android.Content;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>
/// Problem reports for the MVS developer: app crashes, errors inside MVS, and scripts that stopped
/// because of MVS (not because of the script, see <see cref="ScriptProblems"/>). Each report is first
/// saved to files/reports (so one written during a crash survives it), then sent to the movibeskua
/// Firebase project (collection movibeskua_reports) when the phone is online, with account names,
/// passwords and tokens taken out. At most 20 a day, and the same problem only once a day.
/// Settings: "Send problem reports" (prefs reports.send, on by default).
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

    public static bool SendEnabled
    {
        get => Context.GetSharedPreferences("movibeskua.shell", FileCreationMode.Private)?.GetBoolean("reports.send", true) ?? true;
        set => Context.GetSharedPreferences("movibeskua.shell", FileCreationMode.Private)?.Edit()?.PutBoolean("reports.send", value)?.Apply();
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
            await Task.Delay(TimeSpan.FromSeconds(20));
            await UploadPendingAsync();
        });
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
            string stack = ex?.ToString() ?? string.Empty;
            string summary = message ?? $"{ex?.GetType().Name}: {ex?.Message}";
            string signature = Signature(kind, ex, summary);
            string id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{signature[..8]}";
            JsonObject report = new()
            {
                ["id"] = id,
                ["kind"] = kind,
                ["category"] = category,
                ["summary"] = Cut(summary, 500),
                ["exception"] = Cut(ex?.GetType().FullName ?? string.Empty, 200),
                ["stack"] = Cut(stack, MaxStack),
                // A crash keeps the raw lines: reading saved accounts to hide names could fail mid-crash.
                ["log"] = fatal ? RawRecent() : AppLog.RecentText(),
                ["redacted"] = !fatal,
                ["script"] = Cut(script ?? string.Empty, 120),
                ["signature"] = signature,
                ["appVersion"] = AppLog.AppVersion(),
                ["device"] = Cut($"{(global::Android.OS.Build.Manufacturer)} {(global::Android.OS.Build.Model)}", 80),
                ["android"] = Cut(global::Android.OS.Build.VERSION.Release ?? string.Empty, 20),
                ["client"] = Platform.Army.Slot,
                ["time"] = DateTime.UtcNow.ToString("o")
            };
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, id + ".json"), report.ToJsonString());
            Trace.WriteLine($"[Report] saved {kind}/{category} {id}: {Cut(summary, 160)}");
            if (!fatal)
                _ = Task.Run(UploadPendingAsync);
            return id;
        }
        catch
        {
            return null;
        }
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
        if (!SendEnabled || Interlocked.Exchange(ref _uploading, 1) == 1)
            return;
        try
        {
            if (!Directory.Exists(Folder))
                return;
            string[] files = Directory.GetFiles(Folder, "2*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
                return;
            var auth = await Usage.UsageSync.SignedInAsync();
            if (auth is null)
                return;
            Dictionary<string, string> sent = ReadSent();
            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            foreach (string file in files)
            {
                JsonObject? report = JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject;
                string signature = report?["signature"]?.GetValue<string>() ?? string.Empty;
                // "script" reports stay on the phone: they are for the script makers.
                bool keepLocal = report?["category"]?.GetValue<string>() == "script";
                bool duplicate = sent.TryGetValue(signature, out string? day) && day == today;
                int sentToday = sent.Values.Count(d => d == today);
                if (report is null || keepLocal || duplicate || sentToday >= DailyLimit)
                {
                    File.Delete(file);
                    continue;
                }
                if (!await SendAsync(auth.Value, report))
                    break;
                sent[signature] = today;
                WriteSent(sent);
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
    }

    private static async Task<bool> SendAsync((string ProjectId, string Uid, string IdToken) auth, JsonObject report)
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
        using HttpResponseMessage response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            Usage.UsageSync.TokenRefused();
        if (response.StatusCode == HttpStatusCode.Conflict)
            return true; // Already there.
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            Trace.WriteLine($"[Report] refused: {(int)response.StatusCode} {Cut(error, 300)}");
            return false;
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
