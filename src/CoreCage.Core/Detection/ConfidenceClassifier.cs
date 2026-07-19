using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core.Detection
{
    /// <summary>
    /// The activity mode the system is currently in. <see cref="Normal"/> is the
    /// safe default whenever no high-confidence interactive session is detected.
    /// </summary>
    public enum ActivityMode
    {
        Normal = 0,
        Gaming,
        Coding
    }

    /// <summary>
    /// Coarse launcher / parent-context hint for the foreground process. This is
    /// deliberately decoupled from how it is actually collected (Steam overlay,
    /// parent-process tree, install path, etc.) so the decision logic stays pure.
    /// </summary>
    public enum LauncherContext
    {
        None = 0,
        Steam,
        Epic,
        OtherGameStore,   // GOG / Xbox / EA / Ubisoft / Battle.net
        VsCode,
        Terminal,         // Windows Terminal / cmd / pwsh / WSL
        Ide               // Visual Studio / Rider / JetBrains
    }

    /// <summary>
    /// A snapshot of the raw signals used to classify the current activity mode.
    /// Pure data — it carries no logic and performs no system calls, so the
    /// classifier can be exercised with synthetic data. Collectors that populate
    /// these fields from live system state come later and live elsewhere.
    /// </summary>
    public readonly struct SignalSnapshot
    {
        // ── Stage 1: session intent (foreground window state) ────────────────
        /// <summary>Foreground executable file name, e.g. "cs2.exe". Case-insensitive.</summary>
        public string ForegroundExe { get; init; }
        /// <summary>Foreground process display/base name, e.g. "Code", "NinjaTrader".</summary>
        public string ForegroundProcessName { get; init; }
        /// <summary>True when the foreground window is exclusive or borderless fullscreen.</summary>
        public bool IsFullscreen { get; init; }
        /// <summary>Milliseconds since the foreground window last changed. Lower = more recent focus.</summary>
        public int FocusChangedMsAgo { get; init; }

        // ── Stage 2: workload validation ─────────────────────────────────────
        /// <summary>Sustained GPU load, 0–100.</summary>
        public double GpuLoadPct { get; init; }
        /// <summary>Raw input events per second (mouse + keyboard).</summary>
        public double InputRatePerSec { get; init; }
        /// <summary>Recent presented frames per second (frame-pacing signal); 0 if unknown.</summary>
        public double FramesPerSec { get; init; }
        /// <summary>Sustained CPU load, 0–100.</summary>
        public double CpuLoadPct { get; init; }

        // ── Stage 3: process identity / environment ──────────────────────────
        /// <summary>Launcher / parent-process context for the foreground process.</summary>
        public LauncherContext LauncherContext { get; init; }
        /// <summary>True when a compiler/build or active terminal process is running prominently.</summary>
        public bool CompilerOrTerminalActive { get; init; }
        /// <summary>Count of physical monitors with active windows for the session.</summary>
        public int FocusedMonitorCount { get; init; }
    }

    /// <summary>The result of a single classification pass.</summary>
    public readonly struct ModeDecision
    {
        public ActivityMode Mode { get; }
        /// <summary>Confidence in <see cref="Mode"/>, 0.0–1.0.</summary>
        public double Confidence { get; }
        /// <summary>One-line, human-readable explanation of why this mode won.</summary>
        public string Why { get; }
        /// <summary>Raw 0–1 score for every candidate mode (before hysteresis/cooldown).</summary>
        public IReadOnlyDictionary<ActivityMode, double> PerModeScores { get; }

        public ModeDecision(ActivityMode mode, double confidence, string why,
                            IReadOnlyDictionary<ActivityMode, double> perModeScores)
        {
            Mode = mode;
            Confidence = confidence;
            Why = why;
            PerModeScores = perModeScores;
        }
    }

    /// <summary>
    /// A 3-stage, confidence-scored classifier that decides the current activity
    /// MODE (Gaming / Coding / Normal). It is reframed to score
    /// "is the system in an interactive high-performance session, and which kind?"
    /// rather than asking "is this a specific game?".
    ///
    /// The three stages each contribute to a 0–1 score per mode:
    ///   1. Session intent      — fullscreen state + focus recency.
    ///   2. Workload validation — sustained GPU load, input cadence, frame pacing.
    ///   3. Process identity    — foreground process + launcher/feed context.
    ///
    /// <see cref="Classify"/> is a pure function of a single snapshot. The stateful
    /// <see cref="ModeClassifier"/> wraps it with thresholds, hysteresis and a
    /// cooldown so the live mode never flip-flops on a single noisy sample.
    /// </summary>
    public static class ConfidenceClassifier
    {
        // ── Tunables (pure constants, easy to reason about / test) ───────────
        /// <summary>A candidate mode below this score collapses to Normal.</summary>
        public const double DecisionThreshold = 0.55;
        /// <summary>"Recent" focus change window for the session-intent stage.</summary>
        public const int RecentFocusMs = 5000;
        /// <summary>GPU load that counts as a sustained high-performance workload.</summary>
        public const double HighGpuLoadPct = 60.0;
        /// <summary>Input cadence that indicates an actively-driven interactive session.</summary>
        public const double ActiveInputRate = 3.0;

        private static double Clamp01(double v) => double.IsNaN(v) ? 0 : (v < 0 ? 0 : (v > 1 ? 1 : v));

        /// <summary>
        /// Pure, stateless classification of a single snapshot. No hysteresis,
        /// cooldown or live system calls — given the same snapshot it always
        /// returns the same decision.
        /// </summary>
        public static ModeDecision Classify(SignalSnapshot s)
        {
            double gaming = ScoreGaming(s, out string gWhy);
            double coding = ScoreCoding(s, out string cWhy);

            var scores = new Dictionary<ActivityMode, double>
            {
                [ActivityMode.Gaming] = gaming,
                [ActivityMode.Coding] = coding,
                [ActivityMode.Normal] = NormalFloor(gaming, coding),
            };

            // Winner among the two active modes.
            ActivityMode best = ActivityMode.Gaming;
            double bestScore = gaming;
            string bestWhy = gWhy;
            if (coding > bestScore) { best = ActivityMode.Coding; bestScore = coding; bestWhy = cWhy; }

            if (bestScore < DecisionThreshold)
            {
                return new ModeDecision(
                    ActivityMode.Normal,
                    scores[ActivityMode.Normal],
                    $"No high-confidence session (best candidate {best} {bestScore:0.00} < {DecisionThreshold:0.00}).",
                    scores);
            }

            return new ModeDecision(best, bestScore, bestWhy, scores);
        }

        // ── Per-mode scoring ─────────────────────────────────────────────────

        private static double ScoreGaming(SignalSnapshot s, out string why)
        {
            // Stage 1: session intent — fullscreen + recent focus.
            double intent = 0;
            if (s.IsFullscreen) intent += 0.6;
            if (s.FocusChangedMsAgo <= RecentFocusMs) intent += 0.4;
            intent = Clamp01(intent);

            // Stage 2: workload — GPU load is the PRIMARY, always-available signal (a fullscreen GPU-heavy
            // app IS an interactive high-perf session). FPS/input are optional CONFIRMATION bonuses: when the
            // collector can't read them (stubbed → 0) they must NOT drag the score down. So GPU alone reaches ~1.
            double workload = Clamp01(s.GpuLoadPct / HighGpuLoadPct);       // 60%+ GPU → ~1.0
            if (s.FramesPerSec >= 30) workload = Clamp01(workload + 0.15);  // bonus only, never a penalty
            if (s.InputRatePerSec >= ActiveInputRate) workload = Clamp01(workload + 0.10);

            // Stage 3: identity — a KNOWN launcher boosts confidence, but an UNKNOWN launcher is NOT evidence
            // against a game (new titles, EA/Battle.net/standalone exes). Reframe: "is this a high-perf
            // session?" not "is this a *known* game?". Unknown sits at neutral 0.5, not a penalizing 0.2 —
            // this is what makes "detect ANY game with no hardcoded list" actually work.
            double identity = s.LauncherContext switch
            {
                LauncherContext.Steam => 1.0,
                LauncherContext.Epic => 1.0,
                LauncherContext.OtherGameStore => 0.9,
                _ => 0.5
            };

            // Fullscreen + sustained GPU dominate; identity is a lighter tiebreaker (not a gate).
            double score = Clamp01(0.40 * intent + 0.45 * workload + 0.15 * identity);
            why = $"Gaming: fullscreen={s.IsFullscreen}, GPU={s.GpuLoadPct:0}%, " +
                  $"fps={s.FramesPerSec:0}, launcher={s.LauncherContext}.";
            return score;
        }

        private static double ScoreCoding(SignalSnapshot s, out string why)
        {
            // Stage 1: a code editor / IDE in focus (windowed, not fullscreen game).
            double intent = 0;
            if (s.LauncherContext is LauncherContext.VsCode or LauncherContext.Ide or LauncherContext.Terminal)
                intent += 0.7;
            if (!s.IsFullscreen) intent += 0.3; // coding is windowed work
            intent = Clamp01(intent);

            // Stage 2: compiler/terminal dominant + moderate CPU, low GPU, keyboard-led input.
            double workload = 0;
            if (s.CompilerOrTerminalActive) workload += 0.5;
            if (s.GpuLoadPct < HighGpuLoadPct) workload += 0.2; // not a GPU session
            if (s.InputRatePerSec >= ActiveInputRate) workload += 0.3; // active typing
            workload = Clamp01(workload);

            // Stage 3: identity.
            double identity = s.LauncherContext switch
            {
                LauncherContext.VsCode => 1.0,
                LauncherContext.Ide => 1.0,
                LauncherContext.Terminal => 0.8,
                _ => 0.1
            };

            double score = Clamp01(0.30 * intent + 0.40 * workload + 0.30 * identity);
            why = $"Coding: editor={s.LauncherContext}, compilerOrTerminal={s.CompilerOrTerminalActive}, " +
                  $"GPU={s.GpuLoadPct:0}%, input={s.InputRatePerSec:0}/s.";
            return score;
        }

        /// <summary>
        /// Normal is the complement of the best active candidate: the less any
        /// real mode fits, the more confident we are it's just a Normal session.
        /// </summary>
        private static double NormalFloor(double gaming, double coding)
        {
            double best = Math.Max(gaming, coding);
            return Clamp01(1.0 - best);
        }
    }

    /// <summary>
    /// Stateful wrapper around <see cref="ConfidenceClassifier.Classify"/> that
    /// prevents flip-flopping. It applies:
    ///   • HYSTERESIS — a new mode must beat the current mode's score by a margin
    ///     for N consecutive samples before the switch is accepted.
    ///   • COOLDOWN   — once switched, the mode is held for a minimum dwell time
    ///     regardless of incoming samples.
    /// <see cref="Update"/> is the live entry point; it returns the *effective*
    /// mode (after stabilisation) while still reporting the raw per-mode scores.
    /// </summary>
    public sealed class ModeClassifier
    {
        private readonly double _switchMargin;
        private readonly int _consecutiveSamplesToSwitch;
        private readonly TimeSpan _cooldown;
        private readonly Func<DateTime> _now;

        private ActivityMode _currentMode = ActivityMode.Normal;
        private double _currentConfidence;
        private DateTime _lastSwitchAt = DateTime.MinValue;

        // Candidate the snapshots have been "voting" for, and the vote count.
        private ActivityMode _pendingCandidate = ActivityMode.Normal;
        private int _pendingStreak;

        /// <param name="switchMargin">
        /// How much a challenger's raw score must exceed the current mode's raw
        /// score to count as a switch vote. Default 0.15.</param>
        /// <param name="consecutiveSamplesToSwitch">
        /// Consecutive winning samples required before the switch commits.
        /// Default 3 — a single blip cannot flip the mode.</param>
        /// <param name="cooldown">
        /// Minimum dwell time in the current mode after a switch. Default 20s.</param>
        /// <param name="nowProvider">Clock override for deterministic tests.</param>
        public ModeClassifier(
            double switchMargin = 0.15,
            int consecutiveSamplesToSwitch = 3,
            TimeSpan? cooldown = null,
            Func<DateTime>? nowProvider = null)
        {
            _switchMargin = switchMargin;
            _consecutiveSamplesToSwitch = Math.Max(1, consecutiveSamplesToSwitch);
            _cooldown = cooldown ?? TimeSpan.FromSeconds(20);
            _now = nowProvider ?? (() => DateTime.UtcNow);
        }

        /// <summary>The mode currently in effect after stabilisation.</summary>
        public ActivityMode CurrentMode => _currentMode;

        /// <summary>
        /// Feed one snapshot. Returns the effective decision: <see cref="ModeDecision.Mode"/>
        /// is the stabilised mode, while <see cref="ModeDecision.PerModeScores"/> still
        /// reflects this snapshot's raw scores.
        /// </summary>
        public ModeDecision Update(SignalSnapshot snapshot)
        {
            ModeDecision raw = ConfidenceClassifier.Classify(snapshot);
            DateTime now = _now();

            // First-ever sample: adopt directly (no flip-flop risk yet).
            if (_lastSwitchAt == DateTime.MinValue)
            {
                Commit(raw.Mode, raw.Confidence, now);
                return Effective(raw, "initial sample adopted");
            }

            double currentRawScore = raw.PerModeScores.TryGetValue(_currentMode, out var cs) ? cs : 0.0;

            // No change proposed, or challenger doesn't clear the hysteresis margin → reset streak.
            if (raw.Mode == _currentMode || raw.Confidence < currentRawScore + _switchMargin)
            {
                _pendingCandidate = _currentMode;
                _pendingStreak = 0;
                _currentConfidence = raw.Mode == _currentMode ? raw.Confidence : _currentConfidence;
                return Effective(raw, $"holding {_currentMode} (challenger lacks margin)");
            }

            // A genuine challenger. Count consecutive votes.
            if (raw.Mode == _pendingCandidate) _pendingStreak++;
            else { _pendingCandidate = raw.Mode; _pendingStreak = 1; }

            // Cooldown — refuse to switch until the minimum dwell time has elapsed.
            if (now - _lastSwitchAt < _cooldown)
                return Effective(raw, $"cooldown active, holding {_currentMode}");

            // Hysteresis — require N consecutive winning samples.
            if (_pendingStreak >= _consecutiveSamplesToSwitch)
            {
                Commit(raw.Mode, raw.Confidence, now);
                _pendingStreak = 0;
                return Effective(raw, $"switched to {_currentMode} after {_consecutiveSamplesToSwitch} samples");
            }

            return Effective(raw, $"challenger {_pendingCandidate} streak {_pendingStreak}/{_consecutiveSamplesToSwitch}");
        }

        private void Commit(ActivityMode mode, double confidence, DateTime now)
        {
            _currentMode = mode;
            _currentConfidence = confidence;
            _lastSwitchAt = now;
            _pendingCandidate = mode;
        }

        private ModeDecision Effective(ModeDecision raw, string note) =>
            new ModeDecision(_currentMode, _currentConfidence, $"{raw.Why} [{note}]", raw.PerModeScores);
    }
}
