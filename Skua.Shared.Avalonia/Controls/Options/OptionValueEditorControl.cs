using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;

namespace Skua.Shared.Avalonia.Controls.Options;

public class OptionValueEditorControl : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
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
        else if (item.ItemChoices is { Count: > 0 } choices)
        {
            // An item the player owns: pick it from the list, or type a name as before.
            ComboBox itemBox = new()
            {
                IsEditable = true,
                ItemsSource = choices,
                PlaceholderText = "Pick or type an item",
                MinWidth = phone ? 280 : 160,
                MaxWidth = phone ? 320 : 260,
                Height = phone ? 42 : 32,
                MaxDropDownHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            itemBox[!ComboBox.TextProperty] = new Binding(nameof(OptionContainerItemViewModel.TextValue))
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
