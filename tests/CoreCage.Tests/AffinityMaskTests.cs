using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Scheduling;

namespace CoreCage.Tests
{
    [TestClass]
    public class AffinityMaskTests
    {
        [TestMethod]
        public void FromCores_Sets_Correct_Bits()
        {
            Assert.AreEqual(0b10101L, AffinityMask.FromCores(new[] { 0, 2, 4 }));
            Assert.AreEqual(0L, AffinityMask.FromCores(new int[0]));
            // out-of-range cores are ignored
            Assert.AreEqual(1L, AffinityMask.FromCores(new[] { 0, -1, 99 }));
        }

        [TestMethod]
        public void ToCores_Round_Trips_With_FromCores()
        {
            var cores = new[] { 0, 3, 7, 11 };
            long mask = AffinityMask.FromCores(cores);
            CollectionAssert.AreEqual(cores, (System.Collections.ICollection)AffinityMask.ToCores(mask));
        }

        [TestMethod]
        public void AllCores_Covers_Logical_Range()
        {
            Assert.AreEqual(0xFFFL, AffinityMask.AllCores(12)); // 5600G = 12 logical
            Assert.AreEqual(0b1L, AffinityMask.AllCores(1));
            Assert.AreEqual(0L, AffinityMask.AllCores(0));
        }

        [TestMethod]
        public void PhysicalCoresOnly_Picks_First_Sibling_Per_Core()
        {
            // 12 logical / 2 threads-per-core → physical cores {0,2,4,6,8,10}
            long mask = AffinityMask.PhysicalCoresOnly(12, 2);
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 6, 8, 10 },
                (System.Collections.ICollection)AffinityMask.ToCores(mask));
            Assert.AreEqual(1365L, mask);
        }

        [TestMethod]
        public void ExcludeCores_Reserves_Os_Cores()
        {
            long all = AffinityMask.AllCores(12);            // 4095
            long noOs = AffinityMask.ExcludeCores(all, new[] { 0, 1 });
            Assert.AreEqual(4092L, noOs);
            CollectionAssert.DoesNotContain((System.Collections.ICollection)AffinityMask.ToCores(noOs), 0);
            CollectionAssert.DoesNotContain((System.Collections.ICollection)AffinityMask.ToCores(noOs), 1);
        }

        [TestMethod]
        public void CpuCage_And_GameAffinity_Are_Complementary()
        {
            // The arc-cage wired into ThrottleForMode confines background procs to cores 0-1,
            // while EacSafePriority gives the game cores 2-11. Together they must partition the
            // 12 logical CPUs with no overlap and no gap.
            long cage = AffinityMask.FromCores(new[] { 0, 1 });          // background → cores 0-1
            long game = AffinityMask.ExcludeCores(AffinityMask.AllCores(12), new[] { 0, 1 }); // game → 2-11
            Assert.AreEqual(0x3L, cage);
            Assert.AreEqual(0xFFCL, game);                                // matches EacSafePriority 0xFFC
            Assert.AreEqual(0L, cage & game);                            // disjoint — no shared core
            Assert.AreEqual(AffinityMask.AllCores(12), cage | game);     // together cover every core
        }
    }
}
