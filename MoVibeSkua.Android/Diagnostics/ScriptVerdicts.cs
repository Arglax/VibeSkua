using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Diagnostics;

/// <summary>
/// Who can fix a script that stopped with an error: the script's makers, or MVS. No Android code, so
/// tools/ScriptTriageCheck can run it on a PC against real compile errors and real script crashes.
/// The script's own code is told apart by its assembly: Skua compiles each script into one named after
/// its class with ".cs" on the end.
/// </summary>
public static class ScriptVerdicts
{
    public enum Who { Script, Mvs }

    public sealed record Verdict(Who Who, string Reason, string Short);

    private static readonly Regex WindowsOnly = new(
        @"System\.Windows|System\.Drawing|Microsoft\.Win32|WindowsBase|PresentationFramework|PresentationCore|WinForms|" +
        @"user32|kernel32|gdi32|System\.Management|Registry|Clipboard\b|SendKeys|MessageBox\b",
        RegexOptions.Compiled);

    // A script written for a newer bot than MVS ships: a member or type of the bot's API it cannot find.
    private static readonly Regex MissingBotApi = new(
        @"error CS(0117|1061|0246|0234|1503|7036).*'(IScript\w*|Skua\.\w[\w.]*|ScriptInterface|CoreBots)",
        RegexOptions.Compiled);

    private static readonly Regex CompileError = new(@"\((\d+),\d+\): error (CS\d+): (.+)", RegexOptions.Compiled);

    public static Verdict Classify(Exception error)
    {
        Exception ex = Unwrap(error);
        string type = ex.GetType().FullName ?? ex.GetType().Name;

        if (ex is ScriptVersionException)
            return new(Who.Mvs, "The script needs a newer version of MVS.", FirstLine(ex.Message));

        if (ex is ScriptCompileException)
        {
            string message = ex.Message ?? string.Empty;
            Match first = CompileError.Match(message);
            string shortText = first.Success ? $"line {first.Groups[1].Value}: {first.Groups[3].Value.Trim()}" : FirstLine(message);
            if (WindowsOnly.IsMatch(message))
                return new(Who.Mvs, "The script uses a Windows-only feature that MVS on Android does not have yet.", shortText);
            if (MissingBotApi.IsMatch(message))
                return new(Who.Mvs, "The script expects a part of the bot that MVS does not have yet.", shortText);
            return new(Who.Script, "The script has a mistake in its code.", shortText);
        }

        string shortRuntime = $"{ex.GetType().Name}: {FirstLine(ex.Message)}";
        if (ex is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeLoadException
            or MissingMemberException or BadImageFormatException
            || (ex is FileNotFoundException f && (f.FileName ?? f.Message).Contains("Version=", StringComparison.Ordinal))
            || type.StartsWith("Java.", StringComparison.Ordinal) || type.StartsWith("Android.", StringComparison.Ordinal)
            || WindowsOnly.IsMatch(ex.Message ?? string.Empty))
        {
            return new(Who.Mvs, "The script uses something MVS on Android cannot do yet.", shortRuntime);
        }

        Assembly?[] frames = new StackTrace(ex, false).GetFrames()
            .Select(fr => fr.GetMethod()?.DeclaringType?.Assembly)
            .Where(a => a is not null && !IsRuntime(a))
            .ToArray();
        bool scriptInvolved = frames.Any(IsScript);
        bool thrownInScript = frames.FirstOrDefault() is { } top && IsScript(top);

        if (thrownInScript)
            return new(Who.Script, "The script has a mistake in its code.", shortRuntime);
        // The bot refused what the script asked of it (a wrong id, a missing item): the script's call.
        if (scriptInvolved && ex is ArgumentException or System.Collections.Generic.KeyNotFoundException or InvalidCastException or FormatException)
        {
            return new(Who.Script, "The script asked the bot for something that does not exist.", shortRuntime);
        }
        return new(Who.Mvs, "Something went wrong inside MVS while running the script.", shortRuntime);
    }

    public static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            if (ex is TargetInvocationException { InnerException: { } inner })
                ex = inner;
            else if (ex is AggregateException { InnerExceptions.Count: 1 } agg)
                ex = agg.InnerExceptions[0];
            else
                return ex;
        }
    }

    private static bool IsScript(Assembly? a) => a?.GetName().Name?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsRuntime(Assembly? a)
    {
        string name = a?.GetName().Name ?? string.Empty;
        return name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name is "mscorlib" or "netstandard" or "Mono.Android" or "Java.Interop" || name.StartsWith("Newtonsoft", StringComparison.Ordinal);
    }

    public static string FirstLine(string? text)
    {
        string line = (text ?? string.Empty).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        return line.Length > 160 ? line[..160] + "…" : line;
    }

}
