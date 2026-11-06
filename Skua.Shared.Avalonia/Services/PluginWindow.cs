using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;

namespace Skua.Shared.Avalonia.Services;

/// <summary>
/// Shows pages for a host that has no windows, such as MoVibeSkua on Android, where a <see cref="Window"/>
/// can't even be created.
/// </summary>
public interface IPluginPageHost
{
    /// <summary>Shows the view as a page, or brings it to the front. <paramref name="closed"/> runs once, when the page is closed.</summary>
    void Show(string key, string title, Control view, Action closed);

    void Close(string key);
}

/// <summary>
/// A plugin's view in a window of its own, or as a page where the host has no windows. Plugins open
/// their views through this instead of deriving from <see cref="Window"/>, so one build of the plugin
/// runs on both.
/// </summary>
public sealed class PluginWindow
{
    /// <summary>Set by a host without windows; null on desktop.</summary>
    public static IPluginPageHost? PageHost { get; set; }

    private readonly Window? _window;
    private readonly Control? _page;
    private readonly string _key;
    private readonly string _title;
    private bool _closed;

    /// <summary>Raised once, when the window or page is closed.</summary>
    public event EventHandler? Closed;

    public Control View { get; }

    private PluginWindow(Control view, string title, double width, double height, double minWidth, double minHeight, bool ownTitleBar)
    {
        View = view;
        _title = title;
        _key = "plugin:" + title + "#" + view.GetHashCode();
        if (PageHost is not null)
        {
            _page = PageContent(view, minHeight);
            return;
        }

        // The view's own WindowTitleBar draws the title row and the window buttons; otherwise the
        // system's title bar does.
        _window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = minWidth,
            MinHeight = minHeight,
            Content = view,
        };
        if (ownTitleBar)
        {
            _window.SystemDecorations = SystemDecorations.BorderOnly;
            _window.ExtendClientAreaToDecorationsHint = true;
            _window.ExtendClientAreaChromeHints = global::Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            _window.ExtendClientAreaTitleBarHeightHint = 32;
        }
        _window.Classes.Add("skua");
        _window[!Window.BackgroundProperty] = new DynamicResourceExtension("SkuaPageBrush");
        _window.Closed += (_, _) => OnClosed();
        WindowPlacementHelper.CenterOnActiveScreen(_window);
    }

    /// <summary>
    /// Opens <paramref name="view"/>: in a window of the given size on desktop, as a page titled
    /// <paramref name="title"/> otherwise. Call on the UI thread.
    /// </summary>
    public static PluginWindow Show(Control view, string title, double width, double height, double minWidth = 0, double minHeight = 0, bool ownTitleBar = false)
    {
        PluginWindow window = new(view, title, width, height, minWidth, minHeight, ownTitleBar);
        window.Activate();
        return window;
    }

    /// <summary>Brings the window or page to the front.</summary>
    public void Activate()
    {
        if (_closed)
            return;
        if (_window is null)
        {
            PageHost?.Show(_key, _title, _page!, OnClosed);
            return;
        }
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Close()
    {
        if (_window is not null)
            _window.Close();
        else
            PageHost?.Close(_key);
    }

    // A phone page is often shorter than the window was ever allowed to be, and squeezing the view
    // collapses its lists to nothing. Below the window's minimum height the page scrolls instead; the
    // view still gets a fixed height, so its own lists keep scrolling (and virtualizing) inside it.
    private static Control PageContent(Control view, double minHeight)
    {
        ScrollViewer scroller = new()
        {
            Content = view,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        scroller.SizeChanged += (_, e) => view.Height = Math.Max(minHeight, e.NewSize.Height);
        return scroller;
    }

    private void OnClosed()
    {
        if (_closed)
            return;
        _closed = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
