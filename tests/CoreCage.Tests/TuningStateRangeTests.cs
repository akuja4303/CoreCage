using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class TuningStateRangeTests
    {
        [TestMethod]
        public void CoAll_Range_Is_0_To_Minus30()
        {
            var r = TuningState.Range(TuningParam.CoAll);
            Assert.AreEqual(-30, r.Min);
            Assert.AreEqual(0, r.Max);
            Assert.AreEqual(-20, r.Default);
        }

        [TestMethod]
        public void Clamp_Pins_Below_Min_Up_To_Min()
        {
            Assert.AreEqual(-30, TuningState.Clamp(TuningParam.CoAll, -50));
        }

        [TestMethod]
        public void Clamp_Pins_Above_Max_Down_To_Max()
        {
            Assert.AreEqual(95, TuningState.Clamp(TuningParam.Stapm, 120));
        }

        [TestMethod]
        public void Clamp_Passes_Through_In_Range()
        {
            Assert.AreEqual(100, TuningState.Clamp(TuningParam.FastPpt, 100));
        }
    }
}
