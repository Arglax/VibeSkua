using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Build = Android.OS.Build;
using BuildVersionCodes = Android.OS.BuildVersionCodes;
using AndroidX.Core.App;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>The script now running (or that ran last): its name and when it started.</summary>
public static class RunInfo
{
    public static string? ScriptName { get; internal set; }
    public static DateTime? StartedUtc { get; internal set; }
    public static TimeSpan? RunTime => StartedUtc is { } s ? DateTime.UtcNow - s : null;
}

/// <summary>
/// Tells the player how a script run ended with an Android notification, which reaches them with the
/// screen off where a box on screen doesn't: finished (with what it farmed), stopped on a problem in the
/// script (for the script team) or in MVS (for Arglax), or paused because AQW can't be reached. A run
/// the player stopped gets none. Tapping one opens MVS on the details; the first problem also asks the
/// player whether reports may be sent (<see cref="ProblemReports.Mode"/>).
/// </summary>
public static class RunNotifier
{
    public const string ActionRunAgain = "com.arglax.movibeskua.action.RUN_AGAIN";
    public const string ActionCopy = "com.arglax.movibeskua.action.COPY_DETAILS";
    public const string ActionSendReport = "com.arglax.movibeskua.action.SEND_REPORT";

    private const string ChannelId = "script_results";
    private const int FinishedId = 2101, ProblemId = 2102, ConnectionId = 2103;

    private static readonly object Recipient = new();
    private static bool _errored;
    private static string _lastDetails = string.Empty;
    private static string? _lastReportId;
    private static Action? _pendingOnScreen;

    private static Context Context => global::Android.App.Application.Context;

    public static void Start()
    {
        CreateChannel();
        StrongReferenceMessenger.Default.Register<ScriptStartedMessage, int>(Recipient, (int)MessageChannels.ScriptStatus, (_, _) => OnStarted());
        StrongReferenceMessenger.Default.Register<ScriptStoppedMessage, int>(Recipient, (int)MessageChannels.ScriptStatus, (_, _) => OnStopped());
        StrongReferenceMessenger.Default.Register<ReloginPausedMessage, int>(Recipient, (int)MessageChannels.GameEvents, (_, m) => OnReloginPaused(m));
        // A report saved while the player hasn't chosen yet: ask them (now if MVS is on screen).
        ProblemReports.Saved += _ =>
        {
            if (ProblemReports.Mode == ProblemReports.SendMode.NotAsked)
                ShowWhenOnScreen(AskAboutReports);
        };
    }

    private static bool Helper => Bot.AutoEnhance.IsRunning;

    private static void OnStarted()
    {
        if (Helper)
            return;
        _errored = false;
        try
        {
            string? path = Ioc.Default.GetService<IScriptManager>()?.LoadedScript;
            RunInfo.ScriptName = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
        }
        RunInfo.StartedUtc = DateTime.UtcNow;
        Cancel(FinishedId);
        Cancel(ProblemId);
        Cancel(ConnectionId);
    }

    private static void OnStopped()
    {
        if (Helper || _errored)
            return;
        bool external = (Ioc.Default.GetService<IScriptManager>() as Skua.Core.Scripts.ScriptManager)?.LastStopWasExternal ?? true;
        string clock = Clock();
        Trace.WriteLine($"[Run] {RunInfo.ScriptName} ended after {clock}: {(external ? "stopped from outside (player, logout or re-login)" : "finished")}");
        if (external)
            return;
        (string line, string details) = Platform.BotControl.Farmed();
        Post(FinishedId, $"✅ {RunInfo.ScriptName ?? "Script"} finished ({clock})", details.Length > 0 ? details : line,
            ("Run again", ActionRunAgain));
    }

    /// <summary>A script stopped on an error (from <see cref="ScriptProblems"/>), after its report was saved.</summary>
    public static void OnProblem(string script, bool starting, bool scriptsFault, string reason, string shortText, string details, string? reportId)
    {
        _errored = true;
        _lastDetails = details;
        _lastReportId = reportId;
        string who = scriptsFault ? "problem in the script" : "problem in MVS";
        string title = starting ? $"⚠️ {script} could not start: {who}" : $"⚠️ {script} stopped after {Clock()}: {who}";
        string sent = scriptsFault
            ? "For the script team (Skua / VibeSkua / Tato)."
            : ProblemReports.Mode switch
            {
                ProblemReports.SendMode.Auto => "Report sent to Arglax.",
                ProblemReports.SendMode.Ask => "Tap Send report to send it to Arglax.",
                ProblemReports.SendMode.Never => "Reports are off: not sent.",
                _ => "Open MVS to choose whether reports are sent.",
            };
        string text = $"{shortText}\n{reason}\n{sent}";
        if (ProblemReports.Mode == ProblemReports.SendMode.Ask && reportId is not null)
            Post(ProblemId, title, text, ("Copy details", ActionCopy), ("Send report", ActionSendReport));
        else
            Post(ProblemId, title, text, ("Copy details", ActionCopy), (scriptsFault ? "Run again" : "Restart script", ActionRunAgain));
    }

