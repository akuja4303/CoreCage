using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class ProcessWatcherTests
    {
        [TestMethod]
        public void StripExe_Removes_Extension_And_Trims()
        {
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("PioneerGame-d.exe"));
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("  PioneerGame-d.exe  "));
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("PioneerGame-d")); // already bare
        }

        [TestMethod]
        public void StripExe_Is_Case_Insensitive_On_Extension()
        {
            // GetProcessesByName / WMI casing varies; the .EXE strip must not depend on case.
            Assert.AreEqual("Game", ProcessWatcher.StripExe("Game.EXE"));
            Assert.AreEqual("Game", ProcessWatcher.StripExe("Game.Exe"));
        }

        [TestMethod]
        public void StripExe_Handles_Null_And_Empty()
        {
            Assert.AreEqual("", ProcessWatcher.StripExe(null));
            Assert.AreEqual("", ProcessWatcher.StripExe(""));
            Assert.AreEqual("", ProcessWatcher.StripExe("   "));
        }
    }
}
