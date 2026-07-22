# GameTune — Per-Game Max-FPS Presets (Design Spec)

**Date:** 2026-07-21
**Status:** Approved in brainstorm, pending spec review
**Owner:** Nate (psgods101) · built by Claude
**Home:** CoreCage feature (extends existing profile/detection/telemetry subsystems)

---

## One-line

Add a **GameTune** subsystem to CoreCage that auto-detects installed games and writes a **max-FPS / lowest-input-lag** preset into each game's *own* in-game graphics config — safely (game closed, backed up, never inside anti-cheat dirs), for the specific hardware **RTX 3060 12GB / Ryzen 5 5600G / 64GB**.

## Why (the problem)

CoreCage today tunes the **system** layer (core cage, priority, power plan). It never touches the **game's own graphics settings** (DLSS, shadows, textures, VSync) — the single biggest lever on FPS and input lag. Nate manually re-tunes these per game, and settings like FSR Frame-Gen silently reset on some launches. GameTune closes that gap: one click applies a known-good competitive preset per game, reversible.

## Target ("best" = )

**Max FPS / lowest input lag** (competitive-first). Every preset converges toward: highest frame-rate + best 1%-lows + lowest latency, visuals sacrificed where they don't help aim. Fixed direction — not a per-game tier picker in v1.

Canonical preset axes (resolved per game/engine to real keys):
- Motion Blur → **Off**
- Shadows → **Low**
- Textures → **High** (12GB VRAM is not the bottleneck; texture res is near-free on FPS)
- Upscaling (DLSS/FSR/XeSS) → **Quality** (lowers GPU load → more frames → lower latency when GPU-bound)
- **Frame Generation → Off** (adds latency — disqualified for competitive)
- **NVIDIA Reflex / low-latency → On**
- VSync → **Off**
- Ambient occlusion / volumetrics / reflections → **Low/Off**

## Non-goals (explicitly cut)

- ❌ Per-game quality/balanced tiers — v1 is max-FPS only.
- ❌ Any write to files inside a game's install / anti-cheat directory.
- ❌ Any write while the game process is running.
- ❌ Kernel drivers, injection, memory editing — CoreCage stays EAC-safe.
- ❌ The measure-and-tune loop (Prove-It) and AI preset generation in v1 — both are Phase-2 seams (see §7).
- ❌ Image/quality upscaling for looks; this is a competitive tool.

## Installed-game reality (scan 2026-07-21)

| Game | Engine | Anti-cheat | Adapter | v1 |
|------|--------|-----------|---------|----|
| Battlefield 6 | Frostbite | Javelin (kernel) | FrostbiteProfile | ✅ |
| Helldivers 2 | Stingray | GameGuard (kernel) | StingrayConfig | ✅ |
| ARC Raiders | Unreal (UE5) | EAC | UnrealIni | ✅ |
| Dead by Daylight | Unreal | EAC | UnrealIni | ✅ |
| Team Fortress 2 | Source | VAC | SourceCfg | ✅ |
| R.E.P.O. | Unity | none | — | ⚠️ guided-only |
| STRAFTAT | Unity | none | — | ⚠️ guided-only |

One UnrealIni adapter covers **two** games (ARC + DbD). Unity games have no reliable external config surface → flagged "guided-only" (tool shows the preset, user applies in-menu), no auto-write.

---

## Architecture — 3 units + UI

### 1. Data — extend the per-game profile (the "context to detect + tune")

The existing `profiles/*.json` (system tuning: cores/priority/tweaks) gains an optional `graphics` block:

```jsonc
{
  "game": "Arc Raiders",
  "exe": "PioneerGame-Win64-Shipping.exe",
  // ... existing system fields (reservedCores, priority, tweaks, notes) unchanged ...
  "graphics": {
    "format": "unreal-ini",
    "configPath": "%LOCALAPPDATA%\\ArcRaiders\\Saved\\Config\\Windows\\GameUserSettings.ini",
    "safeRoots": ["%LOCALAPPDATA%", "%USERPROFILE%\\Documents"],
    "competitivePreset": {
      "MotionBlur": "0",
      "sg.ShadowQuality": "0",
      "sg.TextureQuality": "3",
      "FrameGeneration": "Off",
      "Reflex": "On",
      "VSync": "0"
    },
    "postApplyNotes": "Re-toggle FSR Frame-Gen off after launch if it resets."
  }
}
```

