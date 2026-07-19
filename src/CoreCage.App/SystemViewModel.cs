using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// Drives the System group: live RAM readout + two real cleanup actions (free working sets, clear
/// standby list) through the in-process MemoryCleaner. Each runs on a background thread with a busy
/// state and honest result; polls RAM every 3s.
/// </summary>
public sealed class SystemViewModel : INotifyPropertyChanged, IDisposable, IPollingGroup
{
    private readonly ISystemService _svc;
    private readonly DispatcherTimer _pollTimer;

    public SystemViewModel() : this(new EngineSystemService()) { }

    public SystemViewModel(ISystemService svc)
    {
        _svc = svc;
        FreeWorkingSetsCommand = new RelayCommand(FreeWorkingSetsAsync, () => !IsBusy);
        ClearStandbyCommand = new RelayCommand(ClearStandbyAsync, () => !IsBusy);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += (_, _) => Refresh();
        _pollTimer.Start();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand FreeWorkingSetsCommand { get; }
    public RelayCommand ClearStandbyCommand { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { FreeWorkingSetsCommand.RaiseCanExecuteChanged(); ClearStandbyCommand.RaiseCanExecuteChanged(); } } }

    private string _ramText = "—";        public string RamText { get => _ramText; private set => Set(ref _ramText, value); }
    private string _availableText = "—";  public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }
    private double _usedPct;              public double UsedPct { get => _usedPct; private set => Set(ref _usedPct, value); }
    private string _statusMessage = "System ready."; public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    private bool _lastOk = true;         public bool LastOk { get => _lastOk; private set => Set(ref _lastOk, value); }

    private void Refresh()
    {
        if (IsBusy) return;
        var r = _svc.ReadRam();
        RamText = $"{r.UsedGb:0.0} / {r.TotalGb:0.0} GB used";
        AvailableText = r.AvailableText;
        UsedPct = r.UsedPct;
    }

    internal Task FreeWorkingSetsAsync() => RunAsync(_svc.FreeWorkingSets, "Free working sets", "Working sets freed.");
    internal Task ClearStandbyAsync()    => RunAsync(_svc.ClearStandbyList, "Clear standby list", "Standby list cleared.");

    private async Task RunAsync(Func<bool> action, string label, string successMsg)
    {
        IsBusy = true;
        StatusMessage = $"{label}…";
        try
        {
            bool ok = await Task.Run(action).ConfigureAwait(true);
            LastOk = ok;
            StatusMessage = ok ? successMsg : $"{label} failed.";
        }
        catch (Exception ex) { LastOk = false; StatusMessage = $"{label} failed: {ex.Message}"; }
        finally { IsBusy = false; Refresh(); }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public void Activate() { _pollTimer.Start(); Refresh(); }
    public void Deactivate() => _pollTimer.Stop();
    public bool IsPolling => _pollTimer.IsEnabled;

    public void Dispose() => _pollTimer.Stop();
}
