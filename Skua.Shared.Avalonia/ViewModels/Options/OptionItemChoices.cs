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

    /// <summary>Matching owned item names (sorted, distinct), or null when the option doesn't ask for an item.</summary>
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
            .Where(i => categories is null || categories.Contains(i.Category))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? names : null;
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
