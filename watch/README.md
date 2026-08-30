# WATCH(1)

## NAME

`watch` — execute a program periodically, showing output fullscreen

## SYNOPSIS

```text
watch [options] command
```

## DESCRIPTION

This managed implementation follows the procps-ng 4.0.6 `watch` command model.
It repeatedly executes a command, captures standard output and standard error,
and displays the visible result through `Icod.DCurses 0.1.0` over
`Icod.Terminal`. The stable DCurses defaults and lifecycle contract own
alternate-screen, cursor, keypad, resize, suspend/resume, physical-screen
invalidation, interrupt, and restoration behavior; `watch` owns only command
policy and the visible screen model.

By default, the command is reconstructed and executed through the host shell.
Use `--exec` to execute the first operand directly while preserving subsequent
argument boundaries.

The default interval is two seconds. `WATCH_INTERVAL` provides the initial
interval when present, and `--interval` overrides it. Intervals are clamped to
0.1 seconds through 31 days. Ordinary mode waits the complete interval after a
child finishes; `--precise` uses monotonic fixed-rate scheduling.

## OPTIONS

```text
-b, --beep                   alert if the command exits non-zero
-c, --color                  interpret ANSI SGR color/style sequences
-C, --no-color               ignore ANSI SGR color/style sequences
-d, --differences[=permanent]
                              highlight visible changes between updates
-e, --errexit                exit if the command exits non-zero
-f, --follow                 follow repeated output without comparisons
-g, --chgexit                exit when visible output changes
-q, --equexit <cycles>       exit after visible output is unchanged for cycles
-n, --interval <secs>        seconds between updates
-p, --precise                include child running time in the requested cadence
-r, --no-rerun               redraw, rather than rerun, solely because of resize
-s, --shotsdir <dir>         accepted for procps compatibility; screenshots deferred
-t, --no-title               suppress the two-row header
-w, --no-wrap                truncate long lines instead of wrapping
-x, --exec                   execute directly instead of through a shell
-h, --help                   display help
-v, --version                display version
```

`--follow` conflicts with difference and comparison-driven exit modes.

## TERMINAL AND DISPLAY MODEL

`watch` requires interactive terminal input and output. Redirected standard
output is therefore rejected by the DCurses session contract.

Child text is converted to a fixed visible cell image using the DCurses Unicode
width policy. Tabs advance to eight-column stops. Carriage return rewinds the
current row. C0/C1 controls other than tab, CR, and LF are discarded. ANSI CSI
sequences are stripped; SGR sequences are translated to semantic DCurses styles
only when `--color` is enabled. Comparison options operate on visible text and
ignore style-only changes and off-screen output.

Resize events reset comparison baselines so geometry changes cannot trigger a
false `--chgexit`. `--no-rerun` redraws the most recent captured output at the
new geometry without launching another child merely because the terminal was
resized.

## EXIT STATUS

`0`
: Requested normal completion, including `--chgexit` or `--equexit`.

`2`
: Invalid command-line usage or terminal/process-management failure.

`130`
: Interactive interruption or cancellation.

With `--errexit`, the watched command's portable exit status is returned.

## PORTABILITY

The implementation targets Windows, Linux, and macOS. Shell mode uses
`cmd.exe /D /S /C` on Windows and `/bin/sh -c` elsewhere. Child execution is
provided by `Icod.Processes`; cadence uses `Icod.Timing`; full-screen rendering
and lifecycle are provided by `Icod.DCurses` and `Icod.Terminal`.

## AUTHORS

The original `watch` was written by Tony Rems in 1991, with modifications and
corrections by Francois Pinard. Mike Coleman substantially reworked it and added
new features in 1999. The modern command is maintained as part of procps-ng.

Migrated to .NET by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

## LICENSE

This executable work is distributed as part of the Icod.ProcPs suite under the
GNU General Public License version 3 or later.
