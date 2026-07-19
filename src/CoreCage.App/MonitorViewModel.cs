using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// Drives the Monitor group: polls the in-process engine every 2s and exposes CPU/GPU/RAM as
/// display-ready text. No engine logic here — it's a thin, testable client of <see cref="IMonitorService"/>.
/// Honest states: a dead sensor renders "—" (never a fake 0°C), and a backend failure flips
/// <see cref="IsError"/> instead of throwing onto the UI thread.
/// </summary>
public sealed class MonitorViewModel : INotifyPropertyChanged, IDisposable, IPollingGroup
{
    private readonly IMonitorService _svc;
    private readonly DispatcherTimer _pollTimer;

    public MonitorViewModel() : this(new EngineMonitorService())
    {
    }

    public MonitorViewModel(IMonitorService svc)
    {
        _svc = svc;
        try { _svc.Initialize(); } catch { /* a failed init surfaces honestly on the first Read */ }

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshNow();
        _pollTimer.Start();

        RefreshNow();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _cpuTempText = "—";
    public string CpuTempText { get => _cpuTempText; private set => Set(ref _cpuTempText, value); }

    private string _gpuTempText = "—";
    public string GpuTempText { get => _gpuTempText; private set => Set(ref _gpuTempText, value); }

    private string _ramText = "—";
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    private string _cpuName = "—";
    public string CpuName { get => _cpuName; private set => Set(ref _cpuName, value); }

    private string _gpuName = "—";
    public string GpuName { get => _gpuName; private set => Set(ref _gpuName, value); }

    private string _statusText = "Reading…";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _isError;
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    /// <summary>Reads the engine once and refreshes the bound text. Never throws.</summary>
    internal void RefreshNow()
    {
        try
        {
            var r = _svc.Read();
            CpuTempText = FormatTemp(r.CpuTempC);
            GpuTempText = FormatTemp(r.GpuTempC);
            RamText = $"{r.RamUsedPct:0}% used";
            CpuName = string.IsNullOrWhiteSpace(r.CpuName) ? "—" : r.CpuName;
            GpuName = string.IsNullOrWhiteSpace(r.GpuName) ? "—" : r.GpuName;
            IsError = false;
            StatusText = "Live";
        }
        catch
        {
            IsError = true;
            StatusText = "Sensor backend unavailable";
            CpuTempText = "—";
            GpuTempText = "—";
            RamText = "—";
        }
    }

    private static string FormatTemp(float? c) => c.HasValue ? $"{c.Value:0.0}°C" : "—";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void Activate() { _pollTimer.Start(); RefreshNow(); }
    public void Deactivate() => _pollTimer.Stop();
    public bool IsPolling => _pollTimer.IsEnabled;

    public void Dispose() => _pollTimer.Stop();
}
