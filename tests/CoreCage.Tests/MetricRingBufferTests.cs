using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    [TestClass]
    public class MetricRingBufferTests
    {
        [TestMethod]
        public void Add_BelowCapacity_GrowsCount_SnapshotIsChronological()
        {
            var buf = new MetricRingBuffer(5);
            buf.Add(1);
            buf.Add(2);
            buf.Add(3);

            Assert.AreEqual(5, buf.Capacity);
            Assert.AreEqual(3, buf.Count);
            CollectionAssert.AreEqual(new[] { 1d, 2d, 3d }, buf.Snapshot());
            Assert.AreEqual(3d, buf.Latest);
        }

        [TestMethod]
        public void Add_BeyondCapacity_Wraps_DropsOldest_StaysChronological()
        {
            var buf = new MetricRingBuffer(3);
            buf.Add(1);
            buf.Add(2);
            buf.Add(3);
            buf.Add(4);
            buf.Add(5);

            Assert.AreEqual(3, buf.Capacity);
            Assert.AreEqual(3, buf.Count);
            CollectionAssert.AreEqual(new[] { 3d, 4d, 5d }, buf.Snapshot());
            Assert.AreEqual(5d, buf.Latest);
        }

        [TestMethod]
        public void Add_ExactlyAtCapacity_KeepsAll()
        {
            var buf = new MetricRingBuffer(3);
            buf.Add(10);
            buf.Add(20);
            buf.Add(30);

            Assert.AreEqual(3, buf.Count);
            CollectionAssert.AreEqual(new[] { 10d, 20d, 30d }, buf.Snapshot());
            Assert.AreEqual(30d, buf.Latest);
        }

        [TestMethod]
        public void Aggregates_OverContents_NonWrapped()
        {
            var buf = new MetricRingBuffer(5);
            buf.Add(2);
            buf.Add(8);
            buf.Add(5);

            Assert.AreEqual(5d, buf.Average(), 1e-9);
            Assert.AreEqual(8d, buf.Max());
            Assert.AreEqual(2d, buf.Min());
        }

        [TestMethod]
        public void Aggregates_OverContents_AfterWrap_OnlyConsiderRetainedSamples()
        {
            var buf = new MetricRingBuffer(3);
            buf.Add(1);   // dropped
            buf.Add(2);   // dropped
            buf.Add(10);
            buf.Add(20);
            buf.Add(30);

            // Retained = {10, 20, 30}
            Assert.AreEqual(20d, buf.Average(), 1e-9);
            Assert.AreEqual(30d, buf.Max());
            Assert.AreEqual(10d, buf.Min());
        }

        [TestMethod]
        public void EmptyBuffer_HasZeroCount_AggregatesAreZero_LatestIsNaN()
        {
            var buf = new MetricRingBuffer(4);

            Assert.AreEqual(0, buf.Count);
            Assert.AreEqual(0, buf.Snapshot().Length);
            Assert.AreEqual(0d, buf.Average());
            Assert.AreEqual(0d, buf.Max());
            Assert.AreEqual(0d, buf.Min());
            Assert.IsTrue(double.IsNaN(buf.Latest), "Latest on an empty buffer should be NaN.");
        }

        [TestMethod]
        public void Snapshot_ReturnsIndependentCopy()
        {
            var buf = new MetricRingBuffer(3);
            buf.Add(1);
            buf.Add(2);

            var first = buf.Snapshot();
            buf.Add(3);
            var second = buf.Snapshot();

            CollectionAssert.AreEqual(new[] { 1d, 2d }, first);
            CollectionAssert.AreEqual(new[] { 1d, 2d, 3d }, second);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Ctor_CapacityZero_Throws()
        {
            _ = new MetricRingBuffer(0);
        }

        [TestMethod]
        public void Ctor_CapacityNegative_Throws_ArgumentException()
        {
            // ArgumentOutOfRangeException derives from ArgumentException; assert the broader contract.
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MetricRingBuffer(-5));
        }

        // ── TelemetryHub ──────────────────────────────────────────────────────

        [TestMethod]
        public void Hub_Push_CreatesSeries_GetReturnsIt()
        {
            var hub = new TelemetryHub();
            Assert.IsNull(hub.Get(TelemetryHub.Cpu));

            hub.Push(TelemetryHub.Cpu, 42d);

            var cpu = hub.Get(TelemetryHub.Cpu);
            Assert.IsNotNull(cpu);
            Assert.AreEqual(1, cpu!.Count);
            Assert.AreEqual(42d, cpu.Latest);
            Assert.AreEqual(TelemetryHub.DefaultCapacity, cpu.Capacity);
        }

        [TestMethod]
        public void Hub_MultipleSeries_AreIndependent()
        {
            var hub = new TelemetryHub();
            hub.Push(TelemetryHub.Cpu, 10d);
            hub.Push(TelemetryHub.Cpu, 11d);
            hub.Push(TelemetryHub.Gpu, 90d);

            var cpu = hub.Get(TelemetryHub.Cpu);
            var gpu = hub.Get(TelemetryHub.Gpu);

            Assert.IsNotNull(cpu);
            Assert.IsNotNull(gpu);
            Assert.AreEqual(2, cpu!.Count);
            Assert.AreEqual(1, gpu!.Count);
            Assert.AreEqual(11d, cpu.Latest);
            Assert.AreEqual(90d, gpu.Latest);
            Assert.IsTrue(hub.Series.ContainsKey(TelemetryHub.Cpu));
            Assert.IsTrue(hub.Series.ContainsKey(TelemetryHub.Gpu));
            Assert.AreEqual(2, hub.Series.Count);
        }

        [TestMethod]
        public void Hub_Get_UnknownSeries_ReturnsNull()
        {
            var hub = new TelemetryHub();
            Assert.IsNull(hub.Get("does-not-exist"));
        }

        [TestMethod]
        public void Hub_Instance_IsSingleton()
        {
            Assert.AreSame(TelemetryHub.Instance, TelemetryHub.Instance);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Hub_Push_BlankSeries_Throws()
        {
            new TelemetryHub().Push("  ", 1d);
        }
    }
}
