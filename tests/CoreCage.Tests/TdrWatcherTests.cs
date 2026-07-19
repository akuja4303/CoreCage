using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Tuning;

namespace CoreCage.Tests
{
    [TestClass]
    public class TdrWatcherTests
    {
        private static readonly DateTime Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime At(int seconds) => Base.AddSeconds(seconds);

        // ── CountInWindow (pure) ─────────────────────────────────────────────────

        [TestMethod]
        public void CountInWindow_CountsOnlyEventsInsideHalfOpenWindow()
        {
            var events = new[] { At(5), At(15), At(25) };
            Assert.AreEqual(1, TdrWatcher.CountInWindow(events, At(10), At(20))); // only At(15)
        }

        [TestMethod]
        public void CountInWindow_IsHalfOpen_StartExclusive_EndInclusive()
        {
            var events = new[] { At(10), At(20) };
            // At(10) == start → excluded; At(20) == end → included.
            Assert.AreEqual(1, TdrWatcher.CountInWindow(events, At(10), At(20)));
        }

        [TestMethod]
        public void CountInWindow_Empty_ReturnsZero()
        {
            Assert.AreEqual(0, TdrWatcher.CountInWindow(Array.Empty<DateTime>(), At(0), At(100)));
        }

        [TestMethod]
        public void OccurredInWindow_ReflectsCount()
        {
            var events = new[] { At(15) };
            Assert.IsTrue(TdrWatcher.OccurredInWindow(events, At(10), At(20)));
            Assert.IsFalse(TdrWatcher.OccurredInWindow(events, At(20), At(30)));
        }

        // ── TdrStabilityProbe (injectable clock + counter) ───────────────────────

        [TestMethod]
        public void StabilityProbe_StableWhenNoTdr_UnstableWhenTdr_AndAdvancesWindow()
        {
            DateTime clock = At(0);
            int tdrCount = 0;
            DateTime windowStartSeen = default;

            var probe = new TdrStabilityProbe(
                countSince: since => { windowStartSeen = since; return tdrCount; },
                now: () => clock);
            // Construction stamps _last = At(0).

            clock = At(10); tdrCount = 0;
            Assert.IsTrue(probe.StableSinceLast());
            Assert.AreEqual(At(0), windowStartSeen);   // first window starts at construction time

            clock = At(20); tdrCount = 1;
            Assert.IsFalse(probe.StableSinceLast());    // a TDR occurred → unstable
            Assert.AreEqual(At(10), windowStartSeen);   // window advanced to the previous call's time

            clock = At(30); tdrCount = 0;
            Assert.IsTrue(probe.StableSinceLast());
            Assert.AreEqual(At(20), windowStartSeen);
        }

        // ── Wires into the auto-tuner as the isStable seam ───────────────────────

        [TestMethod]
        public void StabilityProbe_DrivesAutoTuner_TreatsTdrOffsetAsUnstable()
        {
            // TDRs start once the offset exceeds +120. Model that via the probe's counter.
            int current = 0;
            DateTime clock = At(0);
            var applied = new List<int>();

            var probe = new TdrStabilityProbe(
                countSince: _ => current > 120 ? 1 : 0,   // unstable above +120
                now: () => { clock = clock.AddSeconds(1); return clock; });

            var tuner = new GpuAutoTuner(
                applyOffset: o => { current = o; applied.Add(o); return true; },
                isStable: probe.StableSinceLast,
                measure: () => CoreCage.Core.Telemetry.FrametimeStats.FromFrametimes(new[] { 1000.0 / (120 + current * 0.2) }));

            var result = tuner.Run(new GpuAutoTuneConfig { MinOffset = 0, MaxOffset = 300, Step = 30, NoiseFps = 1.0 });

            Assert.IsTrue(result.HitInstability);
            Assert.AreEqual(120, result.RecommendedOffsetMhz);     // highest stable rising-fitness offset
            Assert.AreEqual(120, applied[applied.Count - 1]);      // card left at the safe recommended offset
        }
    }
}
