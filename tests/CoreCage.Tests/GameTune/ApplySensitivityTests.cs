using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class ApplySensitivityTests
    {
        private string _cfg = "", _bkRoot = "";

        private GraphicsBlock Graphics() => new GraphicsBlock(
            "source-cfg", _cfg, new[] { Path.GetDirectoryName(_cfg)! },
            new Dictionary<string, string>(), false, null);

        [TestInitialize]
        public void Setup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "as_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            _cfg = Path.Combine(dir, "autoexec.cfg");
            File.WriteAllText(_cfg, "sensitivity \"3.0\"\n");
            _bkRoot = Path.Combine(Path.GetTempPath(), "asbk_" + Path.GetRandomFileName());
        }

        private GameTuneService Svc(bool running) => new(new ConfigBackup(_bkRoot), _ => running);

        [TestMethod]
        public void ApplySensitivity_WritesComputedValue_BacksUp()
        {
            var r = Svc(false).ApplySensitivity("tf2", "tf_win64.exe", Graphics(),
                new SensitivityBlock("sensitivity", 0.022), computedSens: 6.15);
            Assert.AreEqual(GameTuneStatus.Applied, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "sensitivity \"6.15\"");
        }

        [TestMethod]
        public void ApplySensitivity_GameRunning_Aborts()
        {
            var r = Svc(true).ApplySensitivity("tf2", "tf_win64.exe", Graphics(),
                new SensitivityBlock("sensitivity", 0.022), 6.15);
            Assert.AreEqual(GameTuneStatus.GameRunning, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "3.0");
        }

        [TestMethod]
        public void ApplySensitivity_GuidedOnly_ReturnsNotSupported()
        {
            var graphics = new GraphicsBlock(
                "source-cfg", _cfg, new[] { Path.GetDirectoryName(_cfg)! },
                new Dictionary<string, string>(), true, null);
            var r = Svc(false).ApplySensitivity("tf2", "tf_win64.exe", graphics,
                new SensitivityBlock("sensitivity", 0.022), 6.15);
            Assert.AreEqual(GameTuneStatus.NotSupported, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "3.0");
        }
    }
}
