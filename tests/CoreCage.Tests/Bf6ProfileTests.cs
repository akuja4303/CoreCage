using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using CoreCage.Core;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests
{
    /// <summary>
    /// Battlefield 6 auto-optimize coverage (2026-07-16). BF6 must be detected by the FAST name
    /// path — signals 2/4 (install path / publisher) need MainModule access, which races the
    /// EA-launcher spawn chain — and its curated profile must resolve by exe so QoS marking and
    /// one-click add work. The cage itself is ThrottleForMode's existing snapshot/restore path;
    /// what BF6 adds is deterministic detection plus EA Javelin on the never-touch list.
    /// </summary>
    [TestClass]
    public class Bf6ProfileTests
    {
        [TestMethod]
        public void Bf6_IsClassifiedAsGame_ByFastNamePath()
        {
            // Fast path is pure string matching — no process needs to exist.
            Assert.AreEqual(ProcessCategory.Game, ProcessWatcher.ClassifyProcess("bf6"),
                "bf6 must hit the name-pattern fast path so detection can't race the EA launcher chain.");
        }

        [TestMethod]
        public void Bf6_HasCuratedDefaultProfile_ResolvableByExe()
        {
            var byExe = DefaultGameProfiles.FindByExe("bf6.exe");
            Assert.IsNotNull(byExe, "bf6.exe must resolve to a curated default profile.");
            Assert.AreEqual("Battlefield 6", byExe!.DisplayName);
            Assert.IsTrue(byExe.EacSafe, "IFEO-only boosts are safe alongside EA Javelin.");
            Assert.AreEqual(46, byExe.QosDscp, "Competitive default is EF (46).");
        }

        [TestMethod]
        public void Bf6_UserProfile_MatchesForegroundExe_CaseAndPathInsensitive()
        {
            var profiles = new List<GameProfile>
            {
                new GameProfile { ExeName = "bf6.exe", DisplayName = "Battlefield 6", Mode = ProfileMode.Gaming }
            };
            Assert.IsNotNull(ProfileMatcher.Match("BF6.EXE", profiles));
            Assert.IsNotNull(ProfileMatcher.Match(@"C:\EA Games\Battlefield 6\bf6.exe", profiles));
            Assert.IsNull(ProfileMatcher.Match("bf2042.exe", profiles), "Other titles must not match bf6's profile.");
        }
    }
}
