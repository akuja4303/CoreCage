using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;
using CoreCage.Core.Caging;
using CoreCage.Core.Ledger;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Task 6: GamingMode records a Tweak Ledger row per pipeline step it applies, and deactivates them
    /// on revert. Drives ApplyAsync/RevertAsync through the fully-faked internal test constructor (both
    /// apply-side and revert-side delegates faked, plus an injected in-memory ledger) so this never runs
    /// the real pipeline or touches the OS/real ledger file — only the sequencing/recording logic here
    /// is under test.
    /// </summary>
    [TestClass]
    public class GamingModeLedgerTests
    {
        private string _statePath = "";
        private string _ledgerPath = "";
        private bool _originalCoreCageEnabled;

        [TestInitialize]
        public void Setup()
        {
            _statePath = Path.Combine(Path.GetTempPath(), $"corecage-ledger-mode-state-{Guid.NewGuid()}.json");
            _ledgerPath = Path.Combine(Path.GetTempPath(), $"corecage-ledger-mode-ledger-{Guid.NewGuid()}.json");
            _originalCoreCageEnabled = FeatureFlags.Current.CoreCageEnabled;
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { }
            try { if (File.Exists(_ledgerPath)) File.Delete(_ledgerPath); } catch { }
            FeatureFlags.Current.CoreCageEnabled = _originalCoreCageEnabled;
        }

        private GamingMode NewFullyFakedMode(TweakLedger ledger, bool coreCageEnabled = false)
        {
            FeatureFlags.Current.CoreCageEnabled = coreCageEnabled;
            return new GamingMode(
                _statePath,
                releaseCoreCage: () => 0,
                restorePolish: _ => 0,
                restoreGamingModePlusPlus: () => { },
                restoreCoreUnpark: () => false,
                gamingProcessList: () => Array.Empty<string>(),
                restoreEverything: () => new RestoreSummary(),
                applyGamingModePlusPlus: () => { },
                applyPolish: _ => 0,
                applyCoreUnpark: () => { },
                applyCoreCage: () => 1,
                ledger: ledger);
        }

        [TestMethod]
        public async Task ApplyAsync_Records_LedgerEntries_ForEachPipelineStep_ActiveTrue_NoBenchmarkYet()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewFullyFakedMode(ledger, coreCageEnabled: false);

            ModeResult result = await mode.ApplyAsync();

            Assert.IsTrue(result.Success);
            var ids = ledger.Entries.Select(e => e.TweakId).ToList();
            CollectionAssert.Contains(ids, "gaming-pipeline");
            CollectionAssert.Contains(ids, "eac-polish");
            CollectionAssert.Contains(ids, "core-unpark");
            CollectionAssert.DoesNotContain(ids, "core-cage", "Core Cage disabled -> no core-cage row.");

            foreach (var e in ledger.Entries)
            {
                Assert.IsTrue(e.Active, $"{e.TweakId} should be recorded Active=true on Apply.");
                Assert.IsNull(e.BaselineFps, $"{e.TweakId} should have no benchmark numbers yet.");
                Assert.IsNull(e.AfterFps, $"{e.TweakId} should have no benchmark numbers yet.");
            }
        }

        [TestMethod]
        public async Task ApplyAsync_Records_CoreCageEntry_WhenCoreCageEnabled()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewFullyFakedMode(ledger, coreCageEnabled: true);

            await mode.ApplyAsync();

            var ids = ledger.Entries.Select(e => e.TweakId).ToList();
            CollectionAssert.Contains(ids, "core-cage");
            Assert.IsTrue(ledger.Entries.Single(e => e.TweakId == "core-cage").Active);
        }

        [TestMethod]
        public async Task RevertAsync_Deactivates_LedgerEntries_AppliedByApply()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewFullyFakedMode(ledger, coreCageEnabled: true);
            await mode.ApplyAsync();
            Assert.IsTrue(ledger.Entries.All(e => e.Active), "sanity: all active right after Apply.");
            // The faked applyCoreCage delegate returns a caged count directly without going through the
            // real ApplyCoreCageReal (which is what normally sets _lastCagePlan) -- simulate "Apply
            // actually caged something" the same way GamingModeRevertReleaseTests does, so
            // RevertAsync's real release-gating decision (hadCagePlan) exercises the Core Cage path.
            mode.LastCagePlanForTests = new CagePlan(0, 0, new List<int> { 111 });

            ModeResult result = await mode.RevertAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(ledger.Entries.Count > 0);
            foreach (var e in ledger.Entries)
                Assert.IsFalse(e.Active, $"{e.TweakId} should be deactivated after Revert.");
        }

        [TestMethod]
        public async Task RevertAsync_WithNoPriorApply_NeverThrows_AndLeavesLedgerEmpty()
        {
            var ledger = new TweakLedger(_ledgerPath);
            var mode = NewFullyFakedMode(ledger, coreCageEnabled: false);

            ModeResult result = await mode.RevertAsync();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, ledger.Entries.Count);
        }
    }
}
