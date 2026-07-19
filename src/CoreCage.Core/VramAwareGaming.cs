using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;

namespace CoreCage.Core
{
    /// <summary>
    /// Feature 1 of the 2026-06-19 workstation/VRAM design: on game launch, FREE VRAM so game
    /// textures don't spill to system RAM. This is the literal fix for the live STRAFTAT stutter —
    /// VRAM was 7806/8192 MB (95%), a 4 GB Ollama model the culprit; unloading it restored frames.
    /// No other gaming optimizer handles LLM VRAM — this is CoreCage's differentiator.
    ///
    /// Three reversible levers, each SNAPSHOTTED for exact restore (same record-before-write pattern
    /// as SystemTweaks.RestoreThrottledProcesses):
    ///   1. Unload Ollama models (keep_alive:0) — snapshot which were loaded, re-warm on exit.
    ///   2. Suspend Wallpaper Engine (wallpaper64.exe) — resume on exit.
    ///   3. Deprioritize browser/Electron VRAM hogs to BelowNormal — NEVER kill (Gmail/charts live there).
    ///
    /// Freed-VRAM delta is MEASURED via nvidia-smi before/after (measured, not claimed). Every step is
    /// best-effort + logged; never throws. 🔒 System change — wired but apply-gated until Nate OKs.
    /// </summary>
    public static class VramAwareGaming
    {
        private const string OllamaBase = "http://localhost:11434";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        // Same nvidia-smi resolution PerformanceTuner uses.
        private static readonly string NvSmi =
            File.Exists(@"C:\Windows\System32\nvidia-smi.exe")
                ? @"C:\Windows\System32\nvidia-smi.exe"
                : @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";

        // ── Snapshot of what Apply() changed, for an exact Restore() ───────────
        private static readonly object _lock = new object();
        private static readonly List<string> _unloadedModels = new List<string>();             // re-warm on exit
        private static readonly HashSet<int> _suspendedPids = new HashSet<int>();               // Wallpaper Engine
        private static readonly Dictionary<int, ProcessPriorityClass> _deprioritized = new();   // browsers/Electron

        // Browser + heavy consumer-Electron GPU/VRAM hogs we GENTLY deprioritize.
        // NOTE: these overlap ProcessWatcher's never-touch list (which protects them from the BULK
        // ThrottleForMode), but VRAM-aware Gaming deliberately targets browsers per the design doc
        // ("deprioritize, don't kill — Gmail/charts live there"). Dev tools (devenv/code/rider) are
        // intentionally ABSENT here, so they're never buried — that honors "don't touch dev tools"
        // by construction. See Turn 19 note: flag if you'd rather VRAM mode skip browsers too.
        private static readonly HashSet<string> VramHogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedge", "chrome", "firefox", "brave", "opera", "vivaldi",
            "discord", "slack", "spotify", "whatsapp", "signal", "telegram",
        };

        // NtSuspendProcess/NtResumeProcess: whole-process freeze in one call (no per-thread walk).
        [DllImport("ntdll.dll")] private static extern uint NtSuspendProcess(IntPtr processHandle);
        [DllImport("ntdll.dll")] private static extern uint NtResumeProcess(IntPtr processHandle);

        // ── PUBLIC ENTRY POINTS ───────────────────────────────────────────────

        /// <summary>Free VRAM for gaming. Idempotent, best-effort, never throws.</summary>
        public static void Apply()
        {
            Logger.Log("=== VRAM-aware Gaming: freeing VRAM ===");
            int before = VramUsedMb();

            // 1. Unload Ollama models — snapshot which were loaded so we can re-warm on exit.
            try
            {
                var loaded = ListLoadedModels();
                lock (_lock) { _unloadedModels.Clear(); _unloadedModels.AddRange(loaded); }
                foreach (var m in loaded)
                {
                    UnloadModel(m);
                    Logger.Log("VRAM: unloaded Ollama model " + m + " (keep_alive:0)");
                }
                if (loaded.Count == 0) Logger.Log("VRAM: no Ollama models loaded (nothing to unload)");
            }
            catch (Exception ex) { Logger.Log("VRAM: Ollama unload failed: " + ex.Message); }

            // 2. Suspend Wallpaper Engine (GPU/VRAM hog while idle).
            try { SuspendWallpaperEngine(); }
            catch (Exception ex) { Logger.Log("VRAM: Wallpaper Engine suspend failed: " + ex.Message); }

            // 3. Deprioritize browser/Electron hogs to BelowNormal — NEVER kill.
            try { DeprioritizeVramHogs(); }
            catch (Exception ex) { Logger.Log("VRAM: deprioritize hogs failed: " + ex.Message); }

            // 4. MEASURED delta (Ollama eviction lags the HTTP return slightly — let it settle).
            Thread.Sleep(1500);
            int after = VramUsedMb();
            if (before >= 0 && after >= 0)
                Logger.Log($"VRAM-aware Gaming: {before} MB → {after} MB used (freed {Math.Max(0, before - after)} MB).");
            else
                Logger.Log("VRAM-aware Gaming: applied (nvidia-smi VRAM delta unavailable — non-NVIDIA or query failed).");
        }

