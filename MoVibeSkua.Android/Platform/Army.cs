using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Android.App;
using Android.Content;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Army: several game clients at once, each in its own process with its own bot, game page and login,
/// the way the desktop Manager runs a client process per account. Client 1 is the launcher's
/// MainActivity; client n is Army{n}Activity in process ":army{n}", in a task of its own so clients
/// switch like apps. Army scripts sync through files in the VibeSkua folder, which every process of
/// the app shares.
/// </summary>
public static class Army
{
    public const int MaxClients = 12;

    /// <summary>Past this many clients the Army page warns about memory and heat.</summary>
    public const int ComfortableClients = 4;

    private const string ProcessSuffix = ":army";
    private static bool _webViewDataSeparated;
    private static string? _lastPublished;

    /// <summary>This process's client number, 1 for the launcher's.</summary>
    public static int Slot { get; } = ReadSlot();

    public static bool IsFirst => Slot == 1;

    public static string Label(int slot) => $"Client {slot}";

    private static readonly (Type Activity, Type Service)[] Slots =
    {
        (typeof(Army2Activity), typeof(Army2Service)),
        (typeof(Army3Activity), typeof(Army3Service)),
        (typeof(Army4Activity), typeof(Army4Service)),
        (typeof(Army5Activity), typeof(Army5Service)),
        (typeof(Army6Activity), typeof(Army6Service)),
        (typeof(Army7Activity), typeof(Army7Service)),
        (typeof(Army8Activity), typeof(Army8Service)),
        (typeof(Army9Activity), typeof(Army9Service)),
        (typeof(Army10Activity), typeof(Army10Service)),
        (typeof(Army11Activity), typeof(Army11Service)),
        (typeof(Army12Activity), typeof(Army12Service)),
    };

    public static Type ActivityType(int slot) => slot == 1 ? typeof(MainActivity) : Slots[slot - 2].Activity;

    /// <summary>The service the first client binds to keep client <paramref name="slot"/>'s process running.</summary>
    public static Type ServiceType(int slot) => Slots[slot - 2].Service;

    private static int ReadSlot()
    {
        string name;
        try
        {
            name = File.ReadAllText("/proc/self/cmdline").TrimEnd('\0');
        }
        catch
        {
            return 1;
        }
        int at = name.LastIndexOf(ProcessSuffix, StringComparison.Ordinal);
        return at >= 0 && int.TryParse(name.AsSpan(at + ProcessSuffix.Length), out int slot) ? slot : 1;
    }

    /// <summary>
    /// Gives this client its own WebView cookies and storage, so each keeps its own login and game
    /// saves. Android refuses a WebView in a second process that shares the first one's data.
    /// </summary>
    public static void SeparateWebViewData()
    {
        if (IsFirst || _webViewDataSeparated || !OperatingSystem.IsAndroidVersionAtLeast(28))
            return;
        _webViewDataSeparated = true;
        global::Android.Webkit.WebView.SetDataDirectorySuffix($"army{Slot}");
    }

    /// <summary>The clients whose process is running, by slot, with their process ids.</summary>
    public static Dictionary<int, int> RunningClients(Context context)
    {
        Dictionary<int, int> running = new();
        if (context.GetSystemService(Context.ActivityService) is not ActivityManager am || am.RunningAppProcesses is not { } processes)
            return running;

        string package = context.PackageName!;
        foreach (ActivityManager.RunningAppProcessInfo process in processes)
        {
            string? name = process.ProcessName;
            if (name == package)
                running[1] = process.Pid;
            else if (name is not null && name.StartsWith(package + ProcessSuffix, StringComparison.Ordinal)
                     && int.TryParse(name.AsSpan(package.Length + ProcessSuffix.Length), out int slot))
                running[slot] = process.Pid;
        }
        return running;
    }

    public static int? NextFreeSlot(Context context)
    {
        Dictionary<int, int> running = RunningClients(context);
        for (int slot = 2; slot <= MaxClients; slot++)
        {
            if (!running.ContainsKey(slot))
                return slot;
        }
        return null;
    }

    /// <summary>Starts client <paramref name="slot"/>, or brings it to the front when it is running.</summary>
    public static void Open(Context context, int slot)
    {
        Intent intent = new(context, ActivityType(slot));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    /// <summary>
    /// Closes client <paramref name="slot"/>: removes its task, and the first client stops keeping its
    /// process alive and ends it.
    /// </summary>
    public static void Close(Context context, int slot)
    {
        if (slot == 1)
            return;

        string activity = Java.Lang.Class.FromType(ActivityType(slot)).Name;
        try
        {
            if (context.GetSystemService(Context.ActivityService) is ActivityManager am && am.AppTasks is { } tasks)
            {
                foreach (ActivityManager.AppTask task in tasks)
                {
                    if (task.TaskInfo?.BaseIntent?.Component?.ClassName == activity)
                        task.FinishAndRemoveTask();
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Army] closing client {slot}'s task failed: {ex.Message}");
        }
        KeepAliveService.Release(context, slot);
    }

    public sealed record ClientStatus(int Slot, bool LoggedIn, string Name, string Map, string Script, bool ScriptRunning);

    private static string StatusDirectory => Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "army");

    private static string StatusPath(int slot) => Path.Combine(StatusDirectory, $"client{slot}.json");

    /// <summary>Writes what this client is doing for the other clients' Army page, when it changed.</summary>
    public static void Publish(ClientStatus status)
    {
        string json = JsonSerializer.Serialize(status);
        if (json == _lastPublished)
            return;
        try
        {
            Directory.CreateDirectory(StatusDirectory);
            string path = StatusPath(status.Slot);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
            _lastPublished = json;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Army] status write failed: {ex.Message}");
        }
    }

    public static ClientStatus? ReadStatus(int slot)
    {
        try
        {
            string path = StatusPath(slot);
            return File.Exists(path) ? JsonSerializer.Deserialize<ClientStatus>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }
}
