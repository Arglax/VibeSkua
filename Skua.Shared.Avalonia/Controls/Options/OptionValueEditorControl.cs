using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;

namespace Skua.Shared.Avalonia.Controls.Options;

public class OptionValueEditorControl : ContentControl
{
    private OptionContainerItemViewModel? _watched;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null)
            _watched.PropertyChanged -= OnItemPropertyChanged;
        _watched = DataContext as OptionContainerItemViewModel;
        if (_watched is not null)
            _watched.PropertyChanged += OnItemPropertyChanged;
        RebuildEditor();
    }

    // The item list arrives after the dialog opens: swap the text box for the dropdown then.
    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OptionContainerItemViewModel.ItemChoices) or nameof(OptionContainerItemViewModel.TypingOwnName))
            RebuildEditor();
    }

    private void RebuildEditor()
    {
        if (DataContext is not OptionContainerItemViewModel item)
        {
            Content = null;
            return;
        }

        bool phone = OperatingSystem.IsAndroid();
        if (item.IsBool && phone)
        {
            // A switch is easier to hit than a checkbox on a phone.
            ToggleSwitch toggle = new()
            {
                OnContent = string.Empty,
                OffContent = string.Empty,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle[!ToggleSwitch.IsCheckedProperty] = new Binding(nameof(OptionContainerItemViewModel.BoolValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = toggle;
        }
        else if (item.IsBool)
        {
            CheckBox checkBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            checkBox[!CheckBox.IsCheckedProperty] = new Binding(nameof(OptionContainerItemViewModel.BoolValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = checkBox;
        }
        else if (item.IsEnum)
        {
            ComboBox comboBox = new()
            {
                ItemsSource = item.EnumValues,
                MinWidth = phone ? 220 : 160,
                MaxWidth = phone ? 320 : 220,
                Height = phone ? 42 : 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            comboBox[!ComboBox.SelectedItemProperty] = new Binding(nameof(OptionContainerItemViewModel.SelectedValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = comboBox;
        }
        else if (item.ItemChoices is { Count: > 0 } && !item.TypingOwnName)
        {
            // An item the player owns, picked from a list that shows each one's enhancement. A plain
            // picker: the editable box on Android stored the pick but went on showing its placeholder.
            // A name not in the list: the last entry switches the row to a text box.
            ComboBox itemBox = new()
            {
                ItemsSource = item.PickerChoices,
                PlaceholderText = string.IsNullOrEmpty(item.TextValue) ? "Pick an item" : item.TextValue,
                MinWidth = phone ? 280 : 160,
                MaxWidth = phone ? 340 : 280,
                Height = phone ? 42 : 32,
                MaxDropDownHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            itemBox[!ComboBox.SelectedItemProperty] = new Binding(nameof(OptionContainerItemViewModel.SelectedChoice))
            {
                Mode = BindingMode.TwoWay
            };
            Content = itemBox;
        }
        else
        {
            TextBox textBox = new()
            {
                MinWidth = phone ? 220 : 140,
                MaxWidth = phone ? 320 : 220,
                Height = phone ? 42 : 32,
                Padding = new Thickness(8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBox[!TextBox.TextProperty] = new Binding(nameof(OptionContainerItemViewModel.TextValue))
            {
                Mode = BindingMode.TwoWay
            };
            Content = textBox;
        }
    }
}
