# TOP(1)

## NAME

`top` — display dynamic process and system activity

## SYNOPSIS

```text
top [options]
```

## DESCRIPTION

`top` provides a continuously refreshed ProcPs view of process activity. The
implementation targets the procps-ng 4.0.6 command model while using the Icod
runtime libraries for process observation, timing, host facts, process control,
and terminal presentation.

Interactive mode opens an `Icod.DCurses 0.1.0` session and owns only
application policy. The stable DCurses defaults provide cbreak/no-echo input,
alternate-screen ownership, keypad mode, and a hidden cursor. Decoded input,
resize/resume synchronization, physical-screen invalidation, and terminal
restoration remain owned by `Icod.DCurses` and `Icod.Terminal`.

Batch mode does not open a terminal and is suitable for redirection or pipelines.

## OPTIONS

`-A`, `--apply-defaults`
: Use the built-in defaults and ignore personal Icod top configuration while
  still honoring Linux `/etc/toprc` system restrictions. As with procps `top`,
  this option must be the only command-line option.

`-b`, `--batch`
: Run without interactive terminal input. Output is written to standard output.

`-c`, `--cmdline-toggle`
: Reverse the remembered COMMAND presentation state after personal configuration
  has been loaded.

`-d SECONDS`, `--delay SECONDS`
: Set the refresh interval. Fractional and zero-second delays are accepted;
  negative, non-finite, and overflowing values are rejected. This option is
  unavailable when secure mode is forced.

`-E SCALE`, `--scale-summary-mem SCALE`
: Select the summary memory scale. `k`, `m`, `g`, `t`, `p`, and `e` select KiB,
  MiB, GiB, TiB, PiB, and EiB respectively.

`-e SCALE`, `--scale-task-mem SCALE`
: Select the VIRT and RES task-column scale using the same scale letters.

`-H`, `--threads-show`
: Show Linux lightweight tasks where the shared ProcPs provider can enumerate
  `/proc/PID/task`. Hosts without defensible lightweight-task enumeration report
  the limitation rather than synthesizing threads.

`-i`, `--idle-toggle`
: Reverse the remembered idle-task suppression state after personal
  configuration has been loaded.

`-n NUMBER`, `--iterations NUMBER`
: Exit after the requested number of refreshes.

`-O`, `--list-fields`
: List the fields implemented by this `top` presentation engine and exit.

`-o FIELD`, `--sort-override FIELD`
: Select the initial sort field. Implemented names are `CPU`, `%CPU`, `MEM`,
  `%MEM`, `PID`, `TIME+`, `VIRT`, `RES`, `USER`, `COMMAND`, `NI`, and `S`.
  Prefix the field with `+` to force high-to-low ordering or `-` to force
  low-to-high ordering; without a prefix the active/default direction is kept.

`-p PIDLIST`, `--pid PIDLIST`
: Restrict the display to a comma- or whitespace-separated set of process IDs.
  Multiple occurrences are combined. At most twenty unique process IDs are
  accepted. PID zero selects the current `top`/`procps` process.

`-s`, `--secure-mode`
: Force secure mode, including for a privileged Linux user. Secure mode disables
  command-line and interactive delay changes plus interactive signal and renice
  commands.

`-S`, `--accum-time-toggle`
: Not currently available. The shared process snapshot does not expose the
  dead-child CPU counters required to implement cumulative time correctly, so
  `top` reports this limitation instead of fabricating a value.

`-U USER`, `--filter-any-user USER`
: Restrict the display to tasks whose observed real or effective user matches
  USER. Prefix USER with `!` to invert the match. The current neutral process
  model does not expose saved-set or filesystem user IDs, so those Linux-only
  identities are not claimed as observed.

`-u USER`, `--filter-only-euser USER`
: Restrict the display to the effective user. Prefix USER with `!` to invert the
  match.

