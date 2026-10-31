using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Skua.App.Avalonia.ViewModels.Grabber;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.Views;

public partial class GrabberListView : UserControl
{
    private const double StackedWidth = 560;
    private const double CompactWidth = 440;
    private bool? _stacked;

    public GrabberListView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
    }

    /// <summary>
    /// Phone-sized panels: the details go under the grabbed items instead of beside them, the panel
    /// scrolls with a fixed-height item list, and the multi-select chip drops its label. Only sizes
    /// change, so the grid keeps its rows and columns.
    /// </summary>
    private void ApplyWidth(double width)
    {
        MultiSelectText.IsVisible = width >= CompactWidth;
        ToolTip.SetTip(MultiSelectToggle, width >= CompactWidth ? "Select several items at once" : "Multi-select");

        bool stacked = width < StackedWidth;
        if (_stacked == stacked)
            return;
        _stacked = stacked;

        Scroller.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ItemsGrid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 10);
        ItemsGrid.ColumnDefinitions[2].Width = new GridLength(stacked ? 0 : 232);
        ItemsGrid.RowDefinitions[1].Height = stacked ? new GridLength(260) : new GridLength(1, GridUnitType.Star);
        ItemsGrid.RowDefinitions[2].Height = stacked ? GridLength.Auto : new GridLength(0);
        ItemsGrid.RowDefinitions[3].Height = stacked ? GridLength.Auto : new GridLength(0);
        DetailsBox.MinHeight = stacked ? 140 : 0;
        DetailsSplitter.IsVisible = !stacked;
        DetailsLabel.Margin = stacked ? new Thickness(0, 10, 0, 8) : default;
        Grid.SetRow(DetailsLabel, stacked ? 2 : 0);
        Grid.SetColumn(DetailsLabel, stacked ? 0 : 2);
        Grid.SetRow(DetailsBox, stacked ? 3 : 1);
        Grid.SetColumn(DetailsBox, stacked ? 0 : 2);
    }

    private List<object> SelectedGrabbedItems()
    {
        if (GrabbedItemsList.SelectedItems is null)
            return new List<object>();

        return GrabbedItemsList.SelectedItems.Cast<object>().ToList();
    }

    private void GrabbedItemsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int count = GrabbedItemsList.SelectedItems?.Count ?? 0;
        SelectedCountText.Text = count == 0 ? string.Empty : $"{count} selected";
    }

    private void UnselectAll_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        GrabbedItemsList.SelectedItems?.Clear();
    }

    private void GrabberTaskButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GrabberTaskViewModel taskVm })
            return;

        taskVm.GrabberTaskCommand.Execute(SelectedGrabbedItems());
    }

    private void CopyButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not GrabberListViewModel vm)
            return;

        vm.Copy(SelectedGrabbedItems());
    }

    private void ExportButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not GrabberListViewModel vm)
            return;

        vm.ExportToFile(SelectedGrabbedItems());
    }
}
