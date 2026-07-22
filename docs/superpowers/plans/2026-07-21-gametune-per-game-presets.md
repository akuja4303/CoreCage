# GameTune — Per-Game Max-FPS Presets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-detect installed games and safely write a max-FPS/low-lag preset into each game's own in-game graphics config, reversibly.

**Architecture:** Extend CoreCage's existing `GameProfile`/profile-loader with an optional `graphics` block. Add a `GameTune` subsystem in `CoreCage.Core`: pure per-engine config adapters (read→plan→write), a path-safety guard, a backup/restore helper, and a `GameTuneService` orchestrator that gates every write (game closed + safe path + backup-first). A WPF "Game Presets" page renders one state-driven card per detected game.

**Tech Stack:** C# / .NET 8, WPF (CoreCage.App), MSTest (CoreCage.Tests), Newtonsoft.Json (already used by the profile loader).

## Global Constraints

- **EAC-safe, no kernel drivers, no injection, no memory editing** — GameTune only reads/writes plain config files.
- **Never write while the game process is running** — abort with a typed reason.
- **Never write a file inside a game's install / anti-cheat directory** — only user-config dirs listed in the profile's `safeRoots`.
- **Never write without a successful backup first** — if backup fails, abort before touching the original.
- Test framework is **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.*`), namespace `CoreCage.Tests`.
- JSON parsing uses **Newtonsoft.Json**, mirroring the existing `CommunityProfileLoader` DTO pattern; the loader must **never throw** on bad input.
- Adapters are **pure and deterministic** — no OS mutation, no global state — so they unit-test against string/file fixtures.
- Optimization target is fixed: **max FPS / lowest input lag** (no quality/balanced tiers in v1).
- Follow existing namespaces (`CoreCage.Core.Profiles`, new `CoreCage.Core.GameTune`) and file layout (`src/CoreCage.Core/…`, tests under `tests/CoreCage.Tests/…`).

---

### Task 1: Graphics preset types + profile `graphics` block loading

**Files:**
- Create: `src/CoreCage.Core/GameTune/GraphicsTypes.cs`
- Modify: `src/CoreCage.Core/Profiles/GameProfile.cs` (add `Graphics` property)
- Modify: `src/CoreCage.Core/Profiles/CommunityProfileLoader.cs` (DTO + mapping)
- Test: `tests/CoreCage.Tests/GameTune/GraphicsBlockLoadingTests.cs`

**Interfaces:**
- Produces:
  - `record GraphicsBlock(string Format, string ConfigPath, IReadOnlyList<string> SafeRoots, IReadOnlyDictionary<string,string> CompetitivePreset, bool GuidedOnly, string? PostApplyNotes)`
  - `record GraphicsSetting(string Key, string? CurrentValue)`
  - `record GraphicsReadResult(IReadOnlyList<GraphicsSetting> Settings)`
  - `record GraphicsChange(string Key, string? From, string To)`
  - `record GraphicsApplyPlan(IReadOnlyList<GraphicsChange> Changes)`
  - `GameProfile.Graphics` (nullable `GraphicsBlock`)

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GraphicsBlockLoadingTests
    {
        private static string WriteTemp(string json)
        {
            var dir = Path.Combine(Path.GetTempPath(), "gt_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "game.json"), json);
            return dir;
        }

        [TestMethod]
        public void Load_ProfileWithGraphicsBlock_PopulatesGraphics()
        {
            var dir = WriteTemp(@"{
              ""game"": ""Arc Raiders"", ""exe"": ""PioneerGame-Win64-Shipping.exe"",
              ""graphics"": {
                ""format"": ""unreal-ini"",
                ""configPath"": ""%LOCALAPPDATA%\\ArcRaiders\\GameUserSettings.ini"",
                ""safeRoots"": [""%LOCALAPPDATA%""],
                ""competitivePreset"": { ""MotionBlur"": ""0"", ""sg.ShadowQuality"": ""0"" }
              }
            }");

            var result = CommunityProfileLoader.Load(dir);

            Assert.AreEqual(0, result.Errors.Count);
            var g = result.Profiles[0].Profile.Graphics;
            Assert.IsNotNull(g);
            Assert.AreEqual("unreal-ini", g!.Format);
            Assert.AreEqual("0", g.CompetitivePreset["MotionBlur"]);
            Assert.IsFalse(g.GuidedOnly);
        }

        [TestMethod]
        public void Load_ProfileWithoutGraphicsBlock_GraphicsIsNull()
        {
            var dir = WriteTemp(@"{ ""game"": ""TF2"", ""exe"": ""tf.exe"" }");
            var result = CommunityProfileLoader.Load(dir);
            Assert.IsNull(result.Profiles[0].Profile.Graphics);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter GraphicsBlockLoadingTests`
Expected: FAIL — `GraphicsBlock` type and `GameProfile.Graphics` do not exist (compile error).

- [ ] **Step 3: Create the types**

`src/CoreCage.Core/GameTune/GraphicsTypes.cs`:

```csharp
using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    /// <summary>The in-game graphics-settings context for one game: where its config lives, its
    /// format, the safe directories the config must sit under, and the max-FPS values to write.</summary>
    public sealed record GraphicsBlock(
        string Format,
        string ConfigPath,
        IReadOnlyList<string> SafeRoots,
        IReadOnlyDictionary<string, string> CompetitivePreset,
        bool GuidedOnly,
        string? PostApplyNotes);

    /// <summary>One setting's current value as read from a config file (null = absent).</summary>
    public sealed record GraphicsSetting(string Key, string? CurrentValue);

    /// <summary>All settings an adapter could read back from a config file.</summary>
    public sealed record GraphicsReadResult(IReadOnlyList<GraphicsSetting> Settings);

    /// <summary>One setting change the apply step will make.</summary>
    public sealed record GraphicsChange(string Key, string? From, string To);

    /// <summary>The diff between current config and the target preset — what Write will apply.</summary>
    public sealed record GraphicsApplyPlan(IReadOnlyList<GraphicsChange> Changes);
}
```

- [ ] **Step 4: Add `Graphics` to `GameProfile`**

In `src/CoreCage.Core/Profiles/GameProfile.cs`, add `using CoreCage.Core.GameTune;` and this property to the `GameProfile` class:

```csharp
        /// <summary>Optional in-game graphics-settings context. Null when the game has no curated
        /// preset (unknown game, or Unity title flagged guided-only). Runtime-relevant → lives on
        /// GameProfile, loaded by CommunityProfileLoader.</summary>
        public GraphicsBlock? Graphics { get; set; }
```

- [ ] **Step 5: Parse the block in `CommunityProfileLoader`**

In `src/CoreCage.Core/Profiles/CommunityProfileLoader.cs`, extend the private DTO and the mapping:

```csharp
    // add to CommunityProfileDto:
    public GraphicsBlockDto? Graphics { get; set; }

    // add new file-scoped DTO alongside SubmittedBenchmarkDto:
    file sealed class GraphicsBlockDto
    {
        public string? Format { get; set; }
        public string? ConfigPath { get; set; }
        public string[]? SafeRoots { get; set; }
        public Dictionary<string, string>? CompetitivePreset { get; set; }
        public bool GuidedOnly { get; set; }
        public string? PostApplyNotes { get; set; }
    }
```