`-w [COLUMNS]`, `--width [COLUMNS]`
: Select batch output width. When no width is supplied, 512 columns are used.
  Without `-w`, a positive `COLUMNS` environment value is honored; otherwise
  the batch default is 512.

`-1`, `--single-cpu-toggle`
: Reverse the remembered aggregate CPU presentation state. The current shared
  metrics model exposes aggregate activity but not one activity counter set per
  logical CPU, so separate per-CPU rows are deliberately not synthesized.

`-V`, `--version`
: Display procps-ng compatibility version information and exit.

`-h`, `--help`
: Display command help and exit.

## INTERACTIVE COMMANDS

The following commands are implemented in the first DCurses-backed interactive
profile:

- `q` quits; Enter or Space requests an immediate refresh.
- `0` toggles suppression of true zero values in the suppressible task
  fields. `VIRT`, `RES`, `%CPU`, `%MEM`, and `TIME+` participate; `PR`,
  `NI`, identity/state fields, unavailable values, and nonzero values that
  merely round to a displayed zero remain visible.
- `n` or `#` prompts for the maximum number of task rows to display. `0`
  restores the unlimited setting; the terminal still caps the result to the
  task rows physically available on screen. This is distinct from the
  command-line `-n` / `--iterations` option.
- `P`, `M`, `N`, and `T` sort by CPU, memory, PID, and cumulative CPU time.
- `R` toggles the active sort direction between high-to-low and low-to-high.
- `<` and `>` move the visible sort field left or right across the neighboring
  visible field without changing which field controls sorting. Hidden fields
  remain hidden and keep their relative placement.
- `B` globally enables or disables use of bold rendition in the summary and
  task areas.
- `b` selects bold versus reverse-video emphasis for highlighted task rows
  and sort fields.
- `z` toggles color versus monochrome presentation for the current window.
- `Z` opens Color Mapping. `S`, `M`, `H`, `T`, and `X` select summary,
  message/prompt, header, task-row, and highlighted-task colors. `0` through
  `7` select portable indexed colors, `@` selects the terminal default, and
  Up/Down cycle the full native procps `-1..255` range. `a` / `w` switch
  windows, while `B`, `b`, and `z` remain available in the manager. Enter
  keeps the edits; `q` or Escape restores the state that existed on entry.
- `l` toggles the load-average/uptime summary line for the current window.
- `t` cycles the task/CPU summary through detailed percentages, a compact bar
  graph, a compact block graph, and hidden. `m` applies the same four-way cycle
  to the memory/swap summary. When a native configuration hides a summary while
  retaining a non-detailed graph selector, the first `t` or `m` restores that
  retained selector instead of discarding it.
- `J` toggles numeric columns between right justification (the default) and
  left justification.
- `j` toggles character columns between left justification (the default) and
  right justification. The trailing COMMAND value remains unpadded in the
  default mode and gains only the minimum header width when right-justified.
- `f` opens Fields Management for the current window. `*` marks displayed
  fields, `S` marks the active sort field, and `>` marks the current selection.
  Arrow keys, Page Up, Page Down, Home, and End navigate. `d` or Space toggles
  field visibility, `s` designates the sort field, Right begins repositioning,
  and Left or Enter commits the new position. `a` and `w` cycle the targeted
  field group while the manager remains open. `q` or Escape returns to the
  process display. Visibility and order changes reset horizontal scrolling but
  preserve the task display's vertical position.
- `x` toggles highlighting of the active sort field. The highlight is clipped
  to the visible portion of that field when the task display is horizontally
  scrolled. A hidden sort field has no task-column highlight.
- `y` toggles highlighting of tasks whose observed state is running. The
  built-in monochrome profile starts with running-row highlighting enabled and
  uses bold emphasis until `b` changes it.
- `A` toggles between full-screen mode and a four-window alternate display.
  The initial task displays are `1:Def`, `2:Job`, `3:Mem`, and `4:Usr`; later
  mode switches preserve the visibility choices made with `-` and `_`.
