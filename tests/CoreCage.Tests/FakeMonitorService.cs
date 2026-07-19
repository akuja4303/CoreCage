using CoreCage.App.Services;

namespace CoreCage.Tests;

/// <summary>In-memory <see cref="IMonitorService"/> so the Monitor VM can be tested with no live sensors.</summary>
internal sealed class FakeMonitorService : IMonitorService
{
    public HardwareReadout Next { get; set; } = new(50f, 60f, 40.0, "Fake CPU", "Fake GPU");
    public bool ThrowOnRead { get; set; }
    public int InitializeCalls { get; private set; }
    public int ReadCalls { get; private set; }

    public void Initialize() => InitializeCalls++;

    public HardwareReadout Read()
    {
        ReadCalls++;
        if (ThrowOnRead) throw new InvalidOperationException("sensor backend down");
        return Next;
    }
}
