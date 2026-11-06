using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Skua.Shared.Avalonia.Controls;

/// <summary>
/// Stacks a multi-column grid into one column when it is narrower than
/// <c>ResponsiveGrid.StackBelow</c>: every cell of column 0 first, then column 1, and so on, each row
/// sized to its content. Side by side on a phone page the columns squeezed their text into one letter
/// per line and overlapped. While stacked, a <see cref="ScrollViewer"/> whose content is the grid scrolls
/// vertically.
/// </summary>
public static class ResponsiveGrid
{
    public static readonly AttachedProperty<double> StackBelowProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>("StackBelow", typeof(ResponsiveGrid));

    static ResponsiveGrid()
    {
        StackBelowProperty.Changed.AddClassHandler<Grid>(OnStackBelowChanged);
    }

    public static double GetStackBelow(Grid grid) => grid.GetValue(StackBelowProperty);

    public static void SetStackBelow(Grid grid, double value) => grid.SetValue(StackBelowProperty, value);

    private sealed class State
    {
        public bool Stacked;
        public ColumnDefinitions? Columns;
        public RowDefinitions? Rows;
        public double ColumnSpacing;
        public readonly Dictionary<Control, (int Row, int Column, int RowSpan, int ColumnSpan)> Cells = new();
        public ScrollViewer? Scroller;
        public ScrollBarVisibility ScrollerVisibility;
    }

    private static readonly ConditionalWeakTable<Grid, State> States = new();

    private static void OnStackBelowChanged(Grid grid, AvaloniaPropertyChangedEventArgs e)
    {
        grid.SizeChanged -= OnSizeChanged;
        if (GetStackBelow(grid) > 0)
            grid.SizeChanged += OnSizeChanged;
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || e.NewSize.Width <= 0)
            return;
        State state = States.GetValue(grid, _ => new State());
        bool stack = e.NewSize.Width < GetStackBelow(grid);
        if (stack == state.Stacked)
            return;

        if (stack)
            Stack(grid, state);
        else
            Unstack(grid, state);
        state.Stacked = stack;
    }

    private static void Stack(Grid grid, State state)
    {
        state.Columns = grid.ColumnDefinitions;
        state.Rows = grid.RowDefinitions;
        state.ColumnSpacing = grid.ColumnSpacing;
        state.Cells.Clear();
        foreach (Control child in grid.Children)
            state.Cells[child] = (Grid.GetRow(child), Grid.GetColumn(child), Grid.GetRowSpan(child), Grid.GetColumnSpan(child));

        int rowCount = Math.Max(1, state.Rows.Count);
        int columnCount = Math.Max(1, state.Columns.Count);
        RowDefinitions rows = new();
        for (int i = 0; i < rowCount * columnCount; i++)
            rows.Add(new RowDefinition(GridLength.Auto));

        grid.ColumnDefinitions = new ColumnDefinitions("*");
        grid.RowDefinitions = rows;
        grid.ColumnSpacing = 0;
        foreach ((Control child, (int row, int column, int rowSpan, _)) in state.Cells)
        {
            Grid.SetColumn(child, 0);
            Grid.SetColumnSpan(child, 1);
            Grid.SetRow(child, Math.Min(column, columnCount - 1) * rowCount + row);
            Grid.SetRowSpan(child, rowSpan);
        }

        // Only the page's own grid (the scroller's content) turns scrolling on; a stacked row inside a
        // card would otherwise switch it back off when it unstacks first.
        state.Scroller = grid.FindAncestorOfType<ScrollViewer>();
        if (state.Scroller is not null && !ReferenceEquals(state.Scroller.Content, grid))
            state.Scroller = null;
        if (state.Scroller is not null)
        {
            state.ScrollerVisibility = state.Scroller.VerticalScrollBarVisibility;
            state.Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }

    private static void Unstack(Grid grid, State state)
    {
        if (state.Columns is null || state.Rows is null)
            return;
        grid.ColumnDefinitions = state.Columns;
        grid.RowDefinitions = state.Rows;
        grid.ColumnSpacing = state.ColumnSpacing;
        foreach ((Control child, (int row, int column, int rowSpan, int columnSpan)) in state.Cells)
        {
            Grid.SetRow(child, row);
            Grid.SetColumn(child, column);
            Grid.SetRowSpan(child, rowSpan);
            Grid.SetColumnSpan(child, columnSpan);
        }
        if (state.Scroller is not null)
            state.Scroller.VerticalScrollBarVisibility = state.ScrollerVisibility;
    }
}
