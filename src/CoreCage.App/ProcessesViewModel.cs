using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// Drives the Processes group: a top-by-memory list you can refresh and kill from. Kill runs on a
/// background thread with a busy state; killing nothing selected is a safe no-op with a hint.
/// </summary>
public sealed class ProcessesViewModel : INotifyPropertyChanged
{
    private const int TopN = 40;
    private readonly IProcessService _svc;

    public ProcessesViewModel() : this(new EngineProcessService()) { }

    public ProcessesViewModel(IProcessService svc)
    {
        _svc = svc;
        RefreshCommand = new RelayCommand(() => { Refresh(); return Task.CompletedTask; }, () => !IsBusy);
        KillCommand = new RelayCommand(KillSelectedAsync, () => !IsBusy);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProcInfo> Processes { get; } = new();
    public RelayCommand RefreshCommand { get; }
    public RelayCommand KillCommand { get; }

    private ProcInfo? _selected;
    public ProcInfo? Selected { get => _selected; set => Set(ref _selected, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { RefreshCommand.RaiseCanExecuteChanged(); KillCommand.RaiseCanExecuteChanged(); } } }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    private bool _lastOk = true;
    public bool LastOk { get => _lastOk; private set => Set(ref _lastOk, value); }

    private StatusKind _statusKind = StatusKind.Neutral;
    /// <summary>Drives the status bar's brush+glyph (review IMPORTANT-1). The process-count readout is
    /// purely informational (Neutral, no ✓/✗) — it isn't reporting the outcome of an action. A
    /// completed Kill is Success/Error once it actually finishes.</summary>
    public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }

    /// <summary>Whether the list came back with nothing to show (e.g. a permission failure returning
    /// zero rows) — drives the Processes page's honest empty state instead of a blank list area.</summary>
    public bool IsEmpty => Processes.Count == 0;

    /// <summary>Inverse of <see cref="IsEmpty"/>, for the list's own visibility binding.</summary>
    public bool HasProcesses => !IsEmpty;

    /// <summary>Explicit user-driven refresh (RefreshCommand, construction): repopulates the list AND
    /// sets the informational count/empty-state message — this is not the outcome of any action, so
    /// it's always <see cref="StatusKind.Neutral"/>.</summary>
    internal void Refresh()
    {
        RefreshList();
        StatusMessage = Processes.Count == 0
            ? "No processes found — this can happen if CoreCage isn't running elevated."
            : $"{Processes.Count} processes (top {TopN} by memory).";
        StatusKind = StatusKind.Neutral;
    }

    /// <summary>Repopulates the list without touching StatusMessage/StatusKind — used after Kill so the
    /// kill result (Success/Error) stays on screen instead of being immediately overwritten by the
    /// informational "N processes…" readout.</summary>
    private void RefreshList()
    {
        Processes.Clear();
        foreach (var p in _svc.ListTopByMemory(TopN)) Processes.Add(p);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProcesses)));
    }

    internal async Task KillSelectedAsync()
    {
        var target = Selected;
        if (target == null) { LastOk = false; StatusKind = StatusKind.Error; StatusMessage = "Select a process to kill."; return; }

        IsBusy = true;
        StatusMessage = $"Killing {target.Name} ({target.Pid})…";
        StatusKind = StatusKind.Neutral;
        try
        {
            bool ok = await Task.Run(() => _svc.Kill(target.Pid)).ConfigureAwait(true);
            LastOk = ok;
            StatusKind = ok ? StatusKind.Success : StatusKind.Error;
            StatusMessage = ok ? $"Killed {target.Name} ({target.Pid})." : $"Could not kill {target.Name} — access denied or already exited.";
        }
        catch (Exception ex) { LastOk = false; StatusKind = StatusKind.Error; StatusMessage = $"Kill failed: {ex.Message}"; }
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
