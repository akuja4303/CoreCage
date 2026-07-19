using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    [TestClass]
    public class FrametimeStatsTests
    {
        [TestMethod]
        public void FromFrametimes_ConstantFrameTime_GivesExactFps()
        {
            // 100 frames of 10 ms each → a clean 100 fps with no spread.
            var ft = new List<double>();
            for (int i = 0; i < 100; i++) ft.Add(10.0);

            var s = FrametimeStats.FromFrametimes(ft);

            Assert.AreEqual(100, s.FrameCount);
            Assert.AreEqual(10.0, s.AvgFrameTimeMs, 1e-9);
            Assert.AreEqual(100.0, s.AvgFps, 1e-9);
            Assert.AreEqual(100.0, s.P1LowFps, 1e-9);   // every frame identical → 1% low == avg
            Assert.AreEqual(100.0, s.P01LowFps, 1e-9);
            Assert.AreEqual(0.0, s.StdDevMs, 1e-9);
            Assert.AreEqual(100.0, s.MinFps, 1e-9);
            Assert.AreEqual(100.0, s.MaxFps, 1e-9);
        }

        [TestMethod]
        public void FromFrametimes_KnownRamp_MatchesHandComputedPercentiles()
        {
            // Frametimes 1..100 ms.
            var ft = new List<double>();
            for (int i = 1; i <= 100; i++) ft.Add(i);

            var s = FrametimeStats.FromFrametimes(ft);

            Assert.AreEqual(100, s.FrameCount);
            Assert.AreEqual(50.5, s.AvgFrameTimeMs, 1e-9);     // mean of 1..100
            Assert.AreEqual(1000.0 / 50.5, s.AvgFps, 1e-9);
            Assert.AreEqual(99.01, s.P99FrameTimeMs, 1e-6);    // R-7 percentile of 1..100 at p=99
            Assert.AreEqual(1000.0 / 99.01, s.P1LowFps, 1e-6); // worst-1% frametime → low fps
            Assert.AreEqual(1000.0 / 100.0, s.MinFps, 1e-9);   // worst single frame = 100 ms
            Assert.AreEqual(1000.0 / 1.0, s.MaxFps, 1e-9);     // best single frame = 1 ms
        }

        [TestMethod]
        public void FromFrametimes_IgnoresInvalidSamples()
        {
            var ft = new List<double> { 10.0, -5.0, 0.0, double.NaN, double.PositiveInfinity, 10.0 };

            var s = FrametimeStats.FromFrametimes(ft);

            Assert.AreEqual(2, s.FrameCount);          // only the two 10 ms frames survive
            Assert.AreEqual(100.0, s.AvgFps, 1e-9);
        }

        [TestMethod]
        public void FromFrametimes_Empty_ReturnsEmpty()
        {
            var s = FrametimeStats.FromFrametimes(new List<double>());
            Assert.AreEqual(0, s.FrameCount);
            Assert.AreEqual(0.0, s.AvgFps, 1e-9);
        }

        [TestMethod]
        public void Percentile_LinearInterpolation_KnownValues()
        {
            var data = new List<double> { 10, 20, 30, 40, 50 }; // already ascending, n=5

            Assert.AreEqual(10.0, FrametimeStats.Percentile(data, 0), 1e-9);
            Assert.AreEqual(20.0, FrametimeStats.Percentile(data, 25), 1e-9);  // rank 1.0
            Assert.AreEqual(30.0, FrametimeStats.Percentile(data, 50), 1e-9);  // rank 2.0
            Assert.AreEqual(50.0, FrametimeStats.Percentile(data, 100), 1e-9);
            Assert.AreEqual(49.6, FrametimeStats.Percentile(data, 99), 1e-9);  // rank 3.96 → 40 + .96*10
        }

        [TestMethod]
        public void Percentile_SingleElement_ReturnsThatElement()
        {
            Assert.AreEqual(42.0, FrametimeStats.Percentile(new List<double> { 42.0 }, 99), 1e-9);
        }

        [TestMethod]
        public void BenchmarkDelta_ComputesSignedDeltasAndPercent()
        {
            var before = FrametimeStats.FromFrametimes(Repeat(10.0, 50)); // 100 fps
            var after = FrametimeStats.FromFrametimes(Repeat(8.0, 50));   // 125 fps

            var d = BenchmarkDelta.Between(before, after);

            Assert.AreEqual(25.0, d.AvgFpsDelta, 1e-9);
            Assert.AreEqual(25.0, d.AvgFpsPercent, 1e-9);
            Assert.AreEqual(-2.0, d.AvgFrameTimeMsDelta, 1e-9); // frametime dropped 10→8 ms (improvement)
            StringAssert.Contains(d.Summary(), "100.0");
            StringAssert.Contains(d.Summary(), "125.0");
        }

        private static List<double> Repeat(double value, int count)
        {
            var list = new List<double>(count);
            for (int i = 0; i < count; i++) list.Add(value);
            return list;
        }
    }
}
