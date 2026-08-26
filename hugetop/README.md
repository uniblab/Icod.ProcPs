# HUGETOP(1)

## NAME

`hugetop` — display Linux huge-page pool and process usage

## SYNOPSIS

```text
hugetop [options]
```

## DESCRIPTION

`hugetop` displays configured Linux hugetlb pools together with processes that
currently map shared or private hugetlb memory. It follows the procps-ng 4.0.6
command profile migrated from `Icod.CoreUtils` while using the current
`Icod.ProcPs` observation and terminal libraries.

Linux sysfs is authoritative for per-NUMA-node huge-page pool sizes and
free/total page counts. Per-process shared and private usage is read from the
`Shared_Hugetlb` and `Private_Hugetlb` fields in detailed `/proc/PID/smaps`
observations. Processes without nonzero hugetlb usage are omitted.

Interactive mode uses `Icod.DCurses` over `Icod.Terminal`/`Icod.TermInfo`.
DCurses owns terminal modes, the alternate screen, cursor state, resize/resume
lifecycle events, and restoration. `hugetop` owns only refresh policy and report
presentation. `--once` does not open a terminal and is suitable for pipelines or
redirected output.

## OPTIONS

`-d SECONDS`, `--delay SECONDS`
: Set the interactive refresh interval. The value must be a positive integer no
  greater than `uint.MaxValue`. The default is three seconds.

`-n`, `--numa`
: Preserve one huge-page-pool summary row per observed NUMA node. Without this
  option, pools with the same page size are aggregated across nodes.

`-o`, `--once`
: Capture and print one report, then exit without opening an interactive
  terminal.

`-H`, `--human`
: Render process shared/private huge-page usage with binary human-readable
  suffixes. Huge-page pool sizes are always rendered with binary units.

`-h`, `--help`
: Display command help and exit.

`-V`, `--version`
: Display procps-ng compatibility version information and exit.

## OUTPUT

The pool summary uses entries of the form:

```text
2.0Mi - FREE/TOTAL
```

The process table contains `PID`, `SHARED`, `PRIVATE`, and `COMMAND`. Rows are
ordered by descending combined shared/private huge-page usage and then by PID.
The command reports only source facts available from the kernel; it does not
estimate hugetlb usage from ordinary RSS or virtual-memory counters.

## PLATFORM MODEL

Operational huge-page observation is Linux-specific. `SystemProcHugePageProvider`
uses Linux sysfs and procfs on Linux. Other platforms return an explicit
`Unsupported` observation rather than synthesizing Linux huge-page semantics.

The Linux provider uses the existing reuse-protected ProcPs process and detailed
memory-map providers. A process whose `smaps` data cannot be read is skipped for
per-process attribution without invalidating the independently observed system
pool totals.

## TERMINAL AND REFRESH MODEL

Interactive mode refreshes on monotonic three-second deadlines by default.
Sampling and rendering time count against the interval, preventing systematic
drift. Resize events rerender the current snapshot at the new geometry without
resampling. Resume events request a physical repaint without resampling.
Interrupt or termination lifecycle events return status 130, and disposing the
DCurses session restores terminal state.

Keyboard input has no command meaning in the current procps-ng-compatible
`hugetop` profile and is ignored until the next refresh or lifecycle event.

## TESTING

`tests/ProcPs.HugeTop.Tests` migrates the CoreUtils command tests onto the
DCurses/monotonic-clock seams and also exercises the Linux huge-page provider
with fixture sysfs and detailed `smaps` observations. The tests cover one-shot
aggregation, NUMA and human-readable output, refresh cadence, resize/repaint
behavior without accidental resampling, controlled provider failures,
pre-terminal option handling, and explicit non-Linux unsupported behavior.

## EXIT STATUS

`0`
: Successful one-shot completion, help, or version output.

`1`
: Invalid options, unavailable required huge-page observations, terminal setup
  failure, unusable geometry, or another controlled runtime failure.

`130`
: Interactive monitoring was interrupted or canceled.

## SEE ALSO

`pmap(1)`, `slabtop(1)`, `top(1)`, `vmstat(8)`
