using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Android.Content;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Bot;

/// <summary>
/// MVS Auto Enhance: before a script starts (and from "Check now"), checks the equipped class, weapon,
/// helm and cape against the recommended enhancements for the class and use case and enhances them
/// when the helm or cape is off or less than <see cref="Threshold"/>% of the set matches. The work is
/// done by a built-in script (Scripts/MVS/MvsAutoEnhance.cs) generated here from a template plus AUQW's
/// own SmartEnhance libraries copied from the downloaded CoreAdvanced.cs, so the picks follow AUQW's
/// updates; Ultra uses the r/AQW Simplified Bosses Guide where it lists the class (mvs-enhance-ultra.json,
/// tools/build_enhance_data.py). Off by default: it spends gold.
/// </summary>
public static class AutoEnhance
{
    public static readonly string[] UseCases = { "Farming", "Bossing", "Ultra" };
    public const int Threshold = 60;

    private const string Prefs = "movibeskua.autoenhance";
    private static ISharedPreferences? P => global::Android.App.Application.Context.GetSharedPreferences(Prefs, FileCreationMode.Private);

    public static bool Enabled
    {
        get => P?.GetBoolean("enabled", false) ?? false;
        set => P?.Edit()?.PutBoolean("enabled", value)?.Apply();
    }

    public static string UseCase
    {
        get
        {
            string v = P?.GetString("useCase", "Bossing") ?? "Bossing";
            return UseCases.Contains(v) ? v : "Bossing";
        }
        set => P?.Edit()?.PutString("useCase", value)?.Apply();
    }

    /// <summary>Slots Auto Enhance may change ("class", "weapon", "helm", "cape"); at most <see cref="MaxSlots"/>.</summary>
    public static readonly string[] Slots = { "class", "weapon", "helm", "cape" };
    public const int MaxSlots = 3;

    public static bool SlotOn(string slot) => P?.GetBoolean("slot." + slot, slot != "class") ?? slot != "class";

    public static void SetSlot(string slot, bool on) => P?.Edit()?.PutBoolean("slot." + slot, on)?.Apply();

    public static string LastResult
    {
        get => P?.GetString("last", "") ?? "";
        private set => P?.Edit()?.PutString("last", value)?.Apply();
    }

    public static string ScriptPath => Path.Combine(ClientFileSources.SkuaScriptsDIR, "MVS", "MvsAutoEnhance.cs");

    private static bool _running;

    /// <summary>Hooked into the script loader: runs before the user's script when Auto Enhance is on.</summary>
    public static void Install()
    {
        Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel.BeforeStart = async () =>
        {
            if (Enabled)
                await RunAsync(beforeScript: true);
        };
    }

    /// <summary>Runs the check now. Returns the summary shown to the player.</summary>
    public static async Task<string> RunAsync(bool beforeScript)
    {
        if (_running)
            return "Auto Enhance is already running.";
        IScriptInterface bot = Ioc.Default.GetRequiredService<IScriptInterface>();
        IScriptManager manager = Ioc.Default.GetRequiredService<IScriptManager>();
        if (!bot.Player.LoggedIn)
            return "Log in first.";
        string? userScript = manager.LoadedScript;
        if (string.Equals(userScript, ScriptPath, StringComparison.OrdinalIgnoreCase) || manager.ScriptRunning && !beforeScript)
            return manager.ScriptRunning ? "Stop the running script first." : "";

        _running = true;
        try
        {
            string? problem = Generate();
            if (problem is not null)
            {
                LastResult = problem;
                Notify(problem, failed: true);
                return problem;
            }
            string resultFile = Path.Combine(ClientFileSources.SkuaOptionsDIR, "mvs-autoenhance-result.json");
            try { File.Delete(resultFile); } catch { }

            manager.SetLoadedScript(ScriptPath);
            Exception? ex = await manager.StartScript();
            if (ex is null)
            {
                DateTime until = DateTime.UtcNow.AddMinutes(5);
                await Task.Delay(500);
                while (manager.ScriptRunning && DateTime.UtcNow < until)
                    await Task.Delay(400);
                if (manager.ScriptRunning)
                    await manager.StopScript();
            }
            if (!string.IsNullOrEmpty(userScript))
                manager.SetLoadedScript(userScript);

            string summary;
            bool failed = false, show = false;
            if (ex is not null)
            {
                summary = "Auto Enhance couldn't start: " + ex.Message;
                failed = show = true;
            }
            else if (File.Exists(resultFile))
            {
                JObject r = JObject.Parse(File.ReadAllText(resultFile));
                string status = (string?)r["status"] ?? "ok";
                summary = string.Join("\n", r["Lines"]?.Select(t => (string?)t) ?? Array.Empty<string?>());
                failed = status is "failed" or "partial" or "error";
                show = failed || status == "enhanced" || !beforeScript;
            }
            else
            {
                summary = "Auto Enhance finished without a result.";
                show = !beforeScript;
            }
            LastResult = $"{DateTime.Now:HH:mm} {summary}";
            Trace.WriteLine("[AutoEnhance] " + summary.Replace('\n', ' '));
            if (show)
                Notify(summary, failed);
            return summary;
        }
        catch (Exception e)
        {
            Trace.WriteLine("[AutoEnhance] " + e);
            if (!string.IsNullOrEmpty(userScript))
                try { manager.SetLoadedScript(userScript); } catch { }
            return "Auto Enhance hit a problem: " + e.Message;
        }
        finally
        {
            _running = false;
        }
    }

