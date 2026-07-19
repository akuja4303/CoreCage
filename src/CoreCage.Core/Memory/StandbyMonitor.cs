using System.Threading;

namespace CoreCage.Core.Memory
{
    /// <summary>
    /// Background ISLC-style monitor: on an interval, purges the standby list when
    /// <see cref="StandbyCleanerPolicy"/> says to. Intended to run only during Gaming Mode
    /// (start on enter, stop on exit). Singleton so the lifecycle is trivial to manage.
    /// </summary>
    public sealed class StandbyMonitor
    {
        public static StandbyMonitor Instance { get; } = new();

        private Timer? _timer;
        private StandbyCleanerPolicy _policy = new();
        private readonly object _gate = new();

        public bool IsRunning => _timer != null;

        public void Start(StandbyCleanerPolicy? policy = null, int intervalMs = 5000)
        {
            lock (_gate)
            {
                _policy = policy ?? new StandbyCleanerPolicy();
                _timer?.Dispose();
                _timer = new Timer(_ =>
                {
                    try { StandbyListCleaner.PurgeIfNeeded(_policy); } catch { }
                }, null, intervalMs, intervalMs);
                Logger.Log($"Standby monitor started (free<{_policy.FreeThresholdMb}MB or standby>{_policy.StandbyThresholdMb}MB, every {intervalMs}ms)");
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_timer == null) return;
                _timer.Dispose();
                _timer = null;
                Logger.Log("Standby monitor stopped");
            }
        }
    }
}
