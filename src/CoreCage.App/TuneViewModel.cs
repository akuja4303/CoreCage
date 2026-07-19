using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// Drives the Tune group: live GPU readout + three real controls (power limit, core-clock offset,
/// digital vibrance). Polls every 2s, seeds the input fields to the current values on first read,
/// and runs each apply on a background thread with a busy state. Honest throughout: no NVIDIA GPU →
/// controls disable with an "unavailable" message; a silent NVAPI no-op is reported as "no change",
/// never a fake success.
/// </summary>
public sealed class TuneViewModel : INotifyPropertyChanged, IDisposable, IPollingGroup
{
    private readonly ITuneService _svc;
    private readonly DispatcherTimer _pollTimer;
    private bool _seeded;

    public TuneViewModel() : this(new EngineTuneService())
    {
    }

    public TuneViewModel(ITuneService svc)
    {
        _svc = svc;
        PowerLimitCommand = new RelayCommand(ApplyPowerLimitAsync, CanApply);
        CoreOffsetCommand = new RelayCommand(ApplyCoreOffsetAsync, CanApply);
        VibranceCommand   = new RelayCommand(ApplyVibranceAsync, CanApply);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshNow();
        _pollTimer.Start();

        RefreshNow();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand PowerLimitCommand { get; }
    public RelayCommand CoreOffsetCommand { get; }
    public RelayCommand VibranceCommand { get; }

    private bool _gpuAvailable;
    public bool GpuAvailable
    {
        get => _gpuAvailable;
        private set
        {
            if (Set(ref _gpuAvailable, value))
            {
                RaiseCanExec();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GpuUnavailable)));
            }
        }
    }

    /// <summary>Inverse for the "GPU tuning unavailable" banner's visibility binding.</summary>
    public bool GpuUnavailable => !GpuAvailable;

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) RaiseCanExec(); } }

    // Live readout (display strings)
    private string _coreText = "—";       public string CoreText { get => _coreText; private set => Set(ref _coreText, value); }
    private string _memText = "—";        public string MemText { get => _memText; private set => Set(ref _memText, value); }
    private string _powerText = "—";      public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
    private string _tempText = "—";       public string TempText { get => _tempText; private set => Set(ref _tempText, value); }
    private string _powerLimitText = "—"; public string PowerLimitText { get => _powerLimitText; private set => Set(ref _powerLimitText, value); }
    private string _coreOffsetText = "—"; public string CoreOffsetText { get => _coreOffsetText; private set => Set(ref _coreOffsetText, value); }
    private string _vibranceText = "—";   public string VibranceText { get => _vibranceText; private set => Set(ref _vibranceText, value); }

    // Editable inputs (two-way bound; seeded from the current values on first read)
    private int _powerLimitInput; public int PowerLimitInput { get => _powerLimitInput; set => Set(ref _powerLimitInput, value); }
    private int _coreOffsetInput; public int CoreOffsetInput { get => _coreOffsetInput; set => Set(ref _coreOffsetInput, value); }
    private int _vibranceInput;   public int VibranceInput   { get => _vibranceInput;   set => Set(ref _vibranceInput, value); }

    private string _statusMessage = "Reading GPU…";
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    private bool _lastOk = true;
    public bool LastOk { get => _lastOk; private set => Set(ref _lastOk, value); }

    private bool CanApply() => GpuAvailable && !IsBusy;

    /// <summary>Reads the GPU once and refreshes the readout. Leaves the status message alone while a
    /// GPU is present (apply results own it); only overwrites it in the unavailable / first-seed cases.</summary>
    internal void RefreshNow()
    {
        if (IsBusy) return; // don't stomp an in-flight apply

        var r = _svc.ReadGpu();
        GpuAvailable = r.Available;

        if (!r.Available)
        {
            StatusMessage = "No NVIDIA GPU detected — GPU tuning is unavailable on this rig.";
            CoreText = MemText = PowerText = TempText = PowerLimitText = CoreOffsetText = VibranceText = "—";
            return;
        }

        CoreText = $"{r.CoreMhz} MHz";
        MemText = $"{r.MemMhz} MHz";
        PowerText = $"{r.PowerW:0} W";
        TempText = $"{r.TempC:0}°C";
        PowerLimitText = $"{r.PowerCurW} W  ({r.PowerMinW}–{r.PowerMaxW})";
        CoreOffsetText = $"{r.CoreOffsetMhz:+#;-#;0} MHz";
        VibranceText = $"{r.Vibrance} / {r.VibranceMax}";

        if (!_seeded)
        {
            PowerLimitInput = r.PowerCurW;
            CoreOffsetInput = r.CoreOffsetMhz;
            VibranceInput = r.Vibrance;
            _seeded = true;
            StatusMessage = "GPU tuning ready.";
        }
    }

    internal Task ApplyPowerLimitAsync() => RunAsync(() => _svc.SetPowerLimit(PowerLimitInput), $"Power limit → {PowerLimitInput} W");
    internal Task ApplyCoreOffsetAsync() => RunAsync(() => _svc.SetCoreOffset(CoreOffsetInput), $"Core offset → {CoreOffsetInput:+#;-#;0} MHz");
    internal Task ApplyVibranceAsync()   => RunAsync(() => _svc.SetVibrance(VibranceInput),   $"Vibrance → {VibranceInput}");

    private async Task RunAsync(Func<bool> action, string label)
    {
        IsBusy = true;
        StatusMessage = $"Applying {label}…";
        try
        {
            bool ok = await Task.Run(action).ConfigureAwait(true);
            LastOk = ok;
            StatusMessage = ok
                ? $"{label} — applied."
                : $"{label} — no change (the driver reported failure or a silent no-op).";
        }
        catch (Exception ex)
        {
            LastOk = false;
            StatusMessage = $"{label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshNow(); // pull fresh telemetry; won't overwrite the result message
        }
    }

    private void RaiseCanExec()
    {
        PowerLimitCommand.RaiseCanExecuteChanged();
        CoreOffsetCommand.RaiseCanExecuteChanged();
        VibranceCommand.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public void Activate() { _pollTimer.Start(); RefreshNow(); }
    public void Deactivate() => _pollTimer.Stop();
    public bool IsPolling => _pollTimer.IsEnabled;

    public void Dispose() => _pollTimer.Stop();
}
