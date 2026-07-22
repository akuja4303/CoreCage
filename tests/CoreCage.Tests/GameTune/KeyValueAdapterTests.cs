using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class KeyValueAdapterTests
    {
        private static string Temp(string content, string ext)
        {
            var p = Path.Combine(Path.GetTempPath(), "kv_" + Path.GetRandomFileName() + ext);
            File.WriteAllText(p, content);
            return p;
        }

        [TestMethod]
        public void SpaceDelimited_Write_PreservesOthers()
        {
            var a = new KeyValueAdapter("frostbite-profsave", ' ', quoteValues: false);
            var path = Temp("GstRender.MotionBlurEnabled 1\nGstRender.Keep 7\n", ".txt");
            a.Write(path, a.Plan(a.Read(path),
                new Dictionary<string, string> { ["GstRender.MotionBlurEnabled"] = "0" }));
            var after = a.Read(path);
            Assert.AreEqual("0", Val(after, "GstRender.MotionBlurEnabled"));
            Assert.AreEqual("7", Val(after, "GstRender.Keep"));
        }

        [TestMethod]
        public void QuotedSource_Write_QuotesValue()
        {
            var a = new KeyValueAdapter("source-cfg", ' ', quoteValues: true);
            var path = Temp("mat_motion_blur_enabled \"1\"\n", ".cfg");
            a.Write(path, a.Plan(a.Read(path),
                new Dictionary<string, string> { ["mat_motion_blur_enabled"] = "0" }));
            Assert.IsTrue(File.ReadAllText(path).Contains("mat_motion_blur_enabled \"0\""));
        }

        [TestMethod]
        public void KeyValueAdapter_DuplicateKey_DoesNotThrow_WritesFirstOnly()
        {
            var a = new KeyValueAdapter("frostbite-profsave", ' ', quoteValues: false);
            var path = Temp("GstRender.X 1\nGstRender.Other 5\nGstRender.X 9\n", ".txt");

            GraphicsReadResult current = a.Read(path);
            GraphicsApplyPlan plan = a.Plan(current, new Dictionary<string, string> { ["GstRender.X"] = "0" });
            Assert.AreEqual(1, plan.Changes.Count);

            a.Write(path, plan);
            var lines = File.ReadAllLines(path);
            var occurrences = new List<string>();
            foreach (var line in lines)
                if (line.Trim().StartsWith("GstRender.X ")) occurrences.Add(line.Trim());

            Assert.AreEqual(2, occurrences.Count);
            Assert.AreEqual("GstRender.X 0", occurrences[0]);   // first occurrence changed
            Assert.AreEqual("GstRender.X 9", occurrences[1]);   // second occurrence untouched
        }

        private static string? Val(GraphicsReadResult r, string k)
        {
            foreach (var s in r.Settings) if (s.Key == k) return s.CurrentValue;
            return null;
        }
    }
}
