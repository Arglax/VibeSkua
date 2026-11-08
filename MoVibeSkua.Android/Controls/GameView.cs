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
    private static string? _lastUrl;
    private static double _visibleFraction = 1;
    private static double _visibleLeft;

    public static AWebView? WebView => _webView;

    // Avalonia's peer for native hosts throws when Android's accessibility bridge asks for its
    // children, which killed the app whenever an accessibility service (TalkBack, password managers,
    // UI automation) read the screen.
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    public static void Navigate(string url)
    {
        _lastUrl = url;
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
    /// Draws only the left part of the game, so a tool page can lie over the rest without the game
    /// being resized. Avalonia draws beneath the native view, so the game has to leave that part empty.
    /// </summary>
    public static void SetVisibleFraction(double fraction) => SetVisibleRange(_visibleLeft, fraction);

    /// <summary>
    /// Draws only the game between two fractions of its width: the menu lies over the left part and a
    /// tool page over the right, without the game being resized or moved.
    /// </summary>
    public static void SetVisibleRange(double left, double right)
    {
        right = Math.Clamp(right, 0, 1);
        left = Math.Clamp(left, 0, right);
        if (right == _visibleFraction && left == _visibleLeft)
            return;
        _visibleFraction = right;
        _visibleLeft = left;
        AWebView? view = _webView;
        view?.Post(() => ApplyClip(view));
    }

    private static int VisibleWidth(AView view) =>
        _visibleFraction >= 1 ? view.Width : (int)Math.Round(view.Width * _visibleFraction);

    private static int VisibleLeft(AView view) =>
        _visibleLeft <= 0 ? 0 : (int)Math.Round(view.Width * _visibleLeft);

    private static void ApplyClip(AView view)
    {
        view.ClipBounds = _visibleFraction >= 1 && _visibleLeft <= 0
            ? null
            : new Rect(VisibleLeft(view), 0, VisibleWidth(view), view.Height);
    }

    /// <summary>
    /// Whether a screen point lands on the shown part of the game.
    /// </summary>
    public static bool Contains(float screenX, float screenY)
    {
        AWebView? view = _webView;
        if (view is null || !view.IsShown || view.Alpha == 0f)
            return false;
        int[] location = new int[2];
        view.GetLocationOnScreen(location);
        return screenX >= location[0] + VisibleLeft(view) && screenX < location[0] + VisibleWidth(view)
            && screenY >= location[1] && screenY < location[1] + view.Height;
    }

    /// <summary>
    /// Shows the on-screen keyboard for the game text field that has focus.
    /// </summary>
    public static void ShowKeyboard()
    {
        Usage.UsageTracker.Track(Usage.Signal.GAME_KEYBOARD);
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
        // Released with the activity that showed it (the activity was destroyed and created again):
        // start over and load the game again.
        if (_webView is { Handle: 0 })
        {
            _webView = null;
            _pendingUrl ??= _lastUrl;
        }
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
        // Keep the WebView (and the running game) alive; just detach it. When the activity is going
        // away Avalonia may have released the view already, and touching it would crash the app.
        if (_webView is { Handle: not 0, Parent: global::Android.Views.ViewGroup parent })
            parent.RemoveView(_webView);
    }

    private static AWebView CreateWebView(Context context)
    {
        // A PC can inspect the game page only in developer mode (Support page).
        AWebView.SetWebContentsDebuggingEnabled(Platform.DevMode.IsOn);
        AWebView view = new GameWebView(context);
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

        // A game in the background (screen off, or an army client not on screen) keeps playing, so
        // its renderer must not be the first thing Android kills to free memory.
        view.SetRendererPriorityPolicy(RendererPriority.Important, false);

        view.SetWebViewClient(new GameWebViewClient());
        view.SetWebChromeClient(new GameChromeClient());
        return view;
    }

    private sealed class GameWebView : AWebView
    {
        public GameWebView(Context context) : base(context)
        {
        }

        public GameWebView(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer) : base(handle, transfer)
        {
        }

        // The game must keep running when MVS is out of sight (the bot plays on). A WebView in a hidden
        // window marks its page hidden, and Chromium suspends a hidden page after a few minutes: the
        // game and every bot call stop (seen on the phone after ~5 min in the background). The page is
        // told about the background by the app instead (bridge "background": no drawing, muted, Ruffle's
        // background ticking).
        protected override void OnWindowVisibilityChanged(global::Android.Views.ViewStates visibility)
            => base.OnWindowVisibilityChanged(global::Android.Views.ViewStates.Visible);

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            ApplyClip(this);
        }

        // A touch that starts on the hidden part is refused, so the parent hands it to Avalonia's view,
        // where the page lying over the game is. So is any touch while a dialog covers the game (it is
        // only made transparent): tapping a dialog's OK also clicked the game button beneath it.
        public override bool DispatchTouchEvent(global::Android.Views.MotionEvent? e)
        {
            if (e?.ActionMasked == global::Android.Views.MotionEventActions.Down
                && (Alpha == 0f || e.GetX() >= VisibleWidth(this) || e.GetX() < VisibleLeft(this)))
                return false;
            return base.DispatchTouchEvent(e);
        }
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
            Usage.UsageTracker.Track(Usage.Signal.SYS_RENDERER_GONE);
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
