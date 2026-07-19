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

    internal void Refresh()
    {
        Profiles.Clear();
        foreach (var p in _svc.List()) Profiles.Add(p);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProfiles)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        StatusMessage = Profiles.Count == 0 ? "No saved profiles yet." : $"{Profiles.Count} profile(s).";
    }

    internal Task ApplySelectedAsync()  => ActOnSelectedAsync(_svc.Apply, "Applied", "Apply");
    internal Task DeleteSelectedAsync() => ActOnSelectedAsync(_svc.Delete, "Deleted", "Delete");

    private async Task ActOnSelectedAsync(Func<string, bool> action, string pastTense, string verb)
    {
        var target = Selected;
        if (target == null) { LastOk = false; StatusMessage = $"Select a profile to {verb.ToLowerInvariant()}."; return; }

        IsBusy = true;
        StatusMessage = $"{verb}ing {target.Name}…";
        try
        {
            bool ok = await Task.Run(() => action(target.Name)).ConfigureAwait(true);
            LastOk = ok;
            StatusMessage = ok ? $"{pastTense} {target.Name}." : $"Could not {verb.ToLowerInvariant()} {target.Name}.";
        }
        catch (Exception ex) { LastOk = false; StatusMessage = $"{verb} failed: {ex.Message}"; }
        finally { IsBusy = false; Refresh(); }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
