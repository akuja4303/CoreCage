using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class TuningOutcomeTests
    {
        [TestMethod]
        public void Empty_HasNoWarnings_SummaryIsNull()
        {
            var o = new TuningOutcome();
            Assert.IsFalse(o.HasWarnings);
            Assert.IsFalse(o.HasErrors);
            Assert.IsNull(o.Summary());
        }

        [TestMethod]
        public void Add_Warning_SurfacesInSummary()
        {
            var o = new TuningOutcome();
            o.Add("CPU Curve Optimizer", "CO -10 did NOT apply.");

            Assert.IsTrue(o.HasWarnings);
            Assert.IsFalse(o.HasErrors);
            StringAssert.Contains(o.Summary(), "did NOT apply");
        }

        [TestMethod]
        public void HasErrors_TrueOnlyForErrorSeverity()
        {
            var o = new TuningOutcome();
            o.Add("CPU power", "param skipped", TuningSeverity.Warning);
            Assert.IsFalse(o.HasErrors);

            o.Add("CPU power limits", "write FAILED", TuningSeverity.Error);
            Assert.IsTrue(o.HasErrors);
        }

        [TestMethod]
        public void Summary_OrdersErrorsBeforeWarnings()
        {
            var o = new TuningOutcome();
            o.Add("CPU power", "warn-one", TuningSeverity.Warning);
            o.Add("CPU power limits", "error-two", TuningSeverity.Error);

            string summary = o.Summary()!;
            Assert.IsTrue(summary.IndexOf("error-two") < summary.IndexOf("warn-one"),
                "Errors should be listed before warnings in the one-line summary.");
            StringAssert.Contains(summary, " · "); // both joined
        }

        // ── ClassifyCurveOptimizer matrix ──────────────────────────────────────

        [TestMethod]
        public void Classify_OffsetZero_NoWarning()
        {
            Assert.IsNull(PerformanceTuner.ClassifyCurveOptimizer(flagEnabled: true, toolAvailable: true, offset: 0, ok: false, verified: false));
        }

        [TestMethod]
        public void Classify_FlagDisabled_NoWarning()
        {
            // Off by design (the new safe default) — must NOT nag the user.
            Assert.IsNull(PerformanceTuner.ClassifyCurveOptimizer(flagEnabled: false, toolAvailable: false, offset: -10, ok: false, verified: false));
        }

        [TestMethod]
        public void Classify_EnabledButToolMissing_Warns()
        {
            var w = PerformanceTuner.ClassifyCurveOptimizer(flagEnabled: true, toolAvailable: false, offset: -10, ok: false, verified: false);
            Assert.IsNotNull(w);
            Assert.AreEqual(TuningSeverity.Warning, w!.Severity);
            StringAssert.Contains(w.Message, "not found");
        }

        [TestMethod]
        public void Classify_EnabledButNotVerified_Warns_TheSilentNoOp()
        {
            // The exact council case: CLI ran (ok) but the SMU silently rejected the write (not verified).
            var w = PerformanceTuner.ClassifyCurveOptimizer(flagEnabled: true, toolAvailable: true, offset: -10, ok: true, verified: false);
            Assert.IsNotNull(w);
            StringAssert.Contains(w!.Message, "did NOT apply");
        }

        [TestMethod]
        public void Classify_EnabledOkAndVerified_NoWarning()
        {
            Assert.IsNull(PerformanceTuner.ClassifyCurveOptimizer(flagEnabled: true, toolAvailable: true, offset: -10, ok: true, verified: true));
        }
    }
}
