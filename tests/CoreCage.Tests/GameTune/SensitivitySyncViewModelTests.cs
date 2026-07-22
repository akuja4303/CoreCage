using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;
using CoreCage.App.ViewModels;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class SensitivitySyncViewModelTests
    {
        private static DetectedGame Game(string id, double yaw, string cfg) => new(
            id, id + ".exe", id,
            new GraphicsBlock("source-cfg", cfg, new[] { Path.GetDirectoryName(cfg)! },
                new Dictionary<string, string>(), false, null),
            new SensitivityBlock("sensitivity", yaw));

        private static (GameTuneService svc, string cfgA, string cfgB) Env()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssvm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            var a = Path.Combine(dir, "a.cfg"); File.WriteAllText(a, "sensitivity \"1\"\n");
            var b = Path.Combine(dir, "b.cfg"); File.WriteAllText(b, "sensitivity \"1\"\n");
            var bk = Path.Combine(Path.GetTempPath(), "ssvmbk_" + Path.GetRandomFileName());
            return (new GameTuneService(new ConfigBackup(bk), _ => false), a, b);
        }

        [TestMethod]
        public void Rows_ComputeTargetSens_FromReferenceGameYaw()
        {
            var (svc, a, b) = Env();
            var games = new[] { Game("src", 0.022, a), Game("tgt", 0.011, b) };
            var vm = new SensitivitySyncViewModel(svc, games) { ReferenceGameId = "src", ReferenceSens = 6.15 };
            vm.Recompute();
            var tgt = vm.Rows.First(r => r.DisplayName == "tgt");
            Assert.AreEqual(12.30, tgt.TargetSens, 1e-6);
        }

        [TestMethod]
        public void SyncAll_WritesEachGameConfig()
        {
            var (svc, a, b) = Env();
            var games = new[] { Game("src", 0.022, a), Game("tgt", 0.011, b) };
            var vm = new SensitivitySyncViewModel(svc, games) { ReferenceGameId = "src", ReferenceSens = 6.15 };
            vm.Recompute();
            vm.SyncAll();
            StringAssert.Contains(File.ReadAllText(a), "sensitivity \"6.15\"");
            StringAssert.Contains(File.ReadAllText(b), "sensitivity \"12.3\"");
        }

        [TestMethod]
        public void Games_WithoutSensitivity_AreSkipped()
        {
            var (svc, a, _) = Env();
            var noSens = new DetectedGame("x", "x.exe", "x",
                new GraphicsBlock("source-cfg", a, new[] { Path.GetDirectoryName(a)! },
                    new Dictionary<string, string>(), false, null), null);
            var vm = new SensitivitySyncViewModel(svc, new[] { noSens }) { ReferenceGameId = "x" };
            vm.Recompute();
            Assert.AreEqual(0, vm.Rows.Count);
        }
    }
}
