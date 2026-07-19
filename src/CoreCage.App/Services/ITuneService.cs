namespace CoreCage.App.Services;

/// <summary>
/// The Tune group's window onto the GPU. Backed in production by <see cref="EngineTuneService"/>
/// (in-process NvApiGpuController); tests use a fake. Every setter returns a bool so the UI can show
/// an honest applied/failed result — NVAPI offset writes in particular can silently no-op on some
/// driver/wrapper combos, and a dishonest "success" would hide that.
/// </summary>
public interface ITuneService
{
    /// <summary>One point-in-time GPU snapshot (telemetry + current limits/offset/vibrance).</summary>
    GpuReadout ReadGpu();

    /// <summary>Sets the GPU power limit in watts. False if unavailable or the write failed.</summary>
    bool SetPowerLimit(int watts);

    /// <summary>Sets the GPU core-clock offset in MHz. False if unsupported or it silently no-op'd.</summary>
    bool SetCoreOffset(int mhz);

    /// <summary>Sets digital vibrance (0 = default, higher = more saturated). False on failure.</summary>
    bool SetVibrance(int level);
}

/// <summary>
/// A GPU snapshot for the Tune panel. <see cref="Available"/> is false on a non-NVIDIA rig / failed
/// NVAPI init, in which case the panel shows an honest "unavailable" state rather than fake zeros.
/// </summary>
public sealed record GpuReadout(
    bool Available,
    int CoreMhz, int MemMhz, double PowerW, double TempC,
    int PowerMinW, int PowerCurW, int PowerMaxW,
    int CoreOffsetMhz, bool SupportsOffset,
    int Vibrance, int VibranceMin, int VibranceMax, bool VibranceOk)
{
    /// <summary>Honest "no GPU control here" snapshot.</summary>
    public static GpuReadout Unavailable { get; } =
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 63, false);
}
