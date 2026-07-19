using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// The pure policy decision at the heart of the browser-kill redesign
    /// (docs/BROWSER-KILL-REDESIGN.md). No process I/O — just the rules, so they can be
    /// proven: NEVER kill, never touch a foreground window, deprioritize by default.
    /// </summary>
    [TestClass]
    public class BrowserGovernorTests
    {
        [TestMethod]
        public void Foreground_Is_Always_Skipped_Regardless_Of_Policy()
        {
            // The 2026-07-02 incident: a performance mode nuked the foreground browser view.
            // A foreground/fullscreen window is untouchable under EVERY policy — hard rule.
            Assert.AreEqual(BrowserAction.Skip, BrowserGovernor.Decide(BrowserPolicy.Off,           isForeground: true));
            Assert.AreEqual(BrowserAction.Skip, BrowserGovernor.Decide(BrowserPolicy.Deprioritize,  isForeground: true));
            Assert.AreEqual(BrowserAction.Skip, BrowserGovernor.Decide(BrowserPolicy.GracefulClose, isForeground: true));
        }

        [TestMethod]
        public void Background_Follows_Policy()
        {
            Assert.AreEqual(BrowserAction.Skip,          BrowserGovernor.Decide(BrowserPolicy.Off,           isForeground: false));
            Assert.AreEqual(BrowserAction.Deprioritize,  BrowserGovernor.Decide(BrowserPolicy.Deprioritize,  isForeground: false));
            Assert.AreEqual(BrowserAction.GracefulClose, BrowserGovernor.Decide(BrowserPolicy.GracefulClose, isForeground: false));
        }

        [TestMethod]
        public void No_Policy_Ever_Yields_Kill()
        {
            // There is no Kill action in the enum by design, but assert the decision never
            // escalates past GracefulClose for any input combination.
            foreach (BrowserPolicy p in new[] { BrowserPolicy.Off, BrowserPolicy.Deprioritize, BrowserPolicy.GracefulClose })
                foreach (bool fg in new[] { true, false })
                {
                    var a = BrowserGovernor.Decide(p, fg);
                    Assert.IsTrue(a == BrowserAction.Skip || a == BrowserAction.Deprioritize || a == BrowserAction.GracefulClose);
                }
        }

        [TestMethod]
        public void Parse_Defaults_To_Deprioritize_On_Junk_Or_Empty()
        {
            // The safe default: a missing/garbage settings value must never mean "close browsers".
            Assert.AreEqual(BrowserPolicy.Deprioritize, BrowserGovernor.ParsePolicy(null));
            Assert.AreEqual(BrowserPolicy.Deprioritize, BrowserGovernor.ParsePolicy(""));
            Assert.AreEqual(BrowserPolicy.Deprioritize, BrowserGovernor.ParsePolicy("nonsense"));
            Assert.AreEqual(BrowserPolicy.Off,           BrowserGovernor.ParsePolicy("off"));
            Assert.AreEqual(BrowserPolicy.GracefulClose, BrowserGovernor.ParsePolicy("gracefulclose"));
            Assert.AreEqual(BrowserPolicy.GracefulClose, BrowserGovernor.ParsePolicy("GracefulClose"));
        }
    }
}