In the method that builds a `GameProfile` from the DTO (where `ReservedCores`/`Priority` are mapped), add — mapping a `graphics` block that is missing required fields to `null` rather than throwing:

```csharp
            CoreCage.Core.GameTune.GraphicsBlock? graphics = null;
            if (dto.Graphics is { } gd && !string.IsNullOrWhiteSpace(gd.Format) && !string.IsNullOrWhiteSpace(gd.ConfigPath))
            {
                graphics = new CoreCage.Core.GameTune.GraphicsBlock(
                    gd.Format!,
                    gd.ConfigPath!,
                    gd.SafeRoots ?? System.Array.Empty<string>(),
                    gd.CompetitivePreset ?? new System.Collections.Generic.Dictionary<string, string>(),
                    gd.GuidedOnly,
                    gd.PostApplyNotes);
            }
            // then set: profile.Graphics = graphics;  (on the GameProfile being returned)
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter GraphicsBlockLoadingTests`
Expected: PASS (2 passed).

- [ ] **Step 7: Commit**

```bash
git add src/CoreCage.Core/GameTune/GraphicsTypes.cs src/CoreCage.Core/Profiles/GameProfile.cs src/CoreCage.Core/Profiles/CommunityProfileLoader.cs tests/CoreCage.Tests/GameTune/GraphicsBlockLoadingTests.cs
git commit -m "feat(gametune): graphics-block types + profile loading"
```

---

### Task 2: PathSafety — env expansion + safe-root / install-dir guard

**Files:**
- Create: `src/CoreCage.Core/GameTune/PathSafety.cs`
- Test: `tests/CoreCage.Tests/GameTune/PathSafetyTests.cs`

