using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Skua.Shared.Avalonia.ViewModels.Options;

/// <summary>
/// For a script's text option that asks for an item ("Absolution: Cape" / "insert Name of your Absolution
/// Cape", "Class 1" / "Preset class 1 to auto-equip"), the names of the matching items the player owns,
/// from the inventory and the bank when it has been loaded this session. The option editor shows them in
/// an editable dropdown, so typing a name by hand still works.
/// </summary>
public static class OptionItemChoices
{
    private static readonly ItemCategory[] Weapons =
    {
        ItemCategory.Sword, ItemCategory.Axe, ItemCategory.Dagger, ItemCategory.Gun, ItemCategory.HandGun,
        ItemCategory.Rifle, ItemCategory.Bow, ItemCategory.Mace, ItemCategory.Gauntlet, ItemCategory.Polearm,
        ItemCategory.Staff, ItemCategory.Wand, ItemCategory.Whip,
    };

    private static readonly (Regex Word, ItemCategory[] Categories)[] Kinds =
    {
        (new Regex(@"\bclass(es)?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Class }),
        (new Regex(@"\bcapes?\b|\bcloaks?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Cape }),
        (new Regex(@"\bhelms?\b|\bhelmets?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Helm }),
        (new Regex(@"\bweapons?\b", RegexOptions.IgnoreCase), Weapons),
        (new Regex(@"\barmou?rs?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Armor }),
        (new Regex(@"\bpets?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Pet }),
        (new Regex(@"\bnecklaces?\b|\bamulets?\b", RegexOptions.IgnoreCase), new[] { ItemCategory.Necklace, ItemCategory.Amulet }),
    };

    // Options naming a person, place or number rather than an item.
    private static readonly Regex NotAnItem = new(@"\baccount\b|\bplayer\b|\bmap\b|\blocation\b|\bshop\b|\bquest\b|\bids?\b|\brange\b", RegexOptions.IgnoreCase);
    private static readonly Regex AnyItem = new(@"name of (your|the) |\bitem name\b", RegexOptions.IgnoreCase);

    private static List<InventoryItem>? _owned;
    private static DateTime _ownedAt;

    /// <summary>
    /// Matching owned items as dropdown text, "(Healer) DoomLord's WarMask" (sorted by name, one per name),
    /// or null when the option doesn't ask for an item. <see cref="NameOf"/> turns an entry back into the
    /// bare name that goes into the option. Reads the game: call it off the UI thread.
    /// </summary>
    public static List<string>? For(IOption option)
    {
        if (option.Type != typeof(string))
            return null;
        string name = option.DisplayName ?? option.Name ?? string.Empty;
        string text = name + " " + (option.Description ?? string.Empty);
        if (NotAnItem.IsMatch(name))
            return null;

        // The option's own name decides first ("Absolution: Cape"), then its description.
        ItemCategory[]? categories = Kinds.FirstOrDefault(k => k.Word.IsMatch(name)).Categories
            ?? Kinds.FirstOrDefault(k => k.Word.IsMatch(option.Description ?? string.Empty)).Categories;
        if (categories is null && !AnyItem.IsMatch(text))
            return null;

        List<InventoryItem> owned = Owned();
        if (owned.Count == 0)
            return null;
        List<string> names = owned
            .Where(i => (categories is null || categories.Contains(i.Category)) && !string.IsNullOrWhiteSpace(i.Name))
            // One entry per name; an enhanced copy wins over an unenhanced one (inventory vs bank).
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.EnhancementLevel).First())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => $"({Enhancement(i)}) {i.Name}")
            .ToList();
        return names.Count > 0 ? names : null;
    }

    private static readonly Regex Prefix = new(@"^\((?<enh>[^()]*)\) (?<name>.+)$");

    /// <summary>The bare item name in a dropdown entry ("(Healer) DoomLord's WarMask" gives "DoomLord's WarMask").</summary>
    public static string NameOf(string text, IReadOnlyCollection<string>? choices)
    {
        if (choices is null || !choices.Contains(text))
            return text;
        Match m = Prefix.Match(text);
        return m.Success ? m.Groups["name"].Value : text;
    }

    /// <summary>What the item is enhanced with, as the game names it: "Healer", "Lucky, Valiance", "Forge", "None".</summary>
    public static string Enhancement(InventoryItem item)
    {
        if (item.EnhancementLevel <= 0 && item.EnhancementPatternID <= 0)
            return item.Category == ItemCategory.Class ? "Class" : "None";
        string type = item.EnhancementPatternID switch
        {
            1 => "Adventurer", 2 => "Fighter", 3 => "Thief", 4 => "Armsman", 5 => "Hybrid", 6 => "Wizard",
            7 => "Healer", 8 => "Spellbreaker", 9 => "Lucky", 10 => "Forge", 11 => "Absolution", 12 => "Avarice",
            23 => "Depths", 24 => "Vainglory", 25 => "Vim", 26 => "Examen", 27 => "Pneuma", 28 => "Anima",
            29 => "Penitence", 30 => "Lament", 32 => "Hearty",
            _ => "Enhanced",
        };
        string? special = item.ProcID switch
        {
            2 => "Spiral Carve", 3 => "Awe Blast", 4 => "Health Vamp", 5 => "Mana Vamp", 6 => "Powerword Die",
            7 => "Lacerate", 8 => "Smite", 9 => "Valiance", 10 => "Arcana's Concerto", 11 => "Acheron",
            12 => "Elysium", 13 => "Praxis", 14 => "Dauntless", 15 => "Ravenous",
            _ => null,
        };
        // A forge weapon special (Lacerate and up) is the whole enhancement; Awe ones sit on a type.
        return special is null ? type : item.ProcID > 6 ? special : $"{type}, {special}";
    }

    // Read once per few seconds: one dialog asks for every option, and each read crosses into the game.
    private static List<InventoryItem> Owned()
    {
        if (_owned is not null && DateTime.UtcNow - _ownedAt < TimeSpan.FromSeconds(20))
            return _owned;
        List<InventoryItem> items = new();
        try
        {
            IScriptInterface? bot = Ioc.Default.GetService<IScriptInterface>();
            if (bot is not null && bot.Player.LoggedIn)
            {
                items.AddRange(bot.Inventory.Items ?? new());
                if (bot.Bank.Loaded)
                    items.AddRange(bot.Bank.Items ?? new());
            }
        }
        catch
        {
        }
        _owned = items;
        _ownedAt = DateTime.UtcNow;
        return items;
    }
}
