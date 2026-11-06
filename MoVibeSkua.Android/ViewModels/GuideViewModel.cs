using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// The player's how-to guide, opened by the Guide button above the MVS crest.
/// </summary>
public sealed partial class GuideViewModel : ObservableObject
{
    private readonly Action _switchRails;
    private readonly Action _openSupport;
    private readonly Action _openBotGuide;

    public GuideViewModel(Action switchRails, Action openSupport, Action openBotGuide)
    {
        _switchRails = switchRails;
        _openSupport = openSupport;
        _openBotGuide = openBotGuide;
    }

    [RelayCommand]
    private void SwitchRails() => _switchRails();

    [RelayCommand]
    private void OpenSupport() => _openSupport();

    [RelayCommand]
    private void OpenBotGuide() => _openBotGuide();
}

/// <summary>
/// The bot tools explained in plain words, by what the player wants to do.
/// </summary>
public sealed partial class BotGuideViewModel : ObservableObject
{
    private readonly Action _showBotTools;

    public BotGuideViewModel(Action showBotTools)
    {
        _showBotTools = showBotTools;
    }

    [RelayCommand]
    private void ShowBotTools() => _showBotTools();

    [RelayCommand]
    private void OpenAuqwDiscord() => SupportViewModel.Open(SupportViewModel.AuqwDiscordUrl);
}

/// <summary>
/// A note from Arglax, help (Discord), ways to support MVS, and credits; one tap on the MVS crest.
/// </summary>
public sealed partial class SupportViewModel : ObservableObject
{
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/arglaxaqwv";
    public const string DiscordUrl = "https://discord.gg/Ybsy7KWE4";
    public const string AuqwDiscordUrl = "https://discord.gg/cZgJTvjGU2";
    public const string SkuaDonationsUrl = "https://auqw.github.io/Skua-and-Grim-Donations/";

    /// <summary>InstaPay details; the card stays hidden until these are filled in.</summary>
    public const string InstaPayName = "";
    public const string InstaPayNumber = "";

    public bool HasInstaPay => InstaPayName.Length > 0 && InstaPayNumber.Length > 0;

    public string InstaPayNameText => InstaPayName;

    public string InstaPayNumberText => InstaPayNumber;

    [RelayCommand]
    private void OpenBuyMeACoffee() => Open(BuyMeACoffeeUrl);

    [RelayCommand]
    private void OpenDiscord() => Open(DiscordUrl);

    [RelayCommand]
    private void OpenAuqwDiscord() => Open(AuqwDiscordUrl);

    [RelayCommand]
    private void OpenSkuaDonations() => Open(SkuaDonationsUrl);

    internal static void Open(string url) => Ioc.Default.GetService<IProcessService>()?.OpenLink(url);
}
