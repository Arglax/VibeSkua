using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AView = Android.Views.View;
using AWebView = Android.Webkit.WebView;
using Android.Content;
using Android.Graphics;
using Android.Views.InputMethods;
using Android.Webkit;
using Avalonia.Android;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Platform;

namespace MoVibeSkua.Android.Controls;

/// <summary>
/// Hosts the Android WebView that runs Ruffle. The WebView is created once and survives the control
/// being re-hosted, so the game never reloads because of a layout change.
/// </summary>
public class GameView : NativeControlHost
{
    private static AWebView? _webView;
    private static string? _pendingUrl;

    public static AWebView? WebView => _webView;

    // Avalonia's peer for native hosts throws when Android's accessibility bridge asks for its
    // children, which killed the app whenever an accessibility service (TalkBack, password managers,
    // UI automation) read the screen.
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    public static void Navigate(string url)
    {
        if (_webView is null)
        {
            _pendingUrl = url;
            return;
        }
        _webView.Post(() => _webView.LoadUrl(url));
    }

    /// <summary>
    /// Shows or hides the native view. Avalonia popups and dialogs draw under native views, so the
    /// shell hides the game while one is open. INVISIBLE keeps the page running.
    /// </summary>
    public static void SetCovered(bool covered)
    {
        AWebView? view = _webView;
        view?.Post(() => view.Alpha = covered ? 0f : 1f);
    }

    /// <summary>
    /// Whether a screen point lands on the game while it is showing.
    /// </summary>
    public static bool Contains(float screenX, float screenY)
    {
        AWebView? view = _webView;
        if (view is null || !view.IsShown || view.Alpha == 0f)
            return false;
        int[] location = new int[2];
        view.GetLocationOnScreen(location);
        return screenX >= location[0] && screenX < location[0] + view.Width
            && screenY >= location[1] && screenY < location[1] + view.Height;
    }

    /// <summary>
    /// Shows the on-screen keyboard for the game text field that has focus.
    /// </summary>
    public static void ShowKeyboard()
    {
        AWebView? view = _webView;
        view?.Post(() =>
        {
            if (!view.HasFocus)
                view.RequestFocus();
            InputMethodManager? imm = view.Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
            imm?.ShowSoftInput(view, 0);
        });
    }

    public static void Resume()
    {
        AWebView? view = _webView;
        view?.Post(() =>
        {
            view.OnResume();
            view.ResumeTimers();
        });
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Context context = (parent as AndroidViewControlHandle)?.View.Context ?? global::Android.App.Application.Context;
        if (_webView is null)
        {
            _webView = CreateWebView(context);
            if (_pendingUrl is not null)
            {
                _webView.LoadUrl(_pendingUrl);
                _pendingUrl = null;
            }
        }
        else if (_webView.Parent is global::Android.Views.ViewGroup oldParent)
        {
            oldParent.RemoveView(_webView);
        }

        return new AndroidViewControlHandle(_webView);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Keep the WebView (and the running game) alive; just detach it.
        if (_webView?.Parent is global::Android.Views.ViewGroup parent)
            parent.RemoveView(_webView);
    }

    private static AWebView CreateWebView(Context context)
    {
        AWebView.SetWebContentsDebuggingEnabled(true);
        AWebView view = new(context);
        view.SetBackgroundColor(Color.Black);
        view.Focusable = true;
        view.FocusableInTouchMode = true;

        WebSettings s = view.Settings;
        s.JavaScriptEnabled = true;
        s.DomStorageEnabled = true;
        s.DatabaseEnabled = true;
        s.MediaPlaybackRequiresUserGesture = false;
        s.MixedContentMode = MixedContentHandling.AlwaysAllow;
        s.LoadWithOverviewMode = true;
        s.UseWideViewPort = false;
        s.SetSupportZoom(false);
        s.BuiltInZoomControls = false;
        s.DisplayZoomControls = false;
        s.CacheMode = CacheModes.Default;
        s.OffscreenPreRaster = false;

        view.SetWebViewClient(new GameWebViewClient());
        view.SetWebChromeClient(new GameChromeClient());
        return view;
    }

    private sealed class GameWebViewClient : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
        {
            global::Android.Net.Uri? uri = request?.Url;
            if (uri is null || uri.Host == "127.0.0.1")
                return false;

            // Wiki, char page and other links leave the app.
            try
            {
                Intent intent = new(Intent.ActionView, uri);
                intent.AddFlags(ActivityFlags.NewTask);
                view?.Context?.StartActivity(intent);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GameView] open link failed: {ex.Message}");
            }
            return true;
        }

        public override bool OnRenderProcessGone(AWebView? view, RenderProcessGoneDetail? detail)
        {
            // Returning false would kill the app with the renderer. Recreate the page instead.
            Trace.WriteLine("[GameView] renderer process gone; reloading");
            if (view is not null)
            {
                string? url = view.Url;
                if (view.Parent is global::Android.Views.ViewGroup parent)
                    parent.RemoveView(view);
                view.Destroy();
                _webView = null;
                _pendingUrl = url;
            }
            GameViewRecovery.RequestRebuild();
            return true;
        }
    }

    private sealed class GameChromeClient : WebChromeClient
    {
        public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
        {
            if (consoleMessage is not null && consoleMessage.InvokeMessageLevel() != ConsoleMessage.MessageLevel.Log)
                Trace.WriteLine($"[WebView] {consoleMessage.InvokeMessageLevel()}: {consoleMessage.Message()}");
            return true;
        }
    }
}

/// <summary>
/// Lets the shell re-create the game host after the WebView renderer died.
/// </summary>
public static class GameViewRecovery
{
    public static event Action? RebuildRequested;

    public static void RequestRebuild() => RebuildRequested?.Invoke();
}
