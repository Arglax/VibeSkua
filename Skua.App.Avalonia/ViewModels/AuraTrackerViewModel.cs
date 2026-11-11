using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces.Services;
using Skua.Core.Models.Auras;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Skua.App.Avalonia.ViewModels;

/// <summary>
/// Aura Tracker: the auras on you and on your target, who applied each one, what it gives, stacks and
/// time left. Reads the game every 250 ms, only while the window is open.
/// </summary>
public partial class AuraTrackerViewModel : BotControlViewModelBase
{
    private const int PollMs = 250;
    private const int IconSize = 32;
    // Icons are drawn by the game; a few per poll so a fight full of new auras doesn't stall it.
    private const int IconsPerPoll = 2;

    private readonly IAuraTrackerService _tracker;
    private readonly Timer _timer;
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.Ordinal);
    private int _polling;

    public AuraTrackerViewModel(IAuraTrackerService tracker)
        : base("Aura Tracker", 400, 620, canResize: true)
    {
        _tracker = tracker;
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ObservableCollection<AuraRowViewModel> SelfAuras { get; } = new();
    public ObservableCollection<AuraRowViewModel> TargetAuras { get; } = new();

    [ObservableProperty]
    private string _targetTitle = "Target";

    [ObservableProperty]
    private bool _hasTarget;

    public bool NoSelfAuras => SelfAuras.Count == 0;
    public bool NoTargetAuras => HasTarget && TargetAuras.Count == 0;

    protected override void OnActivated()
    {
        _timer.Change(0, PollMs);
    }

    protected override void OnDeactivated()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Poll()
    {
        // A slow game read must not stack polls on top of each other.
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return;
        try
        {
            AuraTrackerResult self = _tracker.Read(SubjectType.Self);
            AuraTrackerResult target = _tracker.Read(SubjectType.Target);
            List<(string Key, (int Size, byte[] Argb)? Pixels)> newIcons = new();
            lock (_icons)
            {
                foreach (AuraTrackerEntry a in self.Auras.Concat(target.Auras))
                {
                    if (newIcons.Count >= IconsPerPoll)
                        break;
                    if (a.Icon.Length == 0 || _icons.ContainsKey(a.Icon) || newIcons.Any(n => n.Key == a.Icon))
                        continue;
                    newIcons.Add((a.Icon, _tracker.GetIcon(a.Icon, IconSize)));
                }
            }
            Dispatcher.UIThread.Post(() => Apply(self, target, newIcons));
        }
        catch (Exception)
        {
            // The game was closing or reloading; the next poll tries again.
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Apply(AuraTrackerResult self, AuraTrackerResult target, List<(string Key, (int Size, byte[] Argb)? Pixels)> newIcons)
    {
        lock (_icons)
        {
            foreach ((string key, (int Size, byte[] Argb)? pixels) in newIcons)
                _icons[key] = pixels is { } p ? ToBitmap(p.Size, p.Argb) : null;
        }

        HasTarget = target.Target is not null;
        TargetTitle = target.Target is { } t && t.Name.Length > 0 ? "Target: " + t.Name : "Target";
        Sync(SelfAuras, self.Auras);
        Sync(TargetAuras, HasTarget ? target.Auras : Array.Empty<AuraTrackerEntry>());
        OnPropertyChanged(nameof(NoSelfAuras));
        OnPropertyChanged(nameof(NoTargetAuras));
    }

    // Rows are kept and updated in place, so an opened row stays open and the list doesn't flicker.
    private void Sync(ObservableCollection<AuraRowViewModel> rows, IReadOnlyList<AuraTrackerEntry> auras)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (!auras.Any(a => a.Name == rows[i].Name))
                rows.RemoveAt(i);
        }
        for (int i = 0; i < auras.Count; i++)
        {
            AuraTrackerEntry a = auras[i];
            int at = -1;
            for (int j = 0; j < rows.Count; j++)
            {
                if (rows[j].Name == a.Name)
                {
                    at = j;
                    break;
                }
            }
            AuraRowViewModel row;
            if (at < 0)
            {
                row = new AuraRowViewModel(a.Name);
                rows.Insert(i, row);
            }
            else
            {
                row = rows[at];
                if (at != i)
                    rows.Move(at, i);
            }
            Bitmap? icon = null;
            lock (_icons)
            {
                if (a.Icon.Length > 0)
                    _icons.TryGetValue(a.Icon, out icon);
            }
            row.Update(a, icon);
        }
    }

    private static Bitmap? ToBitmap(int size, byte[] argb)
    {
        try
        {
            WriteableBitmap bmp = new(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            byte[] bgra = new byte[size * size * 4];
            for (int i = 0; i + 3 < bgra.Length && i + 3 < argb.Length; i += 4)
            {
                bgra[i] = argb[i + 3];
                bgra[i + 1] = argb[i + 2];
                bgra[i + 2] = argb[i + 1];
                bgra[i + 3] = argb[i];
            }
            using ILockedFramebuffer fb = bmp.Lock();
            for (int y = 0; y < size; y++)
                Marshal.Copy(bgra, y * size * 4, fb.Address + y * fb.RowBytes, size * 4);
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// One aura row. Tap it to see who applied it, its stacks and duration, and where its text comes from.
/// </summary>
public partial class AuraRowViewModel : ObservableObject
{
    public AuraRowViewModel(string name)
    {
        Name = name;
        Initial = InitialOf(name);
    }

    public string Name { get; }

    /// <summary>Shown in place of the icon when the game has none.</summary>
    public string Initial { get; }

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _stackText = string.Empty;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _endingSoon;
    [ObservableProperty] private string _effect = string.Empty;
    [ObservableProperty] private string _byLine = string.Empty;
    [ObservableProperty] private string _tagText = string.Empty;
    [ObservableProperty] private bool _isNoHeal;
    [ObservableProperty] private bool _isTaunt;
    [ObservableProperty] private bool _isScroll;
    [ObservableProperty] private bool _isFull;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _appliedBy = string.Empty;
    [ObservableProperty] private string _appliedByLabel = "Applied by";
    [ObservableProperty] private string _stacksDetail = string.Empty;
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private string _sourceText = string.Empty;
    [ObservableProperty] private string _tagNote = string.Empty;

    public bool HasIcon => Icon is not null;
    public bool HasTag => TagText.Length > 0;
    public bool HasEffect => Effect.Length > 0;
    public bool HasByLine => ByLine.Length > 0;
    public bool HasStacksDetail => StacksDetail.Length > 0;
    public bool HasSource => SourceText.Length > 0;
    public bool HasTagNote => TagNote.Length > 0 && TagNote != Effect;
    public bool ShowSummary => !IsExpanded && (HasEffect || HasByLine);

    partial void OnIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasIcon));
    partial void OnTagTextChanged(string value) => OnPropertyChanged(nameof(HasTag));
    partial void OnEffectChanged(string value)
    {
        OnPropertyChanged(nameof(HasEffect));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(HasTagNote));
    }
    partial void OnByLineChanged(string value)
    {
        OnPropertyChanged(nameof(HasByLine));
        OnPropertyChanged(nameof(ShowSummary));
    }
    partial void OnStacksDetailChanged(string value) => OnPropertyChanged(nameof(HasStacksDetail));
    partial void OnSourceTextChanged(string value) => OnPropertyChanged(nameof(HasSource));
    partial void OnTagNoteChanged(string value) => OnPropertyChanged(nameof(HasTagNote));
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowSummary));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    internal void Update(AuraTrackerEntry a, Bitmap? icon)
    {
        if (icon is not null)
            Icon = icon;
        StackText = !a.ShowsStacks || (a.Stacks <= 1 && a.MaxStacks <= 1)
            ? string.Empty
            : a.MaxStacks > 1 ? $"{a.Stacks}/{a.MaxStacks}" : $"x{a.Stacks}" + (a.NoCap ? " (no cap)" : string.Empty);
        TimeText = FormatTime(a.Remaining);
        Progress = a.Remaining is double left && a.Duration > 0 ? Math.Clamp(left / a.Duration, 0, 1) : 0;
        EndingSoon = a.Remaining is < 3;
        Effect = a.Effect;
        ByLine = a.ByLine;
        TagText = a.TagText;
        IsNoHeal = a.Tag == AuraTag.NoHeal;
        IsTaunt = a.Tag is AuraTag.Taunt or AuraTag.Reckless or AuraTag.Enrage;
        IsScroll = a.Tag == AuraTag.Scroll;
        IsFull = a.IsFull;
        AppliedBy = a.Casters.Count == 0
            ? "Unknown (the game sent no caster)"
            : string.Join(", ", a.Casters.Select(c => c.DisplayName));
        AppliedByLabel = a.Casters.Count > 1 ? "Applied by (latest first)" : "Applied by";
        StacksDetail = !a.ShowsStacks
            ? string.Empty
            : a.MaxStacks > 1 ? $"{a.Stacks} of {a.MaxStacks}" + (a.IsFull ? " (max)" : string.Empty)
            : a.NoCap ? $"{a.Stacks} (no cap)" : $"{a.Stacks} (limit unknown; most seen {a.MostSeen})";
        DurationText = a.Duration > 0 ? $"{a.Duration:0.#}s" : "until removed";
        SourceText = a.EffectSource.Length > 0 ? $"From {a.EffectSource}, AQW Wiki" : string.Empty;
        TagNote = a.TagNote;
    }

    private static string FormatTime(double? s)
    {
        if (s is not double left)
            return "∞";
        if (left < 0)
            left = 0;
        return left >= 60 ? $"{(int)(left / 60)}m{(int)(left % 60):00}" : $"{left:0.0}s";
    }

    private static string InitialOf(string name)
    {
        string trimmed = System.Text.RegularExpressions.Regex.Replace(name, @"^[IVXLC0-9]+\s*-\s*", string.Empty);
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
    }
}