- `a` and `w` make the next or previous window current in circular order.
  `g` prompts for an exact window number from 1 through 4. Commands and Fields
  Management operate on the current window.
- `G` renames the current field group. Names occupy one through three UTF-8
  bytes, matching the native procps window-name storage contract.
- `-` toggles the current task display in alternate-display mode. `_` inverts
  the visibility of all four task displays. `=` forces the current task
  display visible while clearing its display limits, and `+` clears those
  limits for every window and makes all four task displays visible.
- In alternate-display mode, an explicit `n` maximum controls each non-final
  visible task window. Windows without a maximum share the remaining rows,
  while the final visible window consumes the rows left after earlier panes.
- `c` toggles short command names and observed command lines for the current
  window.
- `H` toggles process/thread presentation globally and resamples immediately.
- `i` toggles current-window idle-task suppression and `V` toggles its forest
  ordering.
- While forest mode is active, `F` toggles parent focus on the topmost displayed
  task. Focus keeps that task at the top and suppresses processes outside its
  descendant subtree. `v` collapses or expands the children of the topmost
  displayed task; a collapsed parent's `%CPU` includes its suppressed
  descendants, matching procps behavior. Both commands are no-ops outside
  forest mode.
- `P`, `M`, `N`, `T`, `R`, Fields Management `s`, and command-line `-o` leave
  forest mode when they change sorting. Forest focus and collapse selections are
  transient per-window display limits rather than persisted configuration.
- `I` toggles Irix CPU normalization, where 100% represents one processor, and
  total-capacity normalization.
- `E` and `e` cycle summary and task memory scales.
- `X` changes the extra width applied to procps fixed-width fields. `0` restores
  defaults, a positive value adds that many columns, and `-1` enables automatic
  widening that grows as needed but does not shrink on later refreshes. Of the
  fields currently exposed by Icod top, USER is the procps `X`-eligible field;
  truncated values end with `+`.
- `Y` opens procps Inspect. The PID prompt defaults to the first task currently
  displayed after sorting, filtering, forest projection, and scrolling. The
  chooser pauses normal refreshes while a configured `file` or `pipe` entry is
  selected and viewed. Inspect output supports arrows, Page Up/Down, Home/End,
  `/` or `L` search, `n` or `&` find-next, and `=` source/status display.
- `d` or `s` opens an in-screen line editor for the refresh delay.
- `u` and `U` open an in-screen user-filter editor; an empty value clears the
  filter and an initial `!` inverts it.
- `O` and `o` add Other Filter criteria using case-sensitive and
  case-insensitive matching, respectively. Criteria use the exact displayed
  field name, one of `=`, `<`, or `>`, and a nonempty selection value; a
  leading `!` makes the criterion an exclusion. Equality is a substring
  match. Relational operators compare formatted field text as strings, and
  their selection value is padded to the field width/alignment when the
  filter is established, matching procps behavior. Multiple criteria combine,
  duplicate raw criteria are rejected, and criteria for hidden fields remain
  dormant until those fields are displayed again.
- `L` prompts for a case-sensitive string and moves the matching task row to
  the top of the task area. Searching considers the horizontally visible
  portion of each fully formatted task row, so command mode, forest layout,
  justification, zero suppression, filters, and horizontal scrolling all
  influence matches. An empty search disables locate-next.
- `&` repeats the active locate operation starting after the current top task;
  searches do not wrap and horizontal scrolling is never changed.
- `k` prompts for PID and signal and performs reuse-protected delivery through
  `Icod.Processes`.
- `r` prompts for PID and nice value and performs reuse-protected priority
  changes through `Icod.Processes`.
- `W` atomically writes the supported persistent four-window state to the
  personal Icod top configuration file without resampling processes.
- Arrow keys, Page Up, Page Down, Home, and End scroll the task display; Left and
  Right scroll horizontally. `C` toggles procps-style scroll coordinates in the
  otherwise-unused message line; prompts and explicit messages take precedence.
