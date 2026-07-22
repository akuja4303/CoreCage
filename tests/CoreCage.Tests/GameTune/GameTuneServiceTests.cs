using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GameTuneServiceTests
    {
        private string _cfg = "", _bkRoot = "";

        private GraphicsBlock Block() => new GraphicsBlock(
            "unreal-ini", _cfg, new[] { Path.GetDirectoryName(_cfg)! },
            new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null);

        [TestInitialize]
        public void Setup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gts_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            _cfg = Path.Combine(dir, "GameUserSettings.ini");
            File.WriteAllText(_cfg, "[/Script/Engine.GameUserSettings]\nMotionBlur=1\n");
            _bkRoot = Path.Combine(Path.GetTempPath(), "gtsbk_" + Path.GetRandomFileName());
        }

        private GameTuneService Svc(bool running) =>
            new GameTuneService(new ConfigBackup(_bkRoot), _ => running);

        [TestMethod]
        public void Apply_HappyPath_WritesPreset_BacksUp_ReturnsApplied()
        {
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.Applied, r.Status);
            Assert.IsNotNull(r.BackupPath);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=0");
        }

        [TestMethod]
        public void Apply_GameRunning_Aborts_DoesNotWrite()
        {
            var r = Svc(running: true).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.GameRunning, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1"); // unchanged
        }

        [TestMethod]
        public void Apply_NoGraphicsBlock_ReturnsNotSupported()
        {
            var r = Svc(running: false).Apply("repo", "REPO.exe", null);
            Assert.AreEqual(GameTuneStatus.NotSupported, r.Status);
        }

        [TestMethod]
        public void Apply_UnsafePath_Aborts()
        {
            var unsafeBlock = new GraphicsBlock("unreal-ini", _cfg,
                new[] { @"C:\SomeOtherRoot" }, new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null);
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", unsafeBlock);
            Assert.AreEqual(GameTuneStatus.UnsafePath, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1");
        }

        [TestMethod]
        public void Restore_UnsafePath_Aborts()
        {
            var unsafeBlock = new GraphicsBlock("unreal-ini", _cfg,
                new[] { @"C:\SomeOtherRoot" }, new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null);
            var r = Svc(running: false).Restore("arc", "PioneerGame.exe", unsafeBlock);
            Assert.AreEqual(GameTuneStatus.UnsafePath, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1");
        }

        [TestMethod]
        public void Apply_ConfigMissing_ReturnsConfigNotFound()
        {
            File.Delete(_cfg);
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.ConfigNotFound, r.Status);
        }

        [TestMethod]
        public void Restore_AfterApply_RevertsFile()
        {
            var svc = Svc(running: false);
            svc.Apply("arc", "PioneerGame.exe", Block());
            var r = svc.Restore("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.Restored, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1");
        }
    }
}
