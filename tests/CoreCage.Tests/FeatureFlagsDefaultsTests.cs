using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Regression guard for the council rank-1 safety fix. The two native hardware-WRITE flags must
    /// stay OFF by default so a fresh/shipped install never auto-applies an unvalidated GPU offset or a
    /// hard-freeze-capable CPU Curve-Optimizer to a stranger's machine. If someone flips a default back
    /// to ON, this test fails loudly. (Tests the CODE defaults via `new FeatureFlags()`, not the
    /// persisted features.json that FeatureFlags.Current loads.)
    /// </summary>
    [TestClass]
    public class FeatureFlagsDefaultsTests
    {
        [TestMethod]
        public void NativeWriteFlags_DefaultOff()
        {
            var f = new FeatureFlags();
            Assert.IsFalse(f.NativeGpuClockOffset, "GPU clock offset must default OFF (opt-in) — see council rank 1.");
            Assert.IsFalse(f.NativeCpuCurveOptimizer, "CPU Curve Optimizer must default OFF (supervised opt-in) — see council rank 1.");
        }

        [TestMethod]
        public void OffsetValues_AreTheDocumentedOptInValues()
        {
            var f = new FeatureFlags();
            // These are the values applied ONLY when the user opts in via the Advanced toggle.
            Assert.AreEqual(150, f.GpuCoreOffsetMhz, "Documented opt-in GPU offset is +150 MHz.");
            Assert.AreEqual(-10, f.CpuCurveOffset, "Documented opt-in CO is -10 (conservative).");
        }

        [TestMethod]
        public void SafeReversibleFeatures_StayOnByDefault()
        {
            var f = new FeatureFlags();
            Assert.IsTrue(f.MeasuredTimerResolution, "Measured timer resolution is safe + reversible → stays ON.");
            Assert.IsTrue(f.AutoApplyGameProfiles, "Auto game-profile apply is safe (no-op until profiles exist) → stays ON.");
        }
    }
}
