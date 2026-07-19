using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class GpuTuningStateTests
    {
        [TestMethod]
        public void ClampCoreOffset_Bounds()
        {
            Assert.AreEqual(GpuTuningState.CoreOffsetMin, GpuTuningState.ClampCoreOffset(-99999));
            Assert.AreEqual(GpuTuningState.CoreOffsetMax, GpuTuningState.ClampCoreOffset(99999));
            Assert.AreEqual(150, GpuTuningState.ClampCoreOffset(150));
        }

        [TestMethod]
        public void ClampMemOffset_Bounds_Are_Conservative()
        {
            Assert.AreEqual(GpuTuningState.MemOffsetMax, GpuTuningState.ClampMemOffset(5000));
            Assert.AreEqual(GpuTuningState.MemOffsetMin, GpuTuningState.ClampMemOffset(-5000));
            Assert.IsTrue(GpuTuningState.MemOffsetMax <= 1000, "mem OC band must stay conservative");
        }

        [TestMethod]
        public void MhzToKhz_Converts()
        {
            Assert.AreEqual(150000, GpuTuningState.MhzToKhz(150));
            Assert.AreEqual(-200000, GpuTuningState.MhzToKhz(-200));
        }

        [TestMethod]
        public void ClampPowerLimit_Respects_Reported_Range()
        {
            Assert.AreEqual(170, GpuTuningState.ClampPowerLimit(999, 100, 170));
            Assert.AreEqual(100, GpuTuningState.ClampPowerLimit(50, 100, 170));
            Assert.AreEqual(130, GpuTuningState.ClampPowerLimit(130, 100, 170));
            // tolerates swapped min/max
            Assert.AreEqual(170, GpuTuningState.ClampPowerLimit(999, 170, 100));
        }

        [TestMethod]
        public void ValidatedGamingOffset_Is_Within_Safe_Band()
        {
            Assert.AreEqual(GpuTuningState.ValidatedGamingCoreOffsetMhz,
                GpuTuningState.ClampCoreOffset(GpuTuningState.ValidatedGamingCoreOffsetMhz));
        }
    }
}
