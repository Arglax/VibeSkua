using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoVibeSkua.Android.Bot;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// The bot rail's Options page: MVS's own bot settings first (Auto Enhance), then the game options
/// the bot can apply (Skua's page, shown under them).
/// </summary>
public sealed partial class BotOptionsViewModel : ObservableObject
{
    public BotOptionsViewModel(object gameOptions)
    {
        GameOptions = gameOptions;
        _autoEnhance = MoVibeSkua.Android.Bot.AutoEnhance.Enabled;
        _useCase = MoVibeSkua.Android.Bot.AutoEnhance.UseCase;
        _lastResult = MoVibeSkua.Android.Bot.AutoEnhance.LastResult;
    }

    public object GameOptions { get; }

    public string[] UseCases => MoVibeSkua.Android.Bot.AutoEnhance.UseCases;

    [ObservableProperty]
    private bool _autoEnhance;

    partial void OnAutoEnhanceChanged(bool value) => MoVibeSkua.Android.Bot.AutoEnhance.Enabled = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCaseNote))]
    private string _useCase;

    partial void OnUseCaseChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            MoVibeSkua.Android.Bot.AutoEnhance.UseCase = value;
    }

    public string UseCaseNote => UseCase switch
    {
        "Ultra" => "Ultra: the r/AQW Simplified Bosses Guide's set where it lists your class, otherwise AUQW's.",
        "Farming" => "Farming: AUQW's recommended set for your class (the same list scripts use).",
        _ => "Bossing: AUQW's recommended set for your class (the same list scripts use).",
    };

    [ObservableProperty]
    private string _lastResult;

    [ObservableProperty]
    private bool _isChecking;

    [RelayCommand]
    private async Task CheckNow()
    {
        IsChecking = true;
        LastResult = "Checking your gear...";
        string summary = await MoVibeSkua.Android.Bot.AutoEnhance.RunAsync(beforeScript: false);
        LastResult = MoVibeSkua.Android.Bot.AutoEnhance.LastResult is { Length: > 0 } last ? last : summary;
        IsChecking = false;
    }
}
