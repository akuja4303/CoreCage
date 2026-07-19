using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreCage.Tests
{
    [TestClass]
    public class SmokeTests
    {
        [TestMethod]
        public void TestHarnessRuns()
        {
            Assert.AreEqual(4, 2 + 2);
        }
    }
}
