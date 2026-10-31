using Avalonia;
using Avalonia.Controls;

namespace Skua.App.Avalonia.Views;

public partial class GrabberView : UserControl
{
    private const double NarrowWidth = 700;

    public GrabberView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
    }

    /// <summary>
    /// Phone-sized panels: the tab list becomes a picker above the tab, so the grabbed items keep the full width.
    /// </summary>
    private void ApplyWidth(double width)
    {
        bool narrow = width < NarrowWidth;
        if (TabPicker.IsVisible == narrow)
            return;

        NavList.IsVisible = !narrow;
        TabPicker.IsVisible = narrow;
        LayoutRoot.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(14, 12, 0, 0);
        LayoutRoot.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : 164);
        Grid.SetRow(TabContent, narrow ? 1 : 0);
        Grid.SetRowSpan(TabContent, narrow ? 1 : 2);
        Grid.SetColumn(TabContent, narrow ? 0 : 1);
        Grid.SetColumnSpan(TabContent, narrow ? 2 : 1);
    }
}
