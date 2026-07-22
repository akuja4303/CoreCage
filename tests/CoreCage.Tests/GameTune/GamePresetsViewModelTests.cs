using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;
using CoreCage.App.ViewModels;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GamePresetsViewModelTests
    {
        private static (GameTuneService svc, string cfg) Svc(bool running)
        {
            var dir = Path.Combine(Path.GetTempPath(), "vm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            var cfg = Path.Combine(dir, "GameUserSettings.ini");
            File.WriteAllText(cfg, "[/Script/Engine.GameUserSettings]\nMotionBlur=1\n");
            var bk = Path.Combine(Path.GetTempPath(), "vmbk_" + Path.GetRandomFileName());
            return (new GameTuneService(new ConfigBackup(bk), _ => running), cfg);
        }

        private static DetectedGame Arc(string cfg) => new(
            "arc", "PioneerGame.exe", "Arc Raiders",
            new GraphicsBlock("unreal-ini", cfg, new[] { Path.GetDirectoryName(cfg)! },
                new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null));

        [TestMethod]
        public void Card_ReadyGame_CanApply_NotRestore()
        {
            var (svc, cfg) = Svc(running: false);
            var vm = new GamePresetsViewModel(svc, new[] { Arc(cfg) });
            var card = vm.Cards[0];
            Assert.AreEqual(CardState.Ready, card.State);
            Assert.IsTrue(card.CanApply);
            Assert.IsFalse(card.CanRestore);
        }

        [TestMethod]
        public void Card_Apply_MovesToApplied_EnablesRestore()
        {
            var (svc, cfg) = Svc(running: false);
            var card = new GamePresetsViewModel(svc, new[] { Arc(cfg) }).Cards[0];
            card.Apply();
            Assert.AreEqual(CardState.Applied, card.State);
            Assert.IsTrue(card.CanRestore);
        }

        [TestMethod]
        public void Card_GameRunning_CannotApply_ShowsReason()
        {
            var (svc, cfg) = Svc(running: true);
            var card = new GamePresetsViewModel(svc, new[] { Arc(cfg) }).Cards[0];
            card.Apply();
            Assert.AreEqual(CardState.GameRunning, card.State);
            Assert.IsFalse(card.CanApply);
            StringAssert.Contains(card.StatusText, "Close the game");
        }

        [TestMethod]
        public void Card_NoGraphics_IsNotSupported()
        {
            var (svc, _) = Svc(running: false);
            var game = new DetectedGame("repo", "REPO.exe", "R.E.P.O.", null);
            var card = new GamePresetsViewModel(svc, new[] { game }).Cards[0];
            Assert.AreEqual(CardState.NotSupported, card.State);
            Assert.IsFalse(card.CanApply);
        }

        [TestMethod]
        public void Vm_NoGames_IsEmpty()
        {
            var (svc, _) = Svc(running: false);
            var vm = new GamePresetsViewModel(svc, new DetectedGame[0]);
            Assert.IsTrue(vm.IsEmpty);
        }
    }
}
