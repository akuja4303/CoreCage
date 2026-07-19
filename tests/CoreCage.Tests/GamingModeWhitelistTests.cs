using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Review finding IMPORTANT-3: Core Cage's whitelist was only "gaming list + audiodg", so a game the
    /// user never added to their own gaming list got caged along with everything else. The fix adds two
    /// more inputs: the foreground process at cage time, and whatever ProcessWatcher's existing
    /// classifier says is a running game right now (reused, not reinvented). The set-construction math
    /// itself (<see cref="GamingMode.BuildWhitelistSet"/>) is pure -- no Process/OS dependency -- so it's
    /// unit-tested directly here. Gathering the real foreground process / running-game names is impure
    /// and lives in GamingMode.BuildCoreCageWhitelist, never invoked by these tests.
    /// </summary>
    [TestClass]
    public class GamingModeWhitelistTests
    {
        [TestMethod]
        public void BuildWhitelistSet_AlwaysIncludes_Audiodg()
        {
            var whitelist = GamingMode.BuildWhitelistSet(Array.Empty<string>(), null, Array.Empty<string>());
            Assert.IsTrue(whitelist.Contains("audiodg"));
        }

        [TestMethod]
        public void BuildWhitelistSet_Includes_GamingListEntries_Normalized()
        {
            var whitelist = GamingMode.BuildWhitelistSet(new[] { "MyGame.exe" }, null, Array.Empty<string>());
            Assert.IsTrue(whitelist.Contains("mygame"));
        }

        [TestMethod]
        public void BuildWhitelistSet_Includes_ForegroundProcess_EvenIfNotOnGamingList()
        {
            var whitelist = GamingMode.BuildWhitelistSet(Array.Empty<string>(), "SomeUnlistedGame.exe", Array.Empty<string>());
            Assert.IsTrue(whitelist.Contains("someunlistedgame"),
                "the running foreground process must never be caged even if the user never added it to their gaming list.");
        }

        [TestMethod]
        public void BuildWhitelistSet_Includes_ProcessWatcherClassifiedGames()
        {
            var whitelist = GamingMode.BuildWhitelistSet(Array.Empty<string>(), null, new[] { "cs2" });
            Assert.IsTrue(whitelist.Contains("cs2"),
                "a process ProcessWatcher classifies as a game must be excluded even if not on the user's list.");
        }

        [TestMethod]
        public void BuildWhitelistSet_NullForegroundProcessName_DoesNotThrow_AndOnlyAudiodg()
        {
            var whitelist = GamingMode.BuildWhitelistSet(Array.Empty<string>(), null, Array.Empty<string>());
            Assert.AreEqual(1, whitelist.Count, "only audiodg when nothing else is supplied.");
        }

        [TestMethod]
        public void BuildWhitelistSet_NullGamingListOrRunningNames_DoesNotThrow()
        {
            var whitelist = GamingMode.BuildWhitelistSet(null!, null, null!);
            Assert.IsTrue(whitelist.Contains("audiodg"));
        }
    }
}
