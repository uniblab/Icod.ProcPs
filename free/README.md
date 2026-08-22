# FREE(1)

## NAME

**free** — display free and used system memory

## SYNOPSIS

```text
free [options]
```

## DESCRIPTION

`Icod.ProcPs.Free` is a managed .NET implementation of procps-ng `free(1)`, modeled on procps-ng 4.0.6.

The command reports physical memory and swap totals together with derived used, free, shared, cache/buffer, and available values. On Linux the shared ProcPs provider can read `/proc/meminfo`; on other supported hosts it uses the native or portable observations made available by `Icod.ProcPs.Shared`.

The default display uses KiB-scale values. The implemented unit switches, human-readable formatting, low/high memory rows, totals, committed-memory display, single-line mode, wide mode, and repeated sampling follow the current command profile.

## OPTIONS

```text
-b, --bytes
    Display values in bytes.

--kilo
    Display values in decimal kilobytes.

--mega
    Display values in decimal megabytes.

--giga
    Display values in decimal gigabytes.

--tera
    Display values in decimal terabytes.

--peta
    Display values in decimal petabytes.

-k, --kibi
    Display values in kibibytes.

-m, --mebi
    Display values in mebibytes.

-g, --gibi
    Display values in gibibytes.

--tebi
    Display values in tebibytes.

--pebi
    Display values in pebibytes.

-h, --human
    Choose a compact human-readable unit for each value.

--si
    Use powers of 1000 rather than 1024.

-l, --lohi
    Include low- and high-memory detail rows.

-L, --line
    Print a compact one-line summary.

-t, --total
    Add a total RAM-plus-swap row.

-v, --committed
    Show committed memory and the commit limit.

-s N, --seconds N
    Repeat the report every N seconds. Fractional intervals are accepted.

-c N, --count N
    Stop after N reports.

-w, --wide
    Split buffers and cache into separate columns.

--help
    Display help and exit.

-V, --version
    Display version information and exit.
```

## EXIT STATUS

```text
0    Report completed successfully.
1    Invalid arguments or required memory observations were unavailable.
130  Operation was cancelled.
```

## PLATFORM NOTES

The project targets .NET 10 and is intended for Windows, Linux, and macOS. Linux `/proc/meminfo` supplies the highest-fidelity procps representation. On other hosts the shared provider maps representable native memory counters into neutral ProcPs fields; unavailable fields are not fabricated.

## AUTHORS

Inspired by the procps `free` lineage: Brian Edmonds and Rafał Maszkowski wrote the original implementation; Robert Love rewrote it; Sami Kerola later substantially reworked it; and Albert Cahalan also contributed to its development.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `free.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`free(1)`, `ps(1)`, `uptime(1)`, `vmstat(8)`
