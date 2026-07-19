using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Monitor;

namespace CoreCage.Tests.Monitor
{
    [TestClass]
    public class CpuStatsTests
    {
        [TestMethod]
        public void History_Min_Avg_Max()
        {
            var h = new CoreHistory(3); h.Push(10); h.Push(20); h.Push(30);
            Assert.AreEqual(10f, h.Min); Assert.AreEqual(30f, h.Max); Assert.AreEqual(20f, h.Avg);
        }
        [TestMethod]
        public void History_Rolling()
        {
            var h = new CoreHistory(2); h.Push(10); h.Push(20); h.Push(40);
            Assert.AreEqual(20f, h.Min); Assert.AreEqual(40f, h.Max); Assert.AreEqual(30f, h.Avg);
        }
        [TestMethod]
        public void PreferredCore_Highest_Clock()
        {
            var cores = new[] { new CoreInfo{Index=1,ClockMhz=4200}, new CoreInfo{Index=2,ClockMhz=4550}, new CoreInfo{Index=3,ClockMhz=4100} };
            Assert.AreEqual(2, CpuStats.PreferredCore(cores));
        }
        [TestMethod]
        public void PreferredCore_Empty() => Assert.AreEqual(-1, CpuStats.PreferredCore(System.Array.Empty<CoreInfo>()));
    }
}
