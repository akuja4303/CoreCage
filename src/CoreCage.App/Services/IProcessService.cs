namespace CoreCage.App.Services;

/// <summary>The Processes group's view onto running processes. Backed by System.Diagnostics; tests use a fake.</summary>
public interface IProcessService
{
    IReadOnlyList<ProcInfo> ListTopByMemory(int count);
    bool Kill(int pid);
}

/// <summary>A running process row.</summary>
public sealed record ProcInfo(int Pid, string Name, double MemoryMb)
{
    /// <summary>Accessible name for screen readers / UIA (not the default record dump).</summary>
    public override string ToString() => $"{Name} (PID {Pid}, {MemoryMb:F0} MB)";
}
