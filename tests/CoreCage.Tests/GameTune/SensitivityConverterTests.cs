using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class SensitivityConverterTests
    {
        [TestMethod]
        public void Convert_SameYaw_ReturnsSameSens()
        {
            Assert.AreEqual(6.15, SensitivityConverter.Convert(6.15, 0.022, 0.022), 1e-9);
        }

        [TestMethod]
        public void Convert_HalfYaw_DoublesSens()
        {
            Assert.AreEqual(12.30, SensitivityConverter.Convert(6.15, 0.022, 0.011), 1e-9);
        }

        [TestMethod]
        public void Cm360_KnownValue()
        {
            var cm = SensitivityConverter.Cm360(6.15, 0.022, 800);
            Assert.AreEqual(8.45, cm, 0.05);
        }
    }
}
