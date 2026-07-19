using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class SmuCliStateTests
    {
        [TestMethod]
        public void ClampOffset_Bounds_To_Safe_Band()
        {
            Assert.AreEqual(-30, SmuCliState.ClampOffset(-999));
            Assert.AreEqual(30, SmuCliState.ClampOffset(999));
            Assert.AreEqual(-15, SmuCliState.ClampOffset(-15));
            Assert.AreEqual(0, SmuCliState.ClampOffset(0));
        }

        [TestMethod]
        public void BuildOffsetArgs_Formats_Per_Core_List()
        {
            string args = SmuCliState.BuildOffsetArgs(new[] { -10, -15, -20, 0, -5, -25 });
            Assert.AreEqual("--offset 0:-10,1:-15,2:-20,3:0,4:-5,5:-25", args);
        }

        [TestMethod]
        public void BuildOffsetArgs_Clamps_Each_Core()
        {
            string args = SmuCliState.BuildOffsetArgs(new[] { -999, 999 });
            Assert.AreEqual("--offset 0:-30,1:30", args);
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentException))]
        public void BuildOffsetArgs_Rejects_Empty_List()
        {
            SmuCliState.BuildOffsetArgs(new int[0]);
        }

        [TestMethod]
        public void UniformOffsets_Fills_All_Cores()
        {
            var u = SmuCliState.UniformOffsets(-20, 6);
            Assert.AreEqual(6, u.Count);
            CollectionAssert.AreEqual(new[] { -20, -20, -20, -20, -20, -20 }, (System.Collections.ICollection)u);
        }

        [TestMethod]
        public void ParseTerseOffsets_Parses_Comma_Values_In_Core_Order()
        {
            // ryzen-smu-cli --get-offsets-terse format
            bool ok = SmuCliState.ParseTerseOffsets("-15,0,2,-20,-10,-25\n", 6, out int[] offsets);
            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { -15, 0, 2, -20, -10, -25 }, offsets);
        }

        [TestMethod]
        public void ParseTerseOffsets_Skips_Banner_Lines()
        {
            bool ok = SmuCliState.ParseTerseOffsets("Reading offsets...\n-15,0,2,-20,-10,-25\n", 6, out int[] offsets);
            Assert.IsTrue(ok);
            Assert.AreEqual(-15, offsets[0]);
            Assert.AreEqual(-25, offsets[5]);
        }

        [TestMethod]
        public void ParseTerseOffsets_Empty_Or_NonNumeric_Returns_False()
        {
            Assert.IsFalse(SmuCliState.ParseTerseOffsets("", 6, out _));
            Assert.IsFalse(SmuCliState.ParseTerseOffsets("no numbers here", 6, out _));
        }

        [TestMethod]
        public void VerifyMatch_True_When_Clamped_Values_Equal()
        {
            Assert.IsTrue(SmuCliState.VerifyMatch(new[] { -10, -20 }, new[] { -10, -20 }));
            Assert.IsTrue(SmuCliState.VerifyMatch(new[] { -999 }, new[] { -30 })); // requested clamps to read-back
        }

        [TestMethod]
        public void VerifyMatch_False_On_Mismatch_Or_Length()
        {
            Assert.IsFalse(SmuCliState.VerifyMatch(new[] { -10, -20 }, new[] { -10, -19 }));
            Assert.IsFalse(SmuCliState.VerifyMatch(new[] { -10 }, new[] { -10, -10 }));
        }
    }
}
