using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Benchmark;
using CoreCage.Core.Detection;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    /// <summary>
    /// Regression tests from the 2026-07-03 adversarial telemetry-math audit. Each pins one
    /// edge-case a hunter found and I verified against the pre-fix code (red-green):
    ///   • NaN GPU load defeating the classifier's Clamp01 (returned Gaming / NaN confidence)
    ///   • "NaN"/"Infinity" tokens slipping through double.TryParse into the frametime stream
    ///   • a UTF-8 BOM on the header cell silently breaking column matching
    ///   • an all-negative jitter sample set reporting a fabricated max of 0
    ///   • a NaN sample producing a NaN graph coordinate
    ///   • a degenerate capture (AvgFps = Infinity) producing an Infinity delta percentage
    /// </summary>
    [TestClass]
    public class TelemetryHardeningTests
    {
        // C1 — a NaN GPU load must not evade Clamp01 and stick the classifier in Gaming/NaN.
        [TestMethod]
        public void Classify_WithNaNGpuLoad_FallsBackToNormal_WithFiniteConfidence()
        {
            var snap = new SignalSnapshot
            {
                IsFullscreen = true,
                FocusChangedMsAgo = 100,
                GpuLoadPct = double.NaN,
                LauncherContext = LauncherContext.None,
            };

            var d = ConfidenceClassifier.Classify(snap);

            Assert.IsFalse(double.IsNaN(d.Confidence), "NaN must not propagate into confidence");
            Assert.AreEqual(ActivityMode.Normal, d.Mode, "a NaN-poisoned signal must not win a real mode");
        }

        // C2 — double.TryParse accepts "NaN"/"Infinity" tokens regardless of NumberStyles;
        // the parser must drop non-finite frametimes so they never poison the live average.
        [TestMethod]
        public void ParseColumn_SkipsNaNAndInfinityTokens()
        {
            var lines = new[] { "MsBetweenPresents", "10.0", "NaN", "Infinity", "-Infinity", "9.25" };

            var ft = PresentMonCsv.ParseColumn(lines);

            CollectionAssert.AreEqual(new List<double> { 10.0, 9.25 }, ft);
        }

        // C4 — a UTF-8 BOM on the header cell must not break column matching (Trim doesn't strip U+FEFF).
        [TestMethod]
        public void ParseColumn_ToleratesBomOnHeader()
        {
            var lines = new[] { "﻿MsBetweenPresents", "8.5" };

            var ft = PresentMonCsv.ParseColumn(lines);

            CollectionAssert.AreEqual(new List<double> { 8.5 }, ft);
        }

        // C3 — an all-negative sample set must report a real max present in the data, not a fabricated 0.
        [TestMethod]
        public void JitterStats_AllNegative_ReturnsRealMax()
        {
            var r = JitterSampler.Stats(new[] { -5.0, -3.0, -1.0 });

            Assert.AreEqual(-1.0, r.MaxMs, 1e-9, "max must be the largest sample, not 0");
        }

        // C2b — a NaN sample must not produce a NaN graph coordinate (both bound checks skip NaN).
        [TestMethod]
        public void BuildPoints_WithNaNSample_ProducesFiniteCoordinates()
        {
            var pts = GraphMath.BuildPoints(new[] { 10.0, double.NaN, 30.0 }, 100, 50, 0, 100);

            foreach (var (x, y) in pts)
            {
                Assert.IsTrue(double.IsFinite(x) && double.IsFinite(y), "every point must be finite");
            }
        }

        // C6 — a degenerate capture (AvgFps = Infinity) must not yield an Infinity percentage in the summary.
        [TestMethod]
        public void BenchmarkDelta_WithNonFiniteAfter_YieldsFinitePercent()
        {
            var before = FrametimeStats.FromFrametimes(new List<double> { 10.0 });          // AvgFps = 100
            var after = FrametimeStats.FromFrametimes(new List<double> { double.Epsilon });  // AvgFps = Infinity

            var delta = BenchmarkDelta.Between(before, after);

            Assert.IsTrue(double.IsFinite(delta.AvgFpsPercent), "percentage must stay finite");
        }
    }
}
