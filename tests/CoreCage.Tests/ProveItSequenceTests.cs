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
        // CRITICAL review fix: Apply()/Revert() failures must abort honestly, not be swallowed
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task ApplyFails_ShortCircuits_NoSecondBench_AndNoLedgerRowRecorded()
        {
            // Mirrors the production Apply() closure: when the underlying ModeResult.Success is false
            // it must throw (naming the failed step) instead of silently returning as if it succeeded.
            var calls = new List<string>();
            int benchCall = 0;
            Task<FrametimeStats> Bench() { benchCall++; calls.Add("bench"); return Task.FromResult(Stats(10)); }
            Task Apply() { calls.Add("apply"); throw new InvalidOperationException("apply: Gaming Mode apply failed (mock)."); }
            Task Revert() { calls.Add("revert"); return Task.CompletedTask; }

            string path = Path.Combine(Path.GetTempPath(), $"corecage-provefail-apply-{Guid.NewGuid()}.json");
            try
            {
                var ledger = new TweakLedger(path);

                Exception? caught = null;
                try
                {
                    (FrametimeStats before, FrametimeStats after) =
                        await EngineOptimizeService.RunProveItSequenceAsync(Bench, Apply, Revert, wasActive: false);
                    // Production code only ever calls RecordWholeStackBenchmark after the sequence
                    // returns successfully -- this line must be unreachable when apply throws.
                    EngineOptimizeService.RecordWholeStackBenchmark(ledger, before, after, active: true);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "apply failure must propagate out of the sequence, not be swallowed.");
                StringAssert.Contains(caught!.Message, "apply");
                CollectionAssert.AreEqual(new[] { "bench", "apply" }, calls,
                    "must short-circuit after apply fails -- the second bench must never run.");
                Assert.AreEqual(1, benchCall, "only the baseline bench may run before an apply failure aborts the sequence.");
                Assert.AreEqual(0, ledger.Entries.Count, "no ledger row may be written when the sequence aborted.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
        }

        [TestMethod]
        public async Task RevertFails_ShortCircuits_NoBenchAtAll_AndNoLedgerRowRecorded()
        {
            // Mirrors the production Revert() closure (pre-revert path, wasActive=true): a failed revert
            // must abort BEFORE the first ("clean baseline") bench even runs -- otherwise bench #1 would
            // capture a tweaks-still-on machine while being reported as the tweaks-off baseline.
            var calls = new List<string>();
            Task<FrametimeStats> Bench() { calls.Add("bench"); return Task.FromResult(Stats(10)); }
            Task Apply() { calls.Add("apply"); return Task.CompletedTask; }
            Task Revert() { calls.Add("revert"); throw new InvalidOperationException("revert: Gaming Mode revert failed (mock)."); }

            string path = Path.Combine(Path.GetTempPath(), $"corecage-provefail-revert-{Guid.NewGuid()}.json");
            try
            {
                var ledger = new TweakLedger(path);

                Exception? caught = null;
                try
                {
                    (FrametimeStats before, FrametimeStats after) =
                        await EngineOptimizeService.RunProveItSequenceAsync(Bench, Apply, Revert, wasActive: true);
                    EngineOptimizeService.RecordWholeStackBenchmark(ledger, before, after, active: true);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "revert failure must propagate out of the sequence, not be swallowed.");
                StringAssert.Contains(caught!.Message, "revert");
                CollectionAssert.AreEqual(new[] { "revert" }, calls,
                    "must short-circuit before the first bench when the pre-revert fails.");
                Assert.AreEqual(0, ledger.Entries.Count, "no ledger row may be written when the sequence aborted.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
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

                EngineOptimizeService.RecordWholeStackBenchmark(ledger, before, after, active: true);

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

        [TestMethod]
        public void RecordWholeStackBenchmark_HonorsActiveParameter_NotHardcodedTrue()
        {
            // CRITICAL review fix: the whole-stack row's Active used to be hardcoded `true` regardless
            // of whether Gaming Mode actually ended up active. It must reflect the real post-sequence
            // IModeModule.IsActive read the caller passes in.
            string path = Path.Combine(Path.GetTempPath(), $"corecage-wholestack-inactive-test-{Guid.NewGuid()}.json");
            try
            {
                var ledger = new TweakLedger(path);
                var before = Stats(150);
                var after = Stats(150);

                EngineOptimizeService.RecordWholeStackBenchmark(ledger, before, after, active: false);

                var row = ledger.Entries.Single(e => e.TweakId == "gaming-stack");
                Assert.IsFalse(row.Active, "must record the real post-sequence active state, not a hardcoded true.");
                Assert.AreEqual(before.AvgFps, row.BaselineFps, "measured numbers must still be recorded even when inactive.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
        }
    }
}
