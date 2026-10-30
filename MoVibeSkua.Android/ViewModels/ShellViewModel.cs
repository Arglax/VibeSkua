using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;

namespace MoVibeSkua.Android.ViewModels;

public sealed partial class ShellPage : ObservableObject
{
    public ShellPage(string key, string title, object content)
    {
        Key = key;
        Title = title;
        Content = content;
    }

    public string Key { get; }
    public string Title { get; }
    public object Content { get; }
}

public sealed class ShellMenuGroup
{
    public ShellMenuGroup(string header, IReadOnlyList<ShellMenuEntry> entries)
    {
        Header = header;
        Entries = entries;
    }

    public string Header { get; }
    public IReadOnlyList<ShellMenuEntry> Entries { get; }
}

public sealed class ShellMenuEntry
{
    public ShellMenuEntry(string header, System.Windows.Input.ICommand command)
    {
        Header = header;
        Command = command;
    }

    public string Header { get; }
    public System.Windows.Input.ICommand Command { get; }
}

/// <summary>
/// One button on the landscape side rails (or the portrait bottom bar).
/// </summary>
public sealed partial class RailAction : ObservableObject
{
    public RailAction(string label, Material.Icons.MaterialIconKind icon, System.Windows.Input.ICommand command, string? pageKey = null)
    {
        Label = label;
        Icon = icon;
        Command = command;
        PageKey = pageKey;
    }

    public string Label { get; }
    public Material.Icons.MaterialIconKind Icon { get; }
    public System.Windows.Input.ICommand Command { get; }

