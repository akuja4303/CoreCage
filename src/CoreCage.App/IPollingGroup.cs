namespace CoreCage.App;

/// <summary>
/// A group whose telemetry timer must only run while its tab is visible. Hidden tabs were polling
/// hardware in the background (Tune spawned nvidia-smi.exe every 2s regardless of the active tab),
/// which wastes CPU and can nick game FPS. The shell activates exactly one group at a time.
/// </summary>
internal interface IPollingGroup
{
    /// <summary>Start polling and refresh immediately (tab became visible).</summary>
    void Activate();

    /// <summary>Stop polling (tab hidden).</summary>
    void Deactivate();

    bool IsPolling { get; }
}
