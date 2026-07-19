using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Detection;
using System;

namespace CoreCage.Tests.Detection
{
    [TestClass]
    public class ConfidenceClassifierTests
    {
        // ── Synthetic snapshot builders ──────────────────────────────────────

        private static SignalSnapshot Gaming() => new()
        {
            ForegroundExe = "cs2.exe",
            ForegroundProcessName = "cs2",
            IsFullscreen = true,
            FocusChangedMsAgo = 500,
            GpuLoadPct = 92,
            InputRatePerSec = 40,
            FramesPerSec = 240,
            CpuLoadPct = 55,
            LauncherContext = LauncherContext.Steam,
            CompilerOrTerminalActive = false,
            FocusedMonitorCount = 1,
        };

        // Real-world "detect ANY game": a NEW game (Battlefield 6 from EA, Arc Raiders, Marathon) — fullscreen
        // + GPU-heavy, but UNKNOWN launcher and the collector's FPS/input are stubbed to 0. Must still be
        // Gaming with HIGH confidence — the whole point of "is this an interactive high-perf session?".
        private static SignalSnapshot UnknownGame() => new()
        {
            ForegroundExe = "bf6.exe",
            ForegroundProcessName = "bf6",
            IsFullscreen = true,
            FocusChangedMsAgo = 500,
            GpuLoadPct = 84,
            InputRatePerSec = 0,                       // stubbed by collector
            FramesPerSec = 0,                          // stubbed by collector
            CpuLoadPct = 45,
            LauncherContext = LauncherContext.None,    // unknown launcher (not Steam/Epic)
            CompilerOrTerminalActive = false,
            FocusedMonitorCount = 1,
        };

        private static SignalSnapshot Coding() => new()
        {
            ForegroundExe = "Code.exe",
            ForegroundProcessName = "Code",
            IsFullscreen = false,
            FocusChangedMsAgo = 800,
            GpuLoadPct = 8,
            InputRatePerSec = 12,
            FramesPerSec = 0,
            CpuLoadPct = 40,
            LauncherContext = LauncherContext.VsCode,
            CompilerOrTerminalActive = true,
            FocusedMonitorCount = 2,
        };

        private static SignalSnapshot Idle() => new()
        {
            ForegroundExe = "explorer.exe",
            ForegroundProcessName = "explorer",
            IsFullscreen = false,
            FocusChangedMsAgo = 60000,
            GpuLoadPct = 2,
            InputRatePerSec = 0,
            FramesPerSec = 0,
            CpuLoadPct = 3,
            LauncherContext = LauncherContext.None,
            CompilerOrTerminalActive = false,
            FocusedMonitorCount = 1,
        };

        // Ambiguous: a windowed browser, some GPU, no launcher/feed context.
        private static SignalSnapshot Ambiguous() => new()
        {
            ForegroundExe = "brave.exe",
            ForegroundProcessName = "brave",
            IsFullscreen = false,
            FocusChangedMsAgo = 8000,
            GpuLoadPct = 18,
            InputRatePerSec = 1,
            FramesPerSec = 0,
            CpuLoadPct = 20,
            LauncherContext = LauncherContext.None,
            CompilerOrTerminalActive = false,
            FocusedMonitorCount = 1,
        };

        // ── Pure Classify() tests ────────────────────────────────────────────

        [TestMethod]
        public void Clear_Gaming_Snapshot_Yields_Gaming_HighConfidence()
        {
            var d = ConfidenceClassifier.Classify(Gaming());
            Assert.AreEqual(ActivityMode.Gaming, d.Mode);
            Assert.IsTrue(d.Confidence >= 0.8, $"expected high confidence, got {d.Confidence:0.00}");
            Assert.IsTrue(d.PerModeScores.ContainsKey(ActivityMode.Coding));
        }

