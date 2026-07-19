using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Diagnostics;

namespace CoreCage.Tests.Diagnostics
{
    [TestClass]
    public class CrashScrubberTests
    {
        [TestMethod]
        public void RedactsWindowsUserPath()
        {
            var r = CrashScrubber.Scrub(@"at Foo() in C:\Users\devuser\dev\CoreCage\Bar.cs:line 9", "devuser", "DESKTOP-1");
            StringAssert.Contains(r, @"C:\Users\<user>\dev");
            Assert.IsFalse(r!.Contains("devuser"));
        }

        [TestMethod]
        public void RedactsBareUsernameAndMachine()
        {
            var r = CrashScrubber.Scrub("user devuser on DESKTOP-1 crashed", "devuser", "DESKTOP-1");
            Assert.AreEqual("user <user> on <machine> crashed", r);
        }

        [TestMethod]
        public void RedactsOtherUsersInPaths_NotJustCurrent()
        {
            var r = CrashScrubber.Scrub(@"C:\Users\Alice\AppData\x", "devuser", "DESKTOP-1");
            StringAssert.Contains(r, @"C:\Users\<user>\AppData");
        }

        [TestMethod]
        public void CaseInsensitiveUsername()
        {
            var r = CrashScrubber.Scrub("DEVUSER did it", "devuser", "m");
            Assert.AreEqual("<user> did it", r);
        }

        [TestMethod]
        public void NullAndEmpty_PassThrough()
        {
            Assert.IsNull(CrashScrubber.Scrub(null, "devuser", "m"));
            Assert.AreEqual("", CrashScrubber.Scrub("", "devuser", "m"));
        }

        [TestMethod]
        public void NoIdentifiers_LeavesTextButStillMasksPaths()
        {
            var r = CrashScrubber.Scrub(@"C:\Users\bob\f.txt", null, null);
            StringAssert.Contains(r, @"C:\Users\<user>\f.txt");
        }
    }
}
