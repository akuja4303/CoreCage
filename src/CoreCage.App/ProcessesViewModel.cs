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

    internal void Refresh()
    {
        Processes.Clear();
        foreach (var p in _svc.ListTopByMemory(TopN)) Processes.Add(p);
        StatusMessage = $"{Processes.Count} processes (top {TopN} by memory).";
    }

    internal async Task KillSelectedAsync()
    {
        var target = Selected;
        if (target == null) { LastOk = false; StatusMessage = "Select a process to kill."; return; }

        IsBusy = true;
        StatusMessage = $"Killing {target.Name} ({target.Pid})…";
        try
        {
            bool ok = await Task.Run(() => _svc.Kill(target.Pid)).ConfigureAwait(true);
            LastOk = ok;
            StatusMessage = ok ? $"Killed {target.Name} ({target.Pid})." : $"Could not kill {target.Name} — access denied or already exited.";
        }
        catch (Exception ex) { LastOk = false; StatusMessage = $"Kill failed: {ex.Message}"; }
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
