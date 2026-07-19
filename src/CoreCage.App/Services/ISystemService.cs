namespace CoreCage.App.Services;

/// <summary>
/// The System group's cleanup actions. Backed by the in-process engine's MemoryCleaner; tests use a
/// fake. Actions return bool so the UI shows an honest applied/failed result.
/// </summary>
public interface ISystemService
{
    RamInfo ReadRam();
    /// <summary>Trims process working sets back to RAM (frees "used" memory). False on failure.</summary>
    bool FreeWorkingSets();
    /// <summary>Purges the Windows standby (cached) list. False on failure.</summary>
    bool ClearStandbyList();
}

/// <summary>RAM snapshot for the System panel.</summary>
public sealed record RamInfo(double UsedGb, double TotalGb, double UsedPct, string AvailableText);
