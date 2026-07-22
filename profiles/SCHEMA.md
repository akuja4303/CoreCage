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
| `reservedCores` | int[] | no | Logical CPU core indices this profile wants reserved **for the game** (CoreCage's background cage kept off them). ⚠️ **Not yet applied at runtime** — see "Mapping onto `GameProfile`" below. Empty/omitted = no per-game override; today CoreCage always runs on its global `CoreCageReservedCores` setting regardless of this field. |
| `priority` | string | no | Foreground process priority tier this profile wants — one of the .NET `ProcessPriorityClass` names, case-insensitive: `Idle`, `BelowNormal`, `Normal`, `AboveNormal`, `High`, `RealTime`. Empty/omitted means "unset" and defaults to `"High"` on the loaded object. ⚠️ **Not yet applied at runtime** — see below. A value that isn't one of these names still loads the file but produces a load **warning** (distinct from a load error). |
| `tweaks` | string[] | no | Ledger tweak ids this profile exercises/relies on, for provenance/correlation with `CoreCage.Core.Ledger.TweakIds` (e.g. `"gaming-stack"` for `TweakIds.GamingStack`). Informational — not auto-applied by the loader. Each id must be a known one (`TweakIds.IsKnown`); an unrecognized id still loads the file but produces a load **warning**. |
| `notes` | string | no | Free-text tips: anti-cheat quirks, in-game settings, launch options, engine-specific gotchas. |
| `submittedBenchmark` | object | no | The submitter's own measured before/after numbers, kept as PR-review evidence (see below). Not applied at runtime — it's provenance, not config. |
| `submittedBenchmark.fps` | number | — | Average FPS observed with the profile applied. |
| `submittedBenchmark.onePctLow` | number | — | 1% low FPS observed with the profile applied (the number that actually reflects stutter/hitching). |
| `submittedBenchmark.rig` | string | — | Hardware the benchmark was measured on, e.g. `"Ryzen 5 5600G / RTX 3060 / 64GB"`. |

### Mapping onto `GameProfile`

`game`, `exe`, `reservedCores`, and `priority` are all captured onto the engine's runtime
`CoreCage.Core.Profiles.GameProfile` (`ExeName`, `DisplayName`, `ReservedCores`, `Priority`) — the
loader validates and carries every one of these fields through today.

⚠️ **Honest status, as of this writing:** only `game` and `exe` currently drive live behavior (via
`ProfileMatcher.Match` picking the right profile for the foreground process). `ReservedCores` and
`Priority` are captured on `GameProfile` and validated at load time, but **nothing reads them yet
when actually applying a profile** — the reserved-cores behavior you get today is always the global
`FeatureFlags.CoreCageReservedCores` setting, and process priority isn't touched per-profile at all.
Filling in `reservedCores`/`priority` is still the right thing to do: they're forward-looking and
will take effect once per-profile application is wired up (a future step), at which point CoreCage
will use *your* values instead of the global default for that game. Just don't expect setting them
today to change what CoreCage does right now.

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
4. A bad `tweaks[]` id or an unparsable `priority` doesn't stop your file from loading — it's
   reported as a load **warning** (`CommunityProfileLoadResult.Warnings`), not an error — but you
   should still fix it: an unknown `priority` means the field just carries through unused, and an
   unknown tweak id breaks the provenance link to `CoreCage.Core.Ledger.TweakIds` reviewers rely on.
5. One file per game. If a game already has a profile, open a PR editing that file instead of
   adding a duplicate.

## graphics (optional)

An optional `graphics` block adds an in-game **max-FPS/low-lag competitive preset** that
CoreCage's GameTune feature can write to the game's own settings file (MotionBlur off, shadows
low, textures high, upscaling on quality mode, frame-gen off, Reflex/low-latency on, VSync off).
Loaded onto `CoreCage.Core.GameTune.GraphicsBlock` and mapped onto `GameProfile.Graphics` by
`CommunityProfileLoader`. Omit the whole block for a game with no curated preset — `Graphics`
stays `null` and GameTune does nothing for that profile.

| Field | Type | Required | Meaning |
|---|---|---|---|
| `format` | string | **yes** | Which config-file syntax `configPath` uses. Must be one of the four values `AdapterRegistry.For` recognizes (below) — an unrecognized format throws at apply time (not at load time). |
| `configPath` | string | **yes** | Absolute path to the game's settings file, with Windows environment variables (`%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `%PROGRAMFILES(X86)%`, ...) left unexpanded in the JSON — `PathSafety.Expand` expands them at apply time. |
| `safeRoots` | string[] | **yes** | One or more environment-variable-prefixed directories `configPath` must resolve underneath. `PathSafety.IsSafe` fully resolves `configPath` and refuses to write unless it both (a) starts with one of these roots and (b) does **not** contain a known install-dir marker (`\steamapps\`, `\Epic Games\`, `\Program Files\`, `\Program Files (x86)\`) anywhere in the path — the install-dir check wins even if a safe root would otherwise match, which is exactly why TF2's cfg (which lives under `\steamapps\`) is `guidedOnly: true` instead of auto-applied. |
| `competitivePreset` | object (string→string) | **yes** | The key/value pairs GameTune writes into the config file to reach the fixed max-FPS/low-lag target. Keys are the config file's own setting names (verbatim, engine-specific — e.g. Unreal's `sg.ShadowQuality`, Frostbite's `GstRender.ShadowQuality`, Source's `r_shadowrendertotexture`); values are always strings, written using the format's own delimiter/quoting rules. |
| `guidedOnly` | bool | no (default `false`) | When `true`, CoreCage never auto-writes this profile's config — it only *shows* the preset as a copy-paste list for the player to apply by hand. Set this whenever `configPath` legitimately can't pass the safe-root/install-dir gate (e.g. a config that only exists inside the Steam library), so the feature degrades to guided instructions instead of silently doing nothing. |
| `postApplyNotes` | string | no | Free-text caveat shown after apply (or alongside the guided list) — e.g. a setting that resets itself on next launch, or a flag that the exact keys/path are unverified and need confirming against a real config dump before being trusted blindly. |

### Valid `format` values (`AdapterRegistry.For`)

| `format` | Engine | Delimiter / quoting | Example games |
|---|---|---|---|
| `unreal-ini` | Unreal Engine `GameUserSettings.ini` | INI `Key=Value` (own adapter) | Arc Raiders, Dead by Daylight |
| `frostbite-profsave` | Frostbite `PROFSAVE_profile` | space-delimited, unquoted values | Battlefield 6 |
| `stingray-config` | Stingray `.config` | `=`-delimited, unquoted values | Helldivers 2 |
| `source-cfg` | Source engine `.cfg` | space-delimited, quoted values | Team Fortress 2 |

### The safe-root / install-dir rule

A `graphics` block is only ever auto-applied if its fully-resolved `configPath`:

1. starts with one of the fully-resolved `safeRoots` entries, **and**
2. does not contain `\steamapps\`, `\Epic Games\`, `\Program Files\`, or `\Program Files (x86)\`
   anywhere in the resolved path.

If either check fails, the write is refused — a profile whose real config path lives inside an
install directory (as TF2's does) should set `guidedOnly: true` rather than pointing `safeRoots`
at the install dir; that only produces a write that's silently and permanently refused, not a
working auto-apply.
