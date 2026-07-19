using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CoreCage.Core.Latency
{
    /// <summary>
    /// Benchmarks each candidate timer resolution by measuring how much a <c>Sleep(1)</c> actually
    /// overshoots, then applies the resolution with the least overshoot (decision logic in the pure,
    /// tested <see cref="TimerResolutionPolicy"/>). This beats blindly forcing 0.5 ms, which on some
    /// systems has worse jitter than 1 ms. Blocking (~a second total) — run off the UI thread.
    /// Not auto-wired; the existing Gaming-Mode 0.5 ms set still applies until this is adopted.
    /// </summary>
    public static class TimerResolutionTuner
    {
        /// <summary>Measures, picks the lowest-overshoot resolution, applies it, and returns it (ms).</summary>
        public static double MeasureAndApply(int samplesPerCandidate = 100)
        {
            var (finest, coarsest, _) = TimerResolution.QueryRangeMs();
            IReadOnlyList<double> candidates = TimerResolutionPolicy.Candidates(finest, coarsest);

            var results = new List<(double resMs, double avgSleepMs)>();
            foreach (double res in candidates)
            {
                double actual = TimerResolution.SetMs(res);
                double overshoot = MeasureSleepOvershootMs(samplesPerCandidate);
                results.Add((res, overshoot));
                Logger.Event("Timer candidate {Res}ms (granted {Actual}ms) → avg Sleep(1) {Overshoot}ms",
                    res, actual, overshoot);
            }

            double best = TimerResolutionPolicy.PickBest(results);
            if (best <= 0) { Logger.Log("Timer tuner: no candidates measured"); return 0; }

            double applied = TimerResolution.SetMs(best);
            Logger.Log($"Timer resolution tuned → {best:F4} ms (granted {applied:F4} ms)");
            return applied;
        }

        /// <summary>Average milliseconds a <c>Thread.Sleep(1)</c> takes over <paramref name="samples"/> iterations.</summary>
        public static double MeasureSleepOvershootMs(int samples)
        {
            if (samples <= 0) return 0;
            var sw = new Stopwatch();
            double total = 0;
            for (int i = 0; i < samples; i++)
            {
                sw.Restart();
                Thread.Sleep(1);
                sw.Stop();
                total += sw.Elapsed.TotalMilliseconds;
            }
            return total / samples;
        }
    }
}
