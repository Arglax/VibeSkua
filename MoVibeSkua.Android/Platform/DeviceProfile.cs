using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content;
using Android.OS;
using Trace = System.Diagnostics.Trace;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// What this phone can do, read once at start: CPU cores and their top clock, memory, Android
/// version, screen refresh rates and whether Android reports thermal headroom. MVS's starting
/// settings follow from it instead of one test phone (a Dimensity 8300): a fast phone starts at full
/// resolution with smooth motion; a slower or smaller one starts lighter, so it runs the game and the
/// bot steadily without the player tuning anything. Anything the player sets on the Graphics page
/// wins over these starting values.
/// </summary>
public sealed class DeviceProfile
{
    public enum Tiers { Low, Mid, High }

    public int Cores { get; private init; }
    /// <summary>Cores whose top clock is at least 2.4 GHz (the "big" cores the game thread wants).</summary>
    public int BigCores { get; private init; }
    public double MaxGHz { get; private init; }
    public double RamGB { get; private init; }
    public bool LowRamDevice { get; private init; }
    public int Sdk { get; private init; }
    public float MaxRefreshHz { get; private init; }
    public bool HasThermalHeadroom { get; private init; }
    /// <summary>A phone rather than a tablet (shortest screen side under 600 dp).</summary>
    public bool IsPhone { get; private init; }
    public Tiers Tier { get; private init; }

    private static DeviceProfile? _current;

    public static DeviceProfile Current => _current ??= Read();

    private static DeviceProfile Read()
    {
        Context context = global::Android.App.Application.Context;
        int cores = System.Environment.ProcessorCount;
        double[] clocks = Enumerable.Range(0, Math.Max(cores, 1)).Select(MaxClockGHz).Where(c => c > 0).ToArray();
        double maxGHz = clocks.Length > 0 ? clocks.Max() : 0;
        int big = clocks.Count(c => c >= 2.4);

        double ramGB = 0;
        bool lowRam = false;
        if (context.GetSystemService(Context.ActivityService) is ActivityManager am)
        {
            ActivityManager.MemoryInfo memory = new();
            am.GetMemoryInfo(memory);
            ramGB = memory.TotalMem / (1024d * 1024 * 1024);
            lowRam = am.IsLowRamDevice;
        }

        float maxHz = 60;
        try
        {
            if (context.GetSystemService(Context.DisplayService) is global::Android.Hardware.Display.DisplayManager displays
                && displays.GetDisplay(0) is { } display)
                maxHz = display.GetSupportedModes()?.Max(m => m.RefreshRate) ?? display.RefreshRate;
        }
        catch
        {
        }

        int smallestDp = context.Resources?.Configuration?.SmallestScreenWidthDp ?? 0;
        bool phone = smallestDp is > 0 and < 600;

        int sdk = (int)Build.VERSION.SdkInt;
        // Unknown clocks (some phones hide them) count as mid-range rather than slow.
        double clock = maxGHz > 0 ? maxGHz : 2.6;
        Tiers tier = lowRam || ramGB < 4.5 || cores <= 4 || clock < 2.2 ? Tiers.Low
            : clock >= 2.8 && ramGB >= 7.5 && cores >= 8 ? Tiers.High
            : Tiers.Mid;

        DeviceProfile profile = new()
        {
            Cores = cores,
            BigCores = big,
            MaxGHz = maxGHz,
            RamGB = ramGB,
            LowRamDevice = lowRam,
            Sdk = sdk,
            MaxRefreshHz = maxHz,
            HasThermalHeadroom = sdk >= 30,
            IsPhone = phone,
            Tier = tier
        };
        Trace.WriteLine($"[Device] {Build.Manufacturer} {Build.Model} ({Build.Hardware}): {cores} cores, {big} big, up to {maxGHz:0.00} GHz, " +
                        $"{ramGB:0.0} GB{(lowRam ? " low-RAM" : "")}, Android API {sdk}, {maxHz:0} Hz, {(phone ? "phone" : "tablet")} ({smallestDp} dp) → {tier} tier");
        return profile;
    }

    // cpuinfo_max_freq is in kHz and readable by apps on most phones; 0 when hidden.
    private static double MaxClockGHz(int core)
    {
        try
        {
            string text = File.ReadAllText($"/sys/devices/system/cpu/cpu{core}/cpufreq/cpuinfo_max_freq").Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long khz) ? khz / 1_000_000d : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ---- Starting values (the Graphics page's saved choices win over these) ----

    /// <summary>Render resolution: the screen's own (capped at 3x) on fast phones, lighter below.</summary>
    public string DefaultResolution => Tier switch { Tiers.High => "0", Tiers.Mid => "2", _ => "1.5" };

    /// <summary>Anti-aliasing: the game's own choice on fast phones, 2x on mid-range, off on slow ones.</summary>
    public string DefaultAntiAlias => Tier switch { Tiers.High => "", Tiers.Mid => "MEDIUM", _ => "LOW" };

    /// <summary>Smooth motion (in-between pictures) costs processor time a slower phone doesn't have.</summary>
    public bool DefaultSmoothMotion => Tier == Tiers.High;

    /// <summary>The most the game page may render at before the Graphics settings arrive.</summary>
    public double StartupPixelRatio => Tier switch { Tiers.High => 3, Tiers.Mid => 2, _ => 1.5 };

    /// <summary>
    /// How many game clients fit comfortably: each takes about 0.9 GB, and Android plus other apps
    /// need about 3 GB.
    /// </summary>
    public int ComfortableClients => Math.Clamp((int)Math.Floor((RamGB - 3) / 0.9), 1, Army.MaxClients);
}