- `format` selects the adapter. `configPath` supports `%ENV%` expansion.
- `safeRoots` is the allow-list the adapter validates the resolved path against (defense-in-depth).
- `competitivePreset` keys/values are engine-real, resolved by hand per game (curated, Approach A).
- **Adding a game = adding one JSON file. No code change.** Unknown/Unity games simply omit `graphics` (or set `"guidedOnly": true`).

### 2. Config Adapters — the writers (one per format)

Interface (mirrors CoreCage's existing module style):

```csharp
public interface IGraphicsConfigAdapter {
    string Format { get; }                          // "unreal-ini", ...
    GraphicsReadResult Read(string configPath);     // current values
    GraphicsApplyPlan Plan(GraphicsReadResult current, IDictionary<string,string> preset); // diff
    void Write(string configPath, GraphicsApplyPlan plan);  // idempotent, preserves unrelated keys
}
```

- **UnrealIniAdapter** — parse/write `.ini` sections (`[/Script/Engine.GameUserSettings]` etc.), preserve untouched keys. Covers ARC + DbD.
- **FrostbiteProfileAdapter** — BF6 `PROFSAVE_profile` key/value settings file.
- **StingrayConfigAdapter** — Helldivers 2 `user_settings.config`.
- **SourceCfgAdapter** — TF2 `.cfg` / `video.txt` (`autoexec.cfg` overrides).
- Each is a **pure, isolated unit**: given a file + preset, produces a deterministic new file. No global state, fully unit-testable via sample fixtures.

### 3. PresetEngine + Safety Gate (the orchestrator)

`GameTuneService.Apply(gameId)`:
1. Resolve profile → require a `graphics` block (else return `NotSupported`).
2. **Safety gate** (all must pass, else abort with a typed reason):
   - Game process **not running** (via `ProcessWatcher`) → else `GameRunning`.
   - Resolved `configPath` exists and sits under one of `safeRoots`, and is **not** under the Steam/EGS install dir → else `UnsafePath`.
3. **Backup** original → `%LOCALAPPDATA%\CoreCage\backups\<gameId>\<utc-timestamp>\<filename>` (return path).
4. Adapter `Read → Plan → Write`.
5. Emit `Applied` with the diff + backup path.

`GameTuneService.Restore(gameId)` → copies the newest backup back (game-closed gate re-checked).

### 4. UI — "Game Presets" panel (UX Pro Max)

One card per **detected** game. Explicit **state for everything**:

| State | Card shows |
|-------|-----------|
| Detected + preset ready | current→recommended diff, **[Apply Max-FPS]** (primary), disabled Restore |
| Applied ✓ | success badge + backup path, **[Restore]** (secondary) enabled |
| Game running | Apply disabled, inline reason "Close the game to apply" |
| Not supported (Unity) | "Guided only" + a link that opens the recommended settings list |
| Config not found | error state + "Launch the game once to generate its config" |
| No supported games | empty state with guidance |

- Feedback: toast on every Apply/Restore; the backup path is always surfaced (trust).
- Accessibility: visible keyboard focus rings, WCAG-AA contrast in **both** light and dark, real type scale + consistent spacing, primary/secondary button hierarchy.
- Progressive disclosure: the per-setting diff is collapsed by default, expandable.

---

## Data flow

```
CoreCage opens
  → ProcessWatcher / Steam scan → detected games
  → match each to profiles/*.json (graphics block?)
  → Game Presets panel renders one card per detected game (with state)
User clicks [Apply Max-FPS] on a card
  → GameTuneService.Apply(gameId)
  → safety gate (not running + safe path)  ── fail ─→ typed reason shown on card
  → backup original → adapter Read/Plan/Write
  → card → Applied ✓ (+ backup path, diff, toast)
User clicks [Restore] → newest backup copied back → card → Detected
```

## What we keep vs add

| Piece | Reuse | Add |
|-------|-------|-----|
| Game/Steam detection (`ProcessWatcher`) | ✅ | |
| `GameProfile` / `profiles/*.json` | ✅ | `graphics` block |
| PresentMon telemetry (`PresentMonInterface`) | ✅ (Phase 2) | |
| Config read/write per engine | | `IGraphicsConfigAdapter` + 4 adapters |
| Safety gate + backup/restore | | `GameTuneService` |
| Settings UI | | "Game Presets" panel |

---

## §7 Phase-2 seams (designed now, built later)

- **B — Prove It (measure-and-tune):** after Apply, offer "Prove It" → launch game, `PresentMonInterface` captures FPS/1%-low, sweep one axis (e.g. shadows/upscaling), re-measure, keep the winner, write it back through the same adapter. Reuses the existing A/B benchmark engine. The `competitivePreset` becomes the *starting point*, not the final word.
- **C — AI filler (unknown games):** a game with no `graphics` block → local BB/Ollama drafts a candidate `graphics` block from (engine, hardware, known key names). Presented **for Nate's review**; never auto-trusted. Fills the long tail without hand-authoring every game.

Both plug into the **same** adapter + safety layer — no rework.

---

## Error handling

| Failure | Behavior |
|---------|----------|
| Game running | Abort, card shows "Close the game to apply" |
| Config path missing | Abort, "Launch the game once to generate its config" |
| Path outside safeRoots / inside install dir | Abort, `UnsafePath` (never write) |
| Adapter parse error (format drift after a patch) | Abort, keep original untouched, surface "config format changed — profile needs update" |
| Backup write fails | Abort **before** touching the original (no write without a backup) |
| Restore with no backup | Disabled Restore button (no-op impossible) |

## Testing

- **Adapter round-trip** (pure, fixture-driven): sample config in → `Read → Plan → Write` → re-read == expected; untouched keys preserved byte-for-byte where possible. One fixture per engine (Unreal ini, Frostbite, Stingray, Source).
- **Safety gate:** refuses when process running; refuses path outside `safeRoots`; refuses inside install dir; aborts if backup fails; backup file actually created.
- **Restore:** newest-backup selection; restores original bytes.
- **Profile loading:** a profile with no `graphics` block → `NotSupported` (no crash); malformed `graphics` → clear error.
- Matches CoreCage's existing pure-unit test style (349 tests today).

## Open items for spec review

- Confirm exact config paths + key names per game at implementation time (verify against a real config dump for BF6/Helldivers — Unreal/Source are well-documented; Frostbite/Stingray need a live-file check).
- Decide whether `guidedOnly` Unity games show a static list or are simply hidden in v1.

---

## Addendum (2026-07-21) — Sensitivity Sync

**Ask:** input one reference sensitivity (e.g. **6.15**) and have GameTune write the *equivalent-feel* sensitivity into every game, so aim is identical everywhere.

**Core math:** aim feel = distance to turn 360° (cm/360). With the same mouse+DPI across games, DPI cancels, so cross-game conversion reduces to the ratio of each game's **yaw** coefficient (degrees turned per count·sens-unit):

- `targetSens = sourceSens × sourceYaw / targetYaw`
- `cm360(sens, yaw, dpi) = (360 / (yaw × sens)) / (dpi / 2.54)`  *(display only)*

**Data:** each game's profile gains an optional `sensitivity` block `{ "key": "<config key>", "yaw": <number> }` that rides on the existing `graphics` block's `configPath`/`format`/`safeRoots` (sens lives in the same config file). Source (TF2) yaw = **0.022** is well-known; Unreal/Frostbite/Stingray yaw values must be **verified against a real config/community source** before trusting (same honesty caveat as the config keys). A game with no `sensitivity` block is skipped by Sync.

**Apply:** reuses the exact adapter + `GameTuneService` safety gate (game-closed, safe path, backup-first). Writing a sensitivity value is a normal config write — not anti-cheat-sensitive.

**UI:** a "Sensitivity Sync" strip on the Game Presets page: reference-game dropdown, reference-sens input (default 6.15), DPI input (default 800, for the cm/360 readout only), **[Sync to all]** button, and per-game rows showing computed sens + cm/360 with the same state model (Applied ✓ / Game running / Not supported / Error).

**Assumptions (correct later in UI if wrong):** DPI default 800; the reference game is whichever the user picks in the dropdown (no hard-coded reference). These don't affect cross-game *feel* (DPI cancels) — only the cm/360 display.
