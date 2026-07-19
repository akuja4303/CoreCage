using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Memory;

namespace CoreCage.Tests
{
    [TestClass]
    public class StandbyCleanerPolicyTests
    {
        private static StandbyCleanerPolicy P() =>
            new() { Enabled = true, FreeThresholdMb = 2048, StandbyThresholdMb = 8192 };

        [TestMethod]
        public void Disabled_Never_Purges()
        {
            var p = P(); p.Enabled = false;
            Assert.IsFalse(p.ShouldPurge(0, 999999));
        }

        [TestMethod]
        public void Purges_When_Free_Below_Threshold()
        {
            Assert.IsTrue(P().ShouldPurge(1000, 0));      // 1000 < 2048
            Assert.IsFalse(P().ShouldPurge(4096, 0));     // ample free, small standby
        }

        [TestMethod]
        public void Purges_When_Standby_Above_Threshold()
        {
            Assert.IsTrue(P().ShouldPurge(16000, 9000));  // free fine but standby 9000 > 8192
            Assert.IsFalse(P().ShouldPurge(16000, 4000)); // both fine
        }

        [TestMethod]
        public void Unknown_Readings_Are_Ignored()
        {
            // -1 = unknown on both → no trigger
            Assert.IsFalse(P().ShouldPurge(-1, -1));
            // unknown free, huge standby still triggers
            Assert.IsTrue(P().ShouldPurge(-1, 9000));
            // unknown standby, low free still triggers
            Assert.IsTrue(P().ShouldPurge(1000, -1));
        }
    }
}
