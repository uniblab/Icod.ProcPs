# Icod.ProcPs

`Icod.ProcPs` is a managed .NET implementation of a selected set of tools from
procps-ng 4.0.6.

The repository provides familiar process- and system-observation commands such
as `ps`, `pgrep`, `pkill`, `free`, `uptime`, `vmstat`, `w`, `watch`, `slabtop`,
and `sysctl`, while factoring common ProcPs behavior into the reusable
`Icod.ProcPs.Shared` library.

The implementation targets .NET 10 and C# 13 and is designed for Windows,
Linux, and macOS. Linux `/proc` remains the authoritative source for Linux
procps-ng semantics; Windows and macOS use native operating-system facilities
where equivalent information can be represented faithfully. Unsupported data is
reported as unavailable rather than synthesized from unrelated metrics.

## Included commands

| Command | Purpose |
|---|---|
| [`free`](free/README.md) | Display physical-memory and swap usage. |
| [`pgrep`](pgrep/README.md) | Find processes by name, regular expression, identity, ownership, session, terminal, and other attributes. |
| [`pkill`](pkill/README.md) | Signal processes selected with the same matching grammar used by `pgrep`. |
| [`pidwait`](pidwait/README.md) | Wait for processes selected by the shared ProcPs matching engine. |
| [`pidof`](pidof/README.md) | Find process identifiers for running programs. |
| [`pwdx`](pwdx/README.md) | Report the current working directory of one or more processes. |
| [`pmap`](pmap/README.md) | Report process memory maps and Linux `smaps` detail. |
| [`ps`](ps/README.md) | Report a snapshot of current processes, including ProcPs-style selection, formatting, sorting, personalities, and thread views. |
| [`slabtop`](slabtop/README.md) | Display Linux slab-cache information in real time or as a one-shot report. |
| [`uptime`](uptime/README.md) | Report system uptime, user count, and load averages. |
| [`vmstat`](vmstat/README.md) | Report virtual-memory, CPU, process, paging, disk, and system activity. |
| [`w`](w/README.md) | Show logged-in users and what their sessions are doing. |
| [`watch`](watch/README.md) | Execute a command periodically and display its output fullscreen. |
| [`sysctl`](sysctl/README.md) | Read and write Linux runtime kernel parameters through `/proc/sys`. |

Each executable directory contains its own man-page-style `README.md` describing
the implemented command-line profile, exit statuses, platform behavior, and
known limitations.

## Icod.ProcPs.Shared

[`Icod.ProcPs.Shared`](Icod.ProcPs.Shared/README.md) is the suite-specific class
library used by the commands in this repository.

It provides the common ProcPs model and behavior for:

- process enumeration and stable process identity;
- Linux `/proc` parsing;
- Windows and macOS native process providers;
- process selection and reporting;
- the shared `pgrep` / `pkill` / `pidwait` matching grammar;
- executable, root, and current-working-directory observations;
- process memory maps;
- CPU, memory, swap, load-average, uptime, and session observations;
- `vmstat` counters and sampling calculations;
- account, terminal, namespace, cgroup, container, and security observations
  where the host can expose them faithfully; and
- provenance and observation-fidelity metadata so callers can distinguish
  exact, equivalent, approximated, unavailable, and unsupported data.

Cross-suite process-control abstractions are supplied by `Icod.Processes`,
while monotonic elapsed-time and periodic scheduling primitives are supplied by
`Icod.Timing`. `Icod.ProcPs.Shared` owns its observation-fidelity policy and
consumes `Icod.CommandFramework.RegularExpressions` only for the managed
GNU/POSIX regular-expression engine.

Interactive full-screen commands use `Icod.DCurses` over `Icod.Terminal` and
`Icod.TermInfo`; ProcPs command code owns application policy rather than native
terminal modes, escape tables, or screen-refresh mechanics.

## Platform model

The project is cross-platform, but it does not pretend that every operating
system exposes Linux `/proc` semantics.

### Linux

Linux is the reference platform for procps-ng compatibility. The implementation
uses procfs and related kernel interfaces for process detail, memory maps,
memory and swap information, load averages, CPU activity, uptime, login
sessions, namespaces, cgroups, signal state, disk statistics, paging counters,
slab information, and other Linux-specific observations.