**Interfaces:**
- Produces:
  - `static string Expand(string pathWithEnv)` — expands `%VAR%`.
  - `static bool IsSafe(string resolvedPath, IReadOnlyList<string> safeRoots)` — true only if the resolved path is under at least one expanded safe root AND not under a games-install marker (`steamapps`, `Epic Games`, `Program Files`).

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class PathSafetyTests
    {
        [TestMethod]
        public void Expand_ReplacesEnvVar()
        {
            Environment.SetEnvironmentVariable("GT_TEST_ROOT", @"C:\Users\x\AppData\Local");
            var p = PathSafety.Expand(@"%GT_TEST_ROOT%\Game\config.ini");
            Assert.AreEqual(@"C:\Users\x\AppData\Local\Game\config.ini", p);
        }

        [TestMethod]
        public void IsSafe_UnderSafeRoot_True()
        {
            var roots = new List<string> { @"C:\Users\x\AppData\Local" };
            Assert.IsTrue(PathSafety.IsSafe(@"C:\Users\x\AppData\Local\Game\config.ini", roots));
        }

        [TestMethod]
        public void IsSafe_OutsideSafeRoot_False()
        {
            var roots = new List<string> { @"C:\Users\x\AppData\Local" };
            Assert.IsFalse(PathSafety.IsSafe(@"C:\Windows\System32\config.ini", roots));
        }

        [TestMethod]
        public void IsSafe_InsideSteamInstallDir_False_EvenIfUnderSafeRoot()
        {
            var roots = new List<string> { @"F:\SteamLibrary" };
            Assert.IsFalse(PathSafety.IsSafe(@"F:\SteamLibrary\steamapps\common\Game\config.ini", roots));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter PathSafetyTests`
Expected: FAIL — `PathSafety` not defined.

- [ ] **Step 3: Implement**

`src/CoreCage.Core/GameTune/PathSafety.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace CoreCage.Core.GameTune
{
    /// <summary>Defense-in-depth path checks for GameTune writes. A config write is only ever
    /// allowed to a fully-resolved path that sits under one of the profile's declared safe roots
    /// and is NOT under a known game-install marker (anti-cheat-protected territory).</summary>
    public static class PathSafety
    {
        private static readonly string[] InstallMarkers =
            { @"\steamapps\", @"\Epic Games\", @"\Program Files\", @"\Program Files (x86)\" };

        public static string Expand(string pathWithEnv) =>
            Environment.ExpandEnvironmentVariables(pathWithEnv ?? "");

        public static bool IsSafe(string resolvedPath, IReadOnlyList<string> safeRoots)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath) || safeRoots == null) return false;
            string full;
            try { full = Path.GetFullPath(resolvedPath); }
            catch { return false; }

            foreach (var marker in InstallMarkers)
                if (full.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

            foreach (var root in safeRoots)
            {
                var r = Path.GetFullPath(Expand(root)).TrimEnd('\\');
                if (full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter PathSafetyTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/CoreCage.Core/GameTune/PathSafety.cs tests/CoreCage.Tests/GameTune/PathSafetyTests.cs
git commit -m "feat(gametune): path-safety guard (env expand + safe-root/install-dir check)"
```

---

### Task 3: ConfigBackup — backup-before-write + restore-newest

**Files:**
- Create: `src/CoreCage.Core/GameTune/ConfigBackup.cs`
- Test: `tests/CoreCage.Tests/GameTune/ConfigBackupTests.cs`

**Interfaces:**
- Produces (instance class so tests can point `BackupRoot` at a temp dir):
  - `ConfigBackup(string backupRoot)`
  - `string Backup(string gameId, string configPath)` — copies file into `backupRoot/gameId/<utcTicks>/<filename>`, returns the backup file path. Throws `IOException` on failure (caller aborts).
  - `bool TryRestoreNewest(string gameId, string configPath)` — copies the newest backup back over `configPath`; false if no backup exists.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class ConfigBackupTests
    {
        private string _root = "";
        private string _cfg = "";

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "gtbk_" + Path.GetRandomFileName());
            var cfgDir = Path.Combine(Path.GetTempPath(), "gtcfg_" + Path.GetRandomFileName());
            Directory.CreateDirectory(cfgDir);
            _cfg = Path.Combine(cfgDir, "config.ini");
            File.WriteAllText(_cfg, "original=1");
        }

        [TestMethod]
        public void Backup_CopiesOriginalBytes_ReturnsPath()
        {
            var bk = new ConfigBackup(_root).Backup("arc", _cfg);
            Assert.IsTrue(File.Exists(bk));
            Assert.AreEqual("original=1", File.ReadAllText(bk));
        }

        [TestMethod]
        public void TryRestoreNewest_RestoresOriginal()
        {
            var b = new ConfigBackup(_root);
            b.Backup("arc", _cfg);
            File.WriteAllText(_cfg, "changed=9");
            Assert.IsTrue(b.TryRestoreNewest("arc", _cfg));
            Assert.AreEqual("original=1", File.ReadAllText(_cfg));
        }

        [TestMethod]
        public void TryRestoreNewest_NoBackup_ReturnsFalse()
        {
            Assert.IsFalse(new ConfigBackup(_root).TryRestoreNewest("never", _cfg));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter ConfigBackupTests`
Expected: FAIL — `ConfigBackup` not defined.

- [ ] **Step 3: Implement**

`src/CoreCage.Core/GameTune/ConfigBackup.cs`:

```csharp
using System;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Copies a game's config file to a timestamped backup before GameTune writes it, and
    /// restores the newest backup on demand. No write is ever performed without a backup succeeding.</summary>
    public sealed class ConfigBackup
    {
        private readonly string _backupRoot;
        public ConfigBackup(string backupRoot) => _backupRoot = backupRoot;

        public string Backup(string gameId, string configPath)
        {
            var stamp = DateTime.UtcNow.Ticks.ToString();
            var dir = Path.Combine(_backupRoot, Sanitize(gameId), stamp);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(configPath));
            File.Copy(configPath, dest, overwrite: true);
            return dest;
        }

        public bool TryRestoreNewest(string gameId, string configPath)
        {
            var gameDir = Path.Combine(_backupRoot, Sanitize(gameId));
            if (!Directory.Exists(gameDir)) return false;
            var newest = Directory.GetDirectories(gameDir)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, Path.GetFileName(configPath)))
                .FirstOrDefault(File.Exists);
            if (newest == null) return false;
            File.Copy(newest, configPath, overwrite: true);
            return true;
        }

        private static string Sanitize(string id) =>
            string.Concat(id.Split(Path.GetInvalidFileNameChars()));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter ConfigBackupTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/CoreCage.Core/GameTune/ConfigBackup.cs tests/CoreCage.Tests/GameTune/ConfigBackupTests.cs
git commit -m "feat(gametune): config backup + restore-newest"
```

---

### Task 4: IGraphicsConfigAdapter + UnrealIniAdapter (covers ARC Raiders + Dead by Daylight)

**Files:**
- Create: `src/CoreCage.Core/GameTune/IGraphicsConfigAdapter.cs`
- Create: `src/CoreCage.Core/GameTune/UnrealIniAdapter.cs`
- Test: `tests/CoreCage.Tests/GameTune/UnrealIniAdapterTests.cs`

**Interfaces:**
- Produces:
  - `interface IGraphicsConfigAdapter { string Format { get; } GraphicsReadResult Read(string path); GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string,string> preset); void Write(string path, GraphicsApplyPlan plan); }`
  - `class UnrealIniAdapter : IGraphicsConfigAdapter` with `Format => "unreal-ini"`.
- Consumes: `GraphicsReadResult`, `GraphicsApplyPlan`, `GraphicsChange` (Task 1).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class UnrealIniAdapterTests
    {
        private const string Sample =
@"[/Script/Engine.GameUserSettings]
MotionBlur=1
sg.ShadowQuality=3
KeepMe=42
";
        private string WriteTemp()
        {
            var p = Path.Combine(Path.GetTempPath(), "ue_" + Path.GetRandomFileName() + ".ini");
            File.WriteAllText(p, Sample);
            return p;
        }

        [TestMethod]
        public void Plan_ProducesOnlyChangedKeys()
        {
            var a = new UnrealIniAdapter();
            var cur = a.Read(WriteTemp());
            var preset = new Dictionary<string, string> { ["MotionBlur"] = "0", ["sg.ShadowQuality"] = "3" };
            var plan = a.Plan(cur, preset);
            Assert.AreEqual(1, plan.Changes.Count);          // ShadowQuality already 3 → no change
            Assert.AreEqual("MotionBlur", plan.Changes[0].Key);
            Assert.AreEqual("0", plan.Changes[0].To);
        }

        [TestMethod]
        public void Write_ChangesTargetKeys_PreservesOthers_RoundTrips()
        {
            var a = new UnrealIniAdapter();
            var path = WriteTemp();
            var plan = a.Plan(a.Read(path), new Dictionary<string, string> { ["MotionBlur"] = "0" });
            a.Write(path, plan);
            var after = a.Read(path);
            Assert.AreEqual("0", Find(after, "MotionBlur"));
            Assert.AreEqual("42", Find(after, "KeepMe"));    // untouched key preserved
        }

        private static string? Find(GraphicsReadResult r, string key)
        {
            foreach (var s in r.Settings) if (s.Key == key) return s.CurrentValue;
            return null;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter UnrealIniAdapterTests`
Expected: FAIL — `IGraphicsConfigAdapter` / `UnrealIniAdapter` not defined.

- [ ] **Step 3: Implement the interface**

`src/CoreCage.Core/GameTune/IGraphicsConfigAdapter.cs`:

```csharp
using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    /// <summary>Reads and writes one game-engine's graphics config format. Pure: Read/Plan never
    /// mutate anything; Write applies only the planned changes and preserves every other line.</summary>
    public interface IGraphicsConfigAdapter
    {
        string Format { get; }
        GraphicsReadResult Read(string configPath);
        GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset);
        void Write(string configPath, GraphicsApplyPlan plan);
    }
}
```

- [ ] **Step 4: Implement UnrealIniAdapter**

`src/CoreCage.Core/GameTune/UnrealIniAdapter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Unreal Engine GameUserSettings.ini (key=value under [Section] headers). Covers
    /// ARC Raiders and Dead by Daylight. Matches keys case-insensitively, ignoring section, and
    /// rewrites values in-place so unrelated keys and comments survive untouched.</summary>
    public sealed class UnrealIniAdapter : IGraphicsConfigAdapter
    {
        public string Format => "unreal-ini";

        public GraphicsReadResult Read(string configPath)
        {
            var list = new List<GraphicsSetting>();
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                list.Add(new GraphicsSetting(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return new GraphicsReadResult(list);
        }

        public GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset)
        {
            var cur = current.Settings.ToDictionary(s => s.Key, s => s.CurrentValue, StringComparer.OrdinalIgnoreCase);
            var changes = new List<GraphicsChange>();
            foreach (var kv in preset)
            {
                cur.TryGetValue(kv.Key, out var existing);
                if (!string.Equals(existing, kv.Value, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new GraphicsChange(kv.Key, existing, kv.Value));
            }
            return new GraphicsApplyPlan(changes);
        }

        public void Write(string configPath, GraphicsApplyPlan plan)
        {
            if (plan.Changes.Count == 0) return;
            var toSet = plan.Changes.ToDictionary(c => c.Key, c => c.To, StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(configPath).ToList();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                var eq = t.IndexOf('=');
                if (eq <= 0 || t.StartsWith(";") || t.StartsWith("[")) continue;
                var key = t.Substring(0, eq).Trim();
                if (toSet.TryGetValue(key, out var val)) { lines[i] = $"{key}={val}"; written.Add(key); }
            }
            // Append any preset key that had no existing line, under the last section (or file end).
            foreach (var kv in toSet)
                if (!written.Contains(kv.Key)) lines.Add($"{kv.Key}={kv.Value}");

            File.WriteAllLines(configPath, lines);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter UnrealIniAdapterTests`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
git add src/CoreCage.Core/GameTune/IGraphicsConfigAdapter.cs src/CoreCage.Core/GameTune/UnrealIniAdapter.cs tests/CoreCage.Tests/GameTune/UnrealIniAdapterTests.cs
git commit -m "feat(gametune): adapter interface + Unreal ini adapter (ARC + DbD)"
```

---

### Task 5: KeyValueAdapter (Frostbite/Stingray/Source) + AdapterRegistry

**Files:**
- Create: `src/CoreCage.Core/GameTune/KeyValueAdapter.cs`
- Create: `src/CoreCage.Core/GameTune/AdapterRegistry.cs`
- Test: `tests/CoreCage.Tests/GameTune/KeyValueAdapterTests.cs`
- Test: `tests/CoreCage.Tests/GameTune/AdapterRegistryTests.cs`

**Note:** Frostbite `PROFSAVE_profile` (space-delimited `GstRender.X 0`), Stingray/Helldivers `.config` (`key = value`), and Source `.cfg` (`name "value"`) are all flat key/value. One delimiter-configurable adapter covers all three (DRY). **Open item from spec:** verify each game's real delimiter/filename against a live config dump before shipping that game's profile (Task 8) — the adapter code is format-correct; only the per-game `format`→delimiter mapping needs confirming.

**Interfaces:**
- Produces:
  - `class KeyValueAdapter(string format, char delimiter, bool quoteValues) : IGraphicsConfigAdapter`
  - `static class AdapterRegistry { static IGraphicsConfigAdapter For(string format); }` returning the right adapter for `"unreal-ini"`, `"frostbite-profsave"`, `"stingray-config"`, `"source-cfg"`; throws `NotSupportedException` for unknown formats.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class KeyValueAdapterTests
    {
        private static string Temp(string content, string ext)
        {
            var p = Path.Combine(Path.GetTempPath(), "kv_" + Path.GetRandomFileName() + ext);
            File.WriteAllText(p, content);
            return p;
        }

        [TestMethod]
        public void SpaceDelimited_Write_PreservesOthers()
        {
            var a = new KeyValueAdapter("frostbite-profsave", ' ', quoteValues: false);
            var path = Temp("GstRender.MotionBlurEnabled 1\nGstRender.Keep 7\n", ".txt");
            a.Write(path, a.Plan(a.Read(path),
                new Dictionary<string, string> { ["GstRender.MotionBlurEnabled"] = "0" }));
            var after = a.Read(path);
            Assert.AreEqual("0", Val(after, "GstRender.MotionBlurEnabled"));
            Assert.AreEqual("7", Val(after, "GstRender.Keep"));
        }

        [TestMethod]
        public void QuotedSource_Write_QuotesValue()
        {
            var a = new KeyValueAdapter("source-cfg", ' ', quoteValues: true);
            var path = Temp("mat_motion_blur_enabled \"1\"\n", ".cfg");
            a.Write(path, a.Plan(a.Read(path),
                new Dictionary<string, string> { ["mat_motion_blur_enabled"] = "0" }));
            Assert.IsTrue(File.ReadAllText(path).Contains("mat_motion_blur_enabled \"0\""));
        }

        private static string? Val(GraphicsReadResult r, string k)
        {
            foreach (var s in r.Settings) if (s.Key == k) return s.CurrentValue;
            return null;
        }
    }
}
```

And `AdapterRegistryTests.cs`:

```csharp
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class AdapterRegistryTests
    {
        [TestMethod]
        public void For_KnownFormats_ReturnMatchingAdapter()
        {
            Assert.AreEqual("unreal-ini", AdapterRegistry.For("unreal-ini").Format);
            Assert.AreEqual("frostbite-profsave", AdapterRegistry.For("frostbite-profsave").Format);
            Assert.AreEqual("stingray-config", AdapterRegistry.For("stingray-config").Format);
            Assert.AreEqual("source-cfg", AdapterRegistry.For("source-cfg").Format);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void For_UnknownFormat_Throws()
        {
            AdapterRegistry.For("does-not-exist");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CoreCage.Tests --filter "KeyValueAdapterTests|AdapterRegistryTests"`
Expected: FAIL — `KeyValueAdapter` / `AdapterRegistry` not defined.

- [ ] **Step 3: Implement KeyValueAdapter**

`src/CoreCage.Core/GameTune/KeyValueAdapter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Flat key/value config adapter parameterised by delimiter and quoting. Covers
    /// Frostbite PROFSAVE (space, unquoted), Stingray/Helldivers .config ('=', unquoted), and
    /// Source .cfg (space, quoted). One code path; per-game specifics live in the profile.</summary>
    public sealed class KeyValueAdapter : IGraphicsConfigAdapter
    {
        private readonly char _delim;
        private readonly bool _quote;
        public string Format { get; }

        public KeyValueAdapter(string format, char delimiter, bool quoteValues)
        {
            Format = format; _delim = delimiter; _quote = quoteValues;
        }

        public GraphicsReadResult Read(string configPath)
        {
            var list = new List<GraphicsSetting>();
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#")) continue;
                var idx = line.IndexOf(_delim);
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim().Trim('"');
                list.Add(new GraphicsSetting(key, val));
            }
            return new GraphicsReadResult(list);
        }

        public GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset)
        {
            var cur = current.Settings.ToDictionary(s => s.Key, s => s.CurrentValue, StringComparer.OrdinalIgnoreCase);
            var changes = new List<GraphicsChange>();
            foreach (var kv in preset)
            {
                cur.TryGetValue(kv.Key, out var existing);
                if (!string.Equals(existing, kv.Value, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new GraphicsChange(kv.Key, existing, kv.Value));
            }
            return new GraphicsApplyPlan(changes);
        }

        public void Write(string configPath, GraphicsApplyPlan plan)
        {
            if (plan.Changes.Count == 0) return;
            var toSet = plan.Changes.ToDictionary(c => c.Key, c => c.To, StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(configPath).ToList();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                var idx = t.IndexOf(_delim);
                if (idx <= 0 || t.StartsWith("//") || t.StartsWith("#")) continue;
                var key = t.Substring(0, idx).Trim();
                if (toSet.TryGetValue(key, out var val)) { lines[i] = Line(key, val); written.Add(key); }
            }
            foreach (var kv in toSet)
                if (!written.Contains(kv.Key)) lines.Add(Line(kv.Key, kv.Value));

            File.WriteAllLines(configPath, lines);
        }

        private string Line(string key, string val) =>
            _quote ? $"{key}{_delim}\"{val}\"" : $"{key}{_delim}{val}";
    }
}
```

- [ ] **Step 4: Implement AdapterRegistry**

`src/CoreCage.Core/GameTune/AdapterRegistry.cs`:

```csharp
using System;

namespace CoreCage.Core.GameTune
{
    /// <summary>Maps a profile's `graphics.format` string to the adapter that handles it.</summary>
    public static class AdapterRegistry
    {
        public static IGraphicsConfigAdapter For(string format) => format switch
        {
            "unreal-ini"        => new UnrealIniAdapter(),
            "frostbite-profsave" => new KeyValueAdapter("frostbite-profsave", ' ', quoteValues: false),
            "stingray-config"   => new KeyValueAdapter("stingray-config", '=', quoteValues: false),
            "source-cfg"        => new KeyValueAdapter("source-cfg", ' ', quoteValues: true),
            _ => throw new NotSupportedException($"No GameTune adapter for format '{format}'.")
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter "KeyValueAdapterTests|AdapterRegistryTests"`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/CoreCage.Core/GameTune/KeyValueAdapter.cs src/CoreCage.Core/GameTune/AdapterRegistry.cs tests/CoreCage.Tests/GameTune/KeyValueAdapterTests.cs tests/CoreCage.Tests/GameTune/AdapterRegistryTests.cs
git commit -m "feat(gametune): key/value adapter (Frostbite/Stingray/Source) + registry"
```

---

### Task 6: GameTuneService — orchestrator + safety gate

**Files:**
- Create: `src/CoreCage.Core/GameTune/GameTuneResult.cs`
- Create: `src/CoreCage.Core/GameTune/GameTuneService.cs`
- Test: `tests/CoreCage.Tests/GameTune/GameTuneServiceTests.cs`

**Interfaces:**
- Consumes: `GraphicsBlock` (Task 1), `PathSafety` (Task 2), `ConfigBackup` (Task 3), `AdapterRegistry`/`IGraphicsConfigAdapter` (Tasks 4-5).
- Produces:
  - `enum GameTuneStatus { Applied, Restored, NotSupported, GameRunning, ConfigNotFound, UnsafePath, BackupFailed, ParseError }`
  - `record GameTuneResult(GameTuneStatus Status, string Message, IReadOnlyList<GraphicsChange> Changes, string? BackupPath)`
  - `GameTuneService(ConfigBackup backup, Func<string,bool> isGameRunning)` — `isGameRunning` injected (production passes a `ProcessWatcher`-backed predicate; tests pass a fake).
  - `GameTuneResult Apply(string gameId, string exeName, GraphicsBlock? graphics)`
  - `GameTuneResult Restore(string gameId, string exeName, GraphicsBlock? graphics)`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GameTuneServiceTests
    {
        private string _cfg = "", _bkRoot = "";

        private GraphicsBlock Block() => new GraphicsBlock(
            "unreal-ini", _cfg, new[] { Path.GetDirectoryName(_cfg)! },
            new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null);

        [TestInitialize]
        public void Setup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gts_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            _cfg = Path.Combine(dir, "GameUserSettings.ini");
            File.WriteAllText(_cfg, "[/Script/Engine.GameUserSettings]\nMotionBlur=1\n");
            _bkRoot = Path.Combine(Path.GetTempPath(), "gtsbk_" + Path.GetRandomFileName());
        }

        private GameTuneService Svc(bool running) =>
            new GameTuneService(new ConfigBackup(_bkRoot), _ => running);

        [TestMethod]
        public void Apply_HappyPath_WritesPreset_BacksUp_ReturnsApplied()
        {
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.Applied, r.Status);
            Assert.IsNotNull(r.BackupPath);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=0");
        }

        [TestMethod]
        public void Apply_GameRunning_Aborts_DoesNotWrite()
        {
            var r = Svc(running: true).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.GameRunning, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1"); // unchanged
        }

        [TestMethod]
        public void Apply_NoGraphicsBlock_ReturnsNotSupported()
        {
            var r = Svc(running: false).Apply("repo", "REPO.exe", null);
            Assert.AreEqual(GameTuneStatus.NotSupported, r.Status);
        }

        [TestMethod]
        public void Apply_UnsafePath_Aborts()
        {
            var unsafeBlock = new GraphicsBlock("unreal-ini", _cfg,
                new[] { @"C:\SomeOtherRoot" }, new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null);
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", unsafeBlock);
            Assert.AreEqual(GameTuneStatus.UnsafePath, r.Status);
        }

        [TestMethod]
        public void Apply_ConfigMissing_ReturnsConfigNotFound()
        {
            File.Delete(_cfg);
            var r = Svc(running: false).Apply("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.ConfigNotFound, r.Status);
        }

        [TestMethod]
        public void Restore_AfterApply_RevertsFile()
        {
            var svc = Svc(running: false);
            svc.Apply("arc", "PioneerGame.exe", Block());
            var r = svc.Restore("arc", "PioneerGame.exe", Block());
            Assert.AreEqual(GameTuneStatus.Restored, r.Status);
            StringAssert.Contains(File.ReadAllText(_cfg), "MotionBlur=1");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter GameTuneServiceTests`
Expected: FAIL — `GameTuneService` / `GameTuneStatus` not defined.

- [ ] **Step 3: Implement the result types**

`src/CoreCage.Core/GameTune/GameTuneResult.cs`:

```csharp
using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    public enum GameTuneStatus
    {
        Applied, Restored, NotSupported, GameRunning, ConfigNotFound, UnsafePath, BackupFailed, ParseError
    }

    /// <summary>Outcome of a GameTune Apply/Restore: a typed status the UI turns into a card state,
    /// a human message, the diff that was (or would be) written, and the backup path for trust.</summary>
    public sealed record GameTuneResult(
        GameTuneStatus Status,
        string Message,
        IReadOnlyList<GraphicsChange> Changes,
        string? BackupPath);
}
```

- [ ] **Step 4: Implement the service**

`src/CoreCage.Core/GameTune/GameTuneService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace CoreCage.Core.GameTune
{
    /// <summary>Orchestrates a per-game preset apply/restore behind the non-negotiable safety gate:
    /// game must be closed, the target path must be safe, and a backup must succeed before any write.
    /// Every failure returns a typed <see cref="GameTuneResult"/> rather than throwing.</summary>
    public sealed class GameTuneService
    {
        private static readonly IReadOnlyList<GraphicsChange> None = Array.Empty<GraphicsChange>();
        private readonly ConfigBackup _backup;
        private readonly Func<string, bool> _isGameRunning;

        public GameTuneService(ConfigBackup backup, Func<string, bool> isGameRunning)
        {
            _backup = backup;
            _isGameRunning = isGameRunning;
        }

        public GameTuneResult Apply(string gameId, string exeName, GraphicsBlock? graphics)
        {
            if (graphics is null || graphics.GuidedOnly)
                return R(GameTuneStatus.NotSupported, "No auto-apply preset for this game.");
            if (_isGameRunning(exeName))
                return R(GameTuneStatus.GameRunning, "Close the game to apply settings.");

            var path = PathSafety.Expand(graphics.ConfigPath);
            if (!PathSafety.IsSafe(path, graphics.SafeRoots))
                return R(GameTuneStatus.UnsafePath, "Config path is outside the allowed safe roots.");
            if (!File.Exists(path))
                return R(GameTuneStatus.ConfigNotFound, "Launch the game once to generate its config.");

            string backupPath;
            try { backupPath = _backup.Backup(gameId, path); }
            catch (Exception ex) { return R(GameTuneStatus.BackupFailed, "Backup failed: " + ex.Message); }

            try
            {
                var adapter = AdapterRegistry.For(graphics.Format);
                var plan = adapter.Plan(adapter.Read(path), graphics.CompetitivePreset);
                adapter.Write(path, plan);
                return new GameTuneResult(GameTuneStatus.Applied,
                    plan.Changes.Count == 0 ? "Already optimal." : $"Applied {plan.Changes.Count} setting(s).",
                    plan.Changes, backupPath);
            }
            catch (Exception ex)
            {
                return R(GameTuneStatus.ParseError, "Could not apply preset: " + ex.Message);
            }
        }

        public GameTuneResult Restore(string gameId, string exeName, GraphicsBlock? graphics)
        {
            if (graphics is null) return R(GameTuneStatus.NotSupported, "Nothing to restore.");
            if (_isGameRunning(exeName))
                return R(GameTuneStatus.GameRunning, "Close the game to restore settings.");
            var path = PathSafety.Expand(graphics.ConfigPath);
            return _backup.TryRestoreNewest(gameId, path)
                ? R(GameTuneStatus.Restored, "Restored your previous config.")
                : R(GameTuneStatus.ConfigNotFound, "No backup found to restore.");
        }

        private static GameTuneResult R(GameTuneStatus s, string msg) => new(s, msg, None, null);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter GameTuneServiceTests`
Expected: PASS (6 passed).

- [ ] **Step 6: Commit**

```bash
git add src/CoreCage.Core/GameTune/GameTuneResult.cs src/CoreCage.Core/GameTune/GameTuneService.cs tests/CoreCage.Tests/GameTune/GameTuneServiceTests.cs
git commit -m "feat(gametune): service orchestrator + safety gate (closed/safe/backup)"
```

---

### Task 7: Ship the per-game `graphics` profiles + schema doc

**Files:**
- Create: `profiles/arc-raiders.json`, `profiles/dead-by-daylight.json`, `profiles/battlefield-6.json`, `profiles/helldivers-2.json`, `profiles/team-fortress-2.json`
- Modify: `profiles/SCHEMA.md` (document the `graphics` block)
- Test: `tests/CoreCage.Tests/GameTune/ShippedProfilesTests.cs`

**Note:** The `configPath`, `format` delimiter, and exact `competitivePreset` keys must be **verified against a real config dump** for Frostbite (BF6) and Stingray (Helldivers) before merge — Unreal (ARC/DbD) and Source (TF2) key names are publicly documented. Values below encode the fixed max-FPS/low-lag target (MotionBlur off, shadows low, textures high, upscaling quality, frame-gen off, reflex on, vsync off).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class ShippedProfilesTests
    {
        // Repo-root-relative: tests run from tests/CoreCage.Tests/bin/<cfg>/net8.0
        private static string ProfilesDir =>
            Path.GetFullPath(Path.Combine(TestContext.CurrentContext_Dir, "..", "..", "..", "..", "..", "profiles"));

        // MSTest has no NUnit-style TestContext.CurrentContext; resolve via AppContext.BaseDirectory instead.
        private static string Dir =>
            Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles"));

        [TestMethod]
        public void AllShippedProfiles_LoadWithoutErrors()
        {
            var result = CommunityProfileLoader.Load(Dir);
            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        [TestMethod]
        public void FiveGames_HaveGraphicsBlock_WithMotionBlurOff()
        {
            var result = CommunityProfileLoader.Load(Dir);
            var withGraphics = result.Profiles.Where(p => p.Profile.Graphics is { GuidedOnly: false }).ToList();
            Assert.IsTrue(withGraphics.Count >= 5, $"expected >=5 auto-apply profiles, got {withGraphics.Count}");
        }
    }
}
```

> Delete the stray `ProfilesDir`/`CurrentContext_Dir` lines before running — they document why `AppContext.BaseDirectory` is used. Keep only `Dir`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter ShippedProfilesTests`
Expected: FAIL — profiles dir has only the example file; `< 5` graphics blocks.

- [ ] **Step 3: Write the profile JSONs**

`profiles/arc-raiders.json`:

```json
{
  "game": "Arc Raiders",
  "exe": "PioneerGame-Win64-Shipping.exe",
  "graphics": {
    "format": "unreal-ini",
    "configPath": "%LOCALAPPDATA%\\ArcRaiders\\Saved\\Config\\Windows\\GameUserSettings.ini",
    "safeRoots": ["%LOCALAPPDATA%"],
    "competitivePreset": {
      "MotionBlur": "0",
      "sg.ShadowQuality": "0",
      "sg.TextureQuality": "3",
      "sg.EffectsQuality": "1",
      "sg.PostProcessQuality": "0",
      "bUseVSync": "False"
    },
    "postApplyNotes": "Re-toggle FSR Frame-Gen OFF after launch if it resets."
  }
}
```

`profiles/dead-by-daylight.json`:

```json
{
  "game": "Dead by Daylight",
  "exe": "DeadByDaylight-Win64-Shipping.exe",
  "graphics": {
    "format": "unreal-ini",
    "configPath": "%LOCALAPPDATA%\\DeadByDaylight\\Saved\\Config\\Windows\\GameUserSettings.ini",
    "safeRoots": ["%LOCALAPPDATA%"],
    "competitivePreset": {
      "MotionBlur": "0",
      "sg.ShadowQuality": "0",
      "sg.TextureQuality": "3",
      "sg.PostProcessQuality": "0",
      "bUseVSync": "False"
    }
  }
}
```

`profiles/battlefield-6.json` (⚠ verify keys/path against a real PROFSAVE dump):

```json
{
  "game": "Battlefield 6",
  "exe": "bf6.exe",
  "graphics": {
    "format": "frostbite-profsave",
    "configPath": "%USERPROFILE%\\Documents\\Battlefield 6\\settings\\PROFSAVE_profile",
    "safeRoots": ["%USERPROFILE%\\Documents"],
    "competitivePreset": {
      "GstRender.MotionBlurEnabled": "0",
      "GstRender.ShadowQuality": "0",
      "GstRender.TextureQuality": "2",
      "GstRender.VSyncEnabled": "0",
      "GstRender.ReflexMode": "1"
    },
    "postApplyNotes": "Verify against a real PROFSAVE_profile before trusting these keys."
  }
}
```

`profiles/helldivers-2.json` (⚠ verify keys/path against a real .config dump):

```json
{
  "game": "Helldivers 2",
  "exe": "helldivers2.exe",
  "graphics": {
    "format": "stingray-config",
    "configPath": "%APPDATA%\\Arrowhead\\Helldivers2\\user_settings.config",
    "safeRoots": ["%APPDATA%"],
    "competitivePreset": {
      "motion_blur": "0",
      "shadow_quality": "0",
      "texture_quality": "2",
      "vsync": "0"
    },
    "postApplyNotes": "Verify against a real user_settings.config before trusting these keys."
  }
}
```

`profiles/team-fortress-2.json`:

```json
{
  "game": "Team Fortress 2",
  "exe": "tf_win64.exe",
  "graphics": {
    "format": "source-cfg",
    "configPath": "%PROGRAMFILES(X86)%\\Steam\\steamapps\\common\\Team Fortress 2\\tf\\cfg\\autoexec.cfg",
    "safeRoots": ["%USERPROFILE%\\Documents"],
    "competitivePreset": {
      "mat_motion_blur_enabled": "0",
      "r_shadowrendertotexture": "0",
      "fps_max": "0"
    },
    "guidedOnly": true,
    "postApplyNotes": "TF2 cfg lives inside the Steam install dir → guided-only (path fails the safe-root gate by design). Shown as a copy-paste list, not auto-written."
  }
}
```

- [ ] **Step 4: Document the block in `profiles/SCHEMA.md`**

Append a `## graphics (optional)` section describing every field (`format`, `configPath`, `safeRoots`, `competitivePreset`, `guidedOnly`, `postApplyNotes`), the four valid `format` values, and the rule that `configPath` must resolve under a `safeRoots` entry and outside any install dir or the write is refused.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter ShippedProfilesTests`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
git add profiles/ tests/CoreCage.Tests/GameTune/ShippedProfilesTests.cs
git commit -m "feat(gametune): ship ARC/DbD/BF6/Helldivers/TF2 graphics profiles + schema"
```

---

### Task 8: GamePresetsViewModel — per-card state machine (headless, fully tested)

**Files:**
- Create: `src/CoreCage.App/ViewModels/GamePresetCardViewModel.cs`
- Create: `src/CoreCage.App/ViewModels/GamePresetsViewModel.cs`
- Test: `tests/CoreCage.Tests/GameTune/GamePresetsViewModelTests.cs`

**Interfaces:**
- Consumes: `GameTuneService`, `GameTuneResult`/`GameTuneStatus` (Task 6); a detected-games list (from `ProcessWatcher`/profile match — passed in as `IReadOnlyList<DetectedGame>` so the VM stays testable).
- Produces:
  - `enum CardState { Ready, Applied, GameRunning, NotSupported, ConfigNotFound, Error }`
  - `record DetectedGame(string GameId, string ExeName, string DisplayName, GraphicsBlock? Graphics)`
  - `class GamePresetCardViewModel { string DisplayName; CardState State; string StatusText; bool CanApply; bool CanRestore; void Apply(); void Restore(); }`
  - `class GamePresetsViewModel { IReadOnlyList<GamePresetCardViewModel> Cards; bool IsEmpty; }`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;
using CoreCage.App.ViewModels;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class GamePresetsViewModelTests
    {
        private static (GameTuneService svc, string cfg) Svc(bool running)
        {
            var dir = Path.Combine(Path.GetTempPath(), "vm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            var cfg = Path.Combine(dir, "GameUserSettings.ini");
            File.WriteAllText(cfg, "[/Script/Engine.GameUserSettings]\nMotionBlur=1\n");
            var bk = Path.Combine(Path.GetTempPath(), "vmbk_" + Path.GetRandomFileName());
            return (new GameTuneService(new ConfigBackup(bk), _ => running), cfg);
        }

        private static DetectedGame Arc(string cfg) => new(
            "arc", "PioneerGame.exe", "Arc Raiders",
            new GraphicsBlock("unreal-ini", cfg, new[] { Path.GetDirectoryName(cfg)! },
                new Dictionary<string, string> { ["MotionBlur"] = "0" }, false, null));

        [TestMethod]
        public void Card_ReadyGame_CanApply_NotRestore()
        {
            var (svc, cfg) = Svc(running: false);
            var vm = new GamePresetsViewModel(svc, new[] { Arc(cfg) });
            var card = vm.Cards[0];
            Assert.AreEqual(CardState.Ready, card.State);
            Assert.IsTrue(card.CanApply);
            Assert.IsFalse(card.CanRestore);
        }

        [TestMethod]
        public void Card_Apply_MovesToApplied_EnablesRestore()
        {
            var (svc, cfg) = Svc(running: false);
            var card = new GamePresetsViewModel(svc, new[] { Arc(cfg) }).Cards[0];
            card.Apply();
            Assert.AreEqual(CardState.Applied, card.State);
            Assert.IsTrue(card.CanRestore);
        }

        [TestMethod]
        public void Card_GameRunning_CannotApply_ShowsReason()
        {
            var (svc, cfg) = Svc(running: true);
            var card = new GamePresetsViewModel(svc, new[] { Arc(cfg) }).Cards[0];
            card.Apply();
            Assert.AreEqual(CardState.GameRunning, card.State);
            Assert.IsFalse(card.CanApply);
            StringAssert.Contains(card.StatusText, "Close the game");
        }

        [TestMethod]
        public void Card_NoGraphics_IsNotSupported()
        {
            var (svc, _) = Svc(running: false);
            var game = new DetectedGame("repo", "REPO.exe", "R.E.P.O.", null);
            var card = new GamePresetsViewModel(svc, new[] { game }).Cards[0];
            Assert.AreEqual(CardState.NotSupported, card.State);
            Assert.IsFalse(card.CanApply);
        }

        [TestMethod]
        public void Vm_NoGames_IsEmpty()
        {
            var (svc, _) = Svc(running: false);
            var vm = new GamePresetsViewModel(svc, new DetectedGame[0]);
            Assert.IsTrue(vm.IsEmpty);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CoreCage.Tests --filter GamePresetsViewModelTests`
Expected: FAIL — VM types not defined.

- [ ] **Step 3: Implement the card VM**

`src/CoreCage.App/ViewModels/GamePresetCardViewModel.cs`:

```csharp
using System.Collections.Generic;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    public enum CardState { Ready, Applied, GameRunning, NotSupported, ConfigNotFound, Error }

    /// <summary>A detected game plus the (optional) graphics preset context the UI needs.</summary>
    public sealed record DetectedGame(string GameId, string ExeName, string DisplayName, GraphicsBlock? Graphics);

    /// <summary>State machine for one game card: computes its state/affordances from the last
    /// GameTune result and drives Apply/Restore through the service.</summary>
    public sealed class GamePresetCardViewModel
    {
        private readonly GameTuneService _svc;
        private readonly DetectedGame _game;

        public string DisplayName => _game.DisplayName;
        public CardState State { get; private set; }
        public string StatusText { get; private set; } = "";
        public IReadOnlyList<GraphicsChange> LastChanges { get; private set; } = System.Array.Empty<GraphicsChange>();
        public string? BackupPath { get; private set; }

        public bool CanApply => State is CardState.Ready or CardState.ConfigNotFound or CardState.Error;
        public bool CanRestore => State is CardState.Applied;

        public GamePresetCardViewModel(GameTuneService svc, DetectedGame game)
        {
            _svc = svc; _game = game;
            State = (game.Graphics is null || game.Graphics.GuidedOnly) ? CardState.NotSupported : CardState.Ready;
            StatusText = State == CardState.NotSupported ? "Guided only — no auto-apply." : "Ready to apply Max-FPS.";
        }

        public void Apply() => Absorb(_svc.Apply(_game.GameId, _game.ExeName, _game.Graphics));
        public void Restore() => Absorb(_svc.Restore(_game.GameId, _game.ExeName, _game.Graphics));

        private void Absorb(GameTuneResult r)
        {
            StatusText = r.Message;
            LastChanges = r.Changes;
            BackupPath = r.BackupPath ?? BackupPath;
            State = r.Status switch
            {
                GameTuneStatus.Applied      => CardState.Applied,
                GameTuneStatus.Restored     => CardState.Ready,
                GameTuneStatus.GameRunning  => CardState.GameRunning,
                GameTuneStatus.NotSupported => CardState.NotSupported,
                GameTuneStatus.ConfigNotFound => CardState.ConfigNotFound,
                GameTuneStatus.UnsafePath   => CardState.NotSupported,
                _                           => CardState.Error
            };
        }
    }
}
```

- [ ] **Step 4: Implement the page VM**

`src/CoreCage.App/ViewModels/GamePresetsViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    /// <summary>Backing model for the Game Presets page: one card per detected game.</summary>
    public sealed class GamePresetsViewModel
    {
        public IReadOnlyList<GamePresetCardViewModel> Cards { get; }
        public bool IsEmpty => Cards.Count == 0;

        public GamePresetsViewModel(GameTuneService svc, IReadOnlyList<DetectedGame> detected)
        {
            Cards = detected.Select(g => new GamePresetCardViewModel(svc, g)).ToList();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CoreCage.Tests --filter GamePresetsViewModelTests`
Expected: PASS (5 passed).

- [ ] **Step 6: Commit**

```bash
git add src/CoreCage.App/ViewModels/GamePresetCardViewModel.cs src/CoreCage.App/ViewModels/GamePresetsViewModel.cs tests/CoreCage.Tests/GameTune/GamePresetsViewModelTests.cs
git commit -m "feat(gametune): game-presets viewmodel + card state machine"
```

---

### Task 9: Game Presets page (WPF) + navigation wiring

**Files:**
- Create: `src/CoreCage.App/Views/GamePresetsPage.xaml`
- Create: `src/CoreCage.App/Views/GamePresetsPage.xaml.cs`
- Modify: `src/CoreCage.App/MainWindow.xaml` (add nav item) and `MainWindow.xaml.cs` (route to the page) — follow the existing pattern used by `MonitorPage`/`OptimizePage`/`ProcessesPage`.

**Note:** This is UI wiring; correctness of the state machine is already covered by Task 8. Verify visually per the UX checklist below.

- [ ] **Step 1: Build the page markup**

`src/CoreCage.App/Views/GamePresetsPage.xaml` — an `ItemsControl` bound to `Cards`, each card a bordered panel showing `DisplayName`, `StatusText`, an **Apply Max-FPS** primary button (`IsEnabled="{Binding CanApply}"`), a **Restore** secondary button (`IsEnabled="{Binding CanRestore}"`), and a collapsed expander for `LastChanges` (the per-setting diff). Include an empty-state `TextBlock` bound to `IsEmpty` visibility.

**UX Pro Max checklist (bake into the XAML):**
- State for everything: the card's `StatusText` + button enablement already reflect Ready/Applied/GameRunning/NotSupported/ConfigNotFound/Error.
- Visual hierarchy: `DisplayName` at a heading size, `StatusText` secondary; primary vs secondary button styling.
- Consistent spacing + type scale: reuse the app's existing styles/margins from `OptimizePage.xaml`.
- WCAG-AA contrast in light AND dark — reuse app theme brushes, don't hardcode colors.
- Keyboard focus rings visible (`FocusVisualStyle` not stripped); Apply is the default focus.
- Feedback on every action: after Apply/Restore, `StatusText` updates and the backup path shows.
- Progressive disclosure: the diff is in a collapsed `Expander`.

- [ ] **Step 2: Wire the code-behind**

`GamePresetsPage.xaml.cs` — construct the `GameTuneService` (with a `ConfigBackup` rooted at `%LOCALAPPDATA%\CoreCage\backups` and an `isGameRunning` predicate backed by the real `ProcessWatcher`), build the `DetectedGame` list from the loaded profiles + detection, set `DataContext = new GamePresetsViewModel(...)`. Bind buttons to `Apply()`/`Restore()` and refresh the bound card after each (raise `INotifyPropertyChanged` or re-set DataContext).

- [ ] **Step 3: Add navigation entry**

In `MainWindow.xaml` add a "Game Presets" nav item next to the existing pages; in `MainWindow.xaml.cs` route it to `new GamePresetsPage()`, mirroring how `OptimizePage` is instantiated.

- [ ] **Step 4: Build + run the app to verify visually**

Run: `dotnet build src/CoreCage.App` then launch **elevated** (per CoreCage's `app.manifest` requireAdministrator).
Expected: "Game Presets" page lists your detected games; a card in Ready state applies and flips to Applied with a backup path; launching a game disables its Apply with "Close the game to apply."

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: all existing tests + the new GameTune tests PASS (349 prior + ~24 new).

- [ ] **Step 6: Commit**

```bash
git add src/CoreCage.App/Views/GamePresetsPage.xaml src/CoreCage.App/Views/GamePresetsPage.xaml.cs src/CoreCage.App/MainWindow.xaml src/CoreCage.App/MainWindow.xaml.cs
git commit -m "feat(gametune): Game Presets WPF page + nav wiring"
```

---

## Self-Review

**Spec coverage:**
- §Data (graphics block on profile) → Task 1 ✅
- §Safety (path guard, backup, game-closed) → Tasks 2, 3, 6 ✅
- §Adapters (Unreal + Frostbite/Stingray/Source) → Tasks 4, 5 ✅
- §PresetEngine + safety gate → Task 6 ✅
- §Data profiles for the 5 games + SCHEMA → Task 7 ✅
- §UI panel with state-for-everything + UX Pro Max → Tasks 8 (logic), 9 (view) ✅
- §Error-handling table → GameTuneStatus + service tests (Task 6) ✅
- §Testing (adapter round-trip, safety gates, restore, profile loading) → Tasks 1-8 ✅
- §Phase-2 seams (Prove-It, AI filler) → intentionally NOT built; the adapter + service seams they plug into exist. ✅

**Placeholder scan:** No TBD/TODO left as requirements. Two profile files (BF6, Helldivers) carry explicit "verify against a real dump" notes — these are genuine data-verification steps, not code placeholders, and the code that consumes them is complete.

**Type consistency:** `GraphicsBlock`, `GraphicsReadResult`, `GraphicsApplyPlan`, `GraphicsChange`, `GraphicsSetting` (Task 1) are used unchanged in Tasks 4-8. `GameTuneStatus`/`GameTuneResult` (Task 6) map cleanly to `CardState` (Task 8). `IGraphicsConfigAdapter.Format`/`Read`/`Plan`/`Write` signatures match across Tasks 4, 5, 6. `AdapterRegistry.For` returns the interface used by the service. Consistent.
