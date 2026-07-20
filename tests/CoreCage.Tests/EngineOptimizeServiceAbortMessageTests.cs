using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App.Services;

namespace CoreCage.Tests
{
    /// <summary>
    /// Pre-publish cleanup (partial-apply honesty): the "Prove it" apply-failure message used to flatly
    /// assert "Gaming Mode is currently OFF" -- an unverified guess, since GamingMode.ApplyAsync catches
    /// internally and reports Success=false without guaranteeing every tweak it already applied got
    /// reverted. Hedged to mirror the revert-branch wording, which already avoided that overclaim. Pure
    /// string-builder -- no PresentMon/ModeRegistry dependency -- unit-tested directly.
    /// </summary>
    [TestClass]
    public class EngineOptimizeServiceAbortMessageTests
    {
        [TestMethod]
        public void BuildAbortMessage_ApplyStep_HedgesInsteadOfAssertingOff()
        {
            string message = EngineOptimizeService.BuildAbortMessage("apply", "boom");

            StringAssert.Contains(message, "apply failed: boom");
            StringAssert.Contains(message, "may be partially applied");
            StringAssert.Contains(message, "Restore Everything");
            Assert.IsFalse(message.Contains("is currently OFF"),
                "must not flatly assert OFF -- ApplyAsync's own catch never guarantees a full revert.");
        }

        [TestMethod]
        public void BuildAbortMessage_RevertStep_KeepsExistingHedgeWording()
        {
            string message = EngineOptimizeService.BuildAbortMessage("revert", "kaboom");

            StringAssert.Contains(message, "revert failed: kaboom");
            StringAssert.Contains(message, "may still be partially active");
        }
    }
}
