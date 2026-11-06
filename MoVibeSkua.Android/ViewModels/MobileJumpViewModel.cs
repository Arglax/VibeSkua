using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.FastTravel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// Jump page: the cell and pad jump, plus Fast Travel's saved spots, each joined with one tap the way
/// typing /join map-room in chat does. The spots are Fast Travel's own list, so both pages show the same.
/// </summary>
public sealed partial class MobileJumpViewModel : ObservableRecipient
{
    private readonly IScriptInterface _bot;
    private readonly IRelayCommand<object> _fastTravelCommand;

    public MobileJumpViewModel(JumpViewModel jump, FastTravelViewModel fastTravel, IScriptInterface bot)
    {
        Jump = jump;
        FastTravel = fastTravel;
        _bot = bot;
        _fastTravelCommand = new RelayCommand<object>(fastTravel.MapService.Travel);
    }

    public JumpViewModel Jump { get; }

    public FastTravelViewModel FastTravel { get; }

    // The shell activates the page it shows; pass that on to the jump card, as when it is its own page.
    protected override void OnActivated() => Jump.IsActive = true;

    protected override void OnDeactivated() => Jump.IsActive = false;

    public ObservableCollection<IFastTravelItemViewModel> Spots => FastTravel.FastTravelItems;

    [ObservableProperty]
    private string _newMap = string.Empty;

    [ObservableProperty]
    private string _newRoom = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [RelayCommand]
    private void AddSpot()
    {
        string map = NewMap.Trim().ToLowerInvariant();
        string room = NewRoom.Trim();
        if (map.Length == 0)
        {
            Status = "Type a map name first.";
            return;
        }
        if (room.Length > 0)
        {
            if (!room.All(char.IsDigit))
            {
                Status = "The room has to be a number.";
                return;
            }
            map = $"{map.Split('-')[0]}-{room}";
        }
        if (Spots.Any(s => string.Equals(s.MapName, map, StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"{map} is already saved.";
            return;
        }

        Spots.Add(new FastTravelItemViewModel(map, map, "Enter", "Spawn", _fastTravelCommand));
        NewMap = string.Empty;
        NewRoom = string.Empty;
        Status = $"Saved {map}.";
    }

    [RelayCommand]
    private void TravelTo(IFastTravelItemViewModel? spot)
    {
        if (spot is null)
            return;
        if (!_bot.Player.Playing)
        {
            Status = "Log in first.";
            return;
        }

        Status = $"Joining {spot.MapName}...";
        if (!spot.MapName.Contains('-'))
        {
            // No room of its own: Fast Travel's route, which also applies its private room setting.
            FastTravel.MapService.Travel(spot);
            return;
        }

        string map = spot.MapName;
        string cell = spot.Cell;
        string pad = spot.Pad;
        _ = Task.Run(() =>
        {
            try
            {
                _bot.Map.Join(map, cell, pad);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Jump] join {map} failed: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void RemoveSpot(IFastTravelItemViewModel? spot)
    {
        if (spot is not null && Spots.Remove(spot))
            Status = $"Removed {spot.MapName}.";
    }
}