- `=` clears current-window idle/max-task limits, PID/user/Other Filter
  restrictions, forest focus/collapse, locate state, and scrolling while
  forcing that task display visible. `+` applies the window-local reset to all
  four field groups.
- `h` or `?` displays a compact help screen.

In secure mode the `d`/`s`, `k`, and `r` commands are disabled.

## CONFIGURATION

On Linux, `/etc/toprc` is honored as the native procps system restrictions file.
If the file contains a first line, ordinary users enter secure mode. A valid
nonnegative numeric value at the start of line two supplies the enforced refresh
delay; otherwise the built-in delay is retained. Linux UID 0 is exempt from this
system restriction, while `-s` forces secure mode even for a privileged user.
`-A` skips personal and system-default configuration but does not bypass
`/etc/toprc`.

Personal configuration is loaded before command-line overrides. Icod first
looks for its lossless JSON configuration at
`$XDG_CONFIG_HOME/procps/icod-toprc.json`, or
`$HOME/.config/procps/icod-toprc.json` when no absolute XDG configuration
directory is available. Windows-style environments may use an absolute
`APPDATA` fallback. `$HOME/.icod-toprc.json` remains an Icod legacy fallback.

On Linux, when no Icod JSON configuration exists, `top` next reads the native
procps configuration in the same order used by procps itself: legacy
`$HOME/.toprc` first, then `$XDG_CONFIG_HOME/procps/toprc` or
`$HOME/.config/procps/toprc`. If no personal native file exists,
`/etc/topdefaultrc` supplies system-wide defaults.

Native reading now supports procps 3.2.8 format `a`, procps-ng 3.3.x formats
`f` through `j`, and the transformed integer 4.x formats `k` through current
format `n`. Reserved historical formats `b` through `e` remain rejected.
Character-encoded files are decoded byte-for-byte as Latin-1 so their high-bit
field-visibility markers survive. The compatibility transform mirrors procps:
3.2.8 field, window-flag, and sort-index remapping plus the historical 3.3.x
field-table expansions. Supported window names, field order/visibility, sort
state, task limits, presentation flags, memory scales, alternate/Irix modes,
delay, and Other Filters are translated into the Icod four-window model.

`W` always writes the lossless `icod-toprc.json`. On Linux it also maintains a
current-format procps `toprc` mirror when doing so is ownership-safe. If a
legacy `$HOME/.toprc` exists, Icod updates it only when its first line carries
the Icod ownership marker; a foreign legacy file is preserved and no XDG file
is created to shadow it. Otherwise the XDG/HOME `.config` `procps/toprc` is
created when absent or refreshed only when it is Icod-owned. Native procps
ignores the first eyecatcher line, so it can consume an Icod mirror directly.
If procps later rewrites that mirror, the Icod marker disappears and subsequent
`W` commands leave the native file alone.

The native mirror contains the state shared by both implementations, including
per-window colors plus the native `graph_cpus` / `graph_mems` selectors and
their summary-visibility flags. Global `Fixed_widest` also round-trips and is
controlled by `X`. Procps Inspect `file` and `pipe` entries are imported,
preserved in the Icod JSON, and written back to Icod-owned native mirrors after
the saved Other Filter block. The native task-memory scale has no EiB encoding,
so an Icod EiB task scale is mirrored as PiB. The JSON file remains
authoritative and lossless.

The Icod JSON persists delay, alternate-display/current-window selection,
global bold enable, memory scales, fixed-width expansion, thread/Irix/zero-
suppression state, and each window's name, task-display visibility, sort
field/direction, emphasis and
justification toggles, maximum tasks, command/idle/forest/CPU-summary state,
load/uptime and scroll-coordinate visibility, color state, CPU/memory summary
visibility and graph selectors, field
order/visibility, active Other Filters, and global Inspect entries. The
alpha.13/14 single-window JSON remains readable and initializes `1:Def` when no window array is present;
the first alpha.15 four-window JSON remains readable because omitted names and
visibility values retain the canonical names and visible defaults. PID
monitoring, user filtering, locate text, scrolling, prompts, field-manager
cursor state, and secure mode remain transient.

