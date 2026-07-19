using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Monitor;
using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Tests.Monitor
{
    [TestClass]
    public class CpuSnapshotTests
    {
        private static List<SensorReading> Sample() => new()
        {
            new("Bus Speed", "Clock", 100f),
            new("Core #1", "Clock", 4200f),
            new("Core #2", "Clock", 3800f),
            new("Core #1 (SMU)", "Power", 4.5f),
            new("Core #2 (SMU)", "Power", 1.2f),
            new("Core #1 VID", "Voltage", 1.35f),
            new("Core #2 VID", "Voltage", 1.20f),
            new("Core (SVI2 TFN)", "Voltage", 1.40f),
            new("SoC (SVI2 TFN)", "Voltage", 1.10f),
            new("CPU Core #1", "Load", 50f),
            new("CPU Core #2", "Load", 30f),
            new("CPU Core #3", "Load", 10f),
            new("CPU Core #4", "Load", 20f),
            new("CPU Total", "Load", 26.25f),
            new("Package", "Power", 22.4f),
            new("Core (Tctl/Tdie)", "Temperature", 51.5f),
        };

        [TestMethod]
        public void Maps_Two_Cores_With_Clock_Power_Vid_Load()
        {
            var s = CpuSnapshot.BuildSnapshot(Sample(), "AMD Ryzen 5 5600G");
            Assert.AreEqual("AMD Ryzen 5 5600G", s.Name);
            Assert.AreEqual(2, s.Cores.Length);
            Assert.AreEqual(1, s.Cores[0].Index);
            Assert.AreEqual(4200f, s.Cores[0].ClockMhz);
            Assert.AreEqual(4.5f, s.Cores[0].PowerW);
            Assert.AreEqual(1.35f, s.Cores[0].Vid);
            Assert.AreEqual(40f, s.Cores[0].LoadPct);
            Assert.AreEqual(15f, s.Cores[1].LoadPct);
            Assert.AreEqual(1.20f, s.Cores[1].Vid);
        }

        [TestMethod]
        public void Maps_Package_Rails_And_Temp()
        {
            var s = CpuSnapshot.BuildSnapshot(Sample(), "");
            Assert.AreEqual(1.40f, s.Vcore);
            Assert.AreEqual(1.10f, s.SocV);
            Assert.AreEqual(22.4f, s.PackagePowerW);
            Assert.AreEqual(51.5f, s.TctlC);
        }

        [TestMethod]
        public void Cores_Are_Sorted_By_Index()
        {
            var s = CpuSnapshot.BuildSnapshot(Sample(), "");
            CollectionAssert.AreEqual(new[] { 1, 2 }, s.Cores.Select(c => c.Index).ToArray());
        }
    }
}
