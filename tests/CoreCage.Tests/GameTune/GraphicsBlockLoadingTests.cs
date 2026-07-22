using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GraphicsBlockLoadingTests
    {
        private static string WriteTemp(string json)
        {
            var dir = Path.Combine(Path.GetTempPath(), "gt_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "game.json"), json);
            return dir;
        }

        [TestMethod]
        public void Load_ProfileWithGraphicsBlock_PopulatesGraphics()
        {
            var dir = WriteTemp(@"{
              ""game"": ""Arc Raiders"", ""exe"": ""PioneerGame-Win64-Shipping.exe"",
              ""graphics"": {
                ""format"": ""unreal-ini"",
                ""configPath"": ""%LOCALAPPDATA%\\ArcRaiders\\GameUserSettings.ini"",
                ""safeRoots"": [""%LOCALAPPDATA%""],
                ""competitivePreset"": { ""MotionBlur"": ""0"", ""sg.ShadowQuality"": ""0"" }
              }
            }");

            var result = CommunityProfileLoader.LoadDirectory(dir);

            Assert.AreEqual(0, result.Errors.Count);
            var g = result.Profiles[0].Profile.Graphics;
            Assert.IsNotNull(g);
            Assert.AreEqual("unreal-ini", g!.Format);
            Assert.AreEqual("0", g.CompetitivePreset["MotionBlur"]);
            Assert.IsFalse(g.GuidedOnly);
        }

        [TestMethod]
        public void Load_ProfileWithoutGraphicsBlock_GraphicsIsNull()
        {
            var dir = WriteTemp(@"{ ""game"": ""TF2"", ""exe"": ""tf.exe"" }");
            var result = CommunityProfileLoader.LoadDirectory(dir);
            Assert.IsNull(result.Profiles[0].Profile.Graphics);
        }
    }
}
