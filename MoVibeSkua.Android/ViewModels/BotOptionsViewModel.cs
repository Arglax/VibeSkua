using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AE = MoVibeSkua.Android.Bot.AutoEnhance;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// The bot rail's Options page: MVS's own bot settings first (Use Best Enhancement), then the game options
/// the bot can apply (Skua's page), each a collapsible section on one scrolling page.
/// </summary>
public sealed partial class BotOptionsViewModel : ObservableObject
{
    public BotOptionsViewModel(object gameOptions)
    {
        GameOptions = gameOptions;
        _autoEnhance = AE.Enabled;
        _useCase = AE.UseCase;
        _lastResult = AE.LastResult;
        foreach (string slot in AE.Slots)
            Rows.Add(new GearRowViewModel(slot, Refresh));
        LoadRows();
        AE.Read += LoadRows;
    }

    public object GameOptions { get; }

    public string[] UseCases => AE.UseCases;

    /// <summary>Run before each script.</summary>
    [ObservableProperty]
    private bool _autoEnhance;

    partial void OnAutoEnhanceChanged(bool value) => AE.Enabled = value;

    [ObservableProperty]
    private string _useCase;

    partial void OnUseCaseChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        AE.UseCase = value;
        // The best set depends on the use case: read it again.
        if (!IsBusy)
            _ = ReadGear();
    }

    public ObservableCollection<GearRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private string _readInfo = "Tap Read my gear to see your class's best enhancements.";

    [ObservableProperty]
    private string _toChange = string.Empty;

    [ObservableProperty]
    private string _lastResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadGearCommand), nameof(EnhanceNowCommand))]
    private bool _isBusy;

    private void LoadRows()
    {
        var read = AE.LastRead;
        foreach (GearRowViewModel row in Rows)
            row.Set(read.Rows.FirstOrDefault(r => r.Slot == row.Slot));
        if (read.Rows.Count > 0)
        {
            string ago = Ago(read.When);
            ReadInfo = $"{read.Class} · {read.UseCase.ToLowerInvariant()} · read {ago}" + (read.Source.Length > 0 ? $" · best from {read.Source}" : "");
        }
        LastResult = AE.LastResult;
        Refresh();
    }

    private void Refresh()
    {
        int n = Rows.Count(r => r.Ticked && r.WillChange);
        ToChange = Rows.All(r => r.Now.Length == 0) ? string.Empty
            : n == 0 ? "Nothing to change: every ticked piece is on its best enhancement."
            : $"{n} to change · unticked pieces are never touched";
    }

    private static string Ago(DateTime whenUtc)
    {
        TimeSpan t = DateTime.UtcNow - whenUtc;
        return t.TotalMinutes < 1 ? "just now" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" : t.TotalDays < 1 ? $"{(int)t.TotalHours} h ago" : $"{(int)t.TotalDays} d ago";
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ReadGear()
    {
        IsBusy = true;
        ReadInfo = "Reading your gear...";
        string summary = await AE.RunAsync(beforeScript: false, preview: true);
        if (AE.LastRead.When < DateTime.UtcNow.AddSeconds(-30))
            ReadInfo = summary;
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task EnhanceNow()
    {
        IsBusy = true;
        LastResult = "Enhancing the ticked pieces...";
        await AE.RunAsync(beforeScript: false);
        IsBusy = false;
    }
}

/// <summary>One piece in the table: tick it to let Use Best Enhancement change it.</summary>
public sealed partial class GearRowViewModel : ObservableObject
{
    private readonly Action _changed;

    public GearRowViewModel(string slot, Action changed)
    {
        Slot = slot;
        _changed = changed;
        _ticked = AE.SlotOn(slot);
    }

    public string Slot { get; }

    public string Label => Slot switch { "class" => "Class (armor)", "weapon" => "Weapon", "helm" => "Helm", _ => "Cape" };

    [ObservableProperty]
    private bool _ticked;

    partial void OnTickedChanged(bool value)
    {
        AE.SetSlot(Slot, value);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
        _changed();
    }

    [ObservableProperty] private string _item = string.Empty;
    [ObservableProperty] private string _now = string.Empty;
    [ObservableProperty] private string _best = string.Empty;
    [ObservableProperty] private bool _ok;
    [ObservableProperty] private bool _locked;

    public bool WillChange => Item.Length > 0 && !Ok && !Locked;

    public string Status => Now.Length == 0 ? string.Empty
        : Item.Length == 0 ? "nothing equipped"
        : Ok ? "already best"
        : !Ticked ? "kept (unticked)"
        : Locked ? "locked: finish its Forge quest"
        : "will change";

    public string StatusColor => Now.Length == 0 || Item.Length == 0 || !Ticked && !Ok ? "#A99A85"
        : Ok ? "#7FC77F" : Locked ? "#E0705F" : "#E8C15A";

    public string BestText => Locked ? Best + " (not unlocked)" : Best;

    internal void Set(AE.GearRow? row)
    {
        Item = row?.Item ?? string.Empty;
        Now = row?.Now ?? string.Empty;
        Best = row?.Best ?? string.Empty;
        Ok = row?.Ok ?? false;
        Locked = row?.Locked ?? false;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(BestText));
        OnPropertyChanged(nameof(WillChange));
    }
}
