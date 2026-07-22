using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class PathSafetyTests
    {
        [TestMethod]
        public void Expand_ReplacesEnvVar()
        {
            Environment.SetEnvironmentVariable("GT_TEST_ROOT", @"C:\Users\x\AppData\Local");
            var p = PathSafety.Expand(@"%GT_TEST_ROOT%\Game\config.ini");
            Assert.AreEqual(@"C:\Users\x\AppData\Local\Game\config.ini", p);
        }

        [TestMethod]
        public void IsSafe_UnderSafeRoot_True()
        {
            var roots = new List<string> { @"C:\Users\x\AppData\Local" };
            Assert.IsTrue(PathSafety.IsSafe(@"C:\Users\x\AppData\Local\Game\config.ini", roots));
        }

        [TestMethod]
        public void IsSafe_OutsideSafeRoot_False()
        {
            var roots = new List<string> { @"C:\Users\x\AppData\Local" };
            Assert.IsFalse(PathSafety.IsSafe(@"C:\Windows\System32\config.ini", roots));
        }

        [TestMethod]
        public void IsSafe_InsideSteamInstallDir_False_EvenIfUnderSafeRoot()
        {
            var roots = new List<string> { @"F:\SteamLibrary" };
            Assert.IsFalse(PathSafety.IsSafe(@"F:\SteamLibrary\steamapps\common\Game\config.ini", roots));
        }
    }
}