    private static void OnReloginPaused(ReloginPausedMessage m)
    {
        if (!m.ScriptWasRunning)
            return;
        Post(ConnectionId, $"📶 {RunInfo.ScriptName ?? "Script"} paused: can't reach AQW",
            $"Re-login failed {m.Rounds} round{(m.Rounds == 1 ? "" : "s")}. Trying again in {m.Minutes} min; the script restarts when it's back in.");
    }

    /// <summary>Runs <paramref name="show"/> now if MVS is on screen, else the next time it is.</summary>
    public static void ShowWhenOnScreen(Action show)
    {
        if (!Platform.BotControl.InBackground && MainActivity.Current is not null)
            Dispatcher.UIThread.Post(show);
        else
            _pendingOnScreen += show;
    }

    /// <summary>MVS came back on screen (MainActivity.OnResume): show what waited for it.</summary>
    public static void OnScreen()
    {
        Action? pending = _pendingOnScreen;
        _pendingOnScreen = null;
        if (pending is not null)
            Dispatcher.UIThread.Post(pending);
    }

    private static bool _asking;

    public static void AskAboutReports()
    {
        if (_asking || ProblemReports.Mode != ProblemReports.SendMode.NotAsked)
            return;
        IDialogService? dialogs = Ioc.Default.GetService<IDialogService>();
        if (dialogs is null)
            return;
        _asking = true;
        try
        {
            DialogResult result = dialogs.ShowMessageBox(
                "When a script stops on an error or MVS crashes, MVS can send what happened to Arglax, the MVS developer, so it gets fixed.\n\n" +
                "Sent: the error, the last few minutes of the app log, the script name, how long it ran, phone model, Android version, memory, temperature and connection quality.\n\n" +
                "Never sent: your account name, password, e-mail or chat. They're removed before anything leaves the phone.\n\n" +
                "You can change this any time in Guide → Support.",
                "Send error reports to the MVS developer?", "Send automatically", "Ask each time", "Don't send");
            ProblemReports.Mode = result.Value switch
            {
                0 => ProblemReports.SendMode.Auto,
                1 => ProblemReports.SendMode.Ask,
                2 => ProblemReports.SendMode.Never,
                _ => ProblemReports.SendMode.NotAsked,
            };
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>A button on one of these notifications (sent through KeepAliveService).</summary>
    public static void Handle(string action)
    {
        Trace.WriteLine($"[Run] notification: {action[(action.LastIndexOf('.') + 1)..]}");
        switch (action)
        {
            case ActionCopy:
                try
                {
                    (Context.GetSystemService(Context.ClipboardService) as ClipboardManager)?.PrimaryClip = (ClipData.NewPlainText("MVS details", _lastDetails));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Run] copy: {ex.Message}");
                }
                break;
            case ActionSendReport:
                if (_lastReportId is { } id)
                {
                    _ = Task.Run(async () =>
                    {
                        bool sent = await ProblemReports.SendOneAsync(id);
                        Post(ProblemId, sent ? "✅ Report sent to Arglax" : "Report not sent", sent ? "Thanks: it helps fix MVS." : "The phone looks offline; it stays on the phone. Try again from Guide → Support.");
                    });
                }
                break;
            case ActionRunAgain:
                Cancel(FinishedId);
                Cancel(ProblemId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        IScriptManager? scripts = Ioc.Default.GetService<IScriptManager>();
                        if (scripts is null || scripts.ScriptRunning || string.IsNullOrWhiteSpace(scripts.LoadedScript))
                            return;
                        Exception? ex = await scripts.StartScript();
                        if (ex is not null && Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel.StartErrorHandler?.Invoke(ex) != true)
                            Trace.WriteLine($"[Run] start: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[Run] run again: {ex.Message}");
                    }
                });
                break;
        }
    }

    private static string Clock()
    {
        TimeSpan t = RunInfo.RunTime ?? TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;
        NotificationChannel channel = new(ChannelId, "Script results", NotificationImportance.Default)
        {
            Description = "When a script finishes, stops on a problem, or can't reach AQW."
        };
        (Context.GetSystemService(Context.NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    private static void Post(int id, string title, string text, params (string Label, string Action)[] actions)
    {
        try
        {
            Intent open = new(Context, typeof(MainActivity));
            open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront | ActivityFlags.NewTask);
            PendingIntent? tap = PendingIntent.GetActivity(Context, id, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            NotificationCompat.Builder builder = new NotificationCompat.Builder(Context, ChannelId)
                .SetContentTitle(title)
                .SetContentText(text.Split('\n')[0])
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
                .SetSmallIcon(Resource.Drawable.ic_notification)
                .SetAutoCancel(true)
                .SetContentIntent(tap)
                .SetPriority(NotificationCompat.PriorityDefault);
            int code = id * 10;
            foreach ((string label, string action) in actions)
            {
                Intent intent = new(Context, typeof(Platform.KeepAliveService));
                intent.SetAction(action);
                builder.AddAction(0, label, PendingIntent.GetService(Context, ++code, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
            }
            NotificationManagerCompat.From(Context).Notify(id, builder.Build()!);
            Trace.WriteLine($"[Run] notified: {title}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Run] notify: {ex.Message}");
        }
    }

    private static void Cancel(int id)
    {
        try
        {
            NotificationManagerCompat.From(Context).Cancel(id);
        }
        catch
        {
        }
    }
}
