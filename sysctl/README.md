# SYSCTL(8)

## NAME

**sysctl** — read and write Linux kernel parameters at runtime

## SYNOPSIS

```text
sysctl [options] [variable[=value] ...]
```

## DESCRIPTION

`Icod.ProcPs.Sysctl` is a managed .NET implementation of procps-ng `sysctl(8)`, modeled on procps-ng 4.0.6.

Operational commands use the Linux `/proc/sys` hierarchy. Keys may be written in the familiar dot form or slash form, and the command can read individual values, enumerate variables, write values, filter by regular expression, apply preload files, or process the standard system configuration directories.

Unlike the other commands in this repository, `sysctl` does not depend on `Icod.ProcPs.Shared`; it uses its own kernel-parameter backend contract.

## OPTIONS

```text
-a, --all
-A
-X
    Display all variables.

--deprecated
    Include deprecated parameters in listings.

--dry-run
    Validate and report writes without changing the kernel value.

-b, --binary
    Print the value without a trailing newline.

-e, --ignore
    Ignore errors for unknown variables.

-N, --names
    Print names without values.

-n, --values
    Print values without names.

-p, --load[=FILE]
-f
    Read settings from FILE. With `-p -`, read configuration text from
    standard input. The default preload file is /etc/sysctl.conf.

--system
    Read configuration files from the standard system directories using
    procps precedence rules.

-r, --pattern=EXPRESSION
    Restrict settings using an extended regular expression.

-q, --quiet
    Do not echo successfully written values.

-w, --write
    Enable assignment of variable=value operands.

-o
-x
    Accepted as BSD compatibility no-ops.

-d
    Alias for help.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

## SYSTEM CONFIGURATION

`--system` considers the following directories in the implemented procps precedence order and then applies `/etc/sysctl.conf`:

```text
/etc/sysctl.d
/run/sysctl.d
/usr/local/lib/sysctl.d
/usr/lib/sysctl.d
/lib/sysctl.d
/etc/sysctl.conf
```

Same-named configuration files in higher-precedence directories mask lower-precedence files. Configuration assignments support procps-style glob matching and exclusions.

## EXIT STATUS

```text
0    Requested operation completed successfully.
>0   One or more controlled read/write/configuration failures occurred.
      Multiple direct read failures may accumulate in the returned status.
130  Operation was cancelled.
```

## PLATFORM NOTES

Help and version output are portable, but kernel-parameter operations require a Linux `/proc/sys` backend. Windows and macOS do not expose Linux sysctl semantics through this command and therefore return a controlled unsupported diagnostic for operational requests.

## AUTHORS

The procps `sysctl` utility was written by George Staikos and has subsequently been maintained by the procps and procps-ng communities.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `sysctl.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`sysctl(8)`, `sysctl.conf(5)`, `proc(5)`, `regex(7)`
