using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;
using CoreCage.Core.Tuning;

namespace CoreCage.Tests
{
    [TestClass]
    public class GpuAutoTuneTests
    {
        private static GpuAutoTuneConfig Cfg(int max = 300, int step = 30, double noise = 1.0)
            => new GpuAutoTuneConfig { MinOffset = 0, MaxOffset = max, Step = step, NoiseFps = noise };

        private static OffsetTrial T(int offset, bool stable, double fps) => new OffsetTrial(offset, stable, fps);

        // ── NextOffset ─────────────────────────────────────────────────────────

        [TestMethod]
        public void NextOffset_NoTrials_StartsAtMinOffset()
        {
            Assert.AreEqual(0, GpuAutoTunePolicy.NextOffset(new List<OffsetTrial>(), Cfg()));
        }

        [TestMethod]
        public void NextOffset_LastUnstable_Stops()
        {
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, false, 0) };
            Assert.IsNull(GpuAutoTunePolicy.NextOffset(trials, Cfg()));
        }

        [TestMethod]
        public void NextOffset_Improving_ClimbsByStep()
        {
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, true, 105) };
            Assert.AreEqual(60, GpuAutoTunePolicy.NextOffset(trials, Cfg()));
        }

        [TestMethod]
        public void NextOffset_Plateau_Stops()
        {
            // 100 → 100.5 is within the 1.0 noise floor → no real gain → stop.
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, true, 100.5) };
            Assert.IsNull(GpuAutoTunePolicy.NextOffset(trials, Cfg()));
        }

        [TestMethod]
        public void NextOffset_MaxOffset_Stops()
        {
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, true, 110) };
            Assert.IsNull(GpuAutoTunePolicy.NextOffset(trials, Cfg(max: 30)));
        }

        // ── Recommend ──────────────────────────────────────────────────────────

        [TestMethod]
        public void Recommend_PicksLowestOffsetWithinNoiseOfBest()
        {
            // 30 and 60 both reach ~best fitness → pick the lower (more stable) offset.
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, true, 110), T(60, true, 110.3) };
            Assert.AreEqual(30, GpuAutoTunePolicy.Recommend(trials, Cfg()));
        }

        [TestMethod]
        public void Recommend_NoStableTrials_FallsBackToMinOffset()
        {
            var trials = new List<OffsetTrial> { T(0, false, 0), T(30, false, 0) };
            Assert.AreEqual(0, GpuAutoTunePolicy.Recommend(trials, Cfg()));
        }

        [TestMethod]
        public void Recommend_IgnoresUnstableWhenChoosingBest()
        {
            var trials = new List<OffsetTrial> { T(0, true, 100), T(30, true, 120), T(60, false, 0) };
            Assert.AreEqual(30, GpuAutoTunePolicy.Recommend(trials, Cfg()));
        }

        // ── End-to-end orchestration with fake hardware ──────────────────────────

        // Builds a fake GPU: applyOffset records the current offset; isStable trips above `ceiling`;
        // measure() returns a single-frame FrametimeStats whose AvgFps follows `fpsFor(currentOffset)`.
        private static GpuAutoTuner FakeTuner(int ceiling, System.Func<int, double> fpsFor, List<int> appliedLog)
        {
            int current = 0;
            return new GpuAutoTuner(
                applyOffset: o => { current = o; appliedLog.Add(o); return true; },
                isStable: () => current <= ceiling,
                measure: () => FrametimeStats.FromFrametimes(new[] { 1000.0 / fpsFor(current) }));
        }

        [TestMethod]
        public void Run_KneeThenPlateau_RecommendsLowestOffsetAtPeakFps()
        {
            // FPS rises to a 144 ceiling at +120, then flat — the knee is +120.
            var applied = new List<int>();
            var tuner = FakeTuner(ceiling: 1000, fpsFor: off => off <= 120 ? 120 + off * 0.2 : 144.0, appliedLog: applied);

            var result = tuner.Run(Cfg());

            Assert.IsFalse(result.HitInstability);
            Assert.AreEqual(120, result.RecommendedOffsetMhz);     // lowest offset reaching the 144 peak
            Assert.AreEqual(120, applied[applied.Count - 1]);      // GPU left at the recommended offset
        }

        [TestMethod]
        public void Run_InstabilityCeiling_StopsAtLastStable_AndNeverLeavesCardUnstable()
        {
            // FPS always rises, but the card TDRs above +150.
            var applied = new List<int>();
            var tuner = FakeTuner(ceiling: 150, fpsFor: off => 120 + off * 0.2, appliedLog: applied);

            var result = tuner.Run(Cfg());

            Assert.IsTrue(result.HitInstability);
            Assert.AreEqual(150, result.RecommendedOffsetMhz);
            // It probed +180 once (found it unstable) but must finish by re-applying the safe +150.
            Assert.IsTrue(result.Trials.Any(t => t.Offset == 180 && !t.Stable));
            Assert.AreEqual(150, applied[applied.Count - 1]);
        }

        [TestMethod]
        public void Run_WhenNoOffsetHelps_RecommendsZero_AndNeverLeavesAnIdleOffset()
        {
            // Adversarial: FPS DECREASES with offset (e.g. power-limited / throttling) — pushing the
            // clock buys nothing. The tuner must fall back to the stock 0 offset, not a useless risky one.
            var applied = new List<int>();
            var tuner = FakeTuner(ceiling: 1000, fpsFor: off => 144.0 - off * 0.1, appliedLog: applied);

            var result = tuner.Run(Cfg());

            Assert.IsFalse(result.HitInstability);
            Assert.AreEqual(0, result.RecommendedOffsetMhz);       // stock wins
            Assert.AreEqual(0, applied[applied.Count - 1]);        // card left at stock
            Assert.IsFalse(result.Trials.Any(t => t.Offset > 30)); // bailed as soon as a step regressed
        }

        [TestMethod]
        public void Run_FitnessRisesToCeiling_CapsAtMaxOffset_NeverOvershoots()
        {
            // Adversarial: fitness keeps rising and the card never TDRs — the search must stop exactly at
            // MaxOffset and never probe beyond the configured bound.
            var applied = new List<int>();
            var tuner = FakeTuner(ceiling: 1000, fpsFor: off => 100 + off * 0.5, appliedLog: applied);

            var result = tuner.Run(Cfg(max: 90, step: 30));

            Assert.IsFalse(result.HitInstability);
            Assert.AreEqual(90, result.RecommendedOffsetMhz);            // climbed to the ceiling
            Assert.IsFalse(result.Trials.Any(t => t.Offset > 90));       // never overshot MaxOffset
            Assert.AreEqual(90, applied[applied.Count - 1]);
        }
    }
}
