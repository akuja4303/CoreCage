using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    [TestClass]
    public class ProcessWatcherTests
    {
        [TestMethod]
        public void StripExe_Removes_Extension_And_Trims()
        {
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("PioneerGame-d.exe"));
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("  PioneerGame-d.exe  "));
            Assert.AreEqual("PioneerGame-d", ProcessWatcher.StripExe("PioneerGame-d")); // already bare
        }

        [TestMethod]
        public void StripExe_Is_Case_Insensitive_On_Extension()
        {
            // GetProcessesByName / WMI casing varies; the .EXE strip must not depend on case.
            Assert.AreEqual("Game", ProcessWatcher.StripExe("Game.EXE"));
            Assert.AreEqual("Game", ProcessWatcher.StripExe("Game.Exe"));
        }

        [TestMethod]
        public void StripExe_Handles_Null_And_Empty()
        {
            Assert.AreEqual("", ProcessWatcher.StripExe(null));
            Assert.AreEqual("", ProcessWatcher.StripExe(""));
            Assert.AreEqual("", ProcessWatcher.StripExe("   "));
        }

        // ── ClassifyFromSignals — the pure classifier extracted from ClassifyProcess ──
        // These lock the detection rules WITHOUT needing a live process, which is exactly
        // what makes anti-cheat games testable: they block MainModule/Modules, so the only
        // signals that survive are the exe PATH (store folder) and the on-disk version info.

        [TestMethod]
        public void Classify_NamePattern_Matches_Known_Game()
        {
            // Helldivers 2's real exe is "helldivers2" — must classify off the name alone.
            Assert.AreEqual(ProcessCategory.Game,
                ProcessWatcher.ClassifyFromSignals("helldivers2", exePath: null, versionInfoText: null, usesGraphicsApi: false));
        }

        [TestMethod]
        public void Classify_StorePath_Detects_AntiCheat_Game_With_No_Process_Access()
        {
            // THE regression under test: an anti-cheat game we can't read modules/version from,
            // not in the name list — but its exe lives under a Steam store folder. Must be a Game.
            Assert.AreEqual(ProcessCategory.Game,
                ProcessWatcher.ClassifyFromSignals(
                    "SomeAntiCheatGame",
                    exePath: @"D:\SteamLibrary\steamapps\common\SomeAntiCheatGame\game.exe",
                    versionInfoText: null,
                    usesGraphicsApi: false));
        }

        [TestMethod]
        public void Classify_Publisher_Detects_Game_From_Disk_VersionInfo()
        {
            Assert.AreEqual(ProcessCategory.Game,
                ProcessWatcher.ClassifyFromSignals(
                    "mysterygame",
                    exePath: @"C:\Games\mysterygame\game.exe",
                    versionInfoText: "cool shooter respawn entertainment",
                    usesGraphicsApi: false));
        }

        [TestMethod]
        public void Classify_SystemProcess_Denylist_Wins_Even_Under_StorePath()
        {
            // A browser dropped in a store folder must never trigger Gaming Mode.
            Assert.AreEqual(ProcessCategory.Unknown,
                ProcessWatcher.ClassifyFromSignals(
                    "chrome",
                    exePath: @"D:\SteamLibrary\steamapps\common\weird\chrome.exe",
                    versionInfoText: null,
                    usesGraphicsApi: true));
        }

        [TestMethod]
        public void Classify_GraphicsApi_Only_Is_Game_NonStrict_But_Not_Strict()
        {
            Assert.AreEqual(ProcessCategory.Game,
                ProcessWatcher.ClassifyFromSignals("randomrenderer", @"C:\app\r.exe", null, usesGraphicsApi: true, strict: false));
            Assert.AreEqual(ProcessCategory.Unknown,
                ProcessWatcher.ClassifyFromSignals("randomrenderer", @"C:\app\r.exe", null, usesGraphicsApi: true, strict: true));
        }

        [TestMethod]
        public void Classify_Plain_Tool_Is_Unknown()
        {
            Assert.AreEqual(ProcessCategory.Unknown,
                ProcessWatcher.ClassifyFromSignals(
                    "randomtool",
                    exePath: @"C:\tools\randomtool.exe",
                    versionInfoText: "some helpful utility",
                    usesGraphicsApi: false));
        }

        [TestMethod]
        public void Classify_Is_Null_Safe_On_Path_And_VersionInfo()
        {
            // Path resolution can fail entirely on a hardened process — must not throw.
            Assert.AreEqual(ProcessCategory.Unknown,
                ProcessWatcher.ClassifyFromSignals("whoknows", exePath: null, versionInfoText: null, usesGraphicsApi: false));
        }

        [TestMethod]
        public void TryGetExecutablePath_Resolves_Current_Process()
        {
            // QueryFullProcessImageName must return this test host's own path (sanity that the P/Invoke works).
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            string? path = ProcessWatcher.TryGetExecutablePath(pid);
            Assert.IsFalse(string.IsNullOrEmpty(path), "Expected a resolved path for the current process.");
            Assert.IsTrue(path!.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase), $"Unexpected path: {path}");
        }
    }
}
