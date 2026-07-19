using System;
using System.Collections.Generic;
using System.Timers;

namespace CoreCage.Core.Detection
{
    /// <summary>
    /// Live brain-stem of the multi-mode redesign. On a timer it: captures live signals
    /// (<see cref="SignalCollector"/>), classifies the activity mode with confidence + hysteresis +
    /// cooldown (<see cref="ModeClassifier"/>), and applies the matching tweak policy
    /// (<see cref="ModePolicy"/>) when the active mode changes. Auto-detects by default; supports a
    /// manual lock/override. Raises events for the StatusDeck UI. Purely additive — wires the three
    /// new detection pieces together without touching any existing pipeline.
    /// </summary>
    public sealed class ModeCoordinator : IDisposable
    {
        private readonly SignalCollector _collector;
        private readonly ModeClassifier _classifier;
        private readonly ModePolicy _policy;
        private readonly System.Timers.Timer _timer;
        private readonly object _gate = new object();

        private bool _locked;
        private ActivityMode _lockedMode = ActivityMode.Normal;
        private bool _appliedOnce;
        private ActivityMode _activeMode = ActivityMode.Normal;

        /// <summary>Latest classifier decision (refreshed every poll) — drives the live confidence ring.</summary>
        public ModeDecision Current { get; private set; }

        /// <summary>The mode whose policy is currently applied to the system.</summary>
        public ActivityMode ActiveMode => _appliedOnce ? _activeMode : ActivityMode.Normal;

        /// <summary>True while a manual lock/override is in effect.</summary>
        public bool IsLocked { get { lock (_gate) { return _locked; } } }

        /// <summary>
        /// When true (default), mode changes are applied to the system. When false, detection still runs
        /// and events still fire, but NO tweaks are applied — a safe "shadow"/observe mode for validation.
        /// </summary>
        public bool AutoApply { get; set; } = true;

        /// <summary>Fires every poll with the latest decision (for live confidence/why UI updates).</summary>
        public event EventHandler<ModeDecision> Ticked;

        /// <summary>Fires only when the ACTIVE (applied) mode actually changes.</summary>
        public event EventHandler<ActivityMode> ModeChanged;

        public ModeCoordinator(SignalCollector collector = null, ModeClassifier classifier = null,
                               ModePolicy policy = null, double intervalMs = 2000)
        {
            _collector = collector ?? new SignalCollector();
            _classifier = classifier ?? new ModeClassifier();
            _policy = policy ?? new ModePolicy();
            _timer = new System.Timers.Timer(intervalMs) { AutoReset = true };
            _timer.Elapsed += (_, __) => Poll();
        }

        /// <summary>Begin auto-detection polling.</summary>
        public void Start() => _timer.Start();

        /// <summary>Stop polling (does not restore tweaks — call <see cref="Restore"/> for that).</summary>
        public void Stop() => _timer.Stop();

        /// <summary>Pin a mode manually (overrides auto-detection) and apply it immediately.</summary>
        public void Lock(ActivityMode mode)
        {
            lock (_gate) { _locked = true; _lockedMode = mode; }
            ApplyMode(mode);
        }

        /// <summary>Release the manual lock; the next poll resumes auto-detection.</summary>
        public void Unlock()
        {
            lock (_gate) { _locked = false; }
        }

        /// <summary>Human-readable active rules for the currently applied mode (for the UI).</summary>
        public IReadOnlyList<string> CurrentActions() => _policy.DescribeActions(ActiveMode);

        /// <summary>Optional: feed the real ForegroundWatcher event for instant focus-change timing.</summary>
        public void NoteForegroundChanged(string exe) => _collector.NoteForegroundChanged(exe);

        /// <summary>Full baseline restore (undo all tweaks) and drop to Normal.</summary>
        public void Restore()
        {
            try
            {
                _policy.Restore();
                _appliedOnce = true;
                _activeMode = ActivityMode.Normal;
                ModeChanged?.Invoke(this, ActivityMode.Normal);
            }
            catch (Exception ex)
            {
                Logger.Log("[ModeCoordinator] restore error: " + ex.Message);
            }
        }

        private void Poll()
        {
            try
            {
                SignalSnapshot snap = _collector.Capture();
                ModeDecision decision = _classifier.Update(snap);
                Current = decision;
                Ticked?.Invoke(this, decision);

                bool locked; ActivityMode lockedMode;
                lock (_gate) { locked = _locked; lockedMode = _lockedMode; }
                ActivityMode target = locked ? lockedMode : decision.Mode;

                if (!_appliedOnce || target != _activeMode)
                    ApplyMode(target);
            }
            catch (Exception ex)
            {
                Logger.Log("[ModeCoordinator] poll error: " + ex.Message);
            }
        }

        private void ApplyMode(ActivityMode mode)
        {
            try
            {
                if (AutoApply) _policy.Apply(mode);
                _appliedOnce = true;
                _activeMode = mode;
                Logger.Log("[ModeCoordinator] active mode -> " + mode + (AutoApply ? "" : " (shadow: not applied)"));
                ModeChanged?.Invoke(this, mode);
            }
            catch (Exception ex)
            {
                Logger.Log("[ModeCoordinator] apply error (" + mode + "): " + ex.Message);
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}
