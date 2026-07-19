using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Review finding MINOR-2: on a &lt;=2-core machine, GamingMode.ApplyCoreCageReal could still ask
    /// CoreCageService.BuildPlan to reserve a core for the game, which -- once FeatureFlags'
    /// defensive clamp bottoms out at reservedForGame == totalCores (e.g. a 1-core box) -- throws
    /// ArgumentOutOfRangeException and would fail the whole Gaming Mode apply. The fix skips BuildPlan
    /// entirely (and logs a skip) whenever totalCores &lt;= 2, before any process enumeration happens.
    /// This is the pure decision half of that guard -- no Process/OS dependency -- so it's asserted
    /// directly; ApplyCoreCageReal itself (which mutates real process affinity) is never invoked here.
    /// </summary>
    [TestClass]
    public class GamingModeCoreCageGuardTests
    {
        [TestMethod]
        public void ShouldSkipCoreCage_TrueForOneOrTwoCores()
        {
            Assert.IsTrue(GamingMode.ShouldSkipCoreCage(1));
            Assert.IsTrue(GamingMode.ShouldSkipCoreCage(2));
        }

        [TestMethod]
        public void ShouldSkipCoreCage_FalseForThreeOrMoreCores()
        {
            Assert.IsFalse(GamingMode.ShouldSkipCoreCage(3));
            Assert.IsFalse(GamingMode.ShouldSkipCoreCage(8));
            Assert.IsFalse(GamingMode.ShouldSkipCoreCage(16));
        }
    }
}
