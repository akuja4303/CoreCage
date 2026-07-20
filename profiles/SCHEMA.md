# Community Game Profile Schema

Profiles in this folder are **PR-able**: anyone can submit a `.json` file here describing how
CoreCage should tune a specific game. `CommunityProfileLoader.LoadDirectory("profiles")`
(`src\CoreCage.Core\Profiles\CommunityProfileLoader.cs`) loads every `*.json` file in this
directory, maps it onto the engine's `GameProfile` type, and reports any file that fails to parse
without blocking the rest of the directory.

## Fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `game` | string | recommended | Display name shown in the CoreCage UI (e.g. `"Arc Raiders"`). Falls back to `exe` if omitted. |
| `exe` | string | **yes** | The game's process executable name, with or without `.exe` (e.g. `"PioneerGame-Win64-Shipping.exe"`). Matched case-insensitively and path-insensitively by `ProfileMatcher.Match` against the foreground process. |
| `reservedCores` | int[] | no | Logical CPU core indices to reserve **for the game** (CoreCage's background cage stays off them). Empty/omitted = no per-game override; CoreCage falls back to its global `CoreCageReservedCores` setting. |
| `priority` | string | no | Foreground process priority tier — one of the .NET `ProcessPriorityClass` names: `"Idle"`, `"BelowNormal"`, `"Normal"`, `"AboveNormal"`, `"High"`, `"RealTime"`. Defaults to `"High"` if omitted. |
| `tweaks` | string[] | no | Ledger tweak ids this profile exercises/relies on, for provenance/correlation with `CoreCage.Core.Ledger.TweakIds` (e.g. `"gaming-stack"` for `TweakIds.GamingStack`). Informational — not auto-applied by the loader. |
| `notes` | string | no | Free-text tips: anti-cheat quirks, in-game settings, launch options, engine-specific gotchas. |
| `submittedBenchmark` | object | no | The submitter's own measured before/after numbers, kept as PR-review evidence (see below). Not applied at runtime — it's provenance, not config. |
| `submittedBenchmark.fps` | number | — | Average FPS observed with the profile applied. |
| `submittedBenchmark.onePctLow` | number | — | 1% low FPS observed with the profile applied (the number that actually reflects stutter/hitching). |
| `submittedBenchmark.rig` | string | — | Hardware the benchmark was measured on, e.g. `"Ryzen 5 5600G / RTX 3060 / 64GB"`. |

### Mapping onto `GameProfile`

`game`, `exe`, `reservedCores`, and `priority` map directly onto the engine's runtime
`CoreCage.Core.Profiles.GameProfile` (`ExeName`, `DisplayName`, `ReservedCores`, `Priority`) —
these are the fields CoreCage actually reads when applying a profile.

`tweaks`, `notes`, and `submittedBenchmark` are **submission metadata**, not runtime config — they
don't bloat `GameProfile`. The loader carries them separately on a `CommunityProfileEntry`
(`Profile`, `Tweaks`, `Notes`, `SubmittedBenchmark`) so PR reviewers and future tooling can see the
evidence behind a submission without the runtime engine needing to know about it.

## Template (copy-paste)

```json
{
  "game": "Your Game Title",
  "exe": "YourGame-Win64-Shipping.exe",
  "reservedCores": [2, 3, 4, 5],
  "priority": "High",
  "tweaks": ["gaming-stack"],
  "notes": "Anti-cheat, engine, and any launch-option tips go here.",
  "submittedBenchmark": {
    "fps": 0,
    "onePctLow": 0,
    "rig": "CPU / GPU / RAM"
  }
}
```

Only `exe` is required — everything else can be omitted and will fall back to a sane default
(`priority` -> `"High"`, `reservedCores` -> none, `game` -> the exe name).

## How to submit

1. Fork the repo and add a new file under `profiles\` named `<your-game>.json` (kebab-case,
   e.g. `profiles\my-game.json`).
2. Fill in the template above with real, measured values — don't guess `submittedBenchmark`;
   run the game with the profile applied and report what you actually saw.
3. Open a PR. `CommunityProfileLoaderTests.cs` and the full test suite must stay green; a
   malformed submission is reported as a load error rather than breaking the build, but it also
   won't do anything for players until it's fixed, so make sure your JSON parses (any JSON
   linter/validator will catch syntax errors before you push).
4. One file per game. If a game already has a profile, open a PR editing that file instead of
   adding a duplicate.
