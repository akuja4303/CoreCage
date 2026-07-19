using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;
using System.Linq;

namespace CoreCage.Tests
{
    [TestClass]
    public class TuningStateParseTests
    {
        [TestMethod]
        public void Parse_Classifies_Success_And_Failure_Lines()
        {
            string stdout = "set stapm limit: 95000\r\nset-coall is not supported on this system\r\n";
            var results = TuningState.ParseRyzenAdjOutput(stdout).ToList();
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[0].Ok);
            Assert.AreEqual("set stapm limit: 95000", results[0].Param);
            Assert.IsFalse(results[1].Ok);
            Assert.AreEqual("set-coall is not supported on this system", results[1].Param);
        }

        [TestMethod]
        public void Parse_Skips_Blank_Lines_And_Trims()
        {
            string stdout = "  \r\n\r\n  set fast limit: 105000  \r\n";
            var results = TuningState.ParseRyzenAdjOutput(stdout).ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("set fast limit: 105000", results[0].Param);
            Assert.IsTrue(results[0].Ok);
        }

        [TestMethod]
        public void Parse_Failure_Keywords_Are_Case_Insensitive()
        {
            var results = TuningState.ParseRyzenAdjOutput("Operation FAILED\r\nunexpected ERROR occurred").ToList();
            Assert.IsFalse(results[0].Ok);
            Assert.IsFalse(results[1].Ok);
        }

        [TestMethod]
        public void Parse_Empty_Returns_Empty()
        {
            Assert.AreEqual(0, TuningState.ParseRyzenAdjOutput("").Count());
            Assert.AreEqual(0, TuningState.ParseRyzenAdjOutput(null).Count());
        }
    }
}
