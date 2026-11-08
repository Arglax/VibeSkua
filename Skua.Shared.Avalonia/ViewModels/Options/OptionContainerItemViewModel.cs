using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Options;
using System;
using System.Collections.Generic;

namespace Skua.Shared.Avalonia.ViewModels.Options;

public partial class OptionContainerItemViewModel : ObservableObject, IOptionContainerItemViewModel
{
    public OptionContainerItemViewModel(IOptionContainer container, IOption option)
    {
        Container = container;
        Option = option;
        Type = option.Type;
        Category = string.IsNullOrWhiteSpace(option.Category) ? "Options" : option.Category;

        string currentRawVal = container.GetDirect(option);

        if (Type.IsEnum)
        {
            string[] enumNames = Enum.GetNames(Type);
            EnumValues = new List<string>(enumNames.Length);
            foreach (string name in enumNames)
                EnumValues.Add(name.Replace('_', ' '));

            string displayStr = currentRawVal.Replace('_', ' ');
            SelectedValue = EnumValues.Contains(displayStr)
                ? displayStr
                : (EnumValues.Count > 0 ? EnumValues[0] : string.Empty);
            _value = SelectedValue;
            return;
        }

        if (Type == typeof(bool))
        {
            if (bool.TryParse(currentRawVal, out bool parsedBool))
                _value = parsedBool;
            else if (option.DefaultValue is bool b)
                _value = b;
            else
                _value = false;
        }
        else
        {
            _value = !string.IsNullOrEmpty(currentRawVal) ? currentRawVal : (option.DefaultValue?.ToString() ?? string.Empty);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private object _value;

    /// <summary>The default as shown to the player ("Default: Off", "Default: empty").</summary>
    public string DefaultText
    {
        get
        {
            object? d = Option.DefaultValue;
            if (Type == typeof(bool))
                return "Default: " + ((d is bool b && b) || (bool.TryParse(d?.ToString(), out bool p) && p) ? "On" : "Off");
            string text = d?.ToString()?.Replace('_', ' ') ?? string.Empty;
            return "Default: " + (string.IsNullOrWhiteSpace(text) ? "empty" : text);
        }
    }

    /// <summary>Whether the value differs from the option's default (shown as a gold bar on MVS).</summary>
    public bool IsChanged
    {
        get
        {
            object? d = Option.DefaultValue;
            if (Type == typeof(bool))
                return BoolValue != ((d is bool b && b) || (bool.TryParse(d?.ToString(), out bool p) && p));
            string def = d?.ToString()?.Replace('_', ' ') ?? string.Empty;
            string now = Type.IsEnum ? (SelectedValue ?? string.Empty) : TextValue;
            return !string.Equals(now.Trim(), def.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [ObservableProperty]
    private List<string>? _enumValues;

    [ObservableProperty]
    private string? _selectedValue;

    partial void OnSelectedValueChanged(string? value)
    {
        _value = value ?? string.Empty;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
    }

    public bool BoolValue
    {
        get => _value is bool b ? b : (bool.TryParse(_value?.ToString(), out bool parsed) && parsed);
        set
        {
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
        }
    }

    public string TextValue
    {
        get => _value?.ToString() ?? string.Empty;
        set
        {
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
        }
    }

    public IOptionContainer Container { get; }
    public IOption Option { get; }
    public Type Type { get; }
    public string Category { get; }

    private List<string>? _itemChoices;
    private bool _itemChoicesRead;

    /// <summary>Owned items matching a text option that asks for one (see <see cref="OptionItemChoices"/>), or null.</summary>
    public List<string>? ItemChoices
    {
        get
        {
            if (!_itemChoicesRead)
            {
                _itemChoicesRead = true;
                _itemChoices = IsText ? OptionItemChoices.For(Option) : null;
            }
            return _itemChoices;
        }
    }

    public bool IsEnum => Type.IsEnum;
    public bool IsBool => Type == typeof(bool);
    public bool IsText => !IsEnum && !IsBool;

    public void ResetToDefault()
    {
        if (Type.IsEnum)
        {
            string defaultStr = Option.DefaultValue?.ToString()?.Replace('_', ' ') ?? (EnumValues?.Count > 0 ? EnumValues[0] : string.Empty);
            SelectedValue = EnumValues != null && EnumValues.Contains(defaultStr) ? defaultStr : (EnumValues?.Count > 0 ? EnumValues[0] : string.Empty);
            _value = SelectedValue;
            OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
        }
        else if (Type == typeof(bool))
        {
            bool defBool = false;
            if (Option.DefaultValue is bool b)
                defBool = b;
            else if (bool.TryParse(Option.DefaultValue?.ToString(), out bool parsedBool))
                defBool = parsedBool;

            BoolValue = defBool;
        }
        else
        {
            TextValue = Option.DefaultValue?.ToString() ?? string.Empty;
        }
    }
}
