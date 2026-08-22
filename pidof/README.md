# PIDOF(1)

## NAME

**pidof** — find process IDs for running programs

## SYNOPSIS

```text
pidof [options] [program [...]]
```

## DESCRIPTION

`Icod.ProcPs.PidOf` is a managed .NET implementation of procps-ng `pidof(1)`, modeled on procps-ng 4.0.6.

The command enumerates running processes and compares the requested program names against observed executable paths, command names, and command-line identity using `Icod.ProcPs.Shared`. Matching PIDs are printed in descending PID order unless an option restricts the result further.

## OPTIONS

```text
-s, --single-shot
    Return only one matching PID.

-c, --check-root
    For a privileged Unix caller, omit processes whose observed root differs
    from the caller's root.

-q, --quiet
    Suppress PID output and communicate the result through exit status.
    Quiet mode also selects single-shot behavior in this implementation.

-w, --with-workers
    Permit command-name matching for worker-like processes whose command line
    is unavailable or empty.

-x
    Also match interpreter processes running a named script.

-o, --omit-pid=PID,...
    Omit listed PIDs. The special token %PPID denotes this command's parent.

-t, --lightweight
    Include Linux lightweight tasks when the procfs task provider is available.

-S, --separator=SEP
-d SEP
    Separate PIDs with SEP instead of a space.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

The compatibility options `-n` and `-m` are accepted and ignored by the current profile.

## EXIT STATUS

```text
0    At least one requested program was found.
1    No requested program was found, or a controlled lookup error occurred.
```

## PLATFORM NOTES

Basic program lookup is cross-platform where executable/process identity can be observed. `--check-root` depends on Unix process-root observations and is effective only for privileged callers. Lightweight-task enumeration is Linux-specific.

## AUTHORS

The procps-ng `pidof` implementation is credited to Jaromir Capik. The older SysV `pidof` command has a separate historical lineage associated with Miquel van Smoorenburg.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pidof.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pidof(1)`, `pgrep(1)`, `pkill(1)`, `ps(1)`
