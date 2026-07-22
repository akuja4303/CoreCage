using System.IO;
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
    }
}
