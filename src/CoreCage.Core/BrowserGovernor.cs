using System;

namespace CoreCage.Core
{
    /// <summary>How Gaming Mode may treat a competing browser. Ships DEPRIORITIZE.</summary>
    public enum BrowserPolicy
    {
        /// <summary>Leave every browser completely alone.</summary>
        Off,
        /// <summary>Drop background browsers to Idle priority / non-game cores (reversible). Default.</summary>
        Deprioritize,
        /// <summary>Ask background browsers to close themselves (WM_CLOSE — they prompt/save). Opt-in.</summary>
        GracefulClose,
    }

    /// <summary>The concrete action for ONE browser process. There is deliberately no Kill.</summary>
    public enum BrowserAction
    {
        Skip,
        Deprioritize,
        GracefulClose,
    }

    /// <summary>
    /// Pure policy brain for the browser-kill redesign (docs/BROWSER-KILL-REDESIGN.md). Given the
    /// user's policy and whether a process owns the foreground, it returns the single allowed action.
    /// No process I/O lives here — the caller does the OS work — so the rules stay provable:
    /// NEVER kill, and NEVER touch a foreground window. Born from a 2026-07-02 incident where
    /// a performance mode killed the user's foreground browser view.
    /// </summary>
    public static class BrowserGovernor
    {
        /// <summary>The one allowed action for a browser process under <paramref name="policy"/>.
        /// A foreground process is ALWAYS skipped, whatever the policy.</summary>
        public static BrowserAction Decide(BrowserPolicy policy, bool isForeground)
        {
            if (isForeground) return BrowserAction.Skip;   // hard rule — never the focused window
            return policy switch
            {
                BrowserPolicy.Deprioritize  => BrowserAction.Deprioritize,
                BrowserPolicy.GracefulClose => BrowserAction.GracefulClose,
                _                           => BrowserAction.Skip,   // Off / unknown
            };
        }

        /// <summary>Parses the persisted setting. Anything missing or unrecognized falls back to the
        /// SAFE default (Deprioritize) — a corrupt settings value must never mean "close browsers".</summary>
        public static BrowserPolicy ParsePolicy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return BrowserPolicy.Deprioritize;
            return raw.Trim().ToLowerInvariant() switch
            {
                "off"           => BrowserPolicy.Off,
                "gracefulclose" => BrowserPolicy.GracefulClose,
                "deprioritize"  => BrowserPolicy.Deprioritize,
                _               => BrowserPolicy.Deprioritize,
            };
        }
    }
}
