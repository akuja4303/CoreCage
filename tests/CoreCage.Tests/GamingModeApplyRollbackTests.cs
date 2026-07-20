using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;
using CoreCage.Core.Ledger;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Pre-publish cleanup (partial-apply honesty): GamingMode.ApplyAsync used to catch a mid-pipeline
    /// exception, report ModeResult(false, ...), and stop -- WITHOUT ever calling SaveState(true) or
    /// SaveState(false). Whatever steps ran before the throw (e.g. Gaming Mode++'s registry/QoS writes)
    /// stayed applied to the real system while the persisted IsActive flag kept whatever value it had
    /// before Apply started -- so a fresh app launch could show "Gaming Mode: OFF" while the rig was
    /// still partially tweaked. Mirrors RevertAsync's existing fallback: on failure, fall back to
    /// RestoreEverything.RestoreAll() (the Big Red Button) so the system and the persisted flag both land
    /// in a single honest, fully-off state instead of an inconsistent partial one. Drives ApplyAsync
    /// through the fully-faked internal test constructor so no OS mutation or real ledger/state file is
    /// ever touched -- only the sequencing/recording logic here is under test.
    /// </summary>
    [TestClass]
    public class GamingModeApplyRollbackTests
    {
        private string _statePath = "";
        private string _ledgerPath = "";
        private bool _originalCoreCageEnabled;

        [TestInitialize]
        public void Setup()
        {
            _statePath = Path.Combine(Path.GetTempPath(), $"corecage-apply-rollback-state-{Guid.NewGuid()}.json");
            _ledgerPath = Path.Combine(Path.GetTempPath(), $"corecage-apply-rollback-ledger-{Guid.NewGuid()}.json");
            _originalCoreCageEnabled = FeatureFlags.Current.CoreCageEnabled;
            FeatureFlags.Current.CoreCageEnabled = false;
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { /* best-effort */ }
            try { if (File.Exists(_ledgerPath)) File.Delete(_ledgerPath); } catch { /* best-effort */ }
            FeatureFlags.Current.CoreCageEnabled = _originalCoreCageEnabled;
        }

        private GamingMode NewModeWithThrowingPolishStep(TweakLedger ledger, Func<RestoreSummary> restoreEverything) =>
            new GamingMode(
                _statePath,
                releaseCoreCage: () => 0,
                restorePolish: _ => 0,
                restoreGamingModePlusPlus: () => { },
                restoreCoreUnpark: () => false,
                gamingProcessList: () => Array.Empty<string>(),
                restoreEverything: restoreEverything,
                applyGamingModePlusPlus: () => { },
                // EAC-safe polish is the second pipeline step -- throwing here proves "gaming-pipeline"
                // already recorded a ledger row (from the successful first step) by the time the rollback
                // fires, so the rollback's ledger-deactivation has something real to undo.
                applyPolish: _ => throw new InvalidOperationException("polish boom"),
                applyCoreUnpark: () => { },
                applyCoreCage: () => 1,
                ledger: ledger);

        [TestMethod]
        public async Task ApplyAsync_WhenAStepThrows_FallsBackToRestoreEverything_AndPersistsInactiveState()
        {
            var ledger = new TweakLedger(_ledgerPath);
            bool restoreEverythingCalled = false;
            var mode = NewModeWithThrowingPolishStep(ledger, () =>
            {
                restoreEverythingCalled = true;
                return new RestoreSummary();
            });

            ModeResult result = await mode.ApplyAsync();

            Assert.IsFalse(result.Success, "Apply itself did not succeed -- Gaming Mode never fully turned on.");
            Assert.IsTrue(restoreEverythingCalled,
                "A mid-pipeline apply failure must fall back to the Big Red Button, same as RevertAsync's fallback, " +
                "so real tweaks applied before the throw don't linger un-reverted.");
            Assert.IsFalse(mode.IsActive,
                "The persisted flag must land on OFF (matching the now-fully-reverted real state) instead of " +
                "silently keeping whatever value it had before Apply started.");
            Assert.IsTrue(result.Steps.Any(s => s.Contains("FAILED")), "Steps should record the failure honestly.");
            Assert.IsTrue(result.Steps.Any(s => s.Contains("RestoreEverything")), "Steps should record the fallback ran.");
        }

        [TestMethod]
        public async Task ApplyAsync_WhenAStepThrows_DeactivatesLedgerEntriesRecordedBeforeTheFailure()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewModeWithThrowingPolishStep(ledger, () => new RestoreSummary());

            await mode.ApplyAsync();

            // "gaming-pipeline" is recorded (Active=true) by the first pipeline step, which succeeds
            // before the throwing "eac-polish" step -- the rollback must not leave that row claiming an
            // active tweak the Big Red Button just reverted.
            var gamingPipelineEntry = ledger.Entries.SingleOrDefault(e => e.TweakId == "gaming-pipeline");
            Assert.IsNotNull(gamingPipelineEntry, "sanity: the first step should have recorded a row before the throw.");
            Assert.IsFalse(gamingPipelineEntry!.Active, "the rollback must deactivate rows recorded before the failure.");
        }

        [TestMethod]
        public async Task ApplyAsync_WhenFallbackRestoreEverythingAlsoThrows_ReturnsFailure_NeverThrows()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewModeWithThrowingPolishStep(ledger, () => throw new InvalidOperationException("restore-everything boom too"));

            ModeResult? result = null;
            Exception? thrown = null;
            try { result = await mode.ApplyAsync(); }
            catch (Exception ex) { thrown = ex; }

            Assert.IsNull(thrown, "Even a fallback failure must never throw out of ApplyAsync.");
            Assert.IsNotNull(result);
            Assert.IsFalse(result!.Success);
        }
    }
}
