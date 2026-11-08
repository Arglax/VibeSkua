using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>
/// When a script stops with an error, decides who can fix it and tells the player in plain words:
///  - the script itself (a mistake in its code, or it used the bot wrongly): scripts come from the AUQW
///    community, so the player is pointed to their Discord with a copy of what they need;
///  - MVS (the mobile port: a Windows-only feature, an Android limit, a fault in MVS's own code or in the
///    bot engine it ships): reported to the MVS developer automatically (<see cref="ProblemReports"/>).
/// The script's own code is told apart by its assembly: Skua compiles each script into one named after
/// its class with ".cs" on the end.
/// </summary>
public static class ScriptProblems
{
    public static void Start()
    {
        StrongReferenceMessenger.Default.Register<ScriptErrorMessage, int>(Recipient, (int)MessageChannels.ScriptStatus,
            (_, m) => Handle(m.Exception, starting: false));
        // Errors while starting (compiling) come to us instead of Skua's own message box.
        Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel.StartErrorHandler = ex =>
        {
            Handle(ex, starting: true);
            return true;
        };
    }

    private static readonly object Recipient = new();

    private static string ScriptName()
    {
        try
        {
            string? path = Ioc.Default.GetService<IScriptManager>()?.LoadedScript;
            return string.IsNullOrEmpty(path) ? "The script" : Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            return "The script";
        }
    }

    private static void Handle(Exception error, bool starting)
    {
        ScriptVerdicts.Verdict verdict;
        try
        {
            verdict = ScriptVerdicts.Classify(error);
        }
        catch
        {
            verdict = new(ScriptVerdicts.Who.Mvs, "Something went wrong inside MVS while running the script.", ScriptVerdicts.FirstLine(error.Message));
        }
        string script = ScriptName();
        Trace.WriteLine($"[Script] {script} {(starting ? "did not start" : "stopped")}: {verdict.Who} ({verdict.Reason}) {verdict.Short}");
        ProblemReports.Save("script", verdict.Who == ScriptVerdicts.Who.Mvs ? "mvs" : "script", ScriptVerdicts.Unwrap(error), $"{script}: {verdict.Short}", script, fatal: false);
        string details = Details(error, script, verdict);
        Dispatcher.UIThread.Post(() => Show(script, starting, verdict, details));
    }

    private static string Details(Exception error, string script, ScriptVerdicts.Verdict verdict)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Script: {script}");
        sb.AppendLine($"App: MVS {AppLog.AppVersion()} on Android {(global::Android.OS.Build.VERSION.Release)}");
        sb.AppendLine($"Problem: {verdict.Short}");
        sb.AppendLine();
        sb.AppendLine(ScriptVerdicts.Unwrap(error).ToString());
        return AppLog.Redact(sb.ToString());
    }

    private static void Show(string script, bool starting, ScriptVerdicts.Verdict verdict, string details)
    {
        IDialogService? dialogs = Ioc.Default.GetService<IDialogService>();
        if (dialogs is null)
            return;
        string what = starting ? "could not start" : "stopped";
        string body;
        string[] buttons;
        if (verdict.Who == ScriptVerdicts.Who.Script)
        {
            body = $"“{script}” {what} because of a problem in the script itself, not in MVS.\n\n" +
                   $"{verdict.Reason}\n{verdict.Short}\n\n" +
                   "Scripts are made by the AUQW community. Tell them on the AUQW Discord so they can fix it: " +
                   "“Copy details” copies what they need.";
            buttons = new[] { "Copy details", "AUQW Discord", "OK" };
        }
        else
        {
            body = $"“{script}” {what} because of a problem in MVS, not in the script.\n\n" +
                   $"{verdict.Reason}\n{verdict.Short}\n\n" +
                   (ProblemReports.SendEnabled
                       ? "It was sent to the MVS developer (without your account details). Trying again often works; if it keeps happening, the MVS Discord can help."
                       : "Problem reports are off, so it was not sent. “Copy details” copies it for the MVS Discord.");
            buttons = new[] { "Copy details", "MVS Discord", "OK" };
        }
        DialogResult result = dialogs.ShowMessageBox(body, starting ? "Script could not start" : "Script stopped", buttons);
        switch (result.Value)
        {
            case 0:
                Ioc.Default.GetService<IClipboardService>()?.SetText(details);
                break;
            case 1:
                ViewModels.SupportViewModel.Open(verdict.Who == ScriptVerdicts.Who.Script ? ViewModels.SupportViewModel.AuqwDiscordUrl : ViewModels.SupportViewModel.DiscordUrl);
                break;
        }
    }
}
