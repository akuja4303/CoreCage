using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class UnrealIniAdapterTests
    {
        private const string Sample =
@"[/Script/Engine.GameUserSettings]
MotionBlur=1
sg.ShadowQuality=3
KeepMe=42
";
        private string WriteTemp()
        {
            var p = Path.Combine(Path.GetTempPath(), "ue_" + Path.GetRandomFileName() + ".ini");
            File.WriteAllText(p, Sample);
            return p;
        }

        [TestMethod]
        public void Plan_ProducesOnlyChangedKeys()
        {
            var a = new UnrealIniAdapter();
            var cur = a.Read(WriteTemp());
            var preset = new Dictionary<string, string> { ["MotionBlur"] = "0", ["sg.ShadowQuality"] = "3" };
            var plan = a.Plan(cur, preset);
            Assert.AreEqual(1, plan.Changes.Count);          // ShadowQuality already 3 → no change
            Assert.AreEqual("MotionBlur", plan.Changes[0].Key);
            Assert.AreEqual("0", plan.Changes[0].To);
        }

        [TestMethod]
        public void Write_ChangesTargetKeys_PreservesOthers_RoundTrips()
        {
            var a = new UnrealIniAdapter();
            var path = WriteTemp();
            var plan = a.Plan(a.Read(path), new Dictionary<string, string> { ["MotionBlur"] = "0" });
            a.Write(path, plan);
            var after = a.Read(path);
            Assert.AreEqual("0", Find(after, "MotionBlur"));
            Assert.AreEqual("42", Find(after, "KeepMe"));    // untouched key preserved
        }

        private static string? Find(GraphicsReadResult r, string key)
        {
            foreach (var s in r.Settings) if (s.Key == key) return s.CurrentValue;
            return null;
        }
    }
}
