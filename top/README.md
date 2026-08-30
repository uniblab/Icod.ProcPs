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
: Use the built-in defaults. This implementation does not yet read personal
  `toprc` configuration, so this option must be the only command-line option and
  selects the normal built-in state.

`-b`, `--batch`
: Run without interactive terminal input. Output is written to standard output.

`-c`, `--cmdline-toggle`
: Start with the COMMAND column showing the observed command line instead of the
  short command name.

`-d SECONDS`, `--delay SECONDS`
: Set the refresh interval. Fractional and zero-second delays are accepted;
  negative, non-finite, and overflowing values are rejected.

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
: Suppress tasks with no measured CPU activity in the most recent interval,
  except tasks currently reported runnable.

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
: Disable the interactive delay-changing, signal, and renice commands.

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
: Toggle the aggregate CPU presentation label. The current shared metrics model
  exposes aggregate activity but not one activity counter set per logical CPU,
  so separate per-CPU rows are deliberately not synthesized.

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
- `B` globally enables or disables use of bold rendition in the summary and
  task areas.
- `b` selects bold versus reverse-video emphasis for highlighted task rows
  and sort fields.
- `J` toggles numeric columns between right justification (the default) and
  left justification.
- `j` toggles character columns between left justification (the default) and
  right justification. The trailing COMMAND value remains unpadded in the
  default mode and gains only the minimum header width when right-justified.
- `x` toggles highlighting of the active sort field. The highlight is clipped
  to the visible portion of that field when the task display is horizontally
  scrolled.
- `y` toggles highlighting of tasks whose observed state is running. The
  built-in monochrome profile starts with running-row highlighting enabled and
  uses bold emphasis until `b` changes it.
- `c` toggles short command names and observed command lines.
- `H` toggles process/thread presentation and resamples immediately.
- `i` toggles idle-task suppression and `V` toggles forest ordering.
- `I` toggles Irix CPU normalization, where 100% represents one processor, and
  total-capacity normalization.
- `E` and `e` cycle summary and task memory scales.
- `d` or `s` opens an in-screen line editor for the refresh delay.
- `u` and `U` open an in-screen user-filter editor; an empty value clears the
  filter and an initial `!` inverts it.
- `k` prompts for PID and signal and performs reuse-protected delivery through
  `Icod.Processes`.
- `r` prompts for PID and nice value and performs reuse-protected priority
  changes through `Icod.Processes`.
- Arrow keys, Page Up, Page Down, Home, and End scroll the task display; Left and
  Right scroll horizontally.
- `=` clears idle/max-task display limits, PID/user restrictions, and scrolling.
- `h` or `?` displays a compact help screen.

In secure mode the `d`/`s`, `k`, and `r` commands are disabled.

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

This tranche establishes the production monitor engine and terminal lifecycle.
The following large procps `top` subsystems are intentionally left for later
tranches rather than represented incompletely:

- personal/system `toprc` persistence beyond the built-in-default behavior;
- alternate-display multi-window/field-group mode;
- the full interactive field-management screen;
- configurable color schemes and color-management screens;
- per-logical-CPU activity rows until the Shared metrics contract exposes them;
- cumulative dead-child CPU time (`-S`); and
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
