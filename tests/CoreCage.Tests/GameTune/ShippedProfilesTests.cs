using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class ShippedProfilesTests
    {
        // MSTest has no NUnit-style TestContext.CurrentContext; resolve the repo's profiles/ dir
        // relative to the test bin dir (tests/CoreCage.Tests/bin/<cfg>/net8.0-windows → up 5 → repo root).
        private static string Dir =>
            Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles"));

        [TestMethod]
        public void AllShippedProfiles_LoadWithoutErrors()
        {
            var result = CommunityProfileLoader.LoadDirectory(Dir);
            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        [TestMethod]
        public void FiveGames_HaveGraphicsBlock()
        {
            var result = CommunityProfileLoader.LoadDirectory(Dir);
            // 5 games ship a graphics block: ARC, DbD, BF6, Helldivers (auto-apply) + TF2 (guided-only).
            var withGraphics = result.Profiles.Where(p => p.Profile.Graphics is not null).ToList();
            Assert.IsTrue(withGraphics.Count >= 5, $"expected >=5 graphics blocks, got {withGraphics.Count}");
        }

        [TestMethod]
        public void FourGames_AreAutoApply_TF2IsGuidedOnly()
        {
            var result = CommunityProfileLoader.LoadDirectory(Dir);
            var autoApply = result.Profiles.Where(p => p.Profile.Graphics is { GuidedOnly: false }).ToList();
            Assert.IsTrue(autoApply.Count >= 4, $"expected >=4 auto-apply profiles, got {autoApply.Count}");
        }
    }
}
