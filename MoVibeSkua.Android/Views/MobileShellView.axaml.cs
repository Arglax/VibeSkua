using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
    private static int _openPopups;

    public static MobileShellView? Instance { get; private set; }

    static MobileShellView()
    {
        // Avalonia popups draw beneath native views; hide the game while any are open.
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((_, e) =>
        {
            _openPopups = Math.Max(0, _openPopups + (e.GetNewValue<bool>() ? 1 : -1));
            Instance?.UpdateCovered();
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
        Root.SizeChanged += (_, _) => UpdateLayoutMode();
        Body.SizeChanged += (_, _) => UpdateBodyLayout();
        GameFrame.SizeChanged += (_, _) => FitGame();
        GameViewRecovery.RebuildRequested += () => Dispatcher.UIThread.Post(RebuildGameView);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ShellViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateBodyLayout();
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
    }

    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        _keyboardHeight = e.NewState == InputPaneState.Open ? e.EndRect.Height : 0;
        ApplyInsets();
    }

    private void ApplyInsets()
    {
        Root.Margin = new Thickness(_safeArea.Left, _safeArea.Top, _safeArea.Right, Math.Max(_safeArea.Bottom, _keyboardHeight));
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
        if (e.PropertyName is nameof(ShellViewModel.HasPage) or nameof(ShellViewModel.IsGameMaximized) or nameof(ShellViewModel.CurrentPage)
            or nameof(ShellViewModel.IsPageWide))
            UpdateBodyLayout();
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
        RightRailFrame.Classes.Set("bottom", !landscape);

        if (landscape)
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Place(LeftRailFrame, 0, 0);
            Place(Split, 0, 1);
            Place(RightRailFrame, 0, 2);
            RightRailItems.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Vertical });
            RightRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
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
        UpdateBodyLayout();
    }

    private static void Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    /// <summary>
    /// Game only, or game plus the open tool page beside it (landscape) or below it (portrait).
    /// The game control is never re-parented, only moved between grid cells.
    /// </summary>
    private void UpdateBodyLayout()
    {
        bool showPage = _vm is { HasPage: true, IsGameMaximized: false };
        double width = Body.Bounds.Width;
        double height = Body.Bounds.Height;
        ColumnDefinition pageColumn = Body.ColumnDefinitions[1];
        RowDefinition gameRow = Body.RowDefinitions[0];
        RowDefinition pageRow = Body.RowDefinitions[1];

        PageFrame.IsVisible = showPage;
        PageHeader.IsVisible = _landscape == true;
        pageColumn.Width = new GridLength(0);
        gameRow.Height = new GridLength(1, GridUnitType.Star);
        pageRow.Height = new GridLength(0);

        if (!showPage)
            return;

        if (width >= height)
        {
            // Pages get a readable width; the game takes what is left and letterboxes itself. Wide pages
            // leave the game a small tile rather than hiding it, so it keeps rendering.
            double pageWidth = _vm!.IsPageWide
                ? width - Math.Max(150, width * 0.22)
                : Math.Clamp(width * 0.46, Math.Min(320, width - 160), Math.Max(320, width - 200));
            pageColumn.Width = new GridLength(pageWidth);
            Place(PageFrame, 0, 1);
        }
        else
        {
            gameRow.Height = new GridLength(Math.Min(height * 0.42, width / GameAspect));
            pageRow.Height = new GridLength(1, GridUnitType.Star);
            Place(PageFrame, 1, 0);
        }
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

    private void RebuildGameView()
    {
        GameFrame.Child = new GameView { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        FitGame();
    }

    public void UpdateCovered()
    {
        GameView.SetCovered(_openPopups > 0 || DialogLayer.Children.Count > 0);
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
