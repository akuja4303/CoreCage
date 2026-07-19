using System;
using System.Runtime.InteropServices;

namespace CoreCage.Core.Latency
{
    /// <summary>
    /// Thin wrapper over the NT timer-resolution API. <see cref="SystemTweaks.SetTimerResolution"/>
    /// still owns the app's blunt 0.5 ms set; this exposes query + arbitrary set so the measured
    /// <see cref="TimerResolutionTuner"/> can sweep candidates. Resolutions are in milliseconds here;
    /// conversion to the NT 100-ns unit is via <see cref="TimerResolutionPolicy"/>.
    /// </summary>
    public static class TimerResolution
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

        /// <summary>
        /// (finestMs, coarsestMs, currentMs). NOTE the NT API names are inverted from intuition:
        /// its "maximum resolution" is the finest (smallest) value, "minimum" is the coarsest.
        /// </summary>
        public static (double finestMs, double coarsestMs, double currentMs) QueryRangeMs()
        {
            try
            {
                if (NtQueryTimerResolution(out uint min, out uint max, out uint cur) == 0)
                    return (TimerResolutionPolicy.FromHundredNs(max),
                            TimerResolutionPolicy.FromHundredNs(min),
                            TimerResolutionPolicy.FromHundredNs(cur));
            }
            catch (Exception ex) { Logger.LogError("NtQueryTimerResolution failed", ex); }
            return (0.5, 15.6, 0); // sensible fallback range
        }

        /// <summary>Requests a timer resolution (ms); returns the actual resolution the kernel granted (ms).</summary>
        public static double SetMs(double ms)
        {
            try
            {
                uint desired = (uint)TimerResolutionPolicy.ToHundredNs(ms);
                if (NtSetTimerResolution(desired, true, out uint current) == 0)
                    return TimerResolutionPolicy.FromHundredNs(current);
            }
            catch (Exception ex) { Logger.LogError($"NtSetTimerResolution({ms} ms) failed", ex); }
            return 0;
        }
    }
}