        /// <summary>Reverse Apply() exactly: restore hog priorities, resume Wallpaper Engine, re-warm
        /// the models we unloaded. Best-effort, never throws.</summary>
        public static void Restore()
        {
            Logger.Log("=== VRAM-aware Gaming: restore ===");

            // Instant reversals first.
            try { RestoreVramHogs(); }
            catch (Exception ex) { Logger.Log("VRAM: restore hog priorities failed: " + ex.Message); }
            try { ResumeWallpaperEngine(); }
            catch (Exception ex) { Logger.Log("VRAM: resume Wallpaper Engine failed: " + ex.Message); }

            // Re-warm the models we unloaded (reversible: snapshot says they were loaded before gaming).
            List<string> models;
            lock (_lock) { models = new List<string>(_unloadedModels); _unloadedModels.Clear(); }
            foreach (var m in models)
            {
                ReloadModel(m);
                Logger.Log("VRAM: re-warmed Ollama model " + m);
            }
            if (models.Count == 0) Logger.Log("VRAM: no Ollama models to re-warm");
        }

        // ── Ollama ─────────────────────────────────────────────────────────────

        private static List<string> ListLoadedModels()
        {
            var names = new List<string>();
            string json = HttpGet(OllamaBase + "/api/ps");
            if (string.IsNullOrWhiteSpace(json)) return names;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var m in arr.EnumerateArray())
                        if (m.TryGetProperty("name", out var n) && n.GetString() is string s && !string.IsNullOrEmpty(s))
                            names.Add(s);
            }
            catch (Exception ex) { Logger.Log("VRAM: /api/ps parse failed: " + ex.Message); }
            return names;
        }

        // keep_alive:0 → unload immediately. No prompt needed.
        private static void UnloadModel(string model) =>
            HttpPostJson(OllamaBase + "/api/generate", "{\"model\":" + JsonEncode(model) + ",\"keep_alive\":0}");

        // No keep_alive → default; loads the model back into VRAM (re-warm).
        private static void ReloadModel(string model) =>
            HttpPostJson(OllamaBase + "/api/generate", "{\"model\":" + JsonEncode(model) + "}");

        // ── Wallpaper Engine ─────────────────────────────────────────────────

        private static void SuspendWallpaperEngine()
        {
            foreach (var p in Process.GetProcessesByName("wallpaper64"))
            {
                try
                {
                    if (NtSuspendProcess(p.Handle) == 0)
                    {
                        lock (_lock) { _suspendedPids.Add(p.Id); }
                        Logger.Log("VRAM: suspended Wallpaper Engine (pid " + p.Id + ")");
                    }
                }
                catch (Exception ex) { Logger.Log("VRAM: NtSuspendProcess wallpaper64 failed: " + ex.Message); }
                finally { p.Dispose(); }
            }
        }

        private static void ResumeWallpaperEngine()
        {
            List<int> pids;
            lock (_lock) { pids = new List<int>(_suspendedPids); _suspendedPids.Clear(); }
            foreach (int pid in pids)
            {
                try { using var p = Process.GetProcessById(pid); NtResumeProcess(p.Handle); }
                catch { /* exited — nothing to resume */ }
            }
        }

        // ── Browser / Electron deprioritize ──────────────────────────────────

        private static void DeprioritizeVramHogs()
        {
            int selfPid = Process.GetCurrentProcess().Id;
            int n = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == selfPid || !VramHogNames.Contains(p.ProcessName)) { p.Dispose(); continue; }
                    // Snapshot original priority ONCE before changing it.
                    lock (_lock) { if (!_deprioritized.ContainsKey(p.Id)) _deprioritized[p.Id] = p.PriorityClass; }
                    if (p.PriorityClass != ProcessPriorityClass.BelowNormal)
                    {
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        n++;
                    }
                    p.Dispose();
                }
                catch { try { p.Dispose(); } catch { } }
            }
            if (n > 0) Logger.Log($"VRAM: deprioritized {n} browser/Electron process(es) → BelowNormal (not killed)");
        }

        private static void RestoreVramHogs()
        {
            Dictionary<int, ProcessPriorityClass> snap;
            lock (_lock) { snap = new Dictionary<int, ProcessPriorityClass>(_deprioritized); _deprioritized.Clear(); }
            int restored = 0;
            foreach (var kv in snap)
            {
                try { using var p = Process.GetProcessById(kv.Key); p.PriorityClass = kv.Value; restored++; }
                catch { /* exited — skip */ }
            }
            if (restored > 0) Logger.Log($"VRAM: restored {restored} browser/Electron process(es) to original priority");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static int VramUsedMb()
        {
            string raw = RunNvSmiCapture("--query-gpu=memory.used --format=csv,noheader,nounits");
            var lines = raw.Split('\n');
            return (lines.Length > 0 && int.TryParse(lines[0].Trim(), out int mb)) ? mb : -1;
        }

        private static string RunNvSmiCapture(string args)
        {
            try
            {
                if (!File.Exists(NvSmi)) return "";
                var psi = new ProcessStartInfo(NvSmi, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(5000);
                return output.Trim();
            }
            catch { return ""; }
        }

        private static string HttpGet(string url)
        {
            try { return Http.GetStringAsync(url).GetAwaiter().GetResult(); }
            catch (Exception ex) { Logger.Log("VRAM: GET " + url + " failed: " + ex.Message); return ""; }
        }

        private static void HttpPostJson(string url, string json)
        {
            try
            {
                using var c = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                Http.PostAsync(url, c).GetAwaiter().GetResult();
            }
            catch (Exception ex) { Logger.Log("VRAM: POST " + url + " failed: " + ex.Message); }
        }

        // Safe JSON string literal (handles colons/quotes in model names like "gemma3:4b").
        private static string JsonEncode(string s) => JsonSerializer.Serialize(s);
    }
}
