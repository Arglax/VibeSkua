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

    /// <summary>Black screen, nothing drawn, the game keeps running; one tap anywhere ends it.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void EnterHeadless() => global::MoVibeSkua.Android.Platform.Headless.Enter();

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

    /// <summary>Crash and app-problem reports to the MVS developer (Diagnostics.ProblemReports).</summary>
    public bool SendReports
    {
        get => Diagnostics.ProblemReports.SendEnabled;
        set
        {
            Diagnostics.ProblemReports.SendEnabled = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string _reportButtonText = "Report a problem";

    /// <summary>Sends the recent app log to the developer, for a problem MVS did not catch by itself.</summary>
    [RelayCommand]
    private void ReportProblem()
    {
        Diagnostics.ProblemReports.Save("user", "mvs", null, "Reported by the player from the Support page", null, fatal: false);
        ReportButtonText = "Thanks! It will be sent to the developer.";
    }

    public string VersionText => $"MVS {Diagnostics.AppLog.AppVersion()}" + (Platform.DevMode.IsOn ? " · developer mode" : "");

    private int _versionTaps;
    private System.DateTime _lastVersionTap;

    [RelayCommand]
    private void VersionTap()
    {
        System.DateTime now = System.DateTime.UtcNow;
        _versionTaps = now - _lastVersionTap < System.TimeSpan.FromSeconds(2) ? _versionTaps + 1 : 1;
        _lastVersionTap = now;
        if (_versionTaps < 7)
            return;
        _versionTaps = 0;
        bool on = Platform.DevMode.Toggle();
        OnPropertyChanged(nameof(VersionText));
        Ioc.Default.GetService<IDialogService>()?.ShowMessageBox(
            on ? "Developer mode is on. Restart MVS to see the developer tools." : "Developer mode is off. Restart MVS to hide the developer tools.",
            "Developer mode");
    }
}
