using System.Reflection;
using System.Security.Principal;

namespace CoreCage.App;

/// <summary>
/// Drives the Settings group: honest about/status. Reports the app version, whether it's actually
/// running elevated (so a user knows why an action might fail), and that the engine is in-process.
/// </summary>
public sealed class SettingsViewModel
{
    public SettingsViewModel()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            IsElevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { IsElevated = false; }
    }

    public string AppName => "CoreCage";

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    public bool IsElevated { get; }

    public string ElevationText => IsElevated
        ? "Running elevated (admin) — hardware changes will apply."
        : "Not elevated — some actions will fail. Relaunch as administrator.";

    public string EngineText => "Engine: in-process (standalone build — no separate service needed).";

    public string About =>
        "CoreCage: a lightweight, open-source gaming-performance app that drives its tuning engine in-process.";
}
