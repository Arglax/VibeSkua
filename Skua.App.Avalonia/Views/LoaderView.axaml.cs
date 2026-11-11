using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Skua.App.Avalonia.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.Views;

public partial class LoaderView : UserControl
{
    private const double NarrowWidth = 560;
    private bool? _narrow;

    public LoaderView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
    }

    /// <summary>
    /// Phone-sized panels: the quest range and the quest actions get lines of their own instead of
    /// running off the right edge, and the panel scrolls with a fixed-height quest list instead of
    /// squeezing it. Only placements change, so the grids keep their rows and columns.
    /// </summary>
    private void ApplyWidth(double width)
    {
        bool narrow = width < NarrowWidth;
        if (_narrow == narrow)
            return;
        _narrow = narrow;

        Scroller.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        SetOrClear(QuestList, HeightProperty, narrow, 220d);

        // The label's visibility is bound to IsLoading, so it is collapsed by width instead.
        CacheGrid.RowSpacing = narrow ? 8 : 0;
        SetOrClear(CacheLabel, MaxWidthProperty, narrow, 0d);
        CacheLabel.Margin = narrow ? default : new Thickness(0, 0, 4, 0);
        Grid.SetRow(ProbeCheck, narrow ? 3 : 0);
        Grid.SetColumn(ProbeCheck, narrow ? 0 : 3);
        Grid.SetColumnSpan(ProbeCheck, narrow ? 6 : 1);
        Grid.SetRow(RangeBox, narrow ? 1 : 0);
        Grid.SetColumn(RangeBox, narrow ? 0 : 4);
        Grid.SetColumnSpan(RangeBox, narrow ? 6 : 1);
        Grid.SetRow(UpdateRangeButton, narrow ? 2 : 0);
        Grid.SetColumn(UpdateRangeButton, narrow ? 0 : 5);
        Grid.SetColumnSpan(UpdateRangeButton, narrow ? 6 : 1);
        SetOrClear(UpdateRangeButton, WidthProperty, narrow, double.NaN);
        SetOrClear(UpdateRangeButton, HorizontalAlignmentProperty, narrow, HorizontalAlignment.Stretch);

        ActionsGrid.RowSpacing = narrow ? 8 : 0;
        SetOrClear(FakeCompleteButton, HorizontalAlignmentProperty, narrow, HorizontalAlignment.Stretch);
        SetOrClear(LoadSelectedButton, HorizontalAlignmentProperty, narrow, HorizontalAlignment.Stretch);
        Grid.SetRow(FakeCompleteButton, narrow ? 1 : 0);
        Grid.SetColumn(FakeCompleteButton, narrow ? 0 : 2);
        Grid.SetColumnSpan(FakeCompleteButton, narrow ? 2 : 1);
        Grid.SetRow(LoadSelectedButton, narrow ? 1 : 0);
        Grid.SetColumn(LoadSelectedButton, narrow ? 2 : 3);
        Grid.SetColumnSpan(LoadSelectedButton, narrow ? 2 : 1);
    }

    private static void SetOrClear<T>(Control control, AvaloniaProperty<T> property, bool set, T value)
    {
        if (set)
            control.SetValue(property, value);
        else
            control.ClearValue(property);
    }

    private List<object> SelectedQuestItems()
    {
        if (QuestList.SelectedItems is null)
            return new List<object>();

        return QuestList.SelectedItems.Cast<object>().ToList();
    }

    private void QuestList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int count = QuestList.SelectedItems?.Count ?? 0;
        SelectedCountText.Text = count == 0 ? "None selected" : $"{count} selected";
    }

    private void LoadSelected_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.LoadQuestsCommand.Execute(SelectedQuestItems());
    }

    private void CopyNames_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.CopyQuestsNamesCommand.Execute(SelectedQuestItems());
    }

    private void CopyIds_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.CopyQuestsIDsCommand.Execute(SelectedQuestItems());
    }

    private void CopyNamesAndIds_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.CopyQuestsNamesAndIDsCommand.Execute(SelectedQuestItems());
    }

    private void UpdateAll_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.UpdateQuestsCommand.Execute(true);
    }

    private void UpdateMissing_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LoaderViewModel vm)
            vm.UpdateQuestsCommand.Execute(false);
    }
}