    // Not a blocking box before a script: the script goes on, the message waits on screen.
    private static void Notify(string text, bool failed)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Ioc.Default.GetService<IDialogService>()?.ShowMessageBox(
                    text + (failed ? "\n\nThe script carries on with what you have." : ""),
                    failed ? "Auto Enhance: couldn't finish" : "Auto Enhance");
            }
            catch { }
        });
    }

    /// <summary>Writes the built-in script, its Ultra data and settings. Returns a problem, or null.</summary>
    public static string? Generate()
    {
        string core = Path.Combine(ClientFileSources.SkuaScriptsDIR, "CoreAdvanced.cs");
        if (!File.Exists(core))
            return "Auto Enhance needs the community scripts: open Repo and download them first.";
        string src = File.ReadAllText(core);
        string? forge = ExtractFunction(src, "bool ForgeEnhancementLibrary()");
        string? awe = ExtractFunction(src, "void AweEnhancementLibrary()");
        if (forge is null || awe is null)
            return "Auto Enhance couldn't read AUQW's enhancement list (CoreAdvanced.cs changed). Update the scripts in Repo, or tell the MVS Discord.";

        StringBuilder lib = new();
        lib.AppendLine("    private " + forge.Trim());
        lib.AppendLine();
        lib.AppendLine("    private " + awe.Trim());
        lib.AppendLine();
        // What the copied code calls on CoreAdvanced, forwarded to it.
        foreach (Match m in Regex.Matches(src, @"public bool (u[A-Z]\w*)\(\)"))
            lib.AppendLine($"    private bool {m.Groups[1].Value}() => Adv.{m.Groups[1].Value}();");
        foreach (string f in new[] { "EnhancementType CurrentClassEnh", "CapeSpecial CurrentCapeSpecial", "HelmSpecial CurrentHelmSpecial", "WeaponSpecial CurrentWeaponSpecial" })
        {
            string name = f.Split(' ')[1];
            // AUQW reads the weapon's pattern, not its proc (see MvsWeaponSpecial in the template).
            lib.AppendLine(name == "CurrentWeaponSpecial" ? $"    private {f}() => MvsWeaponSpecial();" : $"    private {f}() => Adv.{name}();");
        }

        string template = ReadResource("MvsAutoEnhance.template.txt");
        string script = template.Replace("    //@@AUQW_LIBRARIES@@", lib.ToString());
        string dir = Path.GetDirectoryName(ScriptPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ScriptPath, script);
        File.WriteAllText(Path.Combine(dir, "mvs-enhance-ultra.json"), ReadResource("mvs-enhance-ultra.json"));
        Directory.CreateDirectory(ClientFileSources.SkuaOptionsDIR);
        File.WriteAllText(Path.Combine(ClientFileSources.SkuaOptionsDIR, "mvs-autoenhance.json"),
            new JObject { ["useCase"] = UseCase, ["threshold"] = Threshold, ["slots"] = new JArray(Slots.Where(SlotOn).ToArray()) }.ToString());
        return null;
    }

    private static string ReadResource(string name)
    {
        using Stream s = typeof(AutoEnhance).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException(name);
        using StreamReader r = new(s);
        return r.ReadToEnd();
    }

    /// <summary>The whole local function starting at <paramref name="signature"/> (braces matched, strings and comments skipped).</summary>
    public static string? ExtractFunction(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0)
            return null;
        int open = src.IndexOf('{', at);
        if (open < 0)
            return null;
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                int nl = src.IndexOf('\n', i);
                i = nl < 0 ? src.Length : nl;
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? src.Length : end + 1;
                continue;
            }
            if (c == '"')
            {
                for (i++; i < src.Length && src[i] != '"'; i++)
                    if (src[i] == '\\') i++;
                continue;
            }
            if (c == '\'')
            {
                for (i++; i < src.Length && src[i] != '\''; i++)
                    if (src[i] == '\\') i++;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
                return src.Substring(at, i - at + 1);
        }
        return null;
    }
}
