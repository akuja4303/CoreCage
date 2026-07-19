using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Thermal;

namespace CoreCage.Tests.Thermal
{
    [TestClass]
    public class ThermalGuardPolicyTests
    {
        // high=88, release=80
        [TestMethod] public void Cold_NotEngaged_None()
            => Assert.AreEqual(ThermalAction.None, ThermalGuardPolicy.Decide(65, 88, 80, false));

        [TestMethod] public void HitsHigh_NotEngaged_Engage()
            => Assert.AreEqual(ThermalAction.Engage, ThermalGuardPolicy.Decide(90, 88, 80, false));

        [TestMethod] public void Engaged_StillHot_Sustain()
            => Assert.AreEqual(ThermalAction.Sustain, ThermalGuardPolicy.Decide(84, 88, 80, true));

        [TestMethod] public void Engaged_CooledToRelease_Release()
            => Assert.AreEqual(ThermalAction.Release, ThermalGuardPolicy.Decide(80, 88, 80, true));

        [TestMethod] public void Hysteresis_BetweenReleaseAndHigh_NotEngaged_StaysNone()
            => Assert.AreEqual(ThermalAction.None, ThermalGuardPolicy.Decide(85, 88, 80, false));

        [TestMethod] public void BadReading_DoesNotFlip()
        {
            Assert.AreEqual(ThermalAction.None, ThermalGuardPolicy.Decide(0, 88, 80, false));
            Assert.AreEqual(ThermalAction.Sustain, ThermalGuardPolicy.Decide(-5, 88, 80, true));
        }
    }
}
