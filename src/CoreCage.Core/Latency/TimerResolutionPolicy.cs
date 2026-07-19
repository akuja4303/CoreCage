using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core.Latency
{
    /// <summary>
    /// Pure helpers for measured timer-resolution tuning (no I/O — unit-testable). The key idea
    /// (per valleyofdoom/TimerResolution) is that the lowest requested resolution isn't always the
    /// one with the least sleep overshoot — measure candidates and pick the best.
    /// NT timer resolution is expressed in 100-ns units (5000 = 0.5 ms, 10000 = 1 ms).
    /// </summary>
    public static class TimerResolutionPolicy
    {
        public static long ToHundredNs(double ms) => (long)System.Math.Round(ms * 10000.0);
        public static double FromHundredNs(long hundredNs) => hundredNs / 10000.0;

        /// <summary>
        /// Candidate resolutions (ms) to benchmark, within [finestMs, coarsestMs], ascending + distinct:
        /// the finest the hardware supports, the classic 0.5 ms, and 1.0 ms.
        /// </summary>
        public static IReadOnlyList<double> Candidates(double finestMs, double coarsestMs)
        {
            var seed = new[] { finestMs, 0.5, 1.0 };
            var list = new List<double>();
            foreach (double c in seed)
            {
                double v = System.Math.Round(c, 4);
                if (v >= finestMs - 1e-9 && v <= coarsestMs + 1e-9 &&
                    !list.Any(x => System.Math.Abs(x - v) < 1e-6))
                    list.Add(v);
            }
            list.Sort();
            return list;
        }

        /// <summary>
        /// Picks the resolution (ms) with the lowest average sleep overshoot. Ties go to the finer
        /// (smaller) resolution. Returns 0 if there are no samples.
        /// </summary>
        public static double PickBest(IEnumerable<(double resMs, double avgSleepMs)> samples)
        {
            bool any = false;
            double bestRes = 0, bestAvg = double.MaxValue;
            foreach (var (resMs, avgSleepMs) in samples)
            {
                any = true;
                bool better = avgSleepMs < bestAvg - 1e-9;
                bool tieFiner = System.Math.Abs(avgSleepMs - bestAvg) <= 1e-9 && resMs < bestRes;
                if (better || tieFiner) { bestAvg = avgSleepMs; bestRes = resMs; }
            }
            return any ? bestRes : 0;
        }
    }
}
