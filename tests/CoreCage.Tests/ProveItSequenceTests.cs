using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App.Services;
using CoreCage.Core.Ledger;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    /// <summary>
    /// CRITICAL-1/2 review fix: "Prove it" must be an honest A/B (tweaks-off baseline, then
    /// tweaks-on), not two tweaks-on captures, and a single whole-stack bench must not be copied onto
    /// every per-tweak ledger row as if it proved each one individually.
    ///
    /// These tests exercise the pure orchestration (<see cref="EngineOptimizeService.RunProveItSequenceAsync"/>)
    /// and the ledger recording (<see cref="EngineOptimizeService.RecordWholeStackBenchmark"/>) directly,
    /// with fake bench/apply/revert delegates and a temp-file-backed <see cref="TweakLedger"/>. No real
    /// PresentMon capture and no OS mutation happens here.
    /// </summary>
    [TestClass]
    public class ProveItSequenceTests
    {
        private static FrametimeStats Stats(int frameCount)
        {
            var list = new List<double>();
            for (int i = 0; i < frameCount; i++) list.Add(10.0); // 100fps flat
            return FrametimeStats.FromFrametimes(list);
        }

        // ------------------------------------------------------------------
        // Orchestration: revert-first baseline when tweaks were already active
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task WasActive_RevertsFirst_BenchesOffThenOn_AndEndsWithTweaksActive()
        {
            bool modeActive = true; // Gaming Mode was ON when Prove It started
            var calls = new List<string>();
            var benchModeStates = new List<bool>();

            Task<FrametimeStats> Bench()
            {
                calls.Add("bench");
                benchModeStates.Add(modeActive);
                return Task.FromResult(Stats(10));
            }
            Task Apply() { calls.Add("apply"); modeActive = true; return Task.CompletedTask; }
            Task Revert() { calls.Add("revert"); modeActive = false; return Task.CompletedTask; }

            (FrametimeStats before, FrametimeStats after) =
                await EngineOptimizeService.RunProveItSequenceAsync(Bench, Apply, Revert, wasActive: true);

            CollectionAssert.AreEqual(new[] { "revert", "bench", "apply", "bench" }, calls,
                "must revert to a clean baseline BEFORE the first bench, then apply, then bench again.");
            CollectionAssert.AreEqual(new[] { false, true }, benchModeStates,
                "bench #1 must run with tweaks OFF (true baseline); bench #2 must run with tweaks ON.");
            Assert.IsTrue(modeActive, "Prove It must end with tweaks ON -- no post-revert.");
            Assert.AreEqual(10, before.FrameCount);
            Assert.AreEqual(10, after.FrameCount);
        }

        [TestMethod]
        public async Task NotActive_NeverCallsRevert_BenchesOffThenOn_AndEndsWithTweaksActive()
        {
            bool modeActive = false; // Gaming Mode was OFF when Prove It started
            var calls = new List<string>();
            var benchModeStates = new List<bool>();
            bool revertCalled = false;

            Task<FrametimeStats> Bench()
            {
                calls.Add("bench");
                benchModeStates.Add(modeActive);
                return Task.FromResult(Stats(10));
            }
            Task Apply() { calls.Add("apply"); modeActive = true; return Task.CompletedTask; }
            Task Revert() { revertCalled = true; modeActive = false; return Task.CompletedTask; }

            await EngineOptimizeService.RunProveItSequenceAsync(Bench, Apply, Revert, wasActive: false);

            Assert.IsFalse(revertCalled, "no pre-revert when tweaks weren't active to begin with -- same bench/apply/bench shape either way.");
            CollectionAssert.AreEqual(new[] { "bench", "apply", "bench" }, calls);
            CollectionAssert.AreEqual(new[] { false, true }, benchModeStates);
            Assert.IsTrue(modeActive, "must end with tweaks ON.");
        }

        // ------------------------------------------------------------------
        // Ledger recording: one whole-stack row, step rows untouched
        // ------------------------------------------------------------------

        [TestMethod]
        public void RecordWholeStackBenchmark_WritesExactlyOneRow_AndLeavesStepRowsUntouched()
        {
            string path = Path.Combine(Path.GetTempPath(), $"corecage-wholestack-test-{Guid.NewGuid()}.json");
            try
            {
                var ledger = new TweakLedger(path);
                ledger.Record(new LedgerEntry("gaming-pipeline", DateTimeOffset.UtcNow, true, null, null, null, null));
                ledger.Record(new LedgerEntry("eac-polish", DateTimeOffset.UtcNow, true, null, null, null, null));
                ledger.Record(new LedgerEntry("core-cage", DateTimeOffset.UtcNow, true, null, null, null, null));

                var before = Stats(150); // 100fps
                var after = Stats(150);

                EngineOptimizeService.RecordWholeStackBenchmark(ledger, before, after);

                Assert.AreEqual(4, ledger.Entries.Count, "the 3 step rows plus the new whole-stack row.");

                var measuredRows = ledger.Entries.Where(e => e.BaselineFps != null).ToList();
                Assert.AreEqual(1, measuredRows.Count, "exactly one row may carry measured numbers -- a single A/B cannot attribute per-tweak.");
                Assert.AreEqual("gaming-stack", measuredRows[0].TweakId);
                Assert.AreEqual(before.AvgFps, measuredRows[0].BaselineFps);
                Assert.AreEqual(after.AvgFps, measuredRows[0].AfterFps);

                foreach (var stepId in new[] { "gaming-pipeline", "eac-polish", "core-cage" })
                {
                    var row = ledger.Entries.Single(e => e.TweakId == stepId);
                    Assert.IsTrue(row.Active, $"{stepId} must keep its Active status.");
                    Assert.IsNull(row.BaselineFps, $"{stepId} must NOT get a copied delta from the whole-stack bench.");
                }
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
        }
    }
}
