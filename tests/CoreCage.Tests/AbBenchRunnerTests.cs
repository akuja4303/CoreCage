using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Benchmark;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    /// <summary>
    /// AbBenchRunner is the "prove it" primitive: bench (baseline) → apply (the tweak) → bench (after),
    /// with an optional revert. Every delegate here is a fake — this class must never run a real
    /// PresentMon capture or mutate the OS in a test; that only happens through the real delegates the
    /// UI wires in production.
    /// </summary>
    [TestClass]
    public class AbBenchRunnerTests
    {
        private static FrametimeStats Stats(int frameCount) =>
            FrametimeStats.FromFrametimes(BuildFrametimes(frameCount));

        private static List<double> BuildFrametimes(int count)
        {
            var list = new List<double>();
            for (int i = 0; i < count; i++) list.Add(10.0); // 100fps flat
            return list;
        }

        [TestMethod]
        public async Task RunAsync_CallsInOrder_BenchThenApplyThenBench_NoRevertGiven()
        {
            var calls = new List<string>();
            var before = Stats(5);
            var after = Stats(50);
            int benchCall = 0;

            var runner = new AbBenchRunner(
                bench: () => { calls.Add("bench"); benchCall++; return Task.FromResult(benchCall == 1 ? before : after); },
                apply: () => { calls.Add("apply"); return Task.CompletedTask; });

            var (resultBefore, resultAfter) = await runner.RunAsync();

            CollectionAssert.AreEqual(new[] { "bench", "apply", "bench" }, calls);
            Assert.AreSame(before, resultBefore);
            Assert.AreSame(after, resultAfter);
        }

        [TestMethod]
        public async Task RunAsync_CallsRevert_WhenGiven_AfterTheSecondBench()
        {
            var calls = new List<string>();
            int benchCall = 0;

            var runner = new AbBenchRunner(
                bench: () => { calls.Add("bench"); benchCall++; return Task.FromResult(FrametimeStats.Empty); },
                apply: () => { calls.Add("apply"); return Task.CompletedTask; },
                revert: () => { calls.Add("revert"); return Task.CompletedTask; });

            await runner.RunAsync();

            CollectionAssert.AreEqual(new[] { "bench", "apply", "bench", "revert" }, calls);
        }

        [TestMethod]
        public async Task RunAsync_NoRevertGiven_NeverCallsAnything_AfterSecondBench()
        {
            var calls = new List<string>();

            var runner = new AbBenchRunner(
                bench: () => { calls.Add("bench"); return Task.FromResult(FrametimeStats.Empty); },
                apply: () => { calls.Add("apply"); return Task.CompletedTask; });

            await runner.RunAsync();

            Assert.AreEqual(3, calls.Count, "no fourth call when no revert delegate was given.");
        }

        [TestMethod]
        public void Constructor_NullBenchOrApply_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AbBenchRunner(null!, () => Task.CompletedTask));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AbBenchRunner(() => Task.FromResult(FrametimeStats.Empty), null!));
        }
    }
}