        [TestMethod]
        public void Unknown_Fullscreen_GpuHeavy_Game_Yields_Gaming_HighConfidence()
        {
            // The "detect ANY game" promise: a focused fullscreen GPU-heavy app is a high-perf gaming
            // session regardless of launcher or stubbed FPS/input. Must be Gaming with HIGH confidence.
            var d = ConfidenceClassifier.Classify(UnknownGame());
            Assert.AreEqual(ActivityMode.Gaming, d.Mode, $"why: {d.Why}");
            Assert.IsTrue(d.Confidence >= 0.8, $"focused fullscreen GPU-heavy game (any launcher) must score high; got {d.Confidence:0.00}");
        }

        [TestMethod]
        public void Coding_Snapshot_Yields_Coding()
        {
            var d = ConfidenceClassifier.Classify(Coding());
            Assert.AreEqual(ActivityMode.Coding, d.Mode);
            Assert.IsTrue(d.Confidence >= ConfidenceClassifier.DecisionThreshold);
        }

        [TestMethod]
        public void Idle_Snapshot_Yields_Normal()
        {
            var d = ConfidenceClassifier.Classify(Idle());
            Assert.AreEqual(ActivityMode.Normal, d.Mode);
        }

        [TestMethod]
        public void Ambiguous_LowSignal_Yields_Normal_With_Low_Confidence()
        {
            var d = ConfidenceClassifier.Classify(Ambiguous());
            Assert.AreEqual(ActivityMode.Normal, d.Mode);
            // No active mode should clear the decision threshold on a low-signal snapshot.
            Assert.IsTrue(d.PerModeScores[ActivityMode.Gaming] < ConfidenceClassifier.DecisionThreshold);
            Assert.IsTrue(d.PerModeScores[ActivityMode.Coding] < ConfidenceClassifier.DecisionThreshold);
        }

        // ── Stateful ModeClassifier: hysteresis & cooldown ───────────────────

        [TestMethod]
        public void Hysteresis_SingleBlip_Does_Not_Flip_Mode()
        {
            // Fixed clock so cooldown never interferes with the hysteresis check.
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var c = new ModeClassifier(nowProvider: () => clock);

            // Establish a stable Coding baseline.
            c.Update(Coding());
            Assert.AreEqual(ActivityMode.Coding, c.CurrentMode);

            // One single Gaming blip — must NOT switch (needs 3 consecutive).
            var d = c.Update(Gaming());
            Assert.AreEqual(ActivityMode.Coding, d.Mode);
            Assert.AreEqual(ActivityMode.Coding, c.CurrentMode);
        }

        [TestMethod]
        public void Hysteresis_Sustained_Challenger_Eventually_Switches()
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // Advance clock past the cooldown on each sample so only hysteresis gates.
            var c = new ModeClassifier(
                consecutiveSamplesToSwitch: 3,
                cooldown: TimeSpan.FromSeconds(1),
                nowProvider: () => t);

            c.Update(Coding());
            t = t.AddSeconds(5); c.Update(Gaming());           // streak 1
            t = t.AddSeconds(5); c.Update(Gaming());           // streak 2
            t = t.AddSeconds(5); var d = c.Update(Gaming());   // streak 3 → switch

            Assert.AreEqual(ActivityMode.Gaming, d.Mode);
            Assert.AreEqual(ActivityMode.Gaming, c.CurrentMode);
        }

        [TestMethod]
        public void Cooldown_Prevents_Switch_Within_Dwell_Time()
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var c = new ModeClassifier(
                consecutiveSamplesToSwitch: 1,   // hysteresis satisfied immediately
                cooldown: TimeSpan.FromSeconds(30),
                nowProvider: () => t);

            // Initial Coding adoption stamps lastSwitchAt = t.
            c.Update(Coding());
            Assert.AreEqual(ActivityMode.Coding, c.CurrentMode);

            // Strong Gaming challengers within the 30s dwell window — held by cooldown.
            t = t.AddSeconds(5); c.Update(Gaming());
            t = t.AddSeconds(5); var held = c.Update(Gaming());
            Assert.AreEqual(ActivityMode.Coding, held.Mode);

            // After cooldown elapses, the next challenger switches.
            t = t.AddSeconds(40); var sw = c.Update(Gaming());
            Assert.AreEqual(ActivityMode.Gaming, sw.Mode);
        }
    }
}
