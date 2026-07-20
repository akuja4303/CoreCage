# Adding a Mode Module

A **mode** in CoreCage is a self-contained bundle of tweaks with one Apply and one Revert.
"Gaming" is the only built-in today, but the seam is deliberately open: a new mode
(Coding, Streaming, or a private module shipped outside this repo) attaches by implementing
one interface and calling one register method — **no edit to CoreCage.Core source required**.

The registry and the UI drive every mode uniformly; neither knows anything about a mode's
actual tweaks. This is the "modulate later" seam.

## The contract

`src/CoreCage.Core/Modes/IModeModule.cs`:

```csharp
public interface IModeModule
{
    // Stable identifier, e.g. "Gaming". Used as the ModeRegistry.Get key (case-insensitive).
    string Name { get; }

    // Human-readable summary of what this mode does, for UI display.
    string Description { get; }

    // True if this mode is currently applied. Persist this so it survives a crash/relaunch.
    bool IsActive { get; }

    // Apply the mode's tweaks. Report each pipeline step via progress if given.
    Task<ModeResult> ApplyAsync(IProgress<string>? progress = null);

    // Revert the mode's tweaks. Report each pipeline step via progress if given.
    Task<ModeResult> RevertAsync(IProgress<string>? progress = null);
}

// Outcome of an Apply/Revert call.
public sealed record ModeResult(bool Success, string Summary, IReadOnlyList<string> Steps);
```

That is the whole surface. `ModeResult` carries:

- `Success` — did the operation succeed overall.
- `Summary` — a one-line message for the UI (e.g. `"Gaming Mode applied -- caged 41 process(es)"`).
- `Steps` — the ordered list of steps taken, for logs and diagnostics.

## Registering

`src/CoreCage.Core/Modes/ModeRegistry.cs` is the catalog. Register at startup:

```csharp
ModeRegistry.Register(new MyCustomMode());
```

- `ModeRegistry.Modules` — all registered modules (built-ins plus anything `Register` added).
- `ModeRegistry.Register(module)` — adds a module. If one with the same `Name`
  (case-insensitive) already exists, it is **replaced** — so a private module can override a
  built-in, or a test can swap in a fresh instance, without a separate `Unregister` API.
- `ModeRegistry.Get(name)` — looks up a module by `Name` (case-insensitive); `null` if not found.

A private module never needs to touch `CoreCage.Core`; it ships its own `IModeModule`
implementation and calls `Register` once at launch.

## What a good module honors

The built-in `GamingMode` (`src/CoreCage.Core/Modes/GamingMode.cs`) is the reference
implementation. When you write your own, mirror these properties — the app relies on them:

1. **Persist `IsActive`.** `GamingMode` writes a small JSON flag file
   (`%LOCALAPPDATA%\CoreCage\mode-state.json`) so that if the process is killed mid-mode,
   the next launch can detect the half-applied state and offer to finish reverting instead of
   silently believing nothing was ever applied.

2. **Run work off the UI thread.** `ApplyAsync`/`RevertAsync` wrap their pipeline in
   `Task.Run(...)` and report each step through the `IProgress<string>` so the UI can show live
   progress.

3. **Revert must not be able to leave the rig half-tweaked.** `GamingMode.RevertAsync` reverses
   its layers in the opposite order; if any revert step throws, it falls back to
   `RestoreEverything.RestoreAll()` — the Big Red Button (see [safety.md](safety.md)) — so a
   partial revert can never strand the system. Any mode that mutates the OS should do the same.

4. **Never throw out of Apply/Revert.** Catch, log, and return
   `new ModeResult(false, "...", steps)`. A mode that throws breaks the uniform driver.

5. **Stay EAC-safe.** Follow the safety posture in [safety.md](safety.md): user-mode APIs only,
   no kernel drivers, no reads/writes into a running game's memory.
