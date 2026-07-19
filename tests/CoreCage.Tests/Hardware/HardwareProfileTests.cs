using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Hardware;

namespace CoreCage.Tests.Hardware
{
    [TestClass]
    public class HardwareProfileTests
    {
        [TestMethod]
        public void ClassifyCpu_Amd()
        {
            Assert.AreEqual(CpuVendor.Amd, HardwareProfile.ClassifyCpu("AMD Ryzen 5 5600G with Radeon Graphics"));
            Assert.AreEqual(CpuVendor.Amd, HardwareProfile.ClassifyCpu("AMD Ryzen 9 5950X"));
        }

        [TestMethod]
        public void ClassifyCpu_Intel()
        {
            Assert.AreEqual(CpuVendor.Intel, HardwareProfile.ClassifyCpu("Intel(R) Core(TM) i7-12700K"));
            Assert.AreEqual(CpuVendor.Intel, HardwareProfile.ClassifyCpu("Intel Xeon E5-2680"));
        }

        [TestMethod]
        public void ClassifyCpu_UnknownOrEmpty()
        {
            Assert.AreEqual(CpuVendor.Unknown, HardwareProfile.ClassifyCpu(null));
            Assert.AreEqual(CpuVendor.Unknown, HardwareProfile.ClassifyCpu(""));
            Assert.AreEqual(CpuVendor.Unknown, HardwareProfile.ClassifyCpu("Some Future CPU"));
        }

        [TestMethod]
        public void ClassifyGpu_Nvidia()
        {
            Assert.AreEqual(GpuVendor.Nvidia, HardwareProfile.ClassifyGpu("NVIDIA GeForce RTX 3060"));
            Assert.AreEqual(GpuVendor.Nvidia, HardwareProfile.ClassifyGpu("GeForce GTX 1660 SUPER"));
        }

        [TestMethod]
        public void ClassifyGpu_AmdAndIntel()
        {
            Assert.AreEqual(GpuVendor.Amd, HardwareProfile.ClassifyGpu("AMD Radeon RX 6700 XT"));
            Assert.AreEqual(GpuVendor.Intel, HardwareProfile.ClassifyGpu("Intel Arc A770"));
            Assert.AreEqual(GpuVendor.Intel, HardwareProfile.ClassifyGpu("Intel(R) UHD Graphics 770"));
        }

        [TestMethod]
        public void ClassifyGpu_UnknownOrEmpty()
        {
            Assert.AreEqual(GpuVendor.Unknown, HardwareProfile.ClassifyGpu(null));
            Assert.AreEqual(GpuVendor.Unknown, HardwareProfile.ClassifyGpu(""));
        }

        [TestMethod]
        public void PickGpu_ApuPlusDiscrete_PrefersNvidia()
        {
            // The exact 5600G+3060 case: integrated Radeon must NOT mask the discrete RTX.
            var (name, vendor) = HardwareProfile.PickGpu(new[]
            {
                "AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 3060"
            });
            Assert.AreEqual(GpuVendor.Nvidia, vendor);
            StringAssert.Contains(name, "RTX 3060");
        }

        [TestMethod]
        public void PickGpu_NvidiaFirst_StillNvidia()
        {
            var (_, vendor) = HardwareProfile.PickGpu(new[] { "NVIDIA GeForce RTX 4090", "Intel UHD Graphics" });
            Assert.AreEqual(GpuVendor.Nvidia, vendor);
        }

        [TestMethod]
        public void PickGpu_NoNvidia_KeepsFirstKnown()
        {
            var (_, vendor) = HardwareProfile.PickGpu(new[] { "AMD Radeon RX 6700 XT" });
            Assert.AreEqual(GpuVendor.Amd, vendor);
        }

        [TestMethod]
        public void PickGpu_Empty_Unknown()
        {
            var (name, vendor) = HardwareProfile.PickGpu(new string[0]);
            Assert.AreEqual(GpuVendor.Unknown, vendor);
            Assert.AreEqual("", name);
        }
    }
}
