using System;
using System.Diagnostics;
using System.Threading;

namespace CoreCage.Core.Benchmark
{
    /// <summary>avg / max / p99 of a scheduling-jitter burst, in milliseconds.</summary>
    public readonly struct JitterResult
    {
        public double AvgMs { get; }
        public double MaxMs { get; }
        public double P99Ms { get; }
        public JitterResult(double avg, double max, double p99) { AvgMs = avg; MaxMs = max; P99Ms = p99; }
        public static readonly JitterResult Empty = new JitterResult(0, 0, 0);
    }

    /// <summary>Measures scheduling jitter: how much Thread.Sleep(1) overshoots its requested 1 ms.
    /// A cheap proxy for system responsiveness / timer behavior — NOT true kernel DPC latency, but a
    /// good "is the scheduler tight right now" signal that moves with timer resolution + background load.
    /// Runs a short bounded burst on a high-priority thread; call off the UI thread.</summary>
    public static class JitterSampler
    {
        public static JitterResult Sample(int iterations = 64)
        {
            if (iterations < 1) iterations = 1;
            var overshoot = new double[iterations];
            var sw = new Stopwatch();
            var prev = Thread.CurrentThread.Priority;
            try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch { }
            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    sw.Restart();
                    Thread.Sleep(1);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds - 1.0;
                    overshoot[i] = ms < 0 ? 0 : ms;
                }
            }
            finally { try { Thread.CurrentThread.Priority = prev; } catch { } }
            return Stats(overshoot);
        }

        /// <summary>Pure: avg / max / p99 (nearest-rank) of the overshoot samples. Empty -> all zero.</summary>
        public static JitterResult Stats(double[] samplesMs)
        {
            if (samplesMs == null || samplesMs.Length == 0) return JitterResult.Empty;
            int n = samplesMs.Length;
            double sum = 0, max = samplesMs[0];
            for (int i = 0; i < n; i++)
            {
                double v = samplesMs[i];
                sum += v;
                if (v > max) max = v;
            }
            var sorted = (double[])samplesMs.Clone();
            Array.Sort(sorted);
            int idx = (int)Math.Ceiling(0.99 * n) - 1;
            if (idx < 0) idx = 0;
            if (idx >= n) idx = n - 1;
            return new JitterResult(sum / n, max, sorted[idx]);
        }
    }
}
