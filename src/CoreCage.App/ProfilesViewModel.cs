using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>Drives the Profiles group: list saved profiles, apply or delete the selected one. Honest
/// empty state when there are none; apply/delete run on a background thread with a busy state.</summary>
public sealed class ProfilesViewModel : INotifyPropertyChanged
{
    private readonly IProfileService _svc;

    public ProfilesViewModel() : this(new EngineProfileService()) { }

    public ProfilesViewModel(IProfileService svc)
    {
        _svc = svc;
        RefreshCommand = new RelayCommand(() => { Refresh(); return Task.CompletedTask; }, () => !IsBusy);
        ApplyCommand = new RelayCommand(ApplySelectedAsync, () => !IsBusy);
        DeleteCommand = new RelayCommand(DeleteSelectedAsync, () => !IsBusy);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProfileInfo> Profiles { get; } = new();
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand DeleteCommand { get; }

    private ProfileInfo? _selected;
    public ProfileInfo? Selected { get => _selected; set => Set(ref _selected, value); }

    public bool HasProfiles => Profiles.Count > 0;
    public bool IsEmpty => Profiles.Count == 0;

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { RefreshCommand.RaiseCanExecuteChanged(); ApplyCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); } } }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    private bool _lastOk = true;
    public bool LastOk { get => _lastOk; private set => Set(ref _lastOk, value); }

    private StatusKind _statusKind = StatusKind.Neutral;
    /// <summary>Drives the status bar's brush+glyph (review IMPORTANT-1). The profile-count/empty-state
    /// readout is purely informational (Neutral, no ✓/✗); a completed Apply/Delete is Success/Error
    /// once it actually finishes.</summary>
    public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }

    /// <summary>Explicit user-driven refresh (RefreshCommand, construction): repopulates the list AND
    /// sets the informational count/empty-state message — always <see cref="StatusKind.Neutral"/>.</summary>
    internal void Refresh()
    {
        RefreshList();
        StatusMessage = Profiles.Count == 0 ? "No saved profiles yet." : $"{Profiles.Count} profile(s).";
        StatusKind = StatusKind.Neutral;
    }

    /// <summary>Repopulates the list without touching StatusMessage/StatusKind — used after Apply/Delete
    /// so the action's result (Success/Error) stays on screen instead of being immediately overwritten
    /// by the informational "N profile(s)" readout.</summary>
    private void RefreshList()
    {
        Profiles.Clear();
        foreach (var p in _svc.List()) Profiles.Add(p);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProfiles)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
    }

    internal Task ApplySelectedAsync()  => ActOnSelectedAsync(_svc.Apply, "Applied", "Apply");
    internal Task DeleteSelectedAsync() => ActOnSelectedAsync(_svc.Delete, "Deleted", "Delete");

    private async Task ActOnSelectedAsync(Func<string, bool> action, string pastTense, string verb)
    {
        var target = Selected;
        if (target == null) { LastOk = false; StatusKind = StatusKind.Error; StatusMessage = $"Select a profile to {verb.ToLowerInvariant()}."; return; }

        IsBusy = true;
        StatusMessage = $"{verb}ing {target.Name}…";
        StatusKind = StatusKind.Neutral;
        try
        {
            bool ok = await Task.Run(() => action(target.Name)).ConfigureAwait(true);
            LastOk = ok;
            StatusKind = ok ? StatusKind.Success : StatusKind.Error;
            StatusMessage = ok ? $"{pastTense} {target.Name}." : $"Could not {verb.ToLowerInvariant()} {target.Name}.";
        }
        catch (Exception ex) { LastOk = false; StatusKind = StatusKind.Error; StatusMessage = $"{verb} failed: {ex.Message}"; }
        finally { IsBusy = false; RefreshList(); }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
