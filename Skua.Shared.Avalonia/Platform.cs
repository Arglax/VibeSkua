namespace Skua.Shared.Avalonia;

/// <summary>Platform checks usable from XAML via {x:Static}.</summary>
public static class Platform
{
    public static bool IsDesktop => !OperatingSystem.IsAndroid();
}