## CPU ACCOUNTING

`top` keeps cumulative process counters from one refresh to the next and computes
CPU utilization from interval deltas. Stable `Icod.Processes.ProcessIdentity`
values prevent a reused PID from inheriting the previous process's CPU history.

Linux process and system counters are compared in their native procfs units.
Portable .NET process counters use their documented `TimeSpan` tick units, and
Darwin `proc_taskinfo` counters are normalized from nanoseconds. In Irix mode,
100% represents one processor; with Irix mode off, process CPU percentages are
normalized against the process-available processor count observed through
`Icod.Host`.

The first refresh has no prior process sample and therefore reports zero
interval CPU for individual tasks while aggregate CPU summary percentages are
based on the available cumulative system counters.

## MEMORY ACCOUNTING

The summary reports total, free, used, buffer/cache, swap, and available memory
from `Icod.ProcPs.Shared` memory observations. VIRT and RES come directly from
process observations.

The current neutral process model does not expose a defensible per-process
shared-resident (`SHR`) value. The SHR column is therefore rendered as `-`
instead of being estimated from unrelated counters.

## PLATFORM MODEL

Linux `/proc` remains authoritative for procps-ng process semantics. Windows and
macOS use the existing native/managed ProcPs providers where equivalent facts
are available. Missing fields remain explicitly unavailable.

Thread expansion currently requires the Linux lightweight-task provider.
Ordinary process mode, batch mode, sorting, memory summaries, CPU interval
accounting, and supported process controls use the strongest provider available
on the current host.

## CURRENT SCOPE

The production monitor now covers the core summary, task, scrolling, field,
color, four-window, filtering, search, process-control, and configuration
interactions supported by the current observation model. Per-window native
`View_LOADAV` and `View_SCROLL` state round-trips through Icod/native
configuration and is controlled by `l` / `C`; `<` / `>` provide direct sort
field movement in addition to Fields Management.

Forest parent focus/collapse (`F` / `v`) now operate on the same reuse-aware
hierarchy used by ordinary forest rendering. `X` / `Fixed_widest` now controls
the supported fixed-width USER column with explicit or monotonic automatic
widening. `Y` provides persisted procps Inspect file/pipe entries with a paused,
scrollable and searchable viewer. The remaining implementable completion queue
is the applicable procps 4.0.6 bottom-window/message-log behavior.

The following areas remain blocked by facts that are not yet available through
the shared observation contracts:

- separate per-logical-CPU, NUMA, combined-CPU, adjacent-CPU, and P/E-core
  summary views (`1` / `2` / `3` / `4` / `5` / `!`) beyond the current
  aggregate-state toggle;
- cumulative dead-child CPU time (`-S` / interactive `S`); and
- fields whose source facts are not yet present in the neutral process model,
  including exact SHR accounting.

## TESTING

`tests/ProcPs.Top.Tests` exercises the terminal-independent sampler and renderer,
batch scheduling, command-line validation and filtering, and the interactive
DCurses seam with deterministic fake clocks, providers, terminal events, and
process controls. The tests verify resize and repaint behavior without accidental
resampling, fixed-rate refresh deadlines, secure-mode restrictions, reuse-aware
process signalling, terminal disposal, and the pre-terminal help/version/field
listing paths.

## EXIT STATUS

`0`
: Successful completion, including help, version, field listing, iteration
  completion, or an interactive `q` exit.

`1`
: Invalid options, unavailable required observations, unsupported requested
  semantics, terminal setup failure, or another controlled runtime failure.

`130`
: The caller or terminal lifecycle canceled/interrupted the live monitor.

## SEE ALSO

`ps(1)`, `pgrep(1)`, `pkill(1)`, `vmstat(8)`, `free(1)`, `slabtop(1)`,
`watch(1)`
