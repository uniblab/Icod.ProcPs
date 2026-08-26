# TLOAD(1)

## NAME

`tload` — display a scrolling terminal graph of system load averages

## SYNOPSIS

```text
tload [options] [tty]
```

## DESCRIPTION

`tload` displays the one-minute system load average as a continuously scrolling
character graph. The current one-, five-, and fifteen-minute load averages are
printed at the upper left of each frame.

The implementation follows the procps-ng 4.0.6 `tload` command model while using
`Icod.ProcPs.Shared` for load-average observation. Refresh scheduling uses the
shared `ProcSampler` over `Icod.Timing` monotonic timing contracts.

Unlike `top`, `slabtop`, `hugetop`, and `watch`, `tload` does not consume terminal
input and does not need a curses presentation. It writes directly to an output
terminal, uses `Icod.Terminal` to observe live terminal geometry, and uses
`Icod.TermInfo` to select an appropriate cursor-home capability. It does not
change terminal input modes, hide the cursor, or enter the alternate screen.

## OPTIONS

`-d SECONDS`, `--delay SECONDS`
: Set the refresh interval in whole seconds. The value must be at least 1 and no
  greater than `UINT_MAX`. The default is 5 seconds.

`-s NUMBER`, `--scale NUMBER`
: Set the vertical graph scale. A nonnegative finite number is accepted. Zero,
  the default, enables automatic scaling based on the current terminal height.

`-h`, `--help`
: Display command help and exit successfully.

`-V`, `--version`
: Display the Icod.ProcPs command version together with the procps-ng inspiration
  baseline and exit successfully.

## TERMINAL OPERAND

With no `tty` operand, frames are written to standard output and terminal size is
observed from the process standard-output endpoint.

When `tty` is supplied, `tload` opens that path for writing and sends the graph to
that endpoint. The terminal operand is output-only, matching procps-ng; it is not
used as an input device and does not need to be paired with standard input.

If live terminal dimensions cannot be obtained, the command uses the traditional
80-column by 25-row fallback. Consequently, redirected standard output remains
permitted, as in procps-ng, and receives the cursor-home sequence followed by the
complete graph frame on every refresh.

A geometry change detected between refreshes clears the accumulated scrolling
history and begins a new graph at the new size.

## GRAPH

Each refresh contributes one column to the graph. `*` characters represent the
one-minute load average. Horizontal scale marks are rendered with `-`; a mark
that intersects the load graph is rendered as `=`.

With automatic scaling, the graph reduces its scale when the current load would
exceed the available height and gradually increases the scale again when there
is room. An explicit `--scale` value establishes the maximum vertical scale.

The upper-left label has the form:

```text
 0.50, 0.25, 0.10
```

representing the one-, five-, and fifteen-minute load averages.

## PLATFORM MODEL

Linux load averages come from the authoritative procfs-backed
`Icod.ProcPs.Shared` provider. macOS uses its native load-average provider with
equivalent semantics. A host such as Windows that cannot expose a defensible Unix
load average reports a controlled failure rather than synthesizing one.

Terminal geometry is obtained through `Icod.Terminal` when available. The output
path itself remains a simple writable stream, preserving the historical `tload`
ability to target another terminal without taking ownership of terminal input.

## TESTING

`tests/ProcPs.Tload.Tests` covers default graph rendering, delay and scale
options, output-only terminal selection, redirected output, geometry changes,
unavailable and invalid load observations, write failures, cancellation,
command-line validation, and the pre-terminal help/version paths. Timing and
terminal behavior are exercised through deterministic fake ProcPs samplers and
output sessions.

## EXIT STATUS

`0`
: Successful completion of help/version or completion of the supplied sampling
  sequence. The normal system scheduler is continuous.

`1`
: Invalid arguments, unavailable required load averages, invalid terminal
  geometry, an output-open/write failure, or another controlled runtime failure.

`130`
: Sampling or observation was canceled by the caller.

## AUTHORS

The procps-ng 4.0.6 `tload` source credits Craig Small, Jim Warner, Sami Kerola,
Branko Lankester, David Engel, and Michael K. Johnson. This managed port was
created by Timothy J. Bruce.

## SEE ALSO

`uptime(1)`, `w(1)`, `top(1)`, `watch(1)`, `vmstat(8)`
