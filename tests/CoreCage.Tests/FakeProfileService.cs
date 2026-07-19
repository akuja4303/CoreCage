using CoreCage.App.Services;

namespace CoreCage.Tests;

internal sealed class FakeProfileService : IProfileService
{
    public List<ProfileInfo> Items { get; set; } = new()
    {
        new ProfileInfo("Esports", "Max FPS, low latency"),
        new ProfileInfo("Quiet", "Cool + silent"),
    };
    public bool ApplyResult { get; set; } = true;
    public bool DeleteResult { get; set; } = true;
    public string? LastApplied, LastDeleted;

    public IReadOnlyList<ProfileInfo> List() => Items;
    public bool Apply(string name) { LastApplied = name; return ApplyResult; }
    public bool Delete(string name) { LastDeleted = name; return DeleteResult; }
}
