using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoVibeSkua.Android.Platform;
using MoVibeSkua.Android.Usage;
using Skua.Core.Interfaces;

namespace MoVibeSkua.Android.ViewModels;

/// <summary>
/// Army page: the running game clients, each its own process with its own login. Adds a client,
/// switches to one or closes one. The list comes from the running processes and the status each
/// client writes (see <see cref="Army"/>).
/// </summary>
public sealed partial class ArmyViewModel : ObservableRecipient
{
    private readonly Func<bool> _isStreamerMode;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    // The client being started (true) or closed (false); its status note clears once that happened.
    private (int Slot, bool Running)? _awaiting;
    private bool _lowMemory;

    public ArmyViewModel(Func<bool> isStreamerMode, IDialogService dialogs)
    {
        _isStreamerMode = isStreamerMode;
        _dialogs = dialogs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
    }

    private static Context Context => global::Android.App.Application.Context;

    public ObservableCollection<ArmyClientItem> Clients { get; } = new();

    public string ThisClient => $"You are on {Army.Label(Army.Slot)}.";

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _warning = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _canAdd = true;

    protected override void OnActivated()
    {
        Refresh();
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    [RelayCommand]
    private void Add()
    {
        int? slot = Army.NextFreeSlot(Context);
        if (slot is null)
        {
            UsageTracker.Track(Signal.ARMY_FULL);
            Status = $"All {Army.MaxClients} clients are running.";
            return;
        }
        Status = $"Starting {Army.Label(slot.Value)}... log in to its account there.";
        _awaiting = (slot.Value, true);
        UsageTracker.Track(Signal.ARMY_ADD);
        Army.Open(Context, slot.Value);
    }

    [RelayCommand]
    private void SwitchTo(ArmyClientItem? client)
    {
        if (client is null || client.IsThis)
            return;
        UsageTracker.Track(Signal.ARMY_SWITCH);
        Army.Open(Context, client.Slot);
    }

    [RelayCommand]
    private void Close(ArmyClientItem? client)
    {
        if (client is null || client.Slot == 1)
            return;
        if (_dialogs.ShowMessageBox($"Close {client.Title}? It logs out of the game and stops its script.", "Army", true) != true)
            return;
        Status = $"Closing {client.Title}...";
        _awaiting = (client.Slot, false);
        UsageTracker.Track(Signal.ARMY_CLOSE);
        Army.Close(Context, client.Slot);
    }

    private void Refresh()
    {
        Dictionary<int, int> running = Army.RunningClients(Context);
        running[Army.Slot] = global::Android.OS.Process.MyPid();
        bool masked = _isStreamerMode();
        if (_awaiting is { } awaiting && running.ContainsKey(awaiting.Slot) == awaiting.Running)
        {
            Status = string.Empty;
            _awaiting = null;
        }

        List<int> slots = running.Keys.OrderBy(s => s).ToList();
        if (!Clients.Select(c => c.Slot).SequenceEqual(slots))
        {
            Clients.Clear();
            foreach (int slot in slots)
                Clients.Add(new ArmyClientItem(slot));
        }
        foreach (ArmyClientItem client in Clients)
            client.Update(Army.ReadStatus(client.Slot), masked);

        CanAdd = slots.Count < Army.MaxClients;
        Warning = slots.Count >= Army.ComfortableClients
            ? $"{slots.Count} clients: each one is a full copy of AQW. Past {Army.ComfortableClients} the phone may run short of memory or get hot, and Android may close clients to cope."
            : string.Empty;

        if (Context.GetSystemService(Context.ActivityService) is ActivityManager am)
        {
            ActivityManager.MemoryInfo memory = new();
            am.GetMemoryInfo(memory);
            const double gb = 1024d * 1024 * 1024;
            MemoryText = $"Phone memory free: {memory.AvailMem / gb:0.0} of {memory.TotalMem / gb:0.0} GB";
            if (memory.LowMemory && !_lowMemory)
                UsageTracker.Track(Signal.ARMY_LOW_MEMORY);
            _lowMemory = memory.LowMemory;
            if (memory.LowMemory)
                Warning = "The phone is low on memory: another client may not fit, and Android may close some.";
        }
    }
}

public sealed partial class ArmyClientItem : ObservableObject
{
    public ArmyClientItem(int slot)
    {
        Slot = slot;
    }

    public int Slot { get; }

    public bool IsThis => Slot == Army.Slot;

    public string Title => Army.Label(Slot);

    public string Badge => IsThis ? "This one" : string.Empty;

    public bool CanSwitch => !IsThis;

    public bool CanClose => Slot != 1;

    [ObservableProperty]
    private string _detail = "Starting...";

    [ObservableProperty]
    private string _scriptText = string.Empty;

    [ObservableProperty]
    private bool _isScriptRunning;

    public void Update(Army.ClientStatus? status, bool masked)
    {
        if (status is null)
        {
            Detail = "Starting...";
            ScriptText = string.Empty;
            IsScriptRunning = false;
            return;
        }

        Detail = !status.LoggedIn
            ? "Not logged in"
            : $"{(masked ? ShellViewModel.StreamerName : status.Name)} · {status.Map}";
        string script = string.IsNullOrEmpty(status.Script) ? string.Empty : Path.GetFileNameWithoutExtension(status.Script);
        ScriptText = status.ScriptRunning ? $"Running {script}" : script.Length > 0 ? $"Loaded {script}" : "No script loaded";
        IsScriptRunning = status.ScriptRunning;
    }
}
