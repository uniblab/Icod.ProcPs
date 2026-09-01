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
-d, --differences[=<permanent>]
                              highlight changes; attached argument makes them permanent
-e, --errexit                freeze on non-zero exit, then return that status
                              after a fresh key press
-f, --follow                 retain, append, and scroll repeated command output
-g, --chgexit                exit when visible output changes
-q, --equexit <cycles>       exit after visible output is unchanged for cycles
-n, --interval <secs>        seconds between updates
-p, --precise                include child running time in the requested cadence
-r, --no-rerun               redraw, rather than rerun, solely because of resize
-s, --shotsdir <dir>         directory used by the interactive screenshot command
-t, --no-title               suppress the two-row header
-w, --no-wrap                truncate long lines instead of wrapping
-x, --exec                   execute directly instead of through a shell
-h, --help                   display help
-v, --version                display version
```

As in procps-ng 4.0.6 itself, the optional argument to `--differences` is tested
for presence rather than content. Consequently `-d1`, `--differences=1`, and
`--differences=permanent` all enable permanent highlighting. The argument must
remain attached to `-d` or `--differences`; a separate following operand begins
the watched command because option processing stops at the first non-option
operand.

`--follow` conflicts with difference and comparison-driven exit modes.
In follow mode the body is not cleared between executions. A new child begins
writing at the retained cursor position left by the preceding child; newlines
and wrapped text scroll the body when they pass its final row. The retained
state is bounded to the visible body rather than an ever-growing transcript.
Resize preserves the newest representable cells, and `--no-rerun` can repaint
that resized follow state without launching a child solely because geometry
changed.

## KEY CONTROL

Interactive key handling follows the procps-ng 4.0.6 profile:

- Space requests the next command execution immediately. If input arrived while
  the child was running, it is consumed after that child completes and the next
  execution begins without the remaining interval delay.
- `q` exits successfully after the current child has completed.
- `s` writes a plain UTF-8 screenshot of the current visible frame. By default
  screenshots are written to the current working directory; `--shotsdir`
  selects another existing directory.

With `--errexit`, a non-zero child result freezes the completed display and
writes `command exit with a non-zero status, press a key to exit` on the bottom
row. No further child is launched. Input already pending when the child
completed is drained before the prompt is presented, so acknowledgement
requires fresh terminal input. Resize and resume events repaint the frozen
terminal state, while subsequent input exits with the child's portable status.
An interrupt still returns status 130.

Screenshot filenames use `watch_YYYYMMDD-HHMMSS`, with a `-NNN` collision
suffix when needed, and existing files are never overwritten. Repeated `s`
input during one wait interval produces only one screenshot, matching the
idempotent procps-ng key loop.

## TERMINAL AND DISPLAY MODEL

`watch` requires interactive terminal input and output. Redirected standard
output is therefore rejected by the DCurses session contract.

The title follows the procps-ng 4.0.6 two-row model. Its first row begins with
`Every N.Ns:`, places the host name and locale-formatted date/time against the
right edge, and clips the command with an ellipsis when needed. The second row
right-aligns the previous command duration and portable status as
`in N.NNNs (status)`; sub-millisecond runs use `in <0.001s`, and runs longer
than one day use `in >1 day`.

Child text is converted to a fixed visible cell image using the DCurses Unicode
width policy. BEL (`\a`) requests an audible terminal alert and is not rendered;
this is independent of `--beep`, which alerts for a non-zero child status.
Tabs advance to eight-column stops. Carriage return is ignored as non-printing
child output, matching procps-ng 4.0.6 rather than rewinding the current row.
C0/C1 controls other than ESC, BEL, tab, and LF are discarded. With
`--color`, ESC processing follows the procps-ng 4.0.6 numeric SGR consumer:
standard and bright colors, indexed
`38;5;n` / `48;5;n` colors through 255, bold, dim, underline, reverse, and their
supported reset forms are translated to semantic DCurses styles. The upstream
24-bit `38;2;r;g;b` / `48;2;r;g;b` form is explicitly not implemented, and an
unsupported SGR attribute stops interpretation of the remaining attributes in
that sequence. DCurses 0.1 has no italic or blink semantic attributes, so those
recognized SGR requests are consumed without adding a rendition flag.

Without color interpretation, the ESC byte itself is discarded while following
bytes remain ordinary visible output; for example `ESC[31mred` displays as
`[31mred`. This deliberately matches procps rather than stripping an entire CSI
sequence. Comparison options therefore continue to operate on the text that is
actually visible and ignore style-only changes and off-screen output.

As in procps-ng, ordinary mode stops consuming display semantics after the
visible body is exhausted, so BEL characters in the discarded tail do not
alert. `--follow` keeps consuming output while scrolling and therefore continues
to honor BEL characters beyond the first screenful. BEL characters skipped by
`--no-wrap` after a line has been truncated are likewise not consumed. The
discarded remainder is not ANSI-interpreted either, and the following line
starts with default rendition state.

Resize events reset comparison baselines so geometry changes cannot trigger a
false `--chgexit`. `--no-rerun` redraws the most recent captured output at the
new geometry without launching another child merely because the terminal was
resized. In `--follow` mode the redraw uses the retained scrolling body instead
of reconstructing only the most recent child output. Screenshots serialize this
same visible model rather than scraping the
physical terminal, so terminal styles are omitted while visible Unicode text,
header rows, body rows, and geometry are preserved.

The resolved display geometry is also supplied to every watched child through
`COLUMNS` and `LINES`, matching procps-ng. Explicit positive values inherited
through those variables therefore remain fixed geometry overrides; otherwise
the values follow live terminal dimensions after resize.

## EXIT STATUS

`0`
: Requested normal completion, including `--chgexit` or `--equexit`.

`1`
: Invalid command-line usage, non-interactive invocation, or a general terminal
  session failure.

`2`
: Terminal geometry or child-execution management failure before a portable
  watched-command status is available.

`130`
: Interactive interruption or cancellation.

`127`
: Portable watched-command status used when the execution path cannot be
  launched. With `--errexit`, this is returned after fresh acknowledgement.

With `--errexit`, the watched command's portable exit status is returned after
the frozen error display is acknowledged by fresh terminal input.

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
