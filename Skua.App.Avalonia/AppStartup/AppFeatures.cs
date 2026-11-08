namespace Skua.App.Avalonia.AppStartup;

/// <summary>Switches the host app sets before start-up (MVS turns the developer tools off for players).</summary>
public static class AppFeatures
{
    /// <summary>The Debug and Flash log tabs (Logs page). On by default (desktop).</summary>
    public static bool DeveloperTools { get; set; } = true;
}
