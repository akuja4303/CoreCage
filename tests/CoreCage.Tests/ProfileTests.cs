using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests
{
    [TestClass]
    public class ProfileTests
    {
        private static Stream S(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

        // ── ProfileMatcher ──────────────────────────────────────────────────────
        [TestMethod]
        public void Matcher_Matches_Case_And_Path_And_Extension_Insensitively()
        {
            var profiles = new List<GameProfile>
            {
                new() { ExeName = "PioneerGame-d.exe", Mode = ProfileMode.Gaming },
                new() { ExeName = "msedge.exe",        Mode = ProfileMode.Gaming },
            };
            Assert.AreEqual(ProfileMode.Gaming, ProfileMatcher.Match(@"C:\Games\PIONEERGAME-D.EXE", profiles)!.Mode);
            Assert.AreEqual(ProfileMode.Gaming, ProfileMatcher.Match("pioneergame-d", profiles)!.Mode);
            Assert.AreEqual(ProfileMode.Gaming, ProfileMatcher.Match("msedge.exe", profiles)!.Mode);
        }

        [TestMethod]
        public void Matcher_Returns_Null_When_No_Match_Or_Empty()
        {
            var profiles = new List<GameProfile> { new() { ExeName = "game.exe" } };
            Assert.IsNull(ProfileMatcher.Match("notepad.exe", profiles));
            Assert.IsNull(ProfileMatcher.Match("", profiles));
            Assert.IsNull(ProfileMatcher.Match("game.exe", new List<GameProfile>()));
        }

        [TestMethod]
        public void Matcher_Normalize_Strips_Path_And_Exe()
        {
            Assert.AreEqual("game", ProfileMatcher.Normalize(@"D:\a\b\Game.exe"));
            Assert.AreEqual("game", ProfileMatcher.Normalize("game"));
        }

        // ── SteamGames VDF parsing (forward slashes avoid escape-sequence ambiguity) ──
        [TestMethod]
        public void ParseLibraryFolders_Extracts_Paths()
        {
            string vdf = @"
""libraryfolders""
{
    ""0""
    {
        ""path""    ""C:/Program Files (x86)/Steam""
    }
    ""1""
    {
        ""path""    ""D:/SteamLibrary""
    }
}";
            var paths = SteamGames.ParseLibraryFolders(S(vdf));
            Assert.AreEqual(2, paths.Count);
            CollectionAssert.Contains((System.Collections.ICollection)paths, "C:/Program Files (x86)/Steam");
            CollectionAssert.Contains((System.Collections.ICollection)paths, "D:/SteamLibrary");
        }

        [TestMethod]
        public void ParseAppManifest_Extracts_AppId_Name_InstallDir()
        {
            string acf = @"
""AppState""
{
    ""appid""       ""1808500""
    ""name""        ""ARC Raiders""
    ""installdir""  ""ARCRaiders""
}";
            InstalledGame? g = SteamGames.ParseAppManifest(S(acf));
            Assert.IsNotNull(g);
            Assert.AreEqual("1808500", g!.AppId);
            Assert.AreEqual("ARC Raiders", g.Name);
            Assert.AreEqual("ARCRaiders", g.InstallDir);
        }
    }
}
