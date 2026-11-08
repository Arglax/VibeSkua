using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AE = MoVibeSkua.Android.Bot.AutoEnhance;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// The bot rail's Options page: MVS's own bot settings first (Auto Enhance), then the game options
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
        _slotClass = AE.SlotOn("class");
        _slotWeapon = AE.SlotOn("weapon");
        _slotHelm = AE.SlotOn("helm");
        _slotCape = AE.SlotOn("cape");
    }

    public object GameOptions { get; }

    public string[] UseCases => AE.UseCases;

    [ObservableProperty]
    private bool _autoEnhance;

    partial void OnAutoEnhanceChanged(bool value) => AE.Enabled = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCaseNote))]
    private string _useCase;

    partial void OnUseCaseChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            AE.UseCase = value;
    }

    public string UseCaseNote => UseCase switch
    {
        "Ultra" => "Ultra: the r/AQW Simplified Bosses Guide's set where it lists your class, otherwise AUQW's.",
        "Farming" => "Farming: AUQW's recommended set for your class (the same list scripts use).",
        _ => "Bossing: AUQW's recommended set for your class (the same list scripts use).",
    };

    // Which slots Auto Enhance may change: at most 3, so at least one piece always stays yours.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanTickClass), nameof(CanTickWeapon), nameof(CanTickHelm), nameof(CanTickCape), nameof(SlotsNote))]
    private bool _slotClass;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanTickClass), nameof(CanTickWeapon), nameof(CanTickHelm), nameof(CanTickCape), nameof(SlotsNote))]
    private bool _slotWeapon;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanTickClass), nameof(CanTickWeapon), nameof(CanTickHelm), nameof(CanTickCape), nameof(SlotsNote))]
    private bool _slotHelm;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanTickClass), nameof(CanTickWeapon), nameof(CanTickHelm), nameof(CanTickCape), nameof(SlotsNote))]
    private bool _slotCape;

    partial void OnSlotClassChanged(bool value) => AE.SetSlot("class", value);
    partial void OnSlotWeaponChanged(bool value) => AE.SetSlot("weapon", value);
    partial void OnSlotHelmChanged(bool value) => AE.SetSlot("helm", value);
    partial void OnSlotCapeChanged(bool value) => AE.SetSlot("cape", value);

    private int Ticked => new[] { SlotClass, SlotWeapon, SlotHelm, SlotCape }.Count(x => x);
    private bool Room => Ticked < AE.MaxSlots;

    public bool CanTickClass => SlotClass || Room;
    public bool CanTickWeapon => SlotWeapon || Room;
    public bool CanTickHelm => SlotHelm || Room;
    public bool CanTickCape => SlotCape || Room;

    public string SlotsNote => Ticked == 0
        ? "Tick at least one piece, or Auto Enhance has nothing to do."
        : $"Up to {AE.MaxSlots}: the unticked piece stays exactly as you set it.";

    [ObservableProperty]
    private string _lastResult;

    [ObservableProperty]
    private bool _isChecking;

    [RelayCommand]
    private async Task CheckNow()
    {
        IsChecking = true;
        LastResult = "Checking your gear...";
        string summary = await AE.RunAsync(beforeScript: false);
        LastResult = AE.LastResult is { Length: > 0 } last ? last : summary;
        IsChecking = false;
    }
}
