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

        private const string DupSample =
@"[/Script/Engine.GameUserSettings]
MotionBlur=1
sg.ShadowQuality=3

[ScalabilityGroups]
sg.ShadowQuality=1
";
        private string WriteTempDup()
        {
            var p = Path.Combine(Path.GetTempPath(), "ue_dup_" + Path.GetRandomFileName() + ".ini");
            File.WriteAllText(p, DupSample);
            return p;
        }

        [TestMethod]
        public void Plan_DuplicateKeyAcrossSections_DoesNotThrow_UsesFirst()
        {
            var a = new UnrealIniAdapter();
            var cur = a.Read(WriteTempDup());
            var preset = new Dictionary<string, string> { ["MotionBlur"] = "0" };
            var plan = a.Plan(cur, preset);
            Assert.AreEqual(1, plan.Changes.Count);
            Assert.AreEqual("MotionBlur", plan.Changes[0].Key);
            Assert.AreEqual("0", plan.Changes[0].To);
        }

        [TestMethod]
        public void Write_DuplicateKeyAcrossSections_ChangesOnlyFirstOccurrence()
        {
            var a = new UnrealIniAdapter();
            var path = WriteTempDup();
            var plan = a.Plan(a.Read(path), new Dictionary<string, string> { ["sg.ShadowQuality"] = "0" });
            a.Write(path, plan);
            var lines = File.ReadAllLines(path);
            var occurrences = new List<string>();
            foreach (var line in lines)
                if (line.Trim().StartsWith("sg.ShadowQuality=")) occurrences.Add(line.Trim());
            Assert.AreEqual(2, occurrences.Count);
            Assert.AreEqual("sg.ShadowQuality=0", occurrences[0]);   // first occurrence changed
            Assert.AreEqual("sg.ShadowQuality=1", occurrences[1]);   // second occurrence untouched
        }

        [TestMethod]
        public void Write_AppliedTwice_IsIdempotent()
        {
            var a = new UnrealIniAdapter();
            var path = WriteTempDup();
            var plan = a.Plan(a.Read(path), new Dictionary<string, string> { ["MotionBlur"] = "0" });
            a.Write(path, plan);
            var plan2 = a.Plan(a.Read(path), new Dictionary<string, string> { ["MotionBlur"] = "0" });
            Assert.AreEqual(0, plan2.Changes.Count);
        }

        private const string NoMotionBlurSample =
@"[/Script/Engine.GameUserSettings]
sg.ShadowQuality=3
KeepMe=42
";
        private string WriteTempNoMotionBlur()
        {
            var p = Path.Combine(Path.GetTempPath(), "ue_nomb_" + Path.GetRandomFileName() + ".ini");
            File.WriteAllText(p, NoMotionBlurSample);
            return p;
        }

        [TestMethod]
        public void Write_KeyNotPresent_AppendsKey()
        {
            var a = new UnrealIniAdapter();
            var path = WriteTempNoMotionBlur();
            var plan = a.Plan(a.Read(path), new Dictionary<string, string> { ["MotionBlur"] = "0" });
            a.Write(path, plan);
            var after = a.Read(path);
            Assert.AreEqual("0", Find(after, "MotionBlur"));   // appended
            Assert.AreEqual("3", Find(after, "sg.ShadowQuality"));   // prior lines preserved
            Assert.AreEqual("42", Find(after, "KeepMe"));
        }
    }
}
