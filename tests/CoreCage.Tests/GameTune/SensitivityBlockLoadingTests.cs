using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class SensitivityBlockLoadingTests
    {
        private static string WriteTemp(string json)
        {
            var dir = Path.Combine(Path.GetTempPath(), "sens_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "game.json"), json);
            return dir;
        }

        [TestMethod]
        public void Load_ProfileWithSensitivity_PopulatesBlock()
        {
            var dir = WriteTemp("{ \"game\": \"TF2\", \"exe\": \"tf_win64.exe\", \"sensitivity\": { \"key\": \"sensitivity\", \"yaw\": 0.022 } }");
            var result = CommunityProfileLoader.LoadDirectory(dir);
            var s = result.Profiles[0].Profile.Sensitivity;
            Assert.IsNotNull(s);
            Assert.AreEqual("sensitivity", s!.Key);
            Assert.AreEqual(0.022, s.Yaw, 1e-9);
        }

        [TestMethod]
        public void Load_ProfileWithoutSensitivity_IsNull()
        {
            var dir = WriteTemp("{ \"game\": \"REPO\", \"exe\": \"REPO.exe\" }");
            var result = CommunityProfileLoader.LoadDirectory(dir);
            Assert.IsNull(result.Profiles[0].Profile.Sensitivity);
        }

        [TestMethod]
        public void Load_SensitivityMissingKeyOrBadYaw_IsNull()
        {
            var dir = WriteTemp("{ \"game\": \"X\", \"exe\": \"x.exe\", \"sensitivity\": { \"key\": \"\", \"yaw\": 0 } }");
            var result = CommunityProfileLoader.LoadDirectory(dir);
            Assert.IsNull(result.Profiles[0].Profile.Sensitivity);
        }
    }
}
