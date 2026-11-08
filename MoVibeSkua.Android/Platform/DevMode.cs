using Android.Content;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Developer mode, off for players: shows the developer tools (Console, Packet Spammer and Logger, the
/// Debug and Flash log tabs), the detailed FPS readout, and lets a PC debug the game page. Turned on or
/// off by tapping the version line on the Support page seven times; takes full effect after a restart.
/// </summary>
public static class DevMode
{
    private const string Key = "devMode";

    private static ISharedPreferences? Prefs =>
        global::Android.App.Application.Context.GetSharedPreferences("movibeskua.shell", FileCreationMode.Private);

    public static bool IsOn => Prefs?.GetBoolean(Key, false) ?? false;

    public static bool Toggle()
    {
        bool on = !IsOn;
        Prefs?.Edit()?.PutBoolean(Key, on)?.Apply();
        System.Diagnostics.Trace.WriteLine($"[App] developer mode {(on ? "on" : "off")}");
        return on;
    }
}
