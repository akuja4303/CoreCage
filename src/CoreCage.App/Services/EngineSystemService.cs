using CoreCage.Core;

namespace CoreCage.App.Services;

/// <summary>Real System backend: the in-process engine's MemoryCleaner. Actions never throw out.</summary>
public sealed class EngineSystemService : ISystemService
{
    private const double Gb = 1024.0 * 1024.0 * 1024.0;

    public RamInfo ReadRam()
    {
        long avail = MemoryCleaner.GetAvailableRAM();
        long total = MemoryCleaner.GetTotalRAM();
        long used = total - avail;
        double pct = total > 0 ? used * 100.0 / total : 0;
        return new RamInfo(used / Gb, total / Gb, pct, MemoryCleaner.FormatBytes(avail) + " free");
    }

    public bool FreeWorkingSets()
    {
        try { MemoryCleaner.Purge(); return true; } catch { return false; }
    }

    public bool ClearStandbyList()
    {
        try { MemoryCleaner.PurgeStandbyList(); return true; } catch { return false; }
    }
}
