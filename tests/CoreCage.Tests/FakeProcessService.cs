using CoreCage.App.Services;

namespace CoreCage.Tests;

internal sealed class FakeProcessService : IProcessService
{
    public List<ProcInfo> Procs { get; set; } = new()
    {
        new ProcInfo(1000, "chrome", 800),
        new ProcInfo(1001, "game", 1600),
        new ProcInfo(1002, "explorer", 120),
    };
    public bool KillResult { get; set; } = true;
    public int? LastKilledPid { get; private set; }

    public IReadOnlyList<ProcInfo> ListTopByMemory(int count) => Procs;
    public bool Kill(int pid) { LastKilledPid = pid; return KillResult; }
}
