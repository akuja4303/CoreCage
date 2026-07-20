# Safety & Anti-Cheat Posture

CoreCage is built to be run on a machine you also play anti-cheat-protected games on. The
guiding rule is simple:

> **User-mode only. No kernel drivers. Nothing ever reads or writes a running game's memory.**

Every optimization is a change to *Windows*, the *registry*, *process scheduling*, or your
*network stack* — never to the game process. That is what keeps it EAC-, BattlEye-, and
Vanguard-compatible by design.

## What CoreCage does NOT do

- **No kernel-mode driver.** CoreCage ships no `.sys` driver and loads none. Everything runs as
  an ordinary (sometimes elevated) user-mode process.
- **No game memory access.** It never opens a handle into a game to read or write its memory,
  never injects a DLL, never hooks its API calls. Anti-cheat systems flag exactly that; CoreCage
  stays outside the game entirely.
- **No input automation into anti-cheat games.** It tunes the environment around the game, not
  the game.

The one place CoreCage touches a game *by name* is scheduling and QoS — it sets the foreground
game's priority, keeps the background cage off its reserved cores, and can mark its network
traffic (DSCP). These are OS-level operations on the process table and the network stack, the
same things Task Manager and `netsh` expose. They do not touch the game's address space.

## The Big Red Button — full restore guarantee

`RestoreEverything.RestoreAll()` (`src/CoreCage.Core/RestoreEverything.cs`), surfaced as
**Restore Everything** on the Optimize page, reverses every change CoreCage can make, in
dependency-correct order, swallowing errors so one failure never aborts the rest. It restores:

- **Gaming Mode++** — MSI interrupt mode, NIC advanced properties, GameDVR/Game Bar policy,
  background-UWP policy, and QoS policies.
- **Priorities** — every process CoreCage throttled goes back to `Normal`.
- **Affinities** — every process caged onto a core subset goes back to the full-core mask (the
  safety net if a crash lost the in-memory cage plan).
- **Power plan** — back to Balanced; core-park and min-perf floor restored.
- **Timer resolution** — back to the Windows default.
- **Network** — TCP autotuning, RSS, RSC, ECN back to defaults; DNS restored; QoS policies removed.
- **Services** — telemetry/search/print/SysMain re-enabled and started.
- **Registry** — snapshot-before-write captures of your original values restored; IFEO entries
  CoreCage created cleared; TdrDelay reset.
- **Auto-start** — CoreCage scheduled tasks removed.

It returns a summary ("*N change(s) reversed*") and never throws. Individual modes also fall
back to this Big Red Button if any of their own revert steps fail, so a partial revert can never
strand the system. A reboot is recommended after a full restore so MSI mode and TdrDelay revert
completely.

## Per-tweak safety table

**Safe** = user-mode, reversible, no interaction with a game process; on by default.
**Caution** = writes to hardware power/clock state; can fail or need on-rig validation, so it is
**opt-in / off by default** and searches conservatively.

| Tweak | Tier | What it does / why the tier |
|---|---|---|
| Process priority (IFEO + live) | **Safe** | Sets the foreground game high and background hogs lower via IFEO registry + `Process.PriorityClass`. Reversible; same surface as Task Manager. |
| Core Cage (process affinity) | **Safe** | Reserves top cores for the game and confines background processes to the rest via user-mode `Process.ProcessorAffinity`. On by default. Whitelists the game, foreground app, audio engine, and protected system processes; fully reversible. |
| Timer resolution | **Safe** | `NtSetTimerResolution` down to ~0.5 ms for smoother scheduling. Process-scoped, reverts to default. |
| GameDVR / Game Bar | **Safe** | Registry policy to stop the background frame-capture pipeline. Revert removes the policy override so your Settings choice wins again. |
| Background UWP apps | **Safe** | Registry policy to stop UWP apps waking in the background. Reversible. |
| MSI interrupt mode | **Safe** | Registry `MSISupported=1` on GPU/NIC to cut DPC latency. Reversible; needs a reboot to take effect. |
| NIC advanced properties | **Safe** | Disables power-saving NIC features (EEE, flow control, etc.) via standard NDIS registry keywords. Original values are snapshotted per-adapter and restored exactly. |
| QoS DSCP marking | **Safe** | Tags the game's traffic for router priority via `New-NetQosPolicy`. Removed on restore. |
| Power plan / core-unpark / perf floor | **Safe** | `powercfg` changes (unpark cores, raise min-perf floor, set plan). Reversible; restore returns to Balanced. |
| Network stack (TCP/DNS) | **Safe** | `netsh` TCP autotuning/RSS/ECN and DNS tweaks. Reset to Windows defaults on restore. |
| Standby memory list cleaner | **Safe** | Flushes the standby list to free RAM. Read-only w.r.t. games. |
| TdrDelay | **Safe** | Registry GPU-driver timeout bump. Reversible (deleted on restore); reboot for full effect. |
| GPU core-clock offset auto-tune (NVAPI) | **Caution** | Applies a GPU core-clock offset (NVIDIA only). Overclock — can cause a driver TDR/artifacting; the search is stability-gated and settles at the most conservative clock that reaches near-best FPS, but it is still a hardware push. **Off by default**, Advanced opt-in. Memory clock is never touched. |
| GPU power limit | **Caution** | `nvidia-smi -pl` (NVIDIA only). Changes the board power cap. Off by default. |
| CPU Curve Optimizer / SMU (ryzenadj) | **Caution** | Writes to the AMD SMU for undervolt/PBO. **The single highest freeze/brick-risk lever, off by default.** The `ryzenadj`/SMU write path is documented to fault (`0xC0000005`) on some APUs such as Cezanne (Ryzen 5000G), so it can simply fail to apply on that hardware. On a GPU-bound rig it buys ~zero felt FPS, which is why it stays off. The CPU **Thermal Guard** — which only reins in *background* CPU hogs, a Safe workload-throttle — is the working lever there instead. |

If you are ever unsure, hit **Restore Everything** and reboot. Getting back to a clean Windows
state is a first-class feature, not an afterthought.
