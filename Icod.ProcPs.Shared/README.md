# Icod.ProcPs.Shared

`Icod.ProcPs.Shared` is the suite-specific class library for the selected
procps-ng 4.0.6 command set in this repository. It owns process enumeration,
procps field semantics, Linux `/proc` parsing, native Windows and macOS
observation providers, conservative fallback observations for other platforms,
process selection, the shared pgrep/pkill/pidwait matching grammar, system metrics,
exact Linux slab-cache observation and `/proc/slabinfo` parsing, vmstat-specific
cumulative counters and disk observations, sampling calculations, personalities,
sorting, and reusable screen-state models.

Cross-suite process-control mechanics are provided by the published
`Icod.Processes` package: process identities and reuse tokens,
process/process-group/session targets, launching, arbitrary waiting, signal
delivery (including queued values), priority changes, and status translation.
Monotonic elapsed-time and periodic scheduling primitives are provided by the
standalone `Icod.Timing` package. GNU/POSIX regular-expression matching remains
provided by `Icod.CommandFramework.RegularExpressions`; it is the only
CommandFramework subsystem consumed by this library. ProcPs owns observation
provenance and semantic-fidelity policy, including `ProcObservationFidelity`.

Linux `/proc` is the authoritative procps-ng data source. The neutral models do
not require non-Linux systems to pretend that Linux-only counters exist:
Linux-specific CPU, `/proc/loadavg`, `vmstat`, slab, huge-page, namespace, and
container fields remain separately available where applicable, while common CPU
activity (including native counter width), load averages, memory, swap, commit,
uptime, and session observations carry their own provenance and
`ProcObservationFidelity`.

The primary provider matrix is:

| Area | Linux | Windows | macOS |
|---|---|---|---|
| Process detail | `/proc` + shared identity provider | .NET process data augmented by Tool Help and Terminal Services session APIs | .NET process data augmented by Darwin `libproc` and POSIX APIs |
| Process memory maps | `/proc/PID/maps` and `/proc/PID/smaps` | explicitly unsupported until a complete address-space contract is implemented | explicitly unsupported until a complete address-space contract is implemented |
| Memory / swap | `/proc/meminfo` | `GetPerformanceInfo` + `EnumPageFilesW` | Mach VM statistics + `hw.memsize` + `vm.swapusage` |
| vmstat paging / block I/O | `/proc/vmstat` + `/proc/diskstats` + sysfs partition identity | explicitly unavailable when no defensible native equivalent is exposed | Mach page/swap counters; Linux disk modes remain unsupported |
| Slab allocator | `/proc/slabinfo`, including exact `slabdata` counts | unsupported | unsupported |
| CPU activity | `/proc/stat` | `GetSystemTimes` | Mach `host_statistics` |
| Load average | `/proc/loadavg` | unsupported: no native Unix load-average metric | `getloadavg()` |
| Uptime | `/proc/uptime` | `GetTickCount64` | `kern.boottime` |
| Logged-in users | libc `utmpx` | Windows Terminal Services sessions | libc `utmpx` |

Windows Remote Desktop/Terminal Services session identifiers are deliberately
exposed as `PlatformSessionId`; they are not POSIX process-session identifiers.
Likewise, a Windows load average is not synthesized from CPU utilization or
another unrelated counter. Unsupported or unavailable values remain explicit
instead of being invented as zero.

A final portable provider remains for platforms without one of the dedicated
backends. It intentionally exposes only observations whose semantics are
portable enough to defend.

## Slab allocator observation

`ProcSlabCacheEntry` is the neutral record consumed by `slabtop`. It preserves
the cache name, active and total object counts, object size, objects per slab,
pages per slab, and the kernel's explicit active and total slab counts. The
constructor rejects impossible active/total relationships and zero slab geometry
so invalid observations cannot enter the reporting layer silently.

`IProcSlabProvider` is the injectable observation contract.
`SystemProcSlabProvider` selects `LinuxProcSlabProvider` on Linux and otherwise
returns an explicit `Unsupported` observation. The Linux provider reads
`/proc/slabinfo`, marks successful observations as `LinuxProcfs` with exact
fidelity, and maps access, availability, and malformed-data failures into the
normal `ProcObservedValue<T>` contract.

`ProcKernelMemoryParsers.ParseSlabInfo` parses the complete slabinfo text. It
requires the core numeric fields and the `slabdata` active/total slab counts; it
does not approximate slab counts from object totals. The parser is public so
fixture-backed consumers and tests can validate authoritative kernel text
without requiring the host running the test to be Linux.

Presentation and terminal lifecycle do not live in this library. Interactive
`slabtop` consumes these observations and delegates the live screen to
`Icod.DCurses`; monotonic refresh deadlines come from `Icod.Timing`.

## Batch 59 process-matching family

`ProcMatchCommand` is the single procps-ng 4.0.6 engine used by `pgrep`,
`pkill`, and `pidwait`. The command profiles share GNU ERE matching through the
cross-suite managed regular-expression provider, OR-within/AND-between selector
semantics, PID/parent/group/session/user/terminal/state/cgroup/namespace/age/
environment filtering, newest/oldest selection, pidfile policy, ancestor
exclusion, and signal-handler selection. Linux lightweight-task enumeration and
environment/namespace observations stay suite-specific here; actual signal
delivery, queued values, reuse-aware waiting, and signal-disposition observation
continue to use the cross-suite process-control contracts.

`pidwait` is the only installed waiting executable in the pinned procps-ng
4.0.6 profile. No `pwait` launcher or project is created.

## Batch 60 process identity and path lookup

`SystemProcProcessPathProvider` is the shared reuse-aware source for executable,
process-root, and current-working-directory paths used by `pidof` and `pwdx`.
Linux observes `/proc/PID/exe`, `/proc/PID/root`, and `/proc/PID/cwd`; macOS uses
Darwin `libproc` (`proc_pidpath` and `PROC_PIDVNODEPATHINFO`); Windows supplies
executable identity from the native/.NET process surface but deliberately marks
another process's CWD and POSIX-style process root unsupported because Windows
has no stable documented equivalent.

`ProcProcessLookupCommand` contains the pinned procps-ng 4.0.6 lookup profiles so
matching and process-race policy stay in the ProcPs family layer. `pidof` covers
executable/argv identity, script matching, root filtering, omit lists including
`%PPID`, custom separators, single-shot/quiet behavior, and Linux lightweight
tasks. `pwdx` uses the same reuse-protected path observations for one or more
processes and reports vanished, denied, and unsupported observations without
silently substituting the caller's directory or another unrelated pathname.

## Batch 61 process memory maps

`SystemProcMemoryMapProvider` owns reuse-protected process address-space observations for `pmap` and dispatches to the dedicated `LinuxProcMemoryMapProvider` where procfs exists. The Linux provider parses `/proc/PID/maps` for basic/device reports and `/proc/PID/smaps` for `-x`, `-X`, and `-XX`, preserving numeric kernel detail fields and `VmFlags` without hard-coding a permanently fixed `smaps` schema. It checks process identity before and after each read so PID reuse cannot silently attach maps to the wrong process. Because the Linux provider is separately injectable, procfs fixtures and reuse races can be exercised on every CI host.

Windows and macOS deliberately report this Linux-equivalent map capability as
unsupported in Batch 61. Loaded-module lists, working-set summaries, and other
partial native observations are not substituted for a complete address-space
map. A future native provider may expose those systems only after a neutral map
contract can preserve their real protection, backing, region, and naming
semantics without fabricating Linux `/proc` fields.
