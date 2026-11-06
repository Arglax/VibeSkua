using System;
using System.Collections.Generic;
using System.Globalization;
using Android.Content;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// Graphics &amp; Performance page: render resolution, anti-aliasing, colour style and an FPS meter for
/// the game page, plus shortcuts to Skua's own lag options. Saved in the shell prefs and sent to the
/// game page (bridge.js "graphics") on change, when the page connects and after each login.
/// </summary>
public partial class GraphicsViewModel : ObservableObject
{
    public record Choice(string Label, string Value);

    // Render resolution as a pixel ratio: "0" = the screen's own, "-1.2" = 20% above it (supersampling).
    public static readonly IReadOnlyList<Choice> Resolutions = new[]
    {
        new Choice("Battery (1x)", "1"),
        new Choice("Balanced (1.5x)", "1.5"),
        new Choice("Sharp (2x)", "2"),
        new Choice("Native screen", "0"),
        new Choice("Ultra (supersampled)", "-1.2")
    };

    // stage.quality, which also sets Ruffle's anti-aliasing. "" leaves it to the game's own switch.
    public static readonly IReadOnlyList<Choice> AntiAliasing = new[]
    {
        new Choice("Game setting", ""),
        new Choice("Off (fastest)", "LOW"),
        new Choice("2x", "MEDIUM"),
        new Choice("4x", "HIGH"),
        new Choice("8x (best)", "8X8")
    };

    public static readonly IReadOnlyList<Choice> ColourStyles = new[]
    {
        new Choice("Natural", "natural"),
        new Choice("Vivid", "vivid"),
        new Choice("Cinematic", "cinematic"),
        new Choice("Bright", "bright")
    };

    private const string PrefsName = "movibeskua.shell";
    private readonly ISharedPreferences _prefs;
    private bool _loading;

    public GraphicsViewModel()
    {
        _prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        _loading = true;
        Resolution = Find(Resolutions, _prefs.GetString("gfx.dpr", "0"));
        AntiAlias = Find(AntiAliasing, _prefs.GetString("gfx.quality", ""));
        ColourStyle = Find(ColourStyles, _prefs.GetString("gfx.filter", "natural"));
        ShowFps = _prefs.GetBoolean("gfx.fps", false);
        _loading = false;
    }

    public IReadOnlyList<Choice> ResolutionChoices => Resolutions;
    public IReadOnlyList<Choice> AntiAliasChoices => AntiAliasing;
    public IReadOnlyList<Choice> ColourChoices => ColourStyles;

    [ObservableProperty]
    private Choice _resolution;

    [ObservableProperty]
    private Choice _antiAlias;

    [ObservableProperty]
    private Choice _colourStyle;

    [ObservableProperty]
    private bool _showFps;

    /// <summary>Skua's own game options, shown here as shortcuts.</summary>
    public IScriptOption Options => Ioc.Default.GetRequiredService<IScriptOption>();

    partial void OnResolutionChanged(Choice value) => Changed();
    partial void OnAntiAliasChanged(Choice value) => Changed();
    partial void OnColourStyleChanged(Choice value) => Changed();
    partial void OnShowFpsChanged(bool value) => Changed();

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        _loading = true;
        (string dpr, string quality, string filter) = preset switch
        {
            "battery" => ("1", "LOW", "natural"),
            "balanced" => ("1.5", "MEDIUM", "natural"),
            "ultra" => ("-1.2", "8X8", "vivid"),
            _ => ("0", "HIGH", "natural") // quality
        };
        Resolution = Find(Resolutions, dpr);
        AntiAlias = Find(AntiAliasing, quality);
        ColourStyle = Find(ColourStyles, filter);
        _loading = false;
        Changed();
    }

    private void Changed()
    {
        if (_loading)
            return;
        _prefs.Edit()!
            .PutString("gfx.dpr", Resolution.Value)!
            .PutString("gfx.quality", AntiAlias.Value)!
            .PutString("gfx.filter", ColourStyle.Value)!
            .PutBoolean("gfx.fps", ShowFps)!
            .Apply();
        Apply();
    }

    /// <summary>Sends the saved settings to the game page.</summary>
    public void Apply()
    {
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":\"graphics\",\"dpr\":{double.Parse(Resolution.Value, CultureInfo.InvariantCulture)},\"quality\":\"{AntiAlias.Value}\",\"filter\":\"{ColourStyle.Value}\",\"fps\":{(ShowFps ? "true" : "false")}}}");
        Ioc.Default.GetService<Skua.Flash.Ruffle.RuffleFlashUtil>()?.PostCommand(json);
    }

    /// <summary>The pixel ratio to start the game page with, before the page asks for settings.</summary>
    public static double StartupPixelRatio()
    {
        ISharedPreferences? prefs = global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        string value = prefs?.GetString("gfx.dpr", "0") ?? "0";
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio > 0 ? ratio : 3;
    }

    private static Choice Find(IReadOnlyList<Choice> choices, string? value)
    {
        foreach (Choice choice in choices)
        {
            if (choice.Value == value)
                return choice;
        }
        return choices[0];
    }
}
