using CoreCage.Core;

namespace CoreCage.App.Services;

/// <summary>
/// Real Monitor backend: calls the existing engine's <see cref="SystemMonitor"/> IN-PROCESS.
/// This is the proof-of-seam for the standalone architecture — if this compiles and runs, CoreCage.App
/// has the whole engine available without HTTP and without a separate window. Read() never throws;
/// the engine's own methods already degrade to null/"Unknown" when a sensor is absent.
/// </summary>
public sealed class EngineMonitorService : IMonitorService
{
    public void Initialize() => SystemMonitor.Initialize();

    public HardwareReadout Read() => new(
        CpuTempC: SystemMonitor.GetCpuTemperature(),
        GpuTempC: SystemMonitor.GetGpuTemperature(),
        RamUsedPct: SystemMonitor.GetRAMUsagePercent(),
        CpuName: SystemMonitor.GetCpuName(),
        GpuName: SystemMonitor.GetGpuName());
}
