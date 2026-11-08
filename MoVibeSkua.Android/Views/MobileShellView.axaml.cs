using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MoVibeSkua.Android.Controls;
using MoVibeSkua.Android.ViewModels;

namespace MoVibeSkua.Android.Views;

public partial class MobileShellView : UserControl
{
    private const double GameAspect = 958d / 550d;
    private static readonly HashSet<Popup> _openPopups = new();

    public static MobileShellView? Instance { get; private set; }

    static MobileShellView()
    {
        // Avalonia popups draw beneath native views; hide the game while one lies over it. A list that
        // opens on a tool page (the Auto page's class list) stays beside the game, so the game keeps
        // drawing instead of going black. The popup's place is known only after layout, so check again then.
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (e.GetNewValue<bool>())
                _openPopups.Add(popup);
            else
                _openPopups.Remove(popup);
            Instance?.UpdateCovered();
            Dispatcher.UIThread.Post(() => Instance?.UpdateCovered(), DispatcherPriority.Background);
            DispatcherTimer.RunOnce(() => Instance?.UpdateCovered(), TimeSpan.FromMilliseconds(150));
        });
    }

    private ShellViewModel? _vm;
    private TopLevel? _topLevel;
    private Thickness _safeArea;
    private double _keyboardHeight;
    private bool? _landscape;

    public MobileShellView()
    {
        InitializeComponent();
        Instance = this;
        Root.SizeChanged += (_, _) =>
        {
            UpdateLayoutMode();
            UpdateRailScale();
        };
        Body.SizeChanged += (_, _) => UpdateBodyLayout();
        Body.LayoutUpdated += (_, _) => UpdateGameClip();
        GameFrame.SizeChanged += (_, _) => FitGame();
        // The menu lies over the game (Overlay) instead of pushing it narrower; the game hides the part
        // under the menu instead of being resized.
        Split.PropertyChanged += (_, e) =>
        {
            if (e.Property == SplitView.IsPaneOpenProperty || e.Property == SplitView.OpenPaneLengthProperty)
                Dispatcher.UIThread.Post(UpdateGameClip, DispatcherPriority.Background);
        };
        GameViewRecovery.RebuildRequested += () => Dispatcher.UIThread.Post(RebuildGameView);
        PageGrip.PointerPressed += OnGripPressed;
        PageGrip.PointerMoved += OnGripMoved;
        PageGrip.PointerReleased += (_, e) => EndGripDrag(e.Pointer);
        PageGrip.PointerCaptureLost += (_, e) => EndGripDrag(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ShellViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyRailSource();
        ShowCurrentPageView();
        UpdateBodyLayout();
        StartPreparingPages();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is null)
            return;

        _topLevel.BackRequested += OnBackRequested;

        // Use the whole screen: no status/navigation bars, content only kept clear of the camera cutout.
        if (_topLevel.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdge = true;
            insets.IsSystemBarVisible = false;
            insets.SafeAreaChanged += OnSafeAreaChanged;
            _safeArea = insets.SafeAreaPadding;
        }

        // The keyboard draws over the app (edge-to-edge), so lift the shell above it ourselves.
        // StateChanged fires once per show/hide, so the game is not re-laid out every animation frame.
        if (_topLevel.InputPane is { } pane)
            pane.StateChanged += OnInputPaneChanged;

        ApplyInsets();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.BackRequested -= OnBackRequested;
            if (_topLevel.InsetsManager is { } insets)
                insets.SafeAreaChanged -= OnSafeAreaChanged;
            if (_topLevel.InputPane is { } pane)
                pane.StateChanged -= OnInputPaneChanged;
        }
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e)
    {
        _safeArea = e.SafeAreaPadding;
        ApplyInsets();
        UpdateRailScale();
    }

    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        _keyboardHeight = e.NewState == InputPaneState.Open ? e.EndRect.Height : 0;
        ApplyInsets();
    }

    // Rail sizes from MobileStyles; set here instead because the cutout strip widens them.
    private const double RailWidth = 68;
    private const double BottomBarHeight = 60;

    /// <summary>How much larger the landscape rails (and their buttons) are drawn than RailWidth.</summary>
    private double _railScale = 1;

    /// <summary>
    /// In landscape the game keeps its shape, so a wide screen leaves empty bands beside it. The rails
    /// grow into those bands (up to 1.3x, buttons and labels scaled with them) instead of standing
    /// apart from the game; a screen with no spare width keeps the normal size.
    /// </summary>
    private void UpdateRailScale()
    {
        double scale = 1;
        if (_landscape != false && Root.Bounds.Height > 0)
        {
            double spare = Root.Bounds.Width - Root.Bounds.Height * GameAspect - _safeArea.Left - _safeArea.Right;
            scale = Math.Clamp(Math.Floor(spare / (2 * RailWidth) * 50) / 50, 1, 1.3);
        }
        if (Math.Abs(scale - _railScale) < 0.01)
            return;
        _railScale = scale;
        ApplyInsets();
    }

    /// <summary>
    /// The bar along a cutout's edge grows into the strip and keeps its buttons clear of the camera,
    /// so the strip shows the bar's backdrop instead of an empty black band.
    /// </summary>
    private void ApplyInsets()
    {
        Thickness s = _safeArea;
        if (_landscape != false)
        {
            // The keyboard no longer shrinks the shell (that resized and redrew the game). A page's text
            // box lifts only the page; a game text field slides the game up, same size, when it's low.
            Root.Margin = new Thickness(0, s.Top, 0, s.Bottom);
            ApplyKeyboardLandscape();
            LeftRailFrame.Width = RailWidth * _railScale + s.Left;
            LeftRailFrame.Padding = new Thickness(2 + s.Left, 4, 2, 4);
            RightRailFrame.Width = RailWidth * _railScale + s.Right;
            LeftRailScale.LayoutTransform = RightRailScale.LayoutTransform =
                _railScale > 1 ? new Avalonia.Media.ScaleTransform(_railScale, _railScale) : null;
            RightRailFrame.Height = double.NaN;
            RightRailFrame.Padding = new Thickness(2, 4, 2 + s.Right, 4);
            TopBar.Padding = new Thickness(2, 2, 6, 2);
            TopBar.MinHeight = 48;
        }
        else
        {
            GameFrame.Margin = default;
            PageFrame.Margin = default;
            // The keyboard covers the bottom bar, so then the whole shell moves up instead.
            double bottom = _keyboardHeight > 0 ? 0 : s.Bottom;
            Root.Margin = new Thickness(s.Left, 0, s.Right, _keyboardHeight);
            TopBar.Padding = new Thickness(2, 2 + s.Top, 6, 2);
            TopBar.MinHeight = 48 + s.Top;
            RightRailFrame.Width = double.NaN;
            RightRailScale.LayoutTransform = null;
            RightRailFrame.Height = BottomBarHeight + bottom;
            RightRailFrame.Padding = new Thickness(4, 2, 4, 2 + bottom);
        }
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DialogLayer.Children.Count > 0)
            return;
        if (_vm?.HandleBack() == true)
            return;

        // Nothing left to close: go to the background like Home does, so the game and any running
        // script keep going instead of the activity being torn down.
        MainActivity.Current?.MoveTaskToBack(true);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
            ShowCurrentPageView();
        if (e.PropertyName is nameof(ShellViewModel.HasPage) or nameof(ShellViewModel.IsGameMaximized) or nameof(ShellViewModel.CurrentPage)
            or nameof(ShellViewModel.IsPageWide))
            UpdateBodyLayout();
    }

    // Page views stay in PageHost after their first open and are only hidden. A bound ContentControl
    // built the whole view again on every open, and even a kept view put back into the tree had its
    // styles and templates applied again: about a second for Options on the phone. The least recently
    // shown views beyond MaxKeptPages are dropped, so pages opened once don't pile up.
    private const int MaxKeptPages = 12;
    private readonly Dictionary<object, Control> _pageViews = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _pageOrder = new();
    private object? _shownContent;

    private void ShowCurrentPageView()
    {
        ShellPage? page = _vm?.CurrentPage;
        object? content = page?.Content;
        if (ReferenceEquals(content, _shownContent))
            return;
        _shownContent = content;

        Stopwatch sw = Stopwatch.StartNew();
        Control? view = null;
        bool built = false;
        if (content is not null && !_pageViews.TryGetValue(content, out view))
        {
            built = true;
            view = BuildPageView(content);
        }
        if (content is not null)
        {
            _pageOrder.Remove(content);
            _pageOrder.Add(content);
            DropOldPageViews();
        }

        foreach (Control child in PageHost.Children)
            child.IsVisible = child == view;

        if (page is null)
            return;
        string key = page.Key;
        // Background priority runs after the layout and render passes that show the page.
        Dispatcher.UIThread.Post(() => Trace.WriteLine($"[Page] {key} shown in {sw.ElapsedMilliseconds} ms ({(built ? "built" : "kept view")})"),
            DispatcherPriority.Background);
    }

    private Control BuildPageView(object content)
    {
        Control view = content as Control ?? this.FindDataTemplate(content)?.Build(content) ?? new TextBlock { Text = content.GetType().Name };
        if (view != content)
            view.DataContext = content;
        view.IsVisible = false;
        _pageViews[content] = view;
        PageHost.Children.Add(view);
        return view;
    }

    // Bot pages are built ahead of the first tap (see ShellViewModel.BotPagesToPrepare), one per idle
    // moment while the game loads, and measured once off screen so their templates are applied too.
    private bool _prepareStarted;

    private void StartPreparingPages()
    {
        if (_prepareStarted || _vm is null)
            return;
        _prepareStarted = true;
        Queue<object>? queue = null;
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
        int waited = 0;
        timer.Tick += (_, _) =>
        {
            // Start a few seconds in, after the shell's own first layout and the game page.
            if (++waited < 5)
                return;
            try
            {
                queue ??= new Queue<object>(_vm?.BotPagesToPrepare() ?? new List<object>());
                while (queue.Count > 0 && _pageViews.ContainsKey(queue.Peek()))
                    queue.Dequeue();
                if (queue.Count == 0)
                {
                    timer.Stop();
                    Trace.WriteLine("[Page] bot pages prepared");
                    return;
                }
                object content = queue.Dequeue();
                Stopwatch sw = Stopwatch.StartNew();
                Control view = BuildPageView(content);
                // Least recently used, so pages the player opened stay kept first.
                _pageOrder.Insert(0, content);
                double w = PageHost.Bounds.Width > 100 ? PageHost.Bounds.Width : 900;
                double h = PageHost.Bounds.Height > 100 ? PageHost.Bounds.Height : 1000;
                // Lay it out and draw it once into an off-screen image: the first real show then finds its
                // templates, text and drawing code ready (measuring alone left 0.25-0.45 s of first-show work).
                // Shown and hidden again within this one step, so nothing reaches the screen.
                view.IsVisible = true;
                try
                {
                    view.Measure(new Size(w, h));
                    view.Arrange(new Rect(0, 0, w, h));
                    using RenderTargetBitmap image = new(new PixelSize((int)w, (int)h));
                    image.Render(view);
                }
                finally
                {
                    view.IsVisible = ReferenceEquals(content, _shownContent);
                }
                Trace.WriteLine($"[Page] prepared {content.GetType().Name} in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Page] prepare failed: {ex.Message}");
            }
        };
        timer.Start();
    }

    private void DropOldPageViews()
    {
        HashSet<object> open = new(_vm?.Pages.Select(p => p.Content) ?? Enumerable.Empty<object>(), ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _pageOrder.Count && _pageOrder.Count > MaxKeptPages; )
        {
            object old = _pageOrder[i];
            if (open.Contains(old))
            {
                i++;
                continue;
            }
            _pageOrder.RemoveAt(i);
            if (_pageViews.Remove(old, out Control? oldView))
                PageHost.Children.Remove(oldView);
        }
    }

    /// <summary>
    /// Landscape: [left rail | menu | game + page | right rail], game at full height.
    /// Portrait: top bar, game and page stacked, quick tools along the bottom.
    /// </summary>
    private void UpdateLayoutMode()
    {
        bool landscape = Root.Bounds.Width >= Root.Bounds.Height;
        if (_landscape == landscape)
            return;
        _landscape = landscape;

        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();
        TopBar.IsVisible = !landscape;
        LeftRailFrame.IsVisible = landscape;
        MaximizeButton.IsVisible = landscape;
        GuideSlot.IsVisible = landscape;
        ApplyRailSource();
        RightRailFrame.Classes.Set("bottom", !landscape);

        if (landscape)
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Place(LeftRailFrame, 0, 0);
            Place(Split, 0, 1);
            Place(RightRailFrame, 0, 2);
            // Equal shares of the height: the rail fits exactly, no scrolling.
            RightRailItems.ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = 1 });
            RightRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            RightRailScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Split.OpenPaneLength = 230;
        }
        else
        {
            Root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Root.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            Root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Place(TopBar, 0, 0);
            Place(Split, 1, 0);
            Place(RightRailFrame, 2, 0);
            RightRailItems.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
            RightRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            RightRailScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Split.OpenPaneLength = 250;
        }

        Place(DialogLayer, 0, 0);
        Grid.SetRowSpan(DialogLayer, Math.Max(1, Root.RowDefinitions.Count));
        Grid.SetColumnSpan(DialogLayer, Math.Max(1, Root.ColumnDefinitions.Count));
        ApplyInsets();
        UpdateBodyLayout();
    }

    // Landscape: the right rail's own buttons. Portrait: one bottom bar with both rails' buttons (and the
    // guide), since the left rail is hidden there.
    private void ApplyRailSource()
    {
        if (_vm is not null)
            RightRailItems.ItemsSource = _landscape == false ? _vm.AllRail : _vm.RightRail;
    }

    private static void Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    /// <summary>
    /// Game only, or game plus the open tool page over its right side (landscape) or below it (portrait).
    /// The game control is never re-parented, only moved between grid cells.
    /// </summary>
    private void UpdateBodyLayout()
    {
        bool showPage = _vm is { HasPage: true, IsGameMaximized: false };
        double width = Body.Bounds.Width;
        double height = Body.Bounds.Height;
        RowDefinition gameRow = Body.RowDefinitions[0];
        RowDefinition pageRow = Body.RowDefinitions[1];

        PageFrame.IsVisible = showPage;
        PageHeader.IsVisible = _landscape == true;
        PageGrip.IsVisible = _landscape == true;
        PageFrame.Width = double.NaN;
        PageFrame.HorizontalAlignment = HorizontalAlignment.Stretch;
        gameRow.Height = new GridLength(1, GridUnitType.Star);
        pageRow.Height = new GridLength(0);

        if (!showPage)
            return;

        if (width >= height)
        {
            // Pages lie over the game, which keeps its size; UpdateGameClip hides the part they cover.
            // Each page keeps the width it was dragged to; wide pages still leave a strip of the game.
            double share = _vm!.CurrentPage is { } page ? _vm.GetPageWidth(page.Key) ?? 0.5 : 0.5;
            PageFrame.Width = _vm.IsPageWide ? MaxPageWidth(width) : ClampPageWidth(width * share, width);
            PageFrame.HorizontalAlignment = HorizontalAlignment.Right;
            Place(PageFrame, 0, 0);
        }
        else
        {
            gameRow.Height = new GridLength(Math.Min(height * 0.42, width / GameAspect));
            pageRow.Height = new GridLength(1, GridUnitType.Star);
            Place(PageFrame, 1, 0);
        }
    }

    private static double MaxPageWidth(double width) => width - Math.Max(80, width * 0.1);

    private static double ClampPageWidth(double pageWidth, double width)
        => Math.Clamp(pageWidth, Math.Min(280, width * 0.5), MaxPageWidth(width));

    private double _gripStartX;
    private double _gripStartWidth;
    private bool _gripDragging;

    private void OnGripPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _gripDragging = true;
        _gripStartX = e.GetPosition(Body).X;
        _gripStartWidth = PageFrame.Bounds.Width;
        e.Pointer.Capture(PageGrip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_gripDragging)
            return;
        PageFrame.Width = ClampPageWidth(_gripStartWidth + _gripStartX - e.GetPosition(Body).X, Body.Bounds.Width);
        e.Handled = true;
    }

    private void EndGripDrag(Avalonia.Input.IPointer? pointer)
    {
        if (!_gripDragging)
            return;
        _gripDragging = false;
        pointer?.Capture(null);
        if (_vm?.CurrentPage is { } page && Body.Bounds.Width > 0)
            _vm.SetPageWidth(page.Key, PageFrame.Width / Body.Bounds.Width);
    }

    /// <summary>
    /// Sizes the WebView to the game's own aspect ratio and centers it, so the stage fills the frame
    /// edge to edge and the WebView never renders letterbox bars.
    /// </summary>
    private void FitGame()
    {
        if (GameFrame.Child is not Control game)
            return;
        double width = GameFrame.Bounds.Width;
        double height = GameFrame.Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        if (width / height > GameAspect)
            width = height * GameAspect;
        else
            height = width / GameAspect;
        game.Width = Math.Floor(width);
        game.Height = Math.Floor(height);
    }

    /// <summary>
    /// Stops the game drawing from where a page lying over it starts, so the page shows through.
    /// </summary>
    // ---- UI cost diagnostics (developer use; switched from the game page: __skuaExt("mvsUiDebug", ["on"])) ----
    private int _layouts;
    private DispatcherTimer? _uiDebugTimer;

    /// <summary>Shows Avalonia's dirty-rect and frame-rate overlays and logs layout passes per second.</summary>
    public void SetUiDebug(bool on)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        top.RendererDiagnostics.DebugOverlays = on
            ? Avalonia.Rendering.RendererDebugOverlays.DirtyRects | Avalonia.Rendering.RendererDebugOverlays.Fps
            : Avalonia.Rendering.RendererDebugOverlays.None;
        top.LayoutUpdated -= CountLayout;
        _uiDebugTimer?.Stop();
        if (!on)
            return;
        top.LayoutUpdated += CountLayout;
        _layouts = 0;
        _uiDebugTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _uiDebugTimer.Tick += (_, _) =>
        {
            Trace.WriteLine($"[UiDebug] {_layouts / 5.0:0.0} layout passes/s");
            _layouts = 0;
        };
        _uiDebugTimer.Start();
    }

    private void CountLayout(object? sender, EventArgs e) => _layouts++;

    /// <summary>Diagnostics: hides (or shows again) one named part of the shell, to find what repaints.</summary>
    public void SetUiDebugHidden(string name, bool hidden)
    {
        if (this.FindControl<Control>(name) is { } control)
        {
            control.IsVisible = !hidden;
            Trace.WriteLine($"[UiDebug] {name} {(hidden ? "hidden" : "shown")}");
        }
        else if (name == "pages")
        {
            // The kept page views (only the open one is visible): drop them all.
            PageHost.Children.Clear();
            _pageViews.Clear();
            _pageOrder.Clear();
            _shownContent = null;
            Trace.WriteLine("[UiDebug] kept pages removed");
        }
    }

    private void UpdateGameClip()
    {
        double fraction = 1;
        double left = 0;
        if (PageFrame.IsVisible && GameFrame.Child is Control game && game.Bounds.Width > 0
            && PageFrame.TranslatePoint(default, game) is { } pageOrigin
            && new Rect(pageOrigin, PageFrame.Bounds.Size).Intersects(new Rect(game.Bounds.Size)))
            fraction = pageOrigin.X / game.Bounds.Width;
        if (Split.IsPaneOpen && GameFrame.Child is Control g && g.Bounds.Width > 0
            && Split.TranslatePoint(new Point(Split.OpenPaneLength, 0), g) is { } paneEdge)
            left = paneEdge.X / g.Bounds.Width;
        _visibleFraction = fraction;
        _visibleLeft = Math.Clamp(left, 0, 1);
        GameView.SetVisibleRange(_visibleLeft, fraction);
    }

    private double _visibleFraction = 1;
    private double _visibleLeft;

    /// <summary>
    /// Landscape with the keyboard up: a page's text box (search, script options) lifts only the page,
    /// so the game keeps its size and place. A game text field (chat) slides the game up by the
    /// keyboard's height, still the same size, so the field stays above the keyboard; the login screen's
    /// fields sit high enough that it doesn't move there.
    /// </summary>
    private void ApplyKeyboardLandscape()
    {
        double kb = _keyboardHeight;
        bool pageTyping = kb > 0 && _topLevel?.FocusManager?.GetFocusedElement() is TextBox;
        PageFrame.Margin = pageTyping ? new Thickness(0, 0, 0, kb) : default;
        double shift = kb > 0 && !pageTyping && _vm?.IsLoggedIn == true ? kb : 0;
        GameFrame.Margin = shift > 0 ? new Thickness(0, -shift, 0, shift) : default;
    }

    private void RebuildGameView()
    {
        GameFrame.Child = new GameView { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        FitGame();
    }

    public void UpdateCovered()
    {
        GameView.SetCovered(DialogLayer.Children.Count > 0 || _openPopups.Any(PopupCoversGame));
    }

    /// <summary>Whether an open popup lies over the part of the game that is showing.</summary>
    private bool PopupCoversGame(Popup popup)
    {
        if (popup.Child is not Control child || GameFrame.Child is not Control game || game.Bounds.Width <= 0)
            return true;
        // Not laid out yet: keep the game hidden until the popup's place is known.
        if (child.Bounds.Width <= 0 || child.Bounds.Height <= 0)
            return true;
        if (child.TranslatePoint(default, game) is not { } origin)
            return true;
        Rect shown = new(game.Bounds.Width * _visibleLeft, 0, game.Bounds.Width * (_visibleFraction - _visibleLeft), game.Bounds.Height);
        return shown.Intersects(new Rect(origin, child.Bounds.Size));
    }

    public void PushDialog(Control dialog)
    {
        DialogLayer.Children.Add(dialog);
        DialogLayer.IsVisible = true;
        UpdateCovered();
    }

    public void RemoveDialog(Control dialog)
    {
        DialogLayer.Children.Remove(dialog);
        DialogLayer.IsVisible = DialogLayer.Children.Count > 0;
        UpdateCovered();
    }
}
