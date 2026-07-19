namespace CoreCage.App.Services;

/// <summary>The Profiles group's CRUD/apply over saved tweak sets. Backed by the engine's ProfileManager.</summary>
public interface IProfileService
{
    IReadOnlyList<ProfileInfo> List();
    bool Apply(string name);
    bool Delete(string name);
}

/// <summary>A saved profile (name + description).</summary>
public sealed record ProfileInfo(string Name, string Description)
{
    /// <summary>Accessible name for screen readers / UIA (not the default record dump).</summary>
    public override string ToString() => Name;
}
