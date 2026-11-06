using Newtonsoft.Json;
using Skua.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

/// <summary>
/// Community class ratings from the embedded class-ratings.json (see its "sources"), kept as 1-10 and
/// shown as 0-5 stars like the r/AQW Simplified Bosses Guide: overall PvE and PvP, and per use case
/// (farming, solo, bossing, ultra). Ultra comes from that guide's per-fight stars, which the class list
/// can also rank by one fight. Used by the Auto page's class list and,
/// through <see cref="AttachToGame"/>, by the grades drawn in the game's own inventory (only on this
/// screen; nothing is sent to the game servers).
/// </summary>
public static class ClassRatings
{
    /// <summary>The use cases the class list can sort by, in button order. "A-Z" shows PvE-PvP.</summary>
    public static readonly string[] UseCases = { "A-Z", "Farm", "Solo", "Boss", "Ultra", "PvP" };

    public class Rating
    {
        [JsonProperty("pve")] public int Pve { get; set; }
        [JsonProperty("pvp")] public int Pvp { get; set; }
        [JsonProperty("farm")] public int Farm { get; set; }
        [JsonProperty("solo")] public int Solo { get; set; }
        [JsonProperty("boss")] public int Boss { get; set; }
        [JsonProperty("ultra")] public int Ultra { get; set; }
        /// <summary>The class's role in the ultra guide: Support, Healer, Endgame DPS, Chrono DPS or Early DPS.</summary>
        [JsonProperty("ultraRole")] public string? UltraRole { get; set; }
        [JsonProperty("roles")] public List<string>? Roles { get; set; }
        [JsonProperty("note")] public string? Note { get; set; }

        /// <summary>The rating for a use case; overall PvE for "A-Z".</summary>
        public int Score(string useCase) => useCase switch
        {
            "Farm" => Farm,
            "Solo" => Solo,
            "Boss" => Boss,
            "Ultra" => Ultra,
            "PvP" => Pvp,
            _ => Pve
        };
    }

