using CoreCage.App.Services;

namespace CoreCage.Tests;

internal sealed class FakeSystemService : ISystemService
{
    public RamInfo Ram { get; set; } = new(18.0, 64.0, 28.1, "46.0 GB free");
    public bool WorkingSetsResult { get; set; } = true;
    public bool StandbyResult { get; set; } = true;
    public int FreeCalls, StandbyCalls;

    public RamInfo ReadRam() => Ram;
    public bool FreeWorkingSets() { FreeCalls++; return WorkingSetsResult; }
    public bool ClearStandbyList() { StandbyCalls++; return StandbyResult; }
}
