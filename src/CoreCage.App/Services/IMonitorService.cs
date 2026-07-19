namespace CoreCage.App.Services;

/// <summary>
/// The Monitor group's window onto real hardware. In the standalone app this is backed by the
/// in-process engine (<see cref="EngineMonitorService"/> → CoreCage.Core.SystemMonitor);
/// tests use a fake. Temps are nullable so a dead/absent sensor renders a dash, never a fake 0.
/// </summary>
public interface IMonitorService
{
    /// <summary>Idempotent — spins up the shared sensor backend (LibreHardwareMonitor) on first use.</summary>
    void Initialize();

    /// <summary>One point-in-time read of the metrics the dashboard shows.</summary>
    HardwareReadout Read();
}

/// <summary>Point-in-time hardware snapshot. Null temps = sensor unavailable (show a dash, not 0).</summary>
public sealed record HardwareReadout(
    float? CpuTempC,
    float? GpuTempC,
    double RamUsedPct,
    string CpuName,
    string GpuName);