    public class Fight
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        /// <summary>Class name to stars (0-5).</summary>
        [JsonProperty("ratings")] public Dictionary<string, int> Ratings { get; set; } = new();
    }

    private class File
    {
        [JsonProperty("classes")] public Dictionary<string, Rating>? Classes { get; set; }
        [JsonProperty("ultraFights")] public List<Fight>? UltraFights { get; set; }
    }

    private static Dictionary<string, Rating>? _ratings;
    private static List<Fight> _fights = new();

    /// <summary>"All ultras" plus each fight in the ultra guide, for the Ultra use case's picker.</summary>
    public const string AllUltras = "All ultras";

    public static IReadOnlyList<string> UltraFightNames
    {
        get
        {
            _ratings ??= Load();
            return new[] { AllUltras }.Concat(_fights.Select(f => f.Name)).ToList();
        }
    }

    /// <summary>Stars (0-5) for a class in one ultra fight, null when the guide doesn't list it.</summary>
    public static int? FightStars(string fight, string className)
    {
        _ratings ??= Load();
        Fight? f = _fights.FirstOrDefault(x => x.Name == fight);
        if (f is null)
            return null;
        foreach ((string name, int stars) in f.Ratings)
            if (string.Equals(name, className.Trim(), StringComparison.OrdinalIgnoreCase))
                return stars;
        int paren = className.IndexOf('(');
        return paren > 0 ? FightStars(fight, className[..paren]) : null;
    }
    private static IFlashUtil? _flash;
    private static string _focus = "A-Z";

    /// <summary>The use case grades are shown for, here and in the game's inventory.</summary>
    public static string Focus => _focus;

    /// <summary>Raised when the use case changes, also from the chip in the game's inventory.</summary>
    public static event Action<string>? FocusChanged;

    public static Rating? Get(string className)
    {
        _ratings ??= Load();
        if (_ratings.TryGetValue(className.Trim(), out Rating? r))
            return r;
        // Variants share a kit with the base class: "Void Highlord (IoDA)", "Rustbucket Ultra" etc.
        int paren = className.IndexOf('(');
        return paren > 0 && _ratings.TryGetValue(className[..paren].Trim(), out r) ? r : null;
    }

    /// <summary>Rating 1-10 as whole stars 0-5 (rounded up: 9 and 10 are both five stars).</summary>
    public static int Stars(int rating) => Math.Clamp((rating + 1) / 2, 0, 5);

    /// <summary>Five stars, filled for the rating: "★★★★☆".</summary>
    public static string StarText(int stars) => new string('★', Math.Clamp(stars, 0, 5)) + new string('☆', 5 - Math.Clamp(stars, 0, 5));

    /// <summary>A 1-10 rating as "★★★★☆".</summary>
    public static string Grade(int rating) => StarText(Stars(rating));

    /// <summary>One line on what a use case means, for the class list.</summary>
    public static string UseCaseHint(string useCase) => useCase switch
    {
        "Farm" => "Farming: clearing many monsters fast (area damage, mana, little downtime).",
        "Solo" => "Solo: winning alone against tough enemies (self-heal, damage reduction).",
        "Boss" => "Bossing: one strong non-ultra target (single-target damage, survival).",
        "Ultra" => "Ultra: ultra bosses with a team. Stars from the r/AQW Simplified Bosses Guide; pick a fight to rank for it.",
        "PvP" => "PvP: player fights (estimates; the PvP meta shifts often).",
        _ => "Stars are overall PvE. Pick a use case to rank classes for it."
    };

    /// <summary>Sets the use case and shows it in the game's inventory too.</summary>
    public static void SetFocus(string useCase)
    {
        if (!UseCases.Contains(useCase) || useCase == _focus)
            return;
        _focus = useCase;
        if (_flash is { } flash)
            _ = Task.Run(() => SafeCall(flash, "mvsRatingFocus", useCase));
        FocusChanged?.Invoke(useCase);
    }

    /// <summary>
    /// Sends the ratings into the game client each time it loads, so its inventory can show grades,
    /// and follows the use-case chip there.
    /// </summary>
    public static void AttachToGame(IFlashUtil flash)
    {
        if (_flash is not null)
            return;
        _flash = flash;
        flash.FlashCall += (function, args) =>
        {
            if (function == "loaded")
                _ = Task.Run(() => Push(flash));
            else if (function == "mvsRatingFocus" && args is { Length: > 0 } && args[0]?.ToString() is { } focus
                     && UseCases.Contains(focus) && focus != _focus)
            {
                _focus = focus;
                FocusChanged?.Invoke(focus);
            }
        };
        _ = Task.Run(() => Push(flash));
    }

    private static void Push(IFlashUtil flash)
    {
        _ratings ??= Load();
        // [pve, pvp, farm, solo, boss, ultra] as 1-10, then the ultra role.
        Dictionary<string, object[]> compact = _ratings.ToDictionary(
            r => r.Key.ToLowerInvariant(),
            r => new object[] { r.Value.Pve, r.Value.Pvp, r.Value.Farm, r.Value.Solo, r.Value.Boss, r.Value.Ultra, r.Value.UltraRole ?? "" });
        SafeCall(flash, "mvsClassRatings", JsonConvert.SerializeObject(compact));
        SafeCall(flash, "mvsRatingFocus", _focus);
    }

    private static void SafeCall(IFlashUtil flash, string function, string arg)
    {
        try
        {
            flash.Call(function, arg);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ClassRatings] {function}: {ex.Message}");
        }
    }

    private static Dictionary<string, Rating> Load()
    {
        try
        {
            using Stream? stream = typeof(ClassRatings).Assembly.GetManifestResourceStream("class-ratings.json");
            if (stream is not null)
            {
                using StreamReader reader = new(stream);
                File? file = JsonConvert.DeserializeObject<File>(reader.ReadToEnd());
                _fights = file?.UltraFights ?? new();
                if (file?.Classes is { } classes)
                    return new(classes, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ClassRatings] {ex.Message}");
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
