using System.Diagnostics;

namespace CoreCage.App.Services;

/// <summary>Real Processes backend via System.Diagnostics. Never throws out; skips processes it can't read.</summary>
public sealed class EngineProcessService : IProcessService
{
    public IReadOnlyList<ProcInfo> ListTopByMemory(int count)
    {
        var list = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add(new ProcInfo(p.Id, p.ProcessName, p.WorkingSet64 / (1024.0 * 1024.0))); }
            catch { /* process exited / access denied — skip */ }
            finally { try { p.Dispose(); } catch { } }
        }
        return list.OrderByDescending(x => x.MemoryMb).Take(count).ToList();
    }

    public bool Kill(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.Kill(); return true; }
        catch { return false; }
    }
}
