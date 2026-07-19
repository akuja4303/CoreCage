using CoreCage.Core;

namespace CoreCage.App.Services;

/// <summary>
/// Real Tune backend: drives the in-process <see cref="NvApiGpuController"/>. The controller's own
/// ctor never throws (it degrades to IsAvailable=false on a non-NVIDIA rig or failed NVAPI init), so
/// a non-NVIDIA machine just gets an honest <see cref="GpuReadout.Unavailable"/> and no-op setters —
/// the panel stays usable, GPU tuning simply reads "unavailable".
/// </summary>
public sealed class EngineTuneService : ITuneService
{
    private readonly NvApiGpuController _gpu;

    public EngineTuneService() : this(new NvApiGpuController())
    {
    }

    public EngineTuneService(NvApiGpuController gpu)
    {
        _gpu = gpu;
    }

    public GpuReadout ReadGpu()
    {
        if (!_gpu.IsAvailable)
            return GpuReadout.Unavailable;

        try
        {
            var s = _gpu.GetStats();          // (coreMhz, memMhz, powerW, tempC)
            var pl = _gpu.GetPowerLimits();   // (min, current, max)
            var v = _gpu.GetVibrance();       // (ok, current, min, max, def)
            return new GpuReadout(
                Available: true,
                CoreMhz: s.coreMhz, MemMhz: s.memMhz, PowerW: s.powerW, TempC: s.tempC,
                PowerMinW: pl.min, PowerCurW: pl.current, PowerMaxW: pl.max,
                CoreOffsetMhz: _gpu.GetCoreClockOffsetMhz(), SupportsOffset: _gpu.SupportsClockOffset,
                Vibrance: v.current, VibranceMin: v.min, VibranceMax: v.max, VibranceOk: v.ok);
        }
        catch
        {
            return GpuReadout.Unavailable;
        }
    }

    public bool SetPowerLimit(int watts)
    {
        if (!_gpu.IsAvailable) return false;
        try { _gpu.SetPowerLimit(watts); return true; }
        catch { return false; }
    }

    public bool SetCoreOffset(int mhz) => _gpu.IsAvailable && _gpu.SetCoreClockOffsetMhz(mhz);

    public bool SetVibrance(int level) => _gpu.IsAvailable && _gpu.SetVibrance(level);
}
