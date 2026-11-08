using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Skua.App.Avalonia.ViewModels.CoreBotsOptions;

public partial class CoreBotsViewModel : BotControlViewModelBase
{
    public CoreBotsViewModel(List<TabItemViewModel> tabs, IScriptPlayer player, IDialogService dialogService)
        : base("CoreBots Options", 800, 608)
    {
        CoreBotsTabs = tabs;
        _selectedTab = CoreBotsTabs[0];
        _player = player;
        _dialogService = dialogService;
    }

    protected override void OnActivated()
    {
        // Opening the window while logged out just shows "Not Logged In"; only the Load button explains why.
        LoadInternal(showDialog: false);
    }

    protected override void OnDeactivated()
    {
        // Auto-save when closing the window
        if (!string.IsNullOrEmpty(_player.Username))
        {
            SaveInternal(showDialog: false);
        }
        base.OnDeactivated();
    }

    private readonly IScriptPlayer _player;
    private readonly IDialogService _dialogService;
    private Dictionary<string, Dictionary<string, string>> _readValues = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptionsTabSelected))]
    private TabItemViewModel _selectedTab;

    // The Options tab's search sits on the tab row, shown while that tab is open
    public CBOptionsViewModel? OptionsTab => CoreBotsTabs.Select(t => t.Content).OfType<CBOptionsViewModel>().FirstOrDefault();
    public bool IsOptionsTabSelected => SelectedTab?.Content is CBOptionsViewModel;

    [ObservableProperty]
    private string _currentPlayer = string.Empty;

    public List<TabItemViewModel> CoreBotsTabs { get; }

    [RelayCommand]
    private void Save()
    {
        SaveInternal(showDialog: true);
    }

    private void SaveInternal(bool showDialog)
    {
        if (string.IsNullOrEmpty(_player.Username))
        {
            if (showDialog)
            {
                _dialogService.ShowMessageBox("Login first so that we can fetch your username for the save file", "Save");
                CurrentPlayer = string.Empty;
            }
            return;
        }

        StringBuilder bob = new();
        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is IManageCBOptions cbo)
                cbo.Save(bob);
        }

        string optionsDir = ClientFileSources.SkuaOptionsDIR;
        if (!Directory.Exists(optionsDir))
            Directory.CreateDirectory(optionsDir);

        string name = CboName();
        string filePath = CboPath(name);
        File.WriteAllText(filePath, bob.ToString());

        if (showDialog)
        {
            _dialogService.ShowMessageBox($@"Saved to \options\CBO_Storage({name}).txt", "Save Successful!");
        }
        _readValues[name] = ReadValues(File.ReadAllLines(filePath));
    }

    [RelayCommand]
    private void Load()
    {
        LoadInternal(showDialog: true);
    }

    private void LoadInternal(bool showDialog)
    {
        if (string.IsNullOrEmpty(_player.Username))
        {
            if (showDialog)
                _dialogService.ShowMessageBox("Login first so that we can fetch your username to load the options file.", "Load");
            CurrentPlayer = string.Empty;
            return;
        }

        CurrentPlayer = _player.Username;
        string name = CboName();
        if (_readValues.ContainsKey(name))
        {
            SetValues(_readValues[name]);
            return;
        }

        string filePath = CboPath(name);
        if (!File.Exists(filePath) && Directory.Exists(ClientFileSources.LegacySkuaDIR))
        {
            string legacyPath = Path.Combine(ClientFileSources.LegacySkuaDIR, "options", $"CBO_Storage({name}).txt");
            if (File.Exists(legacyPath))
                filePath = legacyPath;
        }

        if (!File.Exists(filePath))
            return;

        Dictionary<string, string> optionsDict = ReadValues(File.ReadAllLines(filePath));

        SetValues(optionsDict);

        _readValues[name] = optionsDict;
    }

    /// <summary>
    /// The name in CoreBots' options file, exactly as CoreBots.cs reads it: the server's name for the
    /// account (sfc.myUserName, lower case), not what was typed at login. Windows ignores letter case in
    /// file names, so the two never differed there; Android doesn't, so "Arglax" typed at login wrote
    /// CBO_Storage(Arglax).txt while scripts read CBO_Storage(arglax).txt and ran on Generic classes.
    /// </summary>
    private string CboName()
    {
        try
        {
            string? raw = Ioc.Default.GetService<IFlashUtil>()?.GetGameObject("sfc.myUserName");
            if (!string.IsNullOrEmpty(raw) && raw.Length > 2 && raw[0] == '"')
                return raw[1..^1];
        }
        catch { }
        return (_player.Username ?? string.Empty).ToLowerInvariant();
    }

    /// <summary>The options file for <paramref name="name"/>; one saved under another letter case is moved to it.</summary>
    private static string CboPath(string name)
    {
        string dir = ClientFileSources.SkuaOptionsDIR;
        string path = Path.Combine(dir, $"CBO_Storage({name}).txt");
        try
        {
            if (!File.Exists(path) && Directory.Exists(dir))
            {
                string? other = Directory.GetFiles(dir, "CBO_Storage(*).txt")
                    .FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                if (other is not null)
                    File.Move(other, path);
            }
        }
        catch { }
        return path;
    }


    // Refreshes the class and equipment lists from the logged-in character's inventory, keeping the
    // selections already made in the window.
    [RelayCommand]
    private void LoadCharacter()
    {
        if (string.IsNullOrEmpty(_player.Username))
        {
            _dialogService.ShowMessageBox("Login first so that we can read your character's classes and equipment.", "Load Character");
            return;
        }

        StringBuilder current = new();
        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is IManageCBOptions cbo)
                cbo.Save(current);
        }
        Dictionary<string, string> selections = ReadValues(current.ToString().Split(Environment.NewLine));

        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is CBOClassSelectViewModel classSelect)
                classSelect.ReloadClassesCommand.Execute(null);
            else if (tab.Content is CBOClassEquipmentViewModel equipment)
                equipment.RefreshInventoryCommand.Execute(null);
        }

        SetValues(selections);
    }

    private Dictionary<string, string> ReadValues(IEnumerable<string> lines)
    {
        Dictionary<string, string> optionsDict = new();
        foreach (string option in lines)
        {
            ReadOnlySpan<string> value = option.Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (value.Length == 2)
                optionsDict.Add(value[0], value[1]);
        }
        return optionsDict;
    }

    private void SetValues(Dictionary<string, string> options)
    {
        foreach (TabItemViewModel tab in CoreBotsTabs)
        {
            if (tab.Content is IManageCBOptions setable)
                setable.SetValues(options);
        }
    }

    private readonly char _separator = ':';
}

internal interface IManageCBOptions
{
    StringBuilder Save(StringBuilder builder);

    void SetValues(Dictionary<string, string> values);
}