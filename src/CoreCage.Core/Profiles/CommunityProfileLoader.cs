using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using CoreCage.Core.Ledger;

namespace CoreCage.Core.Profiles
{
    /// <summary>Community-submitted benchmark provenance for a profile (schema: submittedBenchmark
    /// {fps, onePctLow, rig}). Kept OFF <see cref="GameProfile"/> — it's PR-review evidence, not
    /// something the runtime engine reads when applying a profile.</summary>
    public sealed record SubmittedBenchmark(double Fps, double OnePctLow, string Rig);

    /// <summary>One successfully-loaded community profile: the runtime <see cref="GameProfile"/>
    /// plus the submission metadata (tweaks referenced, notes, benchmark) that doesn't belong on
    /// GameProfile itself.</summary>
    public sealed record CommunityProfileEntry(
        GameProfile Profile,
        IReadOnlyList<string> Tweaks,
        string Notes,
        SubmittedBenchmark? SubmittedBenchmark);

    /// <summary>One file that failed to load, and why. Malformed JSON in one submission must never
    /// stop the rest of the directory from loading.</summary>
    public sealed record ProfileLoadError(string FilePath, string Message);

    /// <summary>One non-fatal issue found in an otherwise-loadable file (e.g. an unrecognized tweak
    /// id, or a `priority` that doesn't parse as a <see cref="ProcessPriorityClass"/>). Unlike
    /// <see cref="ProfileLoadError"/>, a warning never blocks the profile from loading — it's a
    /// "you should look at this" flag for the PR author/reviewer, not a hard failure.</summary>
    public sealed record ProfileLoadWarning(string FilePath, string Message);

    /// <summary>Result of loading a directory of community profiles: what loaded, what didn't, and
    /// what loaded but looks suspicious (<see cref="Warnings"/>).</summary>
    public sealed record CommunityProfileLoadResult(
        IReadOnlyList<CommunityProfileEntry> Profiles,
        IReadOnlyList<ProfileLoadError> Errors,
        IReadOnlyList<ProfileLoadWarning> Warnings);

    /// <summary>Wire shape of a single profiles\*.json submission. Mirrors profiles\SCHEMA.md
    /// exactly — kept private so a schema tweak here can't leak an unintended public JSON contract.</summary>
    file sealed class CommunityProfileDto
    {
        public string? Game { get; set; }
        public string? Exe { get; set; }
        public int[]? ReservedCores { get; set; }
        public string? Priority { get; set; }
        public string[]? Tweaks { get; set; }
        public string? Notes { get; set; }
        public SubmittedBenchmarkDto? SubmittedBenchmark { get; set; }
        public GraphicsBlockDto? Graphics { get; set; }
        public SensitivityBlockDto? Sensitivity { get; set; }
    }

    file sealed class SubmittedBenchmarkDto
    {
        public double Fps { get; set; }
        public double OnePctLow { get; set; }
        public string? Rig { get; set; }
    }

    file sealed class GraphicsBlockDto
    {
        public string? Format { get; set; }
        public string? ConfigPath { get; set; }
        public string[]? SafeRoots { get; set; }
        public Dictionary<string, string>? CompetitivePreset { get; set; }
        public bool GuidedOnly { get; set; }
        public string? PostApplyNotes { get; set; }
    }

    file sealed class SensitivityBlockDto
    {
        public string? Key { get; set; }
        public double Yaw { get; set; }
    }

    /// <summary>
    /// Loads community-submitted, PR-able game profiles from a directory of JSON files
    /// (see profiles\SCHEMA.md) and maps them onto the engine's existing <see cref="GameProfile"/> /
    /// <see cref="ProfileMatcher"/> types. Never throws on bad input: a missing directory yields an
    /// empty result, and a malformed file is reported per-file in
    /// <see cref="CommunityProfileLoadResult.Errors"/> while the rest of the directory still loads.
    /// </summary>
    public static class CommunityProfileLoader
    {
        public static CommunityProfileLoadResult LoadDirectory(string dir)
        {
            var profiles = new List<CommunityProfileEntry>();
            var errors = new List<ProfileLoadError>();
            var warnings = new List<ProfileLoadWarning>();

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return new CommunityProfileLoadResult(profiles, errors, warnings);

            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string text = File.ReadAllText(file);
                    var dto = JsonConvert.DeserializeObject<CommunityProfileDto>(text);
                    if (dto == null)
                    {
                        errors.Add(new ProfileLoadError(file, "File parsed to null (empty or literal 'null')."));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(dto.Exe))
                    {
                        errors.Add(new ProfileLoadError(file, "Missing required field 'exe'."));
                        continue;
                    }

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

                    CoreCage.Core.GameTune.SensitivityBlock? sensitivity = null;
                    if (dto.Sensitivity is { } sd && !string.IsNullOrWhiteSpace(sd.Key) && sd.Yaw > 0)
                        sensitivity = new CoreCage.Core.GameTune.SensitivityBlock(sd.Key!, sd.Yaw);

                    var profile = new GameProfile
                    {
                        ExeName = dto.Exe,
                        DisplayName = dto.Game ?? dto.Exe,
                        Mode = ProfileMode.Gaming,
                        ReservedCores = dto.ReservedCores ?? Array.Empty<int>(),
                        Priority = string.IsNullOrWhiteSpace(dto.Priority) ? "High" : dto.Priority,
                        Graphics = graphics,
                        Sensitivity = sensitivity,
                    };

                    SubmittedBenchmark? bench = dto.SubmittedBenchmark == null
                        ? null
                        : new SubmittedBenchmark(
                            dto.SubmittedBenchmark.Fps,
                            dto.SubmittedBenchmark.OnePctLow,
                            dto.SubmittedBenchmark.Rig ?? "");

                    var tweaks = dto.Tweaks ?? Array.Empty<string>();

                    // MINOR-1: tweaks[] is informational (not auto-applied), but a typo'd id is
                    // still worth flagging — non-fatal, the profile loads either way.
                    foreach (string tweakId in tweaks)
                    {
                        if (!TweakIds.IsKnown(tweakId))
                            warnings.Add(new ProfileLoadWarning(file,
                                $"Unknown tweak id '{tweakId}' (not one of CoreCage.Core.Ledger.TweakIds: " +
                                $"{string.Join(", ", TweakIds.All)})."));
                    }

                    // MINOR-2: priority is unset (allowed) when omitted/blank; otherwise it must
                    // parse as a ProcessPriorityClass name, case-insensitively.
                    if (!string.IsNullOrWhiteSpace(dto.Priority) &&
                        !Enum.TryParse<ProcessPriorityClass>(dto.Priority, ignoreCase: true, out _))
                    {
                        warnings.Add(new ProfileLoadWarning(file,
                            $"Unknown priority '{dto.Priority}' (expected one of: " +
                            $"{string.Join(", ", Enum.GetNames(typeof(ProcessPriorityClass)))})."));
                    }

                    profiles.Add(new CommunityProfileEntry(
                        profile,
                        tweaks,
                        dto.Notes ?? "",
                        bench));
                }
                catch (JsonException ex)
                {
                    errors.Add(new ProfileLoadError(file, $"Invalid JSON: {ex.Message}"));
                }
                catch (Exception ex)
                {
                    errors.Add(new ProfileLoadError(file, $"Failed to load: {ex.Message}"));
                }
            }

            return new CommunityProfileLoadResult(profiles, errors, warnings);
        }
    }

    /// <summary>
    /// Pure foreground-detect -&gt; community-profile match, gated on the existing confidence
    /// classifier so a low-confidence or non-Gaming decision never auto-applies a profile.
    /// No I/O, no OS mutation, no Process access — reuses <see cref="ProfileMatcher.Match"/> for the
    /// actual exe matching. This is the seam Task 7's auto-apply hookup unit-tests against with a
    /// fake detected exe + a synthetic <see cref="Detection.ModeDecision"/>, instead of forcing a
    /// fragile live-event integration into the static <see cref="ProcessWatcher"/>/WMI path (see the
    /// wiring comment at ProcessWatcher.ProcessStarted).
    /// </summary>
    public static class CommunityProfileAutoApply
    {
        public static GameProfile? MatchForAutoApply(
            string detectedExe,
            IReadOnlyList<GameProfile> communityProfiles,
            Detection.ModeDecision decision)
        {
            if (decision.Mode != Detection.ActivityMode.Gaming) return null;
            if (decision.Confidence < Detection.ConfidenceClassifier.DecisionThreshold) return null;
            return ProfileMatcher.Match(detectedExe, communityProfiles);
        }
    }
}
