using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using MoVibeSkua.Android.Platform;
using MoVibeSkua.Android.Usage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.FastTravel;
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

    /// <summary>Runs once the page leaves the stack.</summary>
    public Action? Closed { get; init; }
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
        Command = new CountedCommand(Signal.NAV_MENU, command);
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
        _icon = icon;
        Command = command;
        PageKey = pageKey;
    }

    public string Label { get; }
    public System.Windows.Input.ICommand Command { get; }

    /// <summary>The game's own icon (Images/rail/*.png), shown instead of <see cref="Icon"/> when set.</summary>
    public Avalonia.Media.IImage? Image { get; init; }

    public bool HasImage => Image is not null;

    [ObservableProperty]
    private Material.Icons.MaterialIconKind _icon;

    /// <summary>The page this button opens, used to highlight it while that page is showing.</summary>
    public string? PageKey { get; }

    [ObservableProperty]
    private bool _isActive;

    /// <summary>A count shown on the button (Drops waiting); hidden at 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badge;

    public bool HasBadge => Badge > 0;
}

/// <summary>
/// State of the single-view mobile shell: the page stack that stands in for desktop windows,
/// the slide-out menu and the status strip.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private const string PrefsName = "movibeskua.shell";

    /// <summary>The name and guild Streamer Mode shows in place of the player's own.</summary>
    public const string StreamerName = "MVS User";
    public const string StreamerGuild = "MVS";

    private readonly IScriptInterface _bot;
    private readonly DispatcherTimer _statusTimer;
    private readonly ISharedPreferences? _prefs;
    private RailAction? _streamerAction;
    private MobileJumpViewModel? _jumpPage;
    private ArmyViewModel? _armyPage;

    public ShellViewModel(MainMenuViewModel mainMenu, IScriptInterface bot, ScriptLoaderViewModel scriptLoader)
    {
        MainMenu = mainMenu;
        _bot = bot;
        _prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        _isStreamerMode = _prefs?.GetBoolean("streamerMode", false) ?? false;
        _isBotMode = _prefs?.GetBoolean("botMode", false) ?? false;
        _sidebarOn = _prefs?.GetBoolean("sidebar", true) ?? true;
        HookScriptStreamerMode();
        HookDrops();
        ScriptLoader = scriptLoader;
        ScriptLoader.PropertyChanged += OnScriptLoaderChanged;
        Pages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPreviousPage));
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

    /// <summary>Portrait has one bar under the game: both rails' buttons, left ones first.</summary>
    public ObservableCollection<RailAction> AllRail { get; } = new();

    // The rails' two sets: the game's own bottom-bar buttons (the default), and the bot tools, which
    // a triple tap on the crest switches to and back.
    private readonly List<RailAction> _gameLeft = new();
    private readonly List<RailAction> _gameRight = new();
    private readonly List<RailAction> _botLeft = new();
    private readonly List<RailAction> _botRight = new();

    /// <summary>The rails show the bot tools (Scripts, Auto, Loader...) instead of the game's buttons.</summary>
    [ObservableProperty]
    private bool _isBotMode;

    [ObservableProperty]
    private bool _hasHardwareAlert;

    [ObservableProperty]
    private string _hardwareAlertText = string.Empty;

    [ObservableProperty]
    private Material.Icons.MaterialIconKind _hardwareAlertIcon = Material.Icons.MaterialIconKind.AlertCircle;

    [ObservableProperty]
    private string _hardwareAlertBrush = "#D97706";

    private DispatcherTimer? _hardwareAlertTimer;

    public void PostHardwareAlert(string message, Material.Icons.MaterialIconKind icon = Material.Icons.MaterialIconKind.AlertCircle, string brush = "#D97706", int durationMs = 3800)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HardwareAlertText = message;
            HardwareAlertIcon = icon;
            HardwareAlertBrush = brush;
            HasHardwareAlert = true;

            _hardwareAlertTimer?.Stop();
            _hardwareAlertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _hardwareAlertTimer.Tick += (_, _) =>
            {
                _hardwareAlertTimer?.Stop();
                HasHardwareAlert = false;
            };
            _hardwareAlertTimer.Start();
        });
    }

    private DispatcherTimer? _crestTimer;
    private DateTime _lastCrestTap;
    private int _crestTaps;

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
    /// Landscape: the page covers all of the game but a strip at its left edge.
    /// </summary>
    [ObservableProperty]
    private bool _isPageWide;

    [ObservableProperty]
    private string _statusText = "Not logged in";

    /// <summary>
    /// Keeps the character name out of the status line (name · map · cell), for streaming or screenshots.
    /// </summary>
    [ObservableProperty]
    private bool _isStreamerMode;

    public bool HasPage => CurrentPage is not null;

    /// <summary>A page opened before the current one, which back returns to.</summary>
    public bool HasPreviousPage => Pages.Count > 1;

    public string PageTitle => CurrentPage?.Title ?? "MVS";

    public string ToggleScriptText => ScriptLoader.ToggleScriptText;

    public bool IsScriptRunning => Ioc.Default.GetRequiredService<IScriptManager>().ScriptRunning;

    /// <summary>
    /// Opens a page, or closes the pages when it is already the one showing (rail buttons toggle).
    /// </summary>
    public void TogglePage(string key, Action open)
    {
        if (CurrentPage?.Key == key)
        {
            CloseAllPages();
            return;
        }
        open();
    }

    public void ShowPage(string key, string title, object content, Action? closed = null)
    {
        ShellPage? existing = Pages.FirstOrDefault(p => p.Key == key);
        if (existing is not null)
        {
            Pages.Remove(existing);
        }
        else
        {
            existing = new ShellPage(key, title, content) { Closed = closed };
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

        ShellPage page = Pages[^1];
        Pages.RemoveAt(Pages.Count - 1);
        CurrentPage = Pages.LastOrDefault();
        Dismiss(page);
    }

    /// <summary>Closes one page wherever it is in the stack.</summary>
    public void ClosePage(string key)
    {
        if (Pages.FirstOrDefault(p => p.Key == key) is not { } page)
            return;
        Pages.Remove(page);
        if (CurrentPage == page)
            CurrentPage = Pages.LastOrDefault();
        Dismiss(page);
    }

    private static void Dismiss(ShellPage page)
    {
        SetActive(page, false);
        page.Closed?.Invoke();
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
        List<ShellPage> closed = Pages.ToList();
        Pages.Clear();
        CurrentPage = null;
        foreach (ShellPage page in closed)
            Dismiss(page);
    }

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    [RelayCommand]
    private void ToggleGameMaximized()
    {
        IsGameMaximized = !IsGameMaximized;
        if (IsGameMaximized)
            UsageTracker.Track(Signal.GAME_MAXIMIZED);
    }

    [RelayCommand]
    private void TogglePageWide()
    {
        IsPageWide = !IsPageWide;
        if (IsPageWide)
            UsageTracker.Track(Signal.PAGE_WIDE);
    }

    /// <summary>The width a page was dragged to, as a share of the space between the rails; null until it is dragged.</summary>
    public double? GetPageWidth(string key)
    {
        float share = _prefs?.GetFloat("pageWidth." + key, 0) ?? 0;
        return share > 0 ? share : null;
    }

    public void SetPageWidth(string key, double share)
    {
        _prefs?.Edit()?.PutFloat("pageWidth." + key, (float)share)?.Apply();
        IsPageWide = false;
        UsageTracker.Track(Signal.PAGE_RESIZED);
    }

    [RelayCommand]
    private Task ToggleScript() => ScriptLoader.ToggleScriptCommand.ExecuteAsync(null);

    [RelayCommand]
    private void OpenLogin() => ShowPage("Login", "Accounts", Ioc.Default.GetRequiredService<LoginViewModel>());

    private void OpenManaged(string key) => Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow(key);

    private BotOptionsViewModel? _botOptionsPage;

    /// <summary>MVS's bot settings (Use Best Enhancement) above the game options the bot can apply.</summary>
    private void OpenBotOptions() => ShowPage("Game", "Bot options", BotOptionsPage());

    private BotOptionsViewModel BotOptionsPage()
    {
        if (_botOptionsPage is null)
        {
            object game = (Ioc.Default.GetRequiredService<IWindowService>() as Services.MobileWindowService)?.GetManagedWindow("Game")
                ?? Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.Options.GameOptionsViewModel>();
            _botOptionsPage = new BotOptionsViewModel(game);
        }
        return _botOptionsPage;
    }

    /// <summary>
    /// Jump with Fast Travel's saved spots under it, sharing the Fast Travel page's list.
    /// </summary>
    private void OpenJump() => ShowPage("Jump", "Jump", JumpPage());

    private MobileJumpViewModel JumpPage()
    {
        if (_jumpPage is null)
        {
            FastTravelViewModel fastTravel =
                (Ioc.Default.GetRequiredService<IWindowService>() as Services.MobileWindowService)?.GetManagedWindow("Fast Travel") as FastTravelViewModel
                ?? Ioc.Default.GetRequiredService<FastTravelViewModel>();
            _jumpPage = new MobileJumpViewModel(MainMenu.JumpViewModel, fastTravel, _bot);
        }
        return _jumpPage;
    }

    private void OpenArmy() => ShowPage("Army", "Army", ArmyPage());

    private ArmyViewModel ArmyPage() => _armyPage ??= new ArmyViewModel(() => IsStreamerMode, Ioc.Default.GetRequiredService<IDialogService>());

    /// <summary>
    /// The pages the bot rails open, most used first, for the shell to build before the first tap: a
    /// page's first open compiles and lays out its view on the main thread (0.25-0.7 s on the phone,
    /// seen as a stutter); every later open takes a few ms.
    /// </summary>
    public List<object> BotPagesToPrepare()
    {
        List<object> pages = new();
        void Add(Func<object?> get)
        {
            try
            {
                if (get() is { } page)
                    pages.Add(page);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Page] prepare skipped: {ex.Message}");
            }
        }
        Services.MobileWindowService? windows = Ioc.Default.GetRequiredService<IWindowService>() as Services.MobileWindowService;
        Add(() => windows?.GetManagedWindow("Scripts"));
        Add(() => MainMenu.AutoViewModel);
        Add(BotOptionsPage);
        Add(() => Ioc.Default.GetRequiredService<ScriptRepoViewModel>());
        Add(JumpPage);
        Add(() => windows?.GetManagedWindow("Loader"));
        Add(() => windows?.GetManagedWindow("Grabber"));
        Add(() => windows?.GetManagedWindow("Logs"));
        Add(() => Ioc.Default.GetRequiredService<LoginViewModel>());
        Add(ArmyPage);
        return pages;
    }

    [RelayCommand]
    private void ToggleStreamerMode() => IsStreamerMode = !IsStreamerMode;

    /// <summary>
    /// One Streamer Mode: the rail button and Skua's own option (Options > Streamer Mode) are kept in
    /// step, so either switch turns on everything both do. Skua's side hides other players, gold, coins
    /// and chat and tidies the room name; the page draws the stand-in name, guild and portrait names.
    /// </summary>
    private void HookScriptStreamerMode()
    {
        Skua.Core.Scripts.ScriptOption.StreamerName = StreamerName;
        Skua.Core.Scripts.ScriptOption.StreamerGuild = StreamerGuild;
        Skua.Core.Scripts.ScriptOption.NameTagsDrawnByHost = true;
        _scriptOption = Ioc.Default.GetService<IScriptOption>();
        if (_scriptOption is null)
            return;
        if (_scriptOption.StreamerMode)
            _isStreamerMode = true;
        else if (_isStreamerMode)
            _scriptOption.StreamerMode = true;
        if (_scriptOption is INotifyPropertyChanged changes)
        {
            changes.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IScriptOption.StreamerMode))
                    Dispatcher.UIThread.Post(() => IsStreamerMode = _scriptOption.StreamerMode);
            };
        }
    }

    private IScriptOption? _scriptOption;

    partial void OnIsStreamerModeChanged(bool value)
    {
        if (_scriptOption is not null && _scriptOption.StreamerMode != value)
            _scriptOption.StreamerMode = value;
        _prefs?.Edit()?.PutBoolean("streamerMode", value)?.Apply();
        UsageTracker.Track(value ? Signal.STREAMER_ON : Signal.STREAMER_OFF);
        UpdateStreamerAction();
        PostStreamerMode();
        RefreshStatus();
    }

    // The game page keeps the player's name tag, guild tag and portraits masked while this is on.
    private void PostStreamerMode()
    {
        string json = IsStreamerMode
            ? $"{{\"t\":\"streamer\",\"on\":true,\"name\":\"{StreamerName}\",\"guild\":\"{StreamerGuild}\"}}"
            : "{\"t\":\"streamer\",\"on\":false}";
        Ioc.Default.GetService<Skua.Flash.Ruffle.RuffleFlashUtil>()?.PostCommand(json);
    }

    private void UpdateStreamerAction()
    {
        if (_streamerAction is null)
            return;
        _streamerAction.IsActive = IsStreamerMode;
        _streamerAction.Icon = IsStreamerMode ? Material.Icons.MaterialIconKind.EyeOff : Material.Icons.MaterialIconKind.Eye;
    }

    private void BuildRails()
    {
        RailAction Page(string label, Material.Icons.MaterialIconKind icon, string key, Action open)
            => new(label, icon, new CountedCommand(Signal.NAV_RAIL, new RelayCommand(() => TogglePage(key, open))), key);
        RailAction Managed(string label, Material.Icons.MaterialIconKind icon, string key)
            => Page(label, icon, key, () => OpenManaged(key));
        // One of the game's bottom-bar buttons, tapped inside the game (skua.swf mvsGameButton).
        RailAction GameButton(string label, Material.Icons.MaterialIconKind icon, string button, string image)
            => new(label, icon, new CountedCommand(Signal.NAV_RAIL, new RelayCommand(() => TapGameButton(button)))) { Image = RailImage(image) };

        _gameLeft.Add(GameButton("Quests", Material.Icons.MaterialIconKind.AlertDecagram, "btnQuest", "quests"));
        _gameLeft.Add(GameButton("Temp Inv", Material.Icons.MaterialIconKind.PackageVariant, "temp", "tempinv"));
        _gameLeft.Add(GameButton("Map", Material.Icons.MaterialIconKind.Map, "btnMap", "map"));
        _gameLeft.Add(GameButton("Lore", Material.Icons.MaterialIconKind.BookOpenVariant, "btnBook", "lore"));
        _gameLeft.Add(GameButton("Menu", Material.Icons.MaterialIconKind.ViewGrid, "btnMenu", "menu"));
        _gameLeft.Add(GameButton("House", Material.Icons.MaterialIconKind.Home, "btnHouse", "house"));
        _gameLeft.Add(GameButton("Settings", Material.Icons.MaterialIconKind.Cog, "btnOption", "settings"));

        _gameRight.Add(GameButton("Bag", Material.Icons.MaterialIconKind.BagPersonal, "btnBag", "bag"));
        _dropsAction = new RailAction("Drops", Material.Icons.MaterialIconKind.PackageDown, new CountedCommand(Signal.NAV_RAIL, new RelayCommand(ToggleDrops))) { Image = RailImage("drops") };
        _gameRight.Add(_dropsAction);
        _gameRight.Add(new RailAction("Bank", Material.Icons.MaterialIconKind.Safe, new CountedCommand(Signal.NAV_RAIL, new RelayCommand(OpenBank))) { Image = RailImage("bank") });
        _gameRight.Add(GameButton("Rest", Material.Icons.MaterialIconKind.HeartPulse, "btnRest", "rest"));
        _gameRight.Add(GameButton("Char", Material.Icons.MaterialIconKind.ShieldAccount, "btnChar", "char"));

        _botLeft.Add(Page("Accounts", Material.Icons.MaterialIconKind.AccountPlus, "Login", () => OpenLogin()));
        _botLeft.Add(Managed("Scripts", Material.Icons.MaterialIconKind.ScriptText, "Scripts"));
        _botLeft.Add(Page("Repo", Material.Icons.MaterialIconKind.Bookshelf, "Script Repo", () => ShowPage("Script Repo", "Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>())));
        _botLeft.Add(Managed("Logs", Material.Icons.MaterialIconKind.BookOpenPageVariant, "Logs"));
        _botLeft.Add(Page("Army", Material.Icons.MaterialIconKind.AccountGroup, "Army", OpenArmy));

        _botRight.Add(Page("Auto", Material.Icons.MaterialIconKind.SwordCross, "Auto", () => ShowPage("Auto", "Auto Attack / Hunt", MainMenu.AutoViewModel)));
        _botRight.Add(Page("Jump", Material.Icons.MaterialIconKind.MapMarkerRadius, "Jump", OpenJump));
        _botRight.Add(Managed("Loader", Material.Icons.MaterialIconKind.TreasureChest, "Loader"));
        _botRight.Add(Managed("Grabber", Material.Icons.MaterialIconKind.ClipboardList, "Grabber"));
        _botRight.Add(Page("Options", Material.Icons.MaterialIconKind.Cog, "Game", OpenBotOptions));
        _streamerAction = new RailAction("Streamer", Material.Icons.MaterialIconKind.Eye, ToggleStreamerModeCommand);
        _botRight.Add(_streamerAction);
        UpdateStreamerAction();
        ApplyRails();
    }

    /// <summary>The game's own bottom-bar art (and its gold icon for Bank, its bag item icon for Temp Inv).</summary>
    private static Avalonia.Media.IImage? RailImage(string name)
    {
        try
        {
            using System.IO.Stream stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://MoVibeSkua/Images/rail/{name}.png"));
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Rail] icon {name}: {ex.Message}");
            return null;
        }
    }

    private void ApplyRails()
    {
        LeftRail.Clear();
        RightRail.Clear();
        AllRail.Clear();
        foreach (RailAction action in IsBotMode ? _botLeft : _gameLeft)
        {
            LeftRail.Add(action);
            AllRail.Add(action);
        }
        foreach (RailAction action in IsBotMode ? _botRight : _gameRight)
        {
            RightRail.Add(action);
            AllRail.Add(action);
        }
        if (!IsBotMode)
            AllRail.Add(_guideAction ??= new RailAction("Guide", Material.Icons.MaterialIconKind.HelpCircle, OpenGuideCommand, "Guide"));
    }

    private RailAction? _guideAction;

    /// <summary>The MVS guide, from its button above the crest (portrait: the end of the bottom bar).</summary>
    [RelayCommand]
    private void OpenGuide()
    {
        // The bot tools guide is opened from the guide: Guide closes either.
        if (CurrentPage?.Key == "Bot Guide")
        {
            CloseAllPages();
            return;
        }
        TogglePage("Guide", ShowGuide);
    }

    /// <summary>The game's Options row "Sidebar" (saved here too, so the rails start right before the game loads).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailsShown))]
    private bool _sidebarOn = true;

    /// <summary>Rails brought back for a while by a crest tap while the Sidebar option is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailsShown))]
    private bool _railsPeek;

    public bool RailsShown => SidebarOn || RailsPeek;

    partial void OnSidebarOnChanged(bool value)
    {
        _prefs?.Edit()?.PutBoolean("sidebar", value)?.Apply();
        RailsPeek = false;
    }

    partial void OnIsBotModeChanged(bool value)
    {
        _prefs?.Edit()?.PutBoolean("botMode", value)?.Apply();
        if (!value)
        {
            // Back to the game's buttons: the bot's pages and menu go away with its rails.
            CloseAllPages();
            IsMenuOpen = false;
        }
        ApplyRails();
    }

    private static void TapGameButton(string button)
    {
        if (Ioc.Default.GetService<Skua.Flash.Ruffle.RuffleFlashUtil>() is not { } flash)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                // Quests opens the quest log itself (the game's L key), not the bottom bar's small list.
                if (button == "btnQuest")
                    flash.Call("callGameFunction", "world.toggleQuestLog");
                else
                    flash.Call("mvsGameButton", button);
            }
            catch
            {
                // Not logged in yet, or the game is between maps.
            }
        });
    }

    /// <summary>
    /// The crest: one tap opens Support MVS (or, with the Sidebar option off, shows / hides the rails),
    /// two quick taps switch the rails between the game's buttons and the bot tools. One tap waits a
    /// moment for a second.
    /// </summary>
    [RelayCommand]
    private void CrestTap()
    {
        DateTime now = DateTime.UtcNow;
        _crestTaps = (now - _lastCrestTap).TotalMilliseconds < CrestTapWindowMs ? _crestTaps + 1 : 1;
        _lastCrestTap = now;
        if (_crestTimer is null)
        {
            _crestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CrestTapWindowMs) };
            _crestTimer.Tick += (_, _) =>
            {
                _crestTimer.Stop();
                int taps = _crestTaps;
                _crestTaps = 0;
                if (taps == 1 && !SidebarOn)
                    RailsPeek = !RailsPeek;
                else if (taps == 1)
                    TogglePage("Support", OpenSupport);
            };
        }
        _crestTimer.Stop();
        if (_crestTaps >= 2)
        {
            _crestTaps = 0;
            IsBotMode = !IsBotMode;
            return;
        }
        _crestTimer.Start();
    }

    private const int CrestTapWindowMs = 400;

    private GuideViewModel? _guidePage;
    private SupportViewModel? _supportPage;

    private void ShowGuide() => ShowPage("Guide", "How to use MVS", _guidePage ??= new GuideViewModel(() => IsBotMode = !IsBotMode, OpenSupport, OpenBotGuide));

    private BotGuideViewModel? _botGuidePage;

    private void OpenBotGuide() => ShowPage("Bot Guide", "Bot tools guide", _botGuidePage ??= new BotGuideViewModel(() => IsBotMode = true));

    private void OpenSupport() => ShowPage("Support", "Support MVS", _supportPage ??= new SupportViewModel());

    partial void OnCurrentPageChanged(ShellPage? value)
    {
        foreach (RailAction action in LeftRail.Concat(RightRail).Concat(AllRail).Distinct().Where(a => a.PageKey is not null))
            action.IsActive = action.PageKey == value?.Key;
        UsageTracker.EnterScreen(Screens.OfPage(value?.Key));
    }

    private void BuildMenu()
    {
        MenuGroups.Clear();
        // Pages already on the rails are left out of the menu; the in-game bank takes the top spot.
        MenuGroups.Add(new ShellMenuGroup("Game", new[]
        {
            new ShellMenuEntry("Bot tools guide", new RelayCommand(OpenBotGuide)),
            new ShellMenuEntry("Open Bank", new RelayCommand(OpenBank)),
            new ShellMenuEntry("Graphics & Performance", new RelayCommand(() => ShowPage("Graphics", "Graphics & Performance", Ioc.Default.GetRequiredService<GraphicsViewModel>())))
        }));

        foreach (IMainMenuItemViewModel item in MainMenu.MainMenuItems)
        {
            // Plugins get their own group below; Bank is at the top.
            if (item.Header is "Plugins" or "Bank")
                continue;
            List<ShellMenuEntry> entries = new();
            if (item.SubItems is { Count: > 0 })
            {
                foreach (IMainMenuItemViewModel sub in item.SubItems)
                {
                    if (!DesktopOnlyEntries.Contains(sub.Header) && !OnRails.Contains(sub.Header)
                        && (Platform.DevMode.IsOn || !DeveloperEntries.Contains(sub.Header)))
                        entries.Add(new ShellMenuEntry(sub.Header, sub.Command));
                }
            }
            else
            {
                entries.Add(new ShellMenuEntry(item.Header, item.Command));
            }
            if (entries.Count > 0)
                MenuGroups.Add(new ShellMenuGroup(item.Header, entries));
        }

        List<ShellMenuEntry> pluginEntries = new() { new ShellMenuEntry("Manage Plugins", new RelayCommand(() => OpenManaged("Plugins"))) };
        foreach (IMainMenuItemViewModel plugin in MainMenu.Plugins.Where(p => p.Header != "View Plugins"))
            pluginEntries.Add(new ShellMenuEntry(plugin.Header, plugin.Command));
        MenuGroups.Add(new ShellMenuGroup("Plugins", pluginEntries));
    }

    // Desktop windows that cannot work on a phone: hotkeys hook the desktop window (and drive other
    // clients through Win32), and the interceptor's local proxy is not a socket Ruffle may open.
    private static readonly HashSet<string> DesktopOnlyEntries = new() { "HotKeys", "Interceptor" };

    // Developer tools, only in developer mode: the C# console and the raw packet tools.
    private static readonly HashSet<string> DeveloperEntries = new() { "Console", "Spammer", "Logger" };

    // Menu entries the rails already open (Fast Travel's spots are on the Jump page).
    private static readonly HashSet<string> OnRails = new() { "Script Loader", "Game", "Loader", "Grabber", "Logs", "Fast Travel" };

    // The bank opens in the game; opening it waits for the game, so not on the UI thread.
    private RailAction? _dropsAction;

    /// <summary>Opens or closes the Drops panel over the game (overlays.js).</summary>
    private static void ToggleDrops()
        => Ioc.Default.GetService<Skua.Flash.Ruffle.RuffleFlashUtil>()?.PostCommand("{\"t\":\"drops\"}");

    /// <summary>
    /// MVS Drops: skua.swf keeps drops in the game's own drop store instead of popping them up, and
    /// reports the waiting list ("mvsDrops"), which the Drops button counts. Switched on each time the
    /// game loads.
    /// </summary>
    private void HookDrops()
    {
        Skua.Core.Interfaces.IFlashUtil flash = Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IFlashUtil>();
        flash.FlashCall += (function, args) =>
        {
            if (function == "loaded")
            {
                _ = Task.Run(() =>
                {
                    try { flash.Call("mvsDropsOn", true); }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Drops] {ex.Message}"); }
                });
            }
            else if (function == "mvsDrops" && args is { Length: > 0 })
            {
                int count = 0;
                try { count = Newtonsoft.Json.Linq.JArray.Parse(args[0]?.ToString() ?? "[]").Count; }
                catch { }
                Dispatcher.UIThread.Post(() => { if (_dropsAction is not null) _dropsAction.Badge = count; });
            }
            else if (function == "mvsOption" && args is { Length: > 0 })
            {
                // The game's Options row "MVS Drops" ("mvsDrops:on" / "mvsDrops:off"): off gives the
                // game's own drops, so no Drops button.
                string raw = string.Join(",", args.Select(a => a?.ToString()));
                if (raw.Contains("mvsDrops:"))
                {
                    bool on = !raw.Contains("mvsDrops:off");
                    Dispatcher.UIThread.Post(() => SetDropsRail(on));
                }
                // "Sidebar" (and the "MVS UI" master switch, which skua.swf folds into it): off hides
                // the rails beside the game; the crest stays so one tap brings them back.
                if (raw.Contains("mvsSidebar:"))
                {
                    bool on = !raw.Contains("mvsSidebar:off");
                    Dispatcher.UIThread.Post(() => SidebarOn = on);
                }
            }
        };
        // Already loaded (the shell came up after the game).
        _ = Task.Run(() => { try { flash.Call("mvsDropsOn", true); } catch { } });
    }

    private void SetDropsRail(bool on)
    {
        if (_dropsAction is null || _gameRight.Contains(_dropsAction) == on)
            return;
        if (on)
            _gameRight.Insert(Math.Min(1, _gameRight.Count), _dropsAction);
        else
            _gameRight.Remove(_dropsAction);
        ApplyRails();
    }

    private void OpenBank()
    {
        IsMenuOpen = false;
        _ = Task.Run(() =>
        {
            try
            {
                if (!_bot.Player.LoggedIn)
                    return;
                // A second tap closes it, like the other buttons (Skua's Open does nothing when it is open).
                Skua.Core.Interfaces.IFlashUtil flash = Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IFlashUtil>();
                if (string.Equals(flash.GetGameObject<string>("ui.mcPopup.currentLabel"), "Bank", StringComparison.OrdinalIgnoreCase))
                    flash.CallGameFunction("world.toggleBank");
                else
                    Ioc.Default.GetRequiredService<IScriptBank>().Open();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Bank] open failed: {ex.Message}");
            }
        });
    }

    private void OnScriptLoaderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptLoaderViewModel.ToggleScriptText))
        {
            OnPropertyChanged(nameof(ToggleScriptText));
            OnPropertyChanged(nameof(IsScriptRunning));
        }
    }

    private bool _wasLoggedIn;

    private void RefreshStatus()
    {
        _ = Task.Run(() =>
        {
            string text;
            bool loggedIn = false;
            try
            {
                loggedIn = _bot.Player.LoggedIn;
                // AQW sets its own quality while logging in; put the Graphics page's choice back after.
                if (loggedIn && !_wasLoggedIn)
                    _ = Task.Delay(4000).ContinueWith(_ => Ioc.Default.GetService<GraphicsViewModel>()?.Apply());
                _wasLoggedIn = loggedIn;
                string name = loggedIn ? (IsStreamerMode ? StreamerName : _bot.Player.Username) : string.Empty;
                string map = loggedIn ? _bot.Map.Name : string.Empty;
                text = loggedIn ? $"{name} · {map} · {_bot.Player.Cell}" : "Not logged in";
                // Sent again every tick so a reloaded game page picks it up; the page ignores repeats.
                if (IsStreamerMode)
                    PostStreamerMode();

                IScriptManager scripts = Ioc.Default.GetRequiredService<IScriptManager>();
                Army.Publish(new Army.ClientStatus(Army.Slot, loggedIn, name, map, scripts.LoadedScript ?? string.Empty, scripts.ScriptRunning));
            }
            catch
            {
                text = "Loading...";
            }
            if (!Army.IsFirst)
                text = $"{Army.Label(Army.Slot)} · {text}";
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
