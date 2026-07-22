using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class ConfigBackupTests
    {
        private string _root = "";
        private string _cfg = "";

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "gtbk_" + Path.GetRandomFileName());
            var cfgDir = Path.Combine(Path.GetTempPath(), "gtcfg_" + Path.GetRandomFileName());
            Directory.CreateDirectory(cfgDir);
            _cfg = Path.Combine(cfgDir, "config.ini");
            File.WriteAllText(_cfg, "original=1");
        }

        [TestMethod]
        public void Backup_CopiesOriginalBytes_ReturnsPath()
        {
            var bk = new ConfigBackup(_root).Backup("arc", _cfg);
            Assert.IsTrue(File.Exists(bk));
            Assert.AreEqual("original=1", File.ReadAllText(bk));
        }

        [TestMethod]
        public void TryRestoreNewest_RestoresOriginal()
        {
            var b = new ConfigBackup(_root);
            b.Backup("arc", _cfg);
            File.WriteAllText(_cfg, "changed=9");
            Assert.IsTrue(b.TryRestoreNewest("arc", _cfg));
            Assert.AreEqual("original=1", File.ReadAllText(_cfg));
        }

        [TestMethod]
        public void TryRestoreNewest_NoBackup_ReturnsFalse()
        {
            Assert.IsFalse(new ConfigBackup(_root).TryRestoreNewest("never", _cfg));
        }

        [TestMethod]
        public void Backup_TwiceSameGame_KeepsBothBackups()
        {
            var b = new ConfigBackup(_root);
            b.Backup("g", _cfg);
            File.WriteAllText(_cfg, "new-content=2");
            b.Backup("g", _cfg);

            var gameBackupDir = Path.Combine(_root, "g");
            var files = Directory.GetFiles(gameBackupDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(2, files.Length);
            var contents = files.Select(File.ReadAllText).Distinct().ToList();
            Assert.AreEqual(2, contents.Count);
        }

        [TestMethod]
        public void TryRestoreNewest_WithMultipleBackups_RestoresNewest()
        {
            var b = new ConfigBackup(_root);
            File.WriteAllText(_cfg, "original");
            b.Backup("g", _cfg);
            File.WriteAllText(_cfg, "v2");
            b.Backup("g", _cfg);
            File.WriteAllText(_cfg, "v3");

            Assert.IsTrue(b.TryRestoreNewest("g", _cfg));
            Assert.AreEqual("v2", File.ReadAllText(_cfg));
        }

        [TestMethod]
        public void Backup_DotDotGameId_Throws()
        {
            Assert.ThrowsException<System.ArgumentException>(() => new ConfigBackup(_root).Backup("..", _cfg));
        }
    }
}
