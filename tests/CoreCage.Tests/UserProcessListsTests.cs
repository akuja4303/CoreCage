using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;
using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Tests
{
    [TestClass]
    public class UserProcessListsTests
    {
        [TestMethod]
        public void Normalize_Strips_Path_Extension_And_Lowercases()
        {
            Assert.AreEqual("arcraiders", UserProcessLists.Normalize(@"C:\Games\ArcRaiders.exe"));
            Assert.AreEqual("cs2", UserProcessLists.Normalize("CS2"));
            Assert.AreEqual("msedge", UserProcessLists.Normalize("  MsEdge.EXE  "));
        }

        [TestMethod]
        public void Normalize_Empty_Returns_Empty()
        {
            Assert.AreEqual("", UserProcessLists.Normalize(null));
            Assert.AreEqual("", UserProcessLists.Normalize("   "));
        }

        [TestMethod]
        public void AddNormalized_Dedupes_Case_Insensitively_And_Skips_Blank()
        {
            var list = new List<string> { "cs2" };
            UserProcessLists.AddNormalized(list, @"C:\x\CS2.exe");
            UserProcessLists.AddNormalized(list, "Arcraiders.exe");
            UserProcessLists.AddNormalized(list, "   ");
            CollectionAssert.AreEqual(new List<string> { "cs2", "arcraiders" }, list);
        }

        [TestMethod]
        public void IsListedIn_Matches_By_Process_Name_Case_Insensitive()
        {
            var list = new List<string> { "arcraiders", "cs2" };
            Assert.IsTrue(UserProcessLists.IsListedIn(list, "ArcRaiders"));
            Assert.IsFalse(UserProcessLists.IsListedIn(list, "notepad"));
        }

        [TestMethod]
        public void Migrate_Seeds_Gaming_From_Legacy_When_Gaming_Empty()
        {
            var gaming = new List<string>();
            var legacy = new List<string> { "Steam.exe", "Discord.exe" };
            bool changed = UserProcessLists.Migrate(legacy, gaming);
            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(new List<string> { "steam", "discord" }, gaming);
        }

        [TestMethod]
        public void Migrate_NoOp_When_Gaming_Already_Populated()
        {
            var gaming = new List<string> { "cs2" };
            var legacy = new List<string> { "Steam.exe" };
            bool changed = UserProcessLists.Migrate(legacy, gaming);
            Assert.IsFalse(changed);
            CollectionAssert.AreEqual(new List<string> { "cs2" }, gaming);
        }

        [TestMethod]
        public void Cache_IsListed_Reads_From_SetLists()
        {
            UserProcessLists.SetLists(new List<string> { "arcraiders" });
            Assert.IsTrue(UserProcessLists.IsListed("gaming", "ArcRaiders"));
            Assert.IsFalse(UserProcessLists.IsListed("gaming", "msedge"));
            Assert.IsFalse(UserProcessLists.IsListed("bogus", "arcraiders"));
        }
    }
}
