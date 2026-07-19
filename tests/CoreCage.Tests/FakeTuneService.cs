using CoreCage.App.Services;

namespace CoreCage.Tests;

/// <summary>In-memory <see cref="ITuneService"/> so the Tune VM is tested without touching a real GPU.</summary>
internal sealed class FakeTuneService : ITuneService
{
    public GpuReadout Readout { get; set; } = new(
        Available: true,
        CoreMhz: 1800, MemMhz: 7000, PowerW: 120, TempC: 61,
        PowerMinW: 100, PowerCurW: 170, PowerMaxW: 170,
        CoreOffsetMhz: 0, SupportsOffset: true,
        Vibrance: 50, VibranceMin: 0, VibranceMax: 63, VibranceOk: true);

    public bool PowerResult { get; set; } = true;
    public bool OffsetResult { get; set; } = true;
    public bool VibranceResult { get; set; } = true;

    public int? LastPowerWatts { get; private set; }
    public int? LastOffsetMhz { get; private set; }
    public int? LastVibrance { get; private set; }

    public GpuReadout ReadGpu() => Readout;

    public bool SetPowerLimit(int watts) { LastPowerWatts = watts; return PowerResult; }
    public bool SetCoreOffset(int mhz)   { LastOffsetMhz = mhz;    return OffsetResult; }
    public bool SetVibrance(int level)   { LastVibrance = level;   return VibranceResult; }
}