### Windows

Windows support uses native and .NET process APIs, Tool Help, Terminal Services,
`GetPerformanceInfo`, page-file enumeration, `GetSystemTimes`,
`GetTickCount64`, and other documented Windows facilities.

Linux-only concepts are not fabricated. For example, Windows does not receive a
synthetic Unix load average, and process memory maps remain unsupported until a
provider can expose a sufficiently complete neutral address-space model.

### macOS

macOS support uses Darwin `libproc`, POSIX APIs, Mach VM/CPU statistics,
`getloadavg()`, `kern.boottime`, `utmpx`, and related native facilities.

As on Windows, Linux-only fields and report modes remain explicitly unavailable
when there is no defensible native equivalent.

## Compatibility philosophy

The goal is useful procps-ng compatibility without sacrificing correctness on
non-Linux hosts.

The project therefore follows several rules:

1. Preserve procps-ng command syntax and output behavior where the required
   underlying information exists.
2. Keep process identity reuse-aware so a recycled PID is not silently treated
   as the original process.
3. Preserve provenance and fidelity for observed values.
4. Prefer an explicit unsupported/unavailable result to a plausible-looking but
   semantically incorrect value.
5. Keep neutral process-control mechanics in `Icod.Processes`, neutral timing
   mechanics in `Icod.Timing`, managed GNU/POSIX regular expressions in
   `Icod.CommandFramework.RegularExpressions`, and ProcPs-specific observation
   policy in `Icod.ProcPs.Shared`.

This means that some commands or modes are naturally more portable than others.
For example, `ps`, `pgrep`, `pkill`, `free`, and uptime-related observations can
use substantial native support on multiple operating systems, while full
`pmap` semantics and operational `sysctl` behavior are inherently tied to Linux
interfaces.

## Building

The repository requires a .NET 10 SDK.

On Windows:

```text
build.cmd
```

On Unix-like hosts:

```text
./build.sh
```

Or build the solution directly:

```text
dotnet restore Icod.ProcPs.sln
dotnet build Icod.ProcPs.sln -c Debug --no-restore
dotnet test Icod.ProcPs.sln -c Debug --no-build --no-restore
```

The solution defines `Debug`, `Staging`, and `Release` configurations.

## Continuous integration

Pull requests are restored, built, and tested with .NET 10 on:

- `windows-latest`
- `ubuntu-latest`
- `macos-latest`

Release-oriented builds additionally use the repository's `Release`
configuration, where compiler warnings are treated as errors except for
documentation warning `CS1591`.

## Project layout

```text
Icod.ProcPs/
├── Icod.ProcPs.Shared/    shared ProcPs library
├── free/
├── pgrep/
├── pidof/
├── pidwait/
├── pkill/
├── pmap/
├── ps/
├── pwdx/
├── sysctl/
├── uptime/
├── vmstat/
├── w/
├── tests/                 command and shared-library tests
├── Icod.ProcPs.sln
├── build.cmd
└── build.sh
```

## Documentation

Every executable has a dedicated `README.md` intended to function much like a
manual page. Public, protected, and internal API surfaces in the source are also
documented with XML documentation comments so generated API documentation can
describe the reusable library and command entry points.

For architecture and provider details, see
[`Icod.ProcPs.Shared/README.md`](Icod.ProcPs.Shared/README.md).

## Licensing

The executable tools in this repository use the repository
[`LICENSE`](LICENSE), with a copy included alongside each tool and packaged with
that executable's documentation.

`Icod.ProcPs.Shared` intentionally has its own licensing terms. The shared
library project declares `LGPL-3.0-or-later`; see
[`Icod.ProcPs.Shared/LICENSE`](Icod.ProcPs.Shared/LICENSE) for the complete
license text applicable to that library.

## Upstream inspiration

These programs are inspired by and modeled on the corresponding utilities in
procps-ng 4.0.6. Individual tool READMEs contain more specific historical author
credits for the upstream commands.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## Copyright

Copyright (c) 2026 Timothy J. Bruce
