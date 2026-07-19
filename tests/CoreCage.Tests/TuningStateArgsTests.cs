using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class TuningStateArgsTests
    {
        [TestMethod]
        public void ValidatedDefaults_Is_Gaming_Preset()
        {
            var v = TuningState.ValidatedDefaults();
            Assert.AreEqual(-20, v.CoAll);
            Assert.AreEqual(95, v.StapmW);
            Assert.AreEqual(105, v.FastW);
            Assert.AreEqual(95, v.SlowW);
            Assert.AreEqual(75, v.TdcA);
            Assert.AreEqual(110, v.EdcA);
            Assert.AreEqual(90, v.TctlC);
        }

        [TestMethod]
        public void BuildRyzenAdjArgs_Gaming_Defaults_Exact_Format()
        {
            var v = TuningState.ValidatedDefaults();
            Assert.AreEqual(
                "--stapm-limit=95000 --fast-limit=105000 --slow-limit=95000 --tctl-temp=90 " +
                "--set-coall=1048556 --vrm-current=75000 --vrmmax-current=110000",
                TuningState.BuildRyzenAdjArgs(v));
        }

        [TestMethod]
        public void BuildRyzenAdjArgs_Omits_Zero_Co_And_Zero_Currents()
        {
            var v = new CpuTuningValues { StapmW = 65, FastW = 88, SlowW = 65, TctlC = 85, CoAll = 0, TdcA = 0, EdcA = 0 };
            Assert.AreEqual(
                "--stapm-limit=65000 --fast-limit=88000 --slow-limit=65000 --tctl-temp=85",
                TuningState.BuildRyzenAdjArgs(v));
        }

        [TestMethod]
        public void BuildRyzenAdjArgs_Clamps_Out_Of_Range_Inputs()
        {
            var v = new CpuTuningValues { StapmW = 999, FastW = 999, SlowW = 999, TctlC = 999, CoAll = -999, TdcA = 999, EdcA = 999 };
            Assert.AreEqual(
                "--stapm-limit=95000 --fast-limit=105000 --slow-limit=95000 --tctl-temp=90 " +
                "--set-coall=1048546 --vrm-current=80000 --vrmmax-current=120000",
                TuningState.BuildRyzenAdjArgs(v));
        }
    }
}