    /// <summary>The page this button opens, used to highlight it while that page is showing.</summary>
    public string? PageKey { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// State of the single-view mobile shell: the page stack that stands in for desktop windows,
/// the slide-out menu and the status strip.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IScriptInterface _bot;
    private readonly DispatcherTimer _statusTimer;

    public ShellViewModel(MainMenuViewModel mainMenu, IScriptInterface bot, ScriptLoaderViewModel scriptLoader)
    {
        MainMenu = mainMenu;
        _bot = bot;
        ScriptLoader = scriptLoader;
        ScriptLoader.PropertyChanged += OnScriptLoaderChanged;
        BuildMenu();
        BuildRails();
        if (mainMenu.Plugins is System.Collections.Specialized.INotifyCollectionChanged plugins)
            plugins.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(BuildMenu);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
    }

    public MainMenuViewModel MainMenu { get; }

    public ScriptLoaderViewModel ScriptLoader { get; }

    public ObservableCollection<ShellPage> Pages { get; } = new();

    public ObservableCollection<ShellMenuGroup> MenuGroups { get; } = new();

    public ObservableCollection<RailAction> LeftRail { get; } = new();

    public ObservableCollection<RailAction> RightRail { get; } = new();

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private ShellPage? _currentPage;

    [ObservableProperty]
    private bool _isMenuOpen;

    [ObservableProperty]
    private bool _isGameMaximized;

    /// <summary>
    /// Landscape: the page takes most of the width and the game shrinks to a small, still running, tile.
    /// </summary>
    [ObservableProperty]
    private bool _isPageWide;

    [ObservableProperty]
    private string _statusText = "Not logged in";

    public bool HasPage => CurrentPage is not null;

    public string PageTitle => CurrentPage?.Title ?? "MoVibeSkua";

    public string ToggleScriptText => ScriptLoader.ToggleScriptText;

    public bool IsScriptRunning => Ioc.Default.GetRequiredService<IScriptManager>().ScriptRunning;

    /// <summary>
    /// Opens a page, or closes it when it is already the one showing (rail buttons toggle).
    /// </summary>
    public void TogglePage(string key, Action open)
    {
        if (CurrentPage?.Key == key)
        {
            Back();
            return;
        }
        open();
    }

    public void ShowPage(string key, string title, object content)
    {
        ShellPage? existing = Pages.FirstOrDefault(p => p.Key == key);
        if (existing is not null)
        {
            Pages.Remove(existing);
        }
        else
        {
            existing = new ShellPage(key, title, content);
            SetActive(existing, true);
        }

        Pages.Add(existing);
        CurrentPage = existing;
        IsMenuOpen = false;
        IsGameMaximized = false;
    }

    [RelayCommand]
    private void Back()
    {
        if (IsMenuOpen)
        {
            IsMenuOpen = false;
            return;
        }
        if (Pages.Count == 0)
            return;

        SetActive(Pages[^1], false);
        Pages.RemoveAt(Pages.Count - 1);
        CurrentPage = Pages.LastOrDefault();
    }

    /// <summary>
    /// Pages load their data when activated, the way desktop activates a window while it is open.
    /// </summary>
    private static void SetActive(ShellPage page, bool active)
    {
        if (page.Content is ObservableRecipient recipient)
            recipient.IsActive = active;
    }

    /// <summary>
    /// Handles the system back button. Returns false when there is nothing left to close.
    /// </summary>
    public bool HandleBack()
    {
        if (!IsMenuOpen && Pages.Count == 0)
            return false;
        Back();
        return true;
    }

    [RelayCommand]
    private void CloseAllPages()
    {
        foreach (ShellPage page in Pages)
            SetActive(page, false);
        Pages.Clear();
        CurrentPage = null;
    }

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    [RelayCommand]
    private void ToggleGameMaximized() => IsGameMaximized = !IsGameMaximized;

    [RelayCommand]
    private void TogglePageWide() => IsPageWide = !IsPageWide;

    [RelayCommand]
    private Task ToggleScript() => ScriptLoader.ToggleScriptCommand.ExecuteAsync(null);

    [RelayCommand]
    private void OpenLogin() => ShowPage("Login", "Login", Ioc.Default.GetRequiredService<LoginViewModel>());

    private void OpenManaged(string key) => Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow(key);

    private void BuildRails()
    {
        RailAction Page(string label, Material.Icons.MaterialIconKind icon, string key, Action open)
            => new(label, icon, new RelayCommand(() => TogglePage(key, open)), key);
        RailAction Managed(string label, Material.Icons.MaterialIconKind icon, string key)
            => Page(label, icon, key, () => OpenManaged(key));

        LeftRail.Add(Page("Login", Material.Icons.MaterialIconKind.AccountCircle, "Login", () => OpenLogin()));
        LeftRail.Add(Managed("Scripts", Material.Icons.MaterialIconKind.ScriptText, "Scripts"));
        LeftRail.Add(Page("Repo", Material.Icons.MaterialIconKind.CloudDownload, "Script Repo", () => ShowPage("Script Repo", "Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>())));
        LeftRail.Add(Managed("Logs", Material.Icons.MaterialIconKind.TextLong, "Logs"));

        RightRail.Add(Page("Auto", Material.Icons.MaterialIconKind.Sword, "Auto", () => ShowPage("Auto", "Auto Attack / Hunt", MainMenu.AutoViewModel)));
        RightRail.Add(Page("Jump", Material.Icons.MaterialIconKind.MapMarker, "Jump", () => ShowPage("Jump", "Jump", MainMenu.JumpViewModel)));
        RightRail.Add(Managed("Loader", Material.Icons.MaterialIconKind.Download, "Loader"));
        RightRail.Add(Managed("Grabber", Material.Icons.MaterialIconKind.FormatListBulleted, "Grabber"));
        RightRail.Add(Managed("Options", Material.Icons.MaterialIconKind.Cog, "Game"));
    }

    partial void OnCurrentPageChanged(ShellPage? value)
    {
        foreach (RailAction action in LeftRail.Concat(RightRail))
            action.IsActive = action.PageKey is not null && action.PageKey == value?.Key;
    }

    private void BuildMenu()
    {
        MenuGroups.Clear();
        MenuGroups.Add(new ShellMenuGroup("Account", new[]
        {
            new ShellMenuEntry("Login", OpenLoginCommand),
            new ShellMenuEntry("Script Repo", new RelayCommand(() => ShowPage("Script Repo", "Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>()))),
            new ShellMenuEntry("Auto Attack / Hunt", new RelayCommand(() => ShowPage("Auto", "Auto", MainMenu.AutoViewModel))),
            new ShellMenuEntry("Jump", new RelayCommand(() => ShowPage("Jump", "Jump", MainMenu.JumpViewModel)))
        }));

        foreach (IMainMenuItemViewModel item in MainMenu.MainMenuItems)
        {
            List<ShellMenuEntry> entries = new();
            if (item.SubItems is { Count: > 0 })
            {
                foreach (IMainMenuItemViewModel sub in item.SubItems)
                    entries.Add(new ShellMenuEntry(sub.Header, sub.Command));
            }
            else
            {
                entries.Add(new ShellMenuEntry(item.Header, item.Command));
            }
            MenuGroups.Add(new ShellMenuGroup(item.Header, entries));
        }

        List<ShellMenuEntry> pluginEntries = new() { new ShellMenuEntry("Manage Plugins", new RelayCommand(() => OpenManaged("Plugins"))) };
        foreach (IMainMenuItemViewModel plugin in MainMenu.Plugins)
            pluginEntries.Add(new ShellMenuEntry(plugin.Header, plugin.Command));
        MenuGroups.Add(new ShellMenuGroup("Plugins", pluginEntries));
    }

    private void OnScriptLoaderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptLoaderViewModel.ToggleScriptText))
        {
            OnPropertyChanged(nameof(ToggleScriptText));
            OnPropertyChanged(nameof(IsScriptRunning));
        }
    }

    private void RefreshStatus()
    {
        _ = Task.Run(() =>
        {
            string text;
            bool loggedIn = false;
            try
            {
                loggedIn = _bot.Player.LoggedIn;
                if (!loggedIn)
                    text = "Not logged in";
                else
                    text = $"{_bot.Player.Username} · {_bot.Map.Name} · {_bot.Player.Cell}";
            }
            catch
            {
                text = "Loading...";
            }
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = text;
                IsLoggedIn = loggedIn;
                OnPropertyChanged(nameof(ToggleScriptText));
                OnPropertyChanged(nameof(IsScriptRunning));
            });
        });
    }
}
