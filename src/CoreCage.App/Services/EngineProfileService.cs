using CoreCage.Core;

namespace CoreCage.App.Services;

/// <summary>Real Profiles backend over the engine's ProfileManager. Never throws out.</summary>
public sealed class EngineProfileService : IProfileService
{
    public IReadOnlyList<ProfileInfo> List()
    {
        try { return ProfileManager.LoadCustomProfiles().Select(p => new ProfileInfo(p.Name, p.Description)).ToList(); }
        catch { return new List<ProfileInfo>(); }
    }

    public bool Apply(string name)
    {
        var p = ProfileManager.LoadCustomProfiles().FirstOrDefault(x => x.Name == name);
        if (p == null) return false;
        try { ProfileManager.ApplyProfile(p); return true; } catch { return false; }
    }

    public bool Delete(string name)
    {
        var p = ProfileManager.LoadCustomProfiles().FirstOrDefault(x => x.Name == name);
        if (p == null) return false;
        try { ProfileManager.DeleteCustomProfile(p); return true; } catch { return false; }
    }
}
