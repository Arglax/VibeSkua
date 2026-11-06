using System;
using System.Collections.Generic;

namespace MoVibeSkua.Android.Usage;

/// <summary>
/// Every counter the app sends; the names are the Firestore field names. The rules only accept the
/// names listed here (tools/generate_firestore_rules.py reads this file), so a new signal needs the
/// rules merged and deployed before an APK that sends it ships. Keep one name per line.
/// </summary>
public enum Signal
{
    SYS_SESSION_START,
    SYS_CRASH,
    SYS_RENDERER_GONE,
    SYS_LAYOUT_LANDSCAPE,
    SYS_LAYOUT_PORTRAIT,

    LOGIN_DONE,
    RELOGIN,
    RELOGIN_KICKED,

    SCRIPT_STARTED,
    SCRIPT_STOPPED,
    SCRIPT_ERROR,

    NAV_RAIL,
    NAV_MENU,
    GAME_MAXIMIZED,
    GAME_KEYBOARD,
    PAGE_WIDE,
    PAGE_RESIZED,
    STREAMER_ON,
    STREAMER_OFF,

    ARMY_ADD,
    ARMY_SWITCH,
    ARMY_CLOSE,
    ARMY_FULL,
    ARMY_LOW_MEMORY,
    ARMY_CLIENT_START,
    ARMY_SIZE_2,
    ARMY_SIZE_3,
    ARMY_SIZE_4,
    ARMY_SIZE_5_7,
    ARMY_SIZE_8_PLUS,
    LOGIN_REFUSED,
    LOGIN_QUICK,
    ACCOUNT_SAVED,
    AUTO_ATTACK_START,
    AUTO_HUNT_START
}

/// <summary>
/// The pages whose open time is measured. GAME is the game with no page over it.
/// </summary>
public enum Screen
{
    GAME,
    LOGIN,
    SCRIPTS,
    SCHEDULER,
    SCRIPT_REPO,
    LOADOUTS,
    LOGS,
    ARMY,
    AUTO,
    JUMP,
    LOADER,
    GRABBER,
    GAME_OPTIONS,
    APP_OPTIONS,
    COREBOTS,
    THEMES,
    RUNTIME,
    NOTIFY_DROP,
    FAST_TRAVEL,
    CURRENT_DROPS,
    JUNK_ITEMS,
    STATS,
    CONSOLE,
    DIAGNOSTICS,
    SKILLS,
    PACKET_SPAMMER,
    PACKET_LOGGER,
    PLUGINS,
    OTHER
}

public static class Screens
{
    // Shell page keys (managed window keys and the shell's own pages). Plugin pages and anything
    // else count as OTHER, so no plugin or script name ever leaves the phone.
    private static readonly Dictionary<string, Screen> ByPageKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Login"] = Screen.LOGIN,
        ["Scripts"] = Screen.SCRIPTS,
        ["Script Loader"] = Screen.SCRIPTS,
        ["Scheduler"] = Screen.SCHEDULER,
        ["Script Repo"] = Screen.SCRIPT_REPO,
        ["Loadouts"] = Screen.LOADOUTS,
        ["Loadouts Manager"] = Screen.LOADOUTS,
        ["Logs"] = Screen.LOGS,
        ["Army"] = Screen.ARMY,
        ["Auto"] = Screen.AUTO,
        ["Jump"] = Screen.JUMP,
        ["Loader"] = Screen.LOADER,
        ["Grabber"] = Screen.GRABBER,
        ["Game"] = Screen.GAME_OPTIONS,
        ["Application"] = Screen.APP_OPTIONS,
        ["CoreBots"] = Screen.COREBOTS,
        ["Application Themes"] = Screen.THEMES,
        ["Runtime"] = Screen.RUNTIME,
        ["Notify Drop"] = Screen.NOTIFY_DROP,
        ["Fast Travel"] = Screen.FAST_TRAVEL,
        ["Current Drops"] = Screen.CURRENT_DROPS,
        ["Junk Items"] = Screen.JUNK_ITEMS,
        ["Stats"] = Screen.STATS,
        ["Console"] = Screen.CONSOLE,
        ["Diagnostics"] = Screen.DIAGNOSTICS,
        ["Skills"] = Screen.SKILLS,
        ["Spammer"] = Screen.PACKET_SPAMMER,
        ["Logger"] = Screen.PACKET_LOGGER,
        ["Plugins"] = Screen.PLUGINS,
    };

    public static Screen OfPage(string? key) =>
        key is null ? Screen.GAME : ByPageKey.TryGetValue(key, out Screen screen) ? screen : Screen.OTHER;
}
