# UPTIME(1)

## NAME

**uptime** — report how long the system has been running

## SYNOPSIS

```text
uptime [options]
```

## DESCRIPTION

`Icod.ProcPs.Uptime` is a managed .NET implementation of procps-ng `uptime(1)`, modeled on procps-ng 4.0.6.

The normal display reports the local time, elapsed system uptime, logged-in user count, and, when available, 1-, 5-, and 15-minute load averages. Alternate modes can show a human-readable uptime duration, the boot time, a raw machine-friendly record, or container uptime.

## OPTIONS

```text
-c, --container
    Use container uptime where the host provider can observe it.

-p, --pretty
    Print only a human-readable uptime duration.

-r, --raw
    Print Unix time, uptime seconds, user count, and load averages in a
    machine-friendly form.

-s, --since
    Print the local date and time at which the selected system/container
    started.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

If `PROCPS_CONTAINER` is set, container mode is enabled before command-line options are processed.

## EXIT STATUS

```text
0    Requested uptime information was reported.
1    Invalid arguments or an observation required by the selected mode was unavailable.
130  Operation was cancelled.
```

## PLATFORM NOTES

System uptime itself is available on the principal Windows, Linux, and macOS providers. Standard output reports every observation available on the host: when load averages are unavailable, as on Windows, the load-average clause is omitted while the available local time, uptime, and user-count information is still reported successfully. `--pretty` and `--since` require only uptime.

Raw output intentionally retains stricter procps-compatible semantics and requires uptime, user-count, and load-average observations. A platform that cannot provide all observations required by `--raw` returns a controlled diagnostic rather than inventing substitute load-average values.

## AUTHORS

The procps `uptime` implementation lineage is credited to Larry Greenfield and Michael K. Johnson, with subsequent maintenance by the procps and procps-ng communities.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `uptime.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`uptime(1)`, `w(1)`, `free(1)`, `proc(5)`
