using System;
using System.Collections.Generic;

namespace CoreCage.Core.Telemetry
{
    /// <summary>Compares the PRESENTED frame cadence (MsBetweenPresents — what the game actually
    /// renders) against the DISPLAYED cadence (MsBetweenDisplayChange — what reaches the screen,
    /// including interpolated Frame-Generation frames). When the displayed rate substantially
    /// exceeds the presented rate, frame generation is inflating the FPS number and the PRESENTED
    /// rate is the honest "responsiveness" figure. Pure + unit-testable; no IO.</summary>
    public sealed class FrameGenAnalysis
    {
        public double PresentedFps { get; }
        public double DisplayedFps { get; }
        public double Ratio { get; }          // displayed / presented (0 when unknown)
        public bool   FrameGenLikely { get; }
        public double GeneratedPct { get; }   // estimated % of displayed frames that are generated

        private FrameGenAnalysis(double pFps, double dFps, double ratio, bool fg, double genPct)
        { PresentedFps = pFps; DisplayedFps = dFps; Ratio = ratio; FrameGenLikely = fg; GeneratedPct = genPct; }

        public static readonly FrameGenAnalysis None = new FrameGenAnalysis(0, 0, 0, false, 0);

        // FG is flagged only with enough samples AND a clear cadence gap (avoids false positives
        // from vsync/measurement noise where the two columns differ slightly).
        private const double RatioThreshold = 1.4;
        private const int MinFrames = 30;

        public static FrameGenAnalysis From(IReadOnlyList<double> presentedMs, IReadOnlyList<double> displayedMs)
        {
            double pMean = Mean(presentedMs, out int pN);
            double dMean = Mean(displayedMs, out int dN);
            double pFps = pMean > 0 ? 1000.0 / pMean : 0;
            double dFps = dMean > 0 ? 1000.0 / dMean : 0;

            if (pFps <= 0 || dFps <= 0 || pN < MinFrames || dN < MinFrames)
                return new FrameGenAnalysis(pFps, dFps, 0, false, 0);

            double ratio = dFps / pFps;
            bool fg = ratio >= RatioThreshold;
            double genPct = fg ? Clamp((1.0 - pFps / dFps) * 100.0, 0, 100) : 0;
            return new FrameGenAnalysis(pFps, dFps, ratio, fg, genPct);
        }

        private static double Mean(IReadOnlyList<double> xs, out int count)
        {
            count = 0;
            if (xs == null) return 0;
            double sum = 0;
            foreach (var x in xs)
                if (x > 0 && !double.IsNaN(x) && !double.IsInfinity(x)) { sum += x; count++; }
            return count > 0 ? sum / count : 0;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
