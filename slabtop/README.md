# SLABTOP(1)

## NAME

`slabtop` — display Linux kernel slab-cache information in real time

## SYNOPSIS

```text
slabtop [options]
```

## DESCRIPTION

`slabtop` displays the Linux slab allocator cache table obtained from
`/proc/slabinfo`. Interactive mode uses `Icod.DCurses` over `Icod.Terminal` and
`Icod.TermInfo`; the command does not own termios, Win32 console modes, escape
sequence tables, alternate-screen state, cursor restoration, or physical-screen
refresh mechanics.

The slab observation contract lives in `Icod.ProcPs.Shared`. Linux uses the
exact `/proc/slabinfo` core fields and `slabdata` active/total slab counts. Other
platforms report the capability as unsupported rather than synthesizing slab
data. `--once` bypasses terminal initialization completely and writes the same
logical report as the interactive display without screen-size clipping.

## OPTIONS

`-d SECONDS`, `--delay SECONDS`
: Refresh every positive integer number of seconds. The default is 3 seconds.
  This option cannot be combined with `--once`.

`-s CHAR`, `--sort CHAR`
: Select the sort criterion. Recognized letters are `a`, `b`, `c`, `l`, `v`,
  `n`, `o`, `p`, `s`, and `u`.

| Letter | Sort key | Direction |
|---|---|---|
| `a` | active objects | descending |
| `b` | objects per slab | descending |
| `c` | total cache size | descending |
| `l` | total slabs | descending |
| `v` | active slabs | descending |
| `n` | cache name | ascending |
| `o` | total objects | descending |
| `p` | pages per slab | descending |
| `s` | object size | descending |
| `u` | object utilization | descending |

`-o`, `--once`
: Print one unbounded report to standard output and exit. This mode does not
  require or open an interactive terminal and cannot be combined with
  `--delay`.

`--human`
: Display aggregate and per-cache slab sizes using human-readable binary units.
  As in procps-ng, `OBJ SIZE` and the minimum/average/maximum object-size
  summary remain fixed two-decimal `K` values. Without this option, slab sizes
  also use the procps-style kibibyte presentation.

`-h`, `--help`
: Display help and exit successfully.

`-V`, `--version`
: Display version information and exit successfully.

## REPORT

Each report begins with aggregate allocator information: active and total
objects, slabs, caches, and cache size, followed by minimum, average, and maximum
object sizes. The cache table then reports:

```text
OBJS ACTIVE USE OBJ SIZE SLABS OBJ/SLAB CACHE SIZE NAME
```

The active/total slab values come from the kernel's `slabdata` fields rather
than being inferred from object counts. Cache-size calculations use the observed
slab count, pages per slab, and host page size with saturating arithmetic for
pathological values.

As documented by procps-ng 4.0.6, the aggregate slab-size statistics describe
the bytes represented by slab-layer accounting; they are not a measurement of
the physical memory currently occupied by slabs. On Linux, the `Slab` field in
`/proc/meminfo` is the kernel interface for used slab physical memory.

The `CACHE SIZE` column likewise represents an upper bound rather than an exact
physical-memory measurement. With SLUB under memory pressure, slab-order
fallbacks can reduce the pages used by individual slabs, so a single reported
pages-per-slab value can overstate the memory actually consumed.

## INTERACTIVE TERMINAL MODEL

Interactive mode requires a terminal of at least 40 columns by 9 rows. The
stable `Icod.DCurses 0.1.0` defaults provide the alternate-screen, cbreak,
no-echo, keypad, and hidden-cursor profile. DCurses and `Icod.Terminal` own the
terminal lifecycle and restore the live terminal when the session ends.

The cache-table heading is rendered in reverse video. Interactive frames reserve
the final physical row and therefore display at most `Rows - 8` cache entries,
matching the procps ncurses layout and avoiding terminal scrolling on the last
screen row.

The refresh deadline is monotonic and starts when a sampling cycle begins. Time
spent reading `/proc/slabinfo` and rendering therefore counts against the
configured delay; if a cycle consumes the full interval, the next sample begins
immediately rather than adding another full delay.

DCurses applies resize/resume geometry and physical-screen invalidation before
delivering those lifecycle events. `slabtop` redraws the current slab snapshot
without forcing an extra `/proc/slabinfo` read. An interrupt or termination
event returns status 130.

Interactive commands follow the procps-ng `slabtop` profile:

- `a`, `b`, `c`, `l`, `v`, `n`, `o`, `p`, `s`, and `u` change the active sort
  criterion; uppercase and lowercase letters are accepted.
- Space requests an immediate fresh sample and redraw.
- `q` or `Q` exits successfully.

A sort-key command also requests an immediate fresh sample so the newly selected
order is applied to current data. Unsupported input is ignored without changing
the refresh deadline. Terminal end-of-input exits successfully. `Ctrl+C` and
terminal interrupt/termination lifecycle events remain cancellation and return
status 130.

### Intentional procps-ng 4.0.6 deviations

Two source-level procps-ng 4.0.6 quirks are deliberately not reproduced:

- Native `slabtop` resets its sort criterion to the default `o` ordering before
  interpreting every received character. As a side effect, Space and unsupported
  characters reset a previously selected sort even though the manual describes
  Space only as a refresh command. Icod preserves the active sort for Space and
  unsupported input; only a recognized sort key changes it.
- Native `term_resize` silently substitutes an 80-column by 24-row geometry when
  `TIOCGWINSZ` fails or reports ten rows or fewer. Icod instead uses the live
  geometry supplied by DCurses, accepts usable displays down to 40 columns by
  9 rows, and reports a controlled failure when the actual geometry is unusable
  rather than manufacturing a different terminal size.

These are intentional managed behavior choices; the command otherwise targets
the procps-ng 4.0.6 option, report, sort, and interactive-command model.

## DATA SOURCE AND AVAILABILITY

`SystemProcSlabProvider` selects the exact Linux provider when running on Linux.
`LinuxProcSlabProvider` reads `/proc/slabinfo` and reports observations with
`ProcObservationSource.LinuxProcfs` and exact fidelity. The parser rejects
malformed rows, impossible active/total counts, missing `slabdata` counts, and
zero slab geometry instead of manufacturing replacements.

Read permission failures, a missing procfs interface, malformed input, or other
controlled provider failures produce a diagnostic and status 1. On Windows,
macOS, and other hosts without the Linux slab allocator interface, the system
provider reports the capability as unsupported. No non-Linux allocator is
translated into synthetic `/proc/slabinfo` semantics.

## IMPLEMENTATION BOUNDARIES

`Icod.ProcPs.Shared` owns `ProcSlabCacheEntry`, `IProcSlabProvider`, the system
and Linux providers, provenance, and `/proc/slabinfo` parsing. The `slabtop`
executable owns option parsing, sorting, report composition, and interactive
application policy. `Icod.DCurses` owns screen presentation, while `Icod.Timing`
provides the monotonic clock used to preserve refresh cadence.

The test project injects the slab provider, terminal-session seam, and monotonic
clock. This keeps parser, rendering, resize/repaint, cancellation, failure, and
scheduling behavior deterministic without writing test traffic to the real
terminal.
It also verifies the `--human` object-size boundary, reverse-heading frame
metadata, and procps-style reservation of the final interactive screen row.

## PLATFORM NOTES

Linux is the reference and operational platform because procps-ng `slabtop` is
defined in terms of `/proc/slabinfo`. `--once` remains useful with injected or
fixture-backed providers in tests and applications, but the system provider is
intentionally unsupported on hosts that do not expose the Linux slab allocator
interface.

## FILES

`/proc/slabinfo`
: Linux slab-cache information consumed by `LinuxProcSlabProvider`.

`/proc/meminfo`
: The Linux `Slab` field reports used slab physical memory and is the appropriate
  comparison when interpreting the non-physical aggregate size figures shown by
  `slabtop`.

## EXIT STATUS

`0`
: Successful one-shot report or normal command-line help/version completion.

`1`
: Usage, terminal, provider, permission, malformed-data, or other controlled
  execution failure.

`130`
: Interactive operation was interrupted or canceled.

## AUTHORS

The procps-ng 4.0.6 `slabtop` source credits Craig Small, Jim Warner, Sami
Kerola, Albert Cahalan, Chris Rivera, and Robert Love. The manual identifies
Chris Rivera and Robert Love as the original authors and notes that `slabtop`
was inspired by Martin Bligh's `vmtop` Perl script.

This managed port was created by Timothy J. Bruce.

## SEE ALSO

`free(1)`, `ps(1)`, `top(1)`, `vmstat(8)`, `slabinfo(5)`, `watch(1)`
