using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Review finding IMPORTANT-2: RestoreEverything.RestoreAll() reset process priorities but never
    /// un-pinned ProcessorAffinity, so a crash mid-cage (with the in-memory CagePlan lost) left
    /// processes confined to the caged-core mask until reboot -- the Big Red Button is supposed to be
    /// the one guaranteed way out of that. The fix adds an affinity-reset loop mirroring
    /// ResetAllProcessPriorities' pattern (per-process try/catch, skip on denied). The loop itself
    /// mutates real process affinity and must NEVER run in a unit test; only the pure full-mask math it
    /// depends on is exercised here.
    /// </summary>
    [TestClass]
    public class RestoreEverythingAffinityMaskTests
    {
        [TestMethod]
        public void FullAffinityMask_OneCore_IsBitZeroOnly()
        {
            Assert.AreEqual(0b1L, RestoreEverything.FullAffinityMask(1));
        }

        [TestMethod]
        public void FullAffinityMask_FourCores_IsLowFourBits()
        {
            Assert.AreEqual(0b1111L, RestoreEverything.FullAffinityMask(4));
        }

        [TestMethod]
        public void FullAffinityMask_EightCores_IsAllEightBitsSet()
        {
            Assert.AreEqual(0b11111111L, RestoreEverything.FullAffinityMask(8));
        }

        [TestMethod]
        public void FullAffinityMask_SixteenCores_MatchesExpectedMask()
        {
            Assert.AreEqual(0xFFFFL, RestoreEverything.FullAffinityMask(16));
        }
    }
}
