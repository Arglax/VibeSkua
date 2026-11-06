using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.App.Avalonia.Services;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Skills;
using Skua.Core.Scripts;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels;

public partial class AutoViewModel : BotControlViewModelBase, IDisposable
{
    private CancellationTokenSource? _autoCts;

    public AutoViewModel(IScriptAuto auto, IScriptInventory inventory, IAdvancedSkillContainer advancedSkills, IScriptMonster monsters, IScriptMap map)
        : base("Auto Attack")
    {
        StrongReferenceMessenger.Default.Register<AutoViewModel, StopAutoMessage>(this, async (r, m) => await r.StopAutoAsync());
        StrongReferenceMessenger.Default.Register<AutoViewModel, StartAutoAttackMessage>(this, async (r, m) => await r.StartAutoAttack());
        StrongReferenceMessenger.Default.Register<AutoViewModel, StartAutoHuntMessage>(this, async (r, m) => await r.StartAutoHunt());

        Auto = auto;
        _inventory = inventory;
        _advancedSkills = advancedSkills;
        _monsters = monsters;
        _map = map;
        _pinned = LoadPinned();
        StopAutoAsyncCommand = new AsyncRelayCommand(StopAutoAsync);
        // A new map has other monsters: list them without a tap on Refresh.
        StrongReferenceMessenger.Default.Register<AutoViewModel, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents,
            (r, _) => r.RefreshMonstersSoon());
        ClassRatings.FocusChanged += OnRatingFocusChanged;
    }

    private void RefreshMonstersSoon()
    {
        _ = Task.Run(async () =>
        {
            // The map's monsters arrive just after the join message.
            await Task.Delay(1500);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshMonstersCommand.Execute(null));
        });
    }

    // The use case changed here or on the chip in the game's inventory: re-sort, and use the class's
    // mode for it.
    private void OnRatingFocusChanged(string focus)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(ClassSort));
            OnPropertyChanged(nameof(FilteredClasses));
            OnPropertyChanged(nameof(UseCaseHint));
            OnPropertyChanged(nameof(IsUltraFocus));
            ApplyPreferredMode();
        });
    }

    private readonly IScriptInventory _inventory;
    private readonly IAdvancedSkillContainer _advancedSkills;
    private readonly IScriptMonster _monsters;
    private readonly IScriptMap _map;

    [ObservableProperty]
    private ClassUseMode? _selectedClassMode;

    [ObservableProperty]
    private string? _selectedClassModeString;

    [ObservableProperty]
    private string? _manualMapIDs;

    private bool _syncingEquipped;

    private async void OnSelectedClassStringChanged(string? value)
    {
        // Picking up the class already equipped in the game must not equip it again.
        if (_syncingEquipped)
            return;
        await EquipSelectedClassAsync();
    }

    /// <summary>
    /// Makes the selected class the one equipped in the game (equipping in the game, or picking it
    /// here, both lead to the same class), with the mode for the current use case.
    /// </summary>
    [RelayCommand]
    private async Task SyncEquippedClass()
    {
        string? equipped = await Task.Run(() =>
        {
            try
            {
                return _inventory.Items?.FirstOrDefault(i => i.Category == ItemCategory.Class && i.Equipped)?.Name;
            }
            catch
            {
                return null;
            }
        });
        if (string.IsNullOrWhiteSpace(equipped) || string.Equals(equipped, _selectedClassString, StringComparison.Ordinal))
            return;
        _syncingEquipped = true;
        try
        {
            SelectedClass = equipped;
        }
        finally
        {
            _syncingEquipped = false;
        }
    }

    async partial void OnSelectedClassModeStringChanged(string? value)
    {
        OnPropertyChanged(nameof(ModeHelp));
        await LoadSelectedClassMode();
    }

    /// <summary>When to use the selected mode, in one line.</summary>
    public string ModeHelp => SelectedClassModeString switch
    {
        null or "" => "Mode: Auto uses your equipped class. Classes with skill sets offer modes here.",
        "Base" => "Base: the class's normal skill order. Good for most fights.",
        "Atk" => "Atk: most damage, little defence. Bosses that don't hit hard.",
        "Def" => "Def: survival first. Monsters that hit hard or long fights.",
        "Farm" => "Farm: area skills and mana saving. Many weak monsters.",
        "Solo" => "Solo: damage plus self-heal and damage reduction. Bosses alone.",
        "Supp" => "Supp: buffs and heals for the team. Groups and ultras.",
        "Dodge" => "Dodge: dodge and evasion skills first. Heavy single hits.",
        _ => $"{SelectedClassModeString}: a custom skill set from Skills."
    };

    /// <summary>The mode each use case prefers, best first; the class's first mode if it has none of them.</summary>
    private static string[] PreferredModes(string useCase) => useCase switch
    {
        "Farm" => new[] { "Farm", "Atk", "Base" },
        "Solo" => new[] { "Solo", "Def", "Base" },
        "Boss" => new[] { "Atk", "Solo", "Base" },
        "Ultra" => new[] { "Supp", "Def", "Base" },
        "PvP" => new[] { "Atk", "Base" },
        _ => new[] { "Base" }
    };

    private void ApplyPreferredMode()
    {
        if (_selectedClassString is null || CurrentClassModeStrings.Count == 0)
            return;
        string mode = PreferredModes(ClassRatings.Focus).FirstOrDefault(m => CurrentClassModeStrings.Contains(m))
                      ?? CurrentClassModeStrings[0];
        if (mode != SelectedClassModeString)
            SelectedClassModeString = mode;
        else
            _ = LoadSelectedClassMode();
    }

    /// <summary>Concise use-case description under the class list's buttons.</summary>
    public string UseCaseHint => ClassRatings.UseCaseHint(ClassRatings.Focus);

    /// <summary>"All ultras" and each fight of the ultra guide.</summary>
    public IReadOnlyList<string> UltraFights { get; } = ClassRatings.UltraFightNames;

    public bool IsUltraFocus => ClassRatings.Focus == "Ultra";

    private string _ultraFight = ClassRatings.AllUltras;

    /// <summary>The ultra fight the class list ranks for (Ultra use case).</summary>
    public string UltraFight
    {
        get => _ultraFight;
        set
        {
            if (SetProperty(ref _ultraFight, value ?? ClassRatings.AllUltras))
                OnPropertyChanged(nameof(FilteredClasses));
        }
    }

    public IScriptAuto Auto { get; }
    /// <summary>The owned classes, read by <see cref="ReloadClassesCommand"/> (when the class list opens).</summary>
    public List<string>? PlayerClasses => _classEntries?.Select(c => c.Name).ToList();

    private List<ClassEntry>? _classEntries;
    private string _classSearch = string.Empty;

    /// <summary>Text typed in the class list's search box.</summary>
    public string ClassSearch
    {
        get => _classSearch;
        set
        {
            if (SetProperty(ref _classSearch, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FilteredClasses));
                OnPropertyChanged(nameof(ClassListHint));
            }
        }
    }

    /// <summary>Shown in place of an empty class list; null while it has classes.</summary>
    public string? ClassListHint => FilteredClasses.Count > 0 ? null
        : _classEntries is null || _classEntries.Count == 0 ? "No classes yet. Log in and get into the game, then open this list again."
        : "No class matches that search.";

    /// <summary>The owned classes matching <see cref="ClassSearch"/>, for the class list.</summary>
    public List<ClassEntry> FilteredClasses
    {
        get
        {
            if (_classEntries is null)
                return new();
            string search = _classSearch.Trim();
            IEnumerable<ClassEntry> list = search.Length == 0
                ? _classEntries
                : _classEntries.Where(c => c.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase));
            string sort = ClassRatings.Focus;
            // One ultra fight: rank by the guide's stars for it (classes it doesn't list go last).
            string? fight = sort == "Ultra" && _ultraFight != ClassRatings.AllUltras ? _ultraFight : null;
            list = list.Select(c => c with
            {
                Pinned = _pinned.Contains(c.Name),
                Sort = sort,
                FightStars = fight is null ? null : ClassRatings.FightStars(fight, c.Name)
            });
            // Pinned classes first, then the chosen use case's rating (unrated classes last).
            IOrderedEnumerable<ClassEntry> ordered = list.OrderByDescending(c => c.Pinned);
            if (fight is not null)
                ordered = ordered.ThenByDescending(c => c.FightStars ?? -1).ThenByDescending(c => c.Rating?.Ultra ?? 0);
            else if (sort != "A-Z")
                ordered = ordered.ThenByDescending(c => c.Rating?.Score(sort) ?? 0).ThenByDescending(c => c.Rating?.Pve ?? 0);
            return ordered.ThenBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // The inventory is a large game read plus JSON parsing; done on the UI thread it froze the app
    // while the list opened (about a quarter second on a phone).
    private List<ClassEntry>? ReadPlayerClasses()
    {
        if (_inventory.Items is not { } items)
            return null;

        return items
            .Where(item => item.Category == ItemCategory.Class)
            .GroupBy(item => item.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                return new ClassEntry(g.First().Name, g.Any(i => i.Upgrade), false, ClassRatings.Get(g.First().Name));
            })
            .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [ObservableProperty]
    private List<string> _currentClassModeStrings = new();

    // ----- Pinned classes and sorting -----

    private readonly HashSet<string> _pinned;
    private static string PinnedFile => Path.Combine(ClientFileSources.SkuaDIR, "auto-pinned-classes.json");

    /// <summary>
    /// The use case the class list is ranked for: "A-Z", "Farm", "Solo", "Boss", "Ultra" or "PvP". Shared
    /// with the grades in the game's inventory (<see cref="ClassRatings.Focus"/>).
    /// </summary>
    public string ClassSort
    {
        get => ClassRatings.Focus;
        set => ClassRatings.SetFocus(value);
    }

    [RelayCommand]
    private void SetClassSort(string sort) => ClassRatings.SetFocus(sort);

    [RelayCommand]
    private void TogglePin(ClassEntry? entry)
    {
        if (entry is null)
            return;
        if (!_pinned.Remove(entry.Name))
            _pinned.Add(entry.Name);
        try
        {
            File.WriteAllText(PinnedFile, Newtonsoft.Json.JsonConvert.SerializeObject(_pinned.OrderBy(n => n)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Auto] pins: {ex.Message}");
        }
        OnPropertyChanged(nameof(FilteredClasses));
    }

    private static HashSet<string> LoadPinned()
    {
        try
        {
            if (File.Exists(PinnedFile)
                && Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(PinnedFile)) is { } names)
                return new HashSet<string>(names, System.StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    }

    // ----- Monsters in the map (names and IDs, to pick a manual target) -----

    [ObservableProperty]
    private List<MonsterRow> _mapMonsterRows = new();

    [ObservableProperty]
    private string? _mapMonstersHint = "Lists this map's monsters and their IDs; it refreshes when you join a map.";

    [RelayCommand]
    private async Task RefreshMonsters()
    {
        string? mapName = null;
        List<Monster> monsters = await Task.Run(() =>
        {
            mapName = _map.Name;
            return _monsters.MapMonsters ?? new List<Monster>();
        });
        MonsterIdLog.RecordAll(mapName, monsters);
        MapMonsterRows = monsters
            .GroupBy(m => (Name: m.Name?.Trim() ?? "?", m.ID))
            .Select(g => new MonsterRow(
                g.Key.Name,
                g.Key.ID,
                g.Select(m => m.MapID).OrderBy(i => i).ToArray(),
                string.Join(", ", g.Select(m => m.Cell).Distinct()),
                g.Count(m => m.Alive)))
            .ToList();
        MapMonstersHint = MapMonsterRows.Count == 0
            ? "No monsters here. Join a map with monsters, then tap Refresh."
            : $"{mapName}: tap a monster to fight only it (fills Manual MapIDs). IDs are saved to monster-ids.json.";
    }

    /// <summary>Targets one monster type: its MapIDs go in Manual MapIDs, which override the automatic pick.</summary>
    [RelayCommand]
    private void PickMonster(MonsterRow? row)
    {
        if (row is not null)
            ManualMapIDs = string.Join(", ", row.MapIDs);
    }

    [RelayCommand]
    private void ClearManualIDs() => ManualMapIDs = null;

    private string? _selectedClassString;

    public string? SelectedClass
    {
        get => _selectedClassString;
        set
        {
            if (SetProperty(ref _selectedClassString, value) && value is not null)
            {
                CurrentClassModes = new();
                CurrentClassModeStrings = new List<string>();
                foreach (AdvancedSkill skill in _advancedSkills.LoadedSkills)
                {
                    if (skill.ClassName == _selectedClassString)
                        CurrentClassModes.Add(skill.ClassUseMode);
                }

                Dictionary<string, List<string>> classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(_selectedClassString, out List<string>? modes))
                {
                    CurrentClassModeStrings = new List<string>(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(CurrentClassModes));

                if (CurrentClassModes.Count > 0)
                {
                    SelectedClassMode = CurrentClassModes.First();
                }
                if (CurrentClassModeStrings.Count > 0)
                    ApplyPreferredMode();
                else
                    SelectedClassModeString = null;

                OnSelectedClassStringChanged(_selectedClassString);
            }
        }
    }

    private async Task EquipSelectedClassAsync()
    {
        try
        {
            await Task.Run(() => _inventory.EquipItem(_selectedClassString)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    public List<ClassUseMode>? CurrentClassModes { get; private set; }
    public IAsyncRelayCommand StopAutoAsyncCommand { get; }

    [RelayCommand]
    private async Task ReloadClasses()
    {
        string? selectedClass = _selectedClassString;
        _classEntries = await Task.Run(ReadPlayerClasses);
        OnPropertyChanged(nameof(PlayerClasses));
        OnPropertyChanged(nameof(FilteredClasses));
        OnPropertyChanged(nameof(ClassListHint));

        // Opening the list refreshes it, which clears the combo box's selection. Keep the picked class
        // and its mode while the class is still owned, instead of making them be picked again.
        if (selectedClass is not null && PlayerClasses?.Contains(selectedClass) == true)
        {
            _selectedClassString = selectedClass;
            OnPropertyChanged(nameof(SelectedClass));
            return;
        }

        CurrentClassModes = null;
        CurrentClassModeStrings = new List<string>();
        SelectedClass = null;
        SelectedClassMode = null;
        SelectedClassModeString = null;
    }

    private async Task LoadSelectedClassMode()
    {
        if (string.IsNullOrEmpty(SelectedClassModeString))
            return;

        AdvancedSkill? skill = _advancedSkills.GetClassModeSkills(_selectedClassString, SelectedClassModeString);
        if (skill != null)
        {
            SelectedClassMode = skill.ClassUseMode;
        }
    }

    [RelayCommand]
    private async Task StartAutoHunt()
    {
        await SyncEquippedClass();
        _autoCts?.Cancel();
        _autoCts?.Dispose();
        _autoCts = new CancellationTokenSource();

        int[]? manualMapIDs = ParseManualMapIDs();

        if (_selectedClassString is not null && SelectedClassMode is not null)
        {
            await Task.Factory.StartNew(
                () => Auto.StartAutoHunt(_selectedClassString, (ClassUseMode)SelectedClassMode, manualMapIDs),
                _autoCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return;
        }

        await Task.Factory.StartNew(
            () => Auto.StartAutoHunt(null, ClassUseMode.Base, manualMapIDs),
            _autoCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    [RelayCommand]
    private async Task StartAutoAttack()
    {
        await SyncEquippedClass();
        _autoCts?.Cancel();
        _autoCts?.Dispose();
        _autoCts = new CancellationTokenSource();

        int[]? manualMapIDs = ParseManualMapIDs();

        if (_selectedClassString is not null && SelectedClassMode is not null)
        {
            await Task.Factory.StartNew(
                () => Auto.StartAutoAttack(_selectedClassString, (ClassUseMode)SelectedClassMode, manualMapIDs),
                _autoCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return;
        }

        await Task.Factory.StartNew(
            () => Auto.StartAutoAttack(null, ClassUseMode.Base, manualMapIDs),
            _autoCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private int[]? ParseManualMapIDs()
    {
        if (string.IsNullOrWhiteSpace(ManualMapIDs))
            return null;

        List<int> mapIds = new();
        foreach (string part in ManualMapIDs.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int id))
                mapIds.Add(id);
        }
        return mapIds.Count > 0 ? mapIds.ToArray() : null;
    }

    private async Task StopAutoAsync()
    {
        _autoCts?.Cancel();
        await Auto.StopAsync();
        _autoCts?.Dispose();
        _autoCts = null;
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _autoCts?.Cancel();
                _autoCts?.Dispose();
                StrongReferenceMessenger.Default.UnregisterAll(this);
                ClassRatings.FocusChanged -= OnRatingFocusChanged;
            }

            _disposed = true;
        }
    }

    ~AutoViewModel()
    {
        Dispose(false);
    }
}

/// <summary>A class in the Auto page's class list; <see cref="MemberOnly"/> marks an upgrade (member) item.
/// <see cref="Rating"/> holds the community ratings (null when unrated); <see cref="Sort"/> is the use case;
/// <see cref="FightStars"/> the ultra guide's stars for the chosen fight.</summary>
public record ClassEntry(string Name, bool MemberOnly, bool Pinned, ClassRatings.Rating? Rating, string Sort = "A-Z", int? FightStars = null)
{
    /// <summary>Stars for the use case being ranked (overall PvE on A-Z, the chosen fight on Ultra).</summary>
    public string RatingText => FightStars is int stars ? ClassRatings.StarText(stars)
        : Rating is null ? string.Empty
        : ClassRatings.Grade(Rating.Score(Sort));

    /// <summary>Every use case's stars, and the ultra role, for the line under the name.</summary>
    public string DetailText => Rating is null ? string.Empty
        : $"Farm {ClassRatings.Stars(Rating.Farm)}★ · Solo {ClassRatings.Stars(Rating.Solo)}★ · Boss {ClassRatings.Stars(Rating.Boss)}★ · Ultra {ClassRatings.Stars(Rating.Ultra)}★ · PvP {ClassRatings.Stars(Rating.Pvp)}★"
          + (string.IsNullOrEmpty(Rating.UltraRole) ? "" : $" · {Rating.UltraRole}");

    public bool HasDetail => Rating is not null;
}

/// <summary>One monster type in the map: name, MonID, its MapIDs (one per spawn) and cells.</summary>
public record MonsterRow(string Name, int MonID, int[] MapIDs, string Cells, int Alive)
{
    public string Title => $"{Name}  (MonID {MonID})";
    public string Detail => $"MapID {string.Join(", ", MapIDs)} · {Cells} · {Alive} alive";
}
