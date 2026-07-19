using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Benchmark;

namespace CoreCage.Tests.Benchmark
{
    [TestClass]
    public class JitterSamplerTests
    {
        [TestMethod]
        public void Stats_Empty_ReturnsZero()
        {
            var r = JitterSampler.Stats(new double[0]);
            Assert.AreEqual(0, r.AvgMs, 1e-9);
            Assert.AreEqual(0, r.MaxMs, 1e-9);
            Assert.AreEqual(0, r.P99Ms, 1e-9);
        }

        [TestMethod]
        public void Stats_ComputesAvgAndMax()
        {
            var r = JitterSampler.Stats(new double[] { 0.0, 1.0, 2.0, 1.0 });
            Assert.AreEqual(1.0, r.AvgMs, 1e-9);
            Assert.AreEqual(2.0, r.MaxMs, 1e-9);
        }

        [TestMethod]
        public void Stats_P99_NearestRank_PicksTopForSmallSet()
        {
            // 100 samples 0..99; ceil(0.99*100)-1 = 98 -> value 98.
            var s = new double[100];
            for (int i = 0; i < 100; i++) s[i] = i;
            var r = JitterSampler.Stats(s);
            Assert.AreEqual(98.0, r.P99Ms, 1e-9);
            Assert.AreEqual(99.0, r.MaxMs, 1e-9);
        }

        [TestMethod]
        public void Stats_SingleSample()
        {
            var r = JitterSampler.Stats(new double[] { 3.5 });
            Assert.AreEqual(3.5, r.AvgMs, 1e-9);
            Assert.AreEqual(3.5, r.MaxMs, 1e-9);
            Assert.AreEqual(3.5, r.P99Ms, 1e-9);
        }
    }
}
