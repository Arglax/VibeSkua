using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Options;
using System;
using System.Linq;
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
            // A numeric or flags value Get<T> accepts: show its name, or keep it as its own entry so Save writes it back.
            if (!EnumValues.Contains(displayStr)
                && !string.IsNullOrEmpty(currentRawVal)
                && Enum.TryParse(Type, OptionContainer.ToEnumText(currentRawVal), out object? parsed))
            {
                string parsedStr = parsed?.ToString()?.Replace('_', ' ') ?? string.Empty;
                if (EnumValues.Contains(parsedStr))
                    displayStr = parsedStr;
                else
                    EnumValues.Add(displayStr);
            }
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
            // A picked dropdown entry carries its enhancement in front ("(Absolution) Arsenal of Malice"):
            // the option stores the bare name, and the box keeps showing the entry. Telling the box the bare
            // name blanked it, since no entry matches the name alone.
            string text = value ?? string.Empty;
            string name = OptionItemChoices.NameOf(text, _itemChoices);
            _value = name;
            if (name == text)
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

    /// <summary>
    /// Owned items matching a text option that asks for one (see <see cref="OptionItemChoices"/>), or null.
    /// Read in the background (it asks the game for the inventory, which stalled the dialog while it
    /// opened); null until then, and the editor swaps the text box for the dropdown when it arrives.
    /// </summary>
    public List<string>? ItemChoices
    {
        get
        {
            if (!_itemChoicesRead)
            {
                _itemChoicesRead = true;
                if (IsText)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        List<string>? choices = null;
                        try { choices = OptionItemChoices.For(Option); } catch { }
                        if (choices is null)
                            return;
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _itemChoices = choices;
                            OnPropertyChanged(nameof(ItemChoices));
                        });
                    });
                }
            }
            return _itemChoices;
        }
    }

    /// <summary>Last entry of the item picker: switches the row to a text box for a name not in the list.</summary>
    public const string TypeOwnName = "Type a name…";

    /// <summary>Picker entries: the owned items, then <see cref="TypeOwnName"/>.</summary>
    public List<string>? PickerChoices => _itemChoices is null ? null : new List<string>(_itemChoices) { TypeOwnName };

    /// <summary>True once the player chose to type a name; the editor shows a text box then.</summary>
    public bool TypingOwnName { get; private set; }

    /// <summary>
    /// The picker entry for the stored item name ("(Absolution) Arsenal of Malice" for "Arsenal of Malice"),
    /// so the picker shows what is set, also when the window opens again; picking one stores the bare name.
    /// </summary>
    public string? SelectedChoice
    {
        get
        {
            string current = TextValue;
            return _itemChoices?.FirstOrDefault(c => OptionItemChoices.NameOf(c, _itemChoices) == current);
        }
        set
        {
            if (value is null)
                return;
            if (value == TypeOwnName)
            {
                TypingOwnName = true;
                OnPropertyChanged(nameof(TypingOwnName));
                return;
            }
            TextValue = value;
            OnPropertyChanged();
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
