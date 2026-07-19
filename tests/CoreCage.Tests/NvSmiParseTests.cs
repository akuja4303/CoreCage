using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class NvSmiParseTests
    {
        [TestMethod]
        public void Parses_Min_Current_Max_From_Csv_Row()
        {
            var ok = TuningState.ParseNvSmiPowerLimits("100.00, 170.00, 170.00", out int min, out int cur, out int max);
            Assert.IsTrue(ok);
            Assert.AreEqual(100, min);
            Assert.AreEqual(170, cur);
            Assert.AreEqual(170, max);
        }

        [TestMethod]
        public void Returns_False_On_Garbage()
        {
            var ok = TuningState.ParseNvSmiPowerLimits("N/A", out int min, out int cur, out int max);
            Assert.IsFalse(ok);
        }
    }
}
