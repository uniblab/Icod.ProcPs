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
sequence tables, alternate-screen state, or terminal restoration.

The slab observation contract lives in `Icod.ProcPs.Shared`. Linux uses the
exact `/proc/slabinfo` `slabdata` active/total slab counts. Other platforms
report the capability as unsupported rather than synthesizing slab data.

## OPTIONS

`-d SECONDS`, `--delay SECONDS`
: Refresh every positive integer number of seconds. The default is 3 seconds.

`-s CHAR`, `--sort CHAR`
: Select the sort criterion. Accepted letters are `a`, `b`, `c`, `l`, `v`,
  `n`, `o`, `p`, `s`, and `u`.

`-o`, `--once`
: Print one unbounded report to standard output and exit. This mode does not
  require an interactive terminal and cannot be combined with `--delay`.

`--human`
: Display sizes using human-readable binary units.

`-h`, `--help`
: Display help and exit successfully.

`-V`, `--version`
: Display version information and exit successfully.

## INTERACTIVE TERMINAL MODEL

Interactive mode requires a terminal of at least 40 columns by 9 rows. It uses
a DCurses alternate-screen session in cbreak mode with echo disabled and the
cursor hidden. Resize events redraw the current slab snapshot immediately
without forcing an extra `/proc/slabinfo` read. Resume events invalidate and
repaint the physical screen. Interrupt or termination returns status 130 and
DCurses/Terminal owns restoration.

## PLATFORM NOTES

Linux is the reference platform and reads `/proc/slabinfo`. On Windows, macOS,
and other hosts without that Linux allocator interface, `slabtop` reports a
controlled unsupported diagnostic. `--once` remains useful with injected or
fixture-backed providers in tests and applications.

## EXIT STATUS

`0`
: Successful one-shot report or normal command-line help/version completion.

`1`
: Usage, terminal, provider, permission, malformed-data, or other controlled
  execution failure.

`130`
: Interactive operation was interrupted or canceled.

## SEE ALSO

`procps(1)`, `watch(1)`, `slabinfo(5)`
