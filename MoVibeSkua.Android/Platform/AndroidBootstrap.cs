using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// First-run file setup: seeds the Skua data folder, unpacks the script compiler references and
/// the bundled plugins from the APK assets.
/// </summary>
public static class AndroidBootstrap
{
    private static bool _prepared;

    public static string ReferenceDirectory { get; private set; } = string.Empty;

    public static void Prepare(Context context)
    {
        if (_prepared)
            return;
        _prepared = true;

        try
        {
            Directory.CreateDirectory(ClientFileSources.SkuaDIR);
            string version = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.LastUpdateTime.ToString() ?? "0";

            ReferenceDirectory = Path.Combine(context.FilesDir!.AbsolutePath, "script-refs");
            string stamp = Path.Combine(ReferenceDirectory, ".stamp");
            if (!File.Exists(stamp) || File.ReadAllText(stamp) != version)
            {
                if (Directory.Exists(ReferenceDirectory))
                    Directory.Delete(ReferenceDirectory, true);
                CopyAssetFolder(context, "refs", ReferenceDirectory, overwrite: true);
                File.WriteAllText(stamp, version);

                // Scripts compiled against the previous build's references may no longer load.
                string scriptCache = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Cached-Scripts");
                if (Directory.Exists(scriptCache))
                    Directory.Delete(scriptCache, true);
            }
            Skua.Core.AppStartup.Services.ScriptReferenceDirectories.Add(ReferenceDirectory);
            Skua.Core.Compiler.ScriptPrelude = ScriptShims.Source;

            // Seed data the desktop client ships next to the executable, without clobbering user copies.
            CopyAssetFolder(context, "seed", ClientFileSources.SkuaDIR, overwrite: false);
            // Primary client updates bundled plugins on startup; Army clients only fill in any missing
            // files so they never overwrite a DLL another running client already loaded.
            CopyAssetFolder(context, "plugins", ClientFileSources.SkuaPluginsDIR, overwrite: Army.IsFirst);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Bootstrap] {ex}");
        }
    }

    private static void CopyAssetFolder(Context context, string assetFolder, string target, bool overwrite)
    {
        string[]? files = context.Assets?.List(assetFolder);
        if (files is null || files.Length == 0)
            return;

        Directory.CreateDirectory(target);
        foreach (string file in files)
        {
            string destination = Path.Combine(target, file);
            if (!overwrite && File.Exists(destination))
                continue;
            using Stream input = context.Assets!.Open($"{assetFolder}/{file}");
            using FileStream output = File.Create(destination);
            input.CopyTo(output);
        }
    }
}
