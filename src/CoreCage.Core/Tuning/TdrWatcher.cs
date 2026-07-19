using System;
using System.Collections.Generic;

namespace CoreCage.Core.Tuning
{
    /// <summary>
    /// Detects GPU driver TDRs ("display driver stopped responding and has recovered" / bugcheck) so the
    /// GPU offset auto-tune can treat an offset that TDR'd as unstable (Council rank 6 / rank 13's
    /// stability seam). The windowing decision is pure + unit-tested; the live System-event-log read is a
    /// thin WMI wrapper (reuses System.Management — no new dependency) that degrades to 0 on any failure.
    /// </summary>
    public static class TdrWatcher
    {
        /// <summary>
        /// System-log Event IDs that indicate a TDR: 4101 = driver recovered (soft TDR), 4103 = TDR
        /// bugcheck path. Either one inside a test window means that offset was unstable.
        /// </summary>
        public static readonly int[] TdrEventIds = { 4101, 4103 };

        /// <summary>
        /// PURE: how many of <paramref name="eventTimes"/> fall in the half-open window
        /// (<paramref name="windowStart"/>, <paramref name="windowEnd"/>]. Order-independent.
        /// </summary>
        public static int CountInWindow(IEnumerable<DateTime> eventTimes, DateTime windowStart, DateTime windowEnd)
        {
            if (eventTimes == null) throw new ArgumentNullException(nameof(eventTimes));
            int n = 0;
            foreach (var t in eventTimes)
                if (t > windowStart && t <= windowEnd) n++;
            return n;
        }

        /// <summary>PURE: did any TDR occur in the window?</summary>
        public static bool OccurredInWindow(IEnumerable<DateTime> eventTimes, DateTime windowStart, DateTime windowEnd)
            => CountInWindow(eventTimes, windowStart, windowEnd) > 0;

        /// <summary>
        /// LIVE: count TDR events written to the System event log since <paramref name="since"/> (UTC).
        /// Uses WMI (<c>Win32_NTLogEvent</c>); returns 0 on any query failure so a monitoring hiccup is
        /// never mistaken for instability.
        /// </summary>
        public static int RecentTdrCount(DateTime since)
        {
            try
            {
                string dmtf = System.Management.ManagementDateTimeConverter.ToDmtfDateTime(since.ToLocalTime());
                string q = "SELECT TimeGenerated FROM Win32_NTLogEvent WHERE Logfile='System' " +
                           "AND (EventCode=4101 OR EventCode=4103) " +
                           $"AND TimeGenerated >= '{dmtf}'";
                using var searcher = new System.Management.ManagementObjectSearcher(q);
                int count = 0;
                foreach (var _ in searcher.Get()) count++;
                return count;
            }
            catch (Exception ex)
            {
                Logger.Log($"TdrWatcher.RecentTdrCount failed ({ex.Message}) — assuming 0 (no TDR).");
                return 0;
            }
        }
    }

    /// <summary>
    /// Stateful stability check for <see cref="GpuAutoTuner"/>: each call reports whether a TDR occurred
    /// since the previous call, then advances its clock. The injectable seams (<c>countSince</c>,
    /// <c>now</c>) keep it fully unit-testable; the live default reads <see cref="TdrWatcher.RecentTdrCount"/>
    /// against the wall clock. Pass <see cref="StableSinceLast"/> as the auto-tuner's <c>isStable</c> seam.
    /// </summary>
    public sealed class TdrStabilityProbe
    {
        private readonly Func<DateTime, int> _countSince;
        private readonly Func<DateTime> _now;
        private DateTime _last;

        public TdrStabilityProbe(Func<DateTime, int>? countSince = null, Func<DateTime>? now = null)
        {
            _countSince = countSince ?? TdrWatcher.RecentTdrCount;
            _now = now ?? (() => DateTime.UtcNow);
            _last = _now();
        }

        /// <summary>
        /// True when no TDR has been recorded since the previous call (or since construction on the first
        /// call). Advances the internal clock so the next call covers the next offset's window.
        /// </summary>
        public bool StableSinceLast()
        {
            DateTime windowStart = _last;
            DateTime nowT = _now();
            _last = nowT;
            return _countSince(windowStart) == 0;
        }
    }
}
