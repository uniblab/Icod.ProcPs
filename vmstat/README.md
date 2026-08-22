# VMSTAT(8)

## NAME

**vmstat** — report virtual-memory and system activity statistics

## SYNOPSIS

```text
vmstat [options] [delay [count]]
```

## DESCRIPTION

`Icod.ProcPs.Vmstat` is a managed .NET implementation of procps-ng `vmstat(8)`, modeled on procps-ng 4.0.6.

The default report combines process queues, memory, swap, paging/I/O, system event, and CPU activity. With no delay, one report is printed; with a delay, samples are repeated at the requested interval, optionally limited by `count`.

Specialized report modes expose fork totals, disk statistics, partition statistics, slab information, and cumulative statistics where the active platform provider supplies the required counters.

## OPTIONS

```text
-a, --active
    Show active/inactive memory in place of buffer/cache columns.

-f, --forks
    Print the number of forks since boot and exit.

-m, --slabs
    Display slab-cache statistics.

-n, --one-header
    Do not periodically redisplay the column header.

-s, --stats
    Display cumulative event-counter statistics.

-d, --disk
    Display per-disk statistics.

-D, --disk-sum
    Display a summary of disk statistics.

-p, --partition=DEV
    Display statistics for one partition.

-S, --unit=CHAR
    Select the display unit. The implemented profile accepts the procps
    k, K, m, and M unit families.

-w, --wide
    Use the wider column layout.

-t, --timestamp
    Append a timestamp to default/disk reports where supported.

-y, --no-first
    Skip the since-boot first sample and wait for an interval sample.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

## SAMPLING

```text
vmstat
    Print a single since-boot/default sample.

vmstat DELAY
    Continue sampling every DELAY seconds.

vmstat DELAY COUNT
    Print COUNT samples separated by DELAY seconds.
```

Counter deltas use wrap-aware arithmetic. The Linux default profile also preserves procps-compatible handling of CPU guest ticks and backward-moving idle counters.

## EXIT STATUS

```text
0    Requested statistics were reported.
1    Invalid arguments or a required capability was unavailable.
130  Operation was cancelled.
```

## PLATFORM NOTES

Linux provides the complete procfs-backed profile, including disk, partition, slab, fork, paging, and cumulative event data. Windows and macOS provide native memory/CPU information and other counters where faithfully representable. The default report can render known values with explicit `-` placeholders for unavailable fields; specialized modes return a controlled error when their capability is absent.

## AUTHORS

Written originally by Henry Ware. Fabian Frédérick added disk statistics, slab, partition, and related functionality to the procps implementation.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `vmstat.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`vmstat(8)`, `free(1)`, `ps(1)`, `uptime(1)`
