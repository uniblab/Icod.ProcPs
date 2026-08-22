# PKILL(1)

## NAME

**pkill** — signal processes selected by name and other attributes

## SYNOPSIS

```text
pkill [options] <pattern>
```

## DESCRIPTION

`Icod.ProcPs.Pkill` is a managed .NET implementation of procps-ng `pkill(1)`, modeled on procps-ng 4.0.6.

The command uses the same process-selection engine as `pgrep`, but sends a signal to each selected process rather than listing its PID. Signal parsing and process-control operations are supplied through the cross-platform process contracts in `Icod.CommandFramework` and the ProcPs control adapter.

If no signal is specified, the command uses the process-control layer's normal `pkill` default signal behavior.

## SIGNAL AND ACTION OPTIONS

```text
-SIGNAL
    Select the signal to send by symbolic name or number.

--signal=SIGNAL
    Select the signal to send and the signal used by --require-handler.

-q, --queue=VALUE
    Queue VALUE with the selected signal where queued signals are supported.

-e, --echo
    Report the processes acted upon.

-m, --mrelease
    After a successful signal, request Linux process_mrelease memory release.
    The current implementation supports this only on supported Linux
    x86-64 and ARM64 hosts.
```

## SELECTION OPTIONS

The process-matching engine combines different categories of selection criteria with AND semantics. Lists within one criterion are alternatives.

```text
-c, --count
    Report the number of matching processes.

-f, --full
    Match against the full command line rather than only the command name.

-g, --pgroup=PGID,...
    Match process-group identifiers.

-G, --group=GID,...
    Match real group identifiers or names.

-i, --ignore-case
    Match the regular expression without regard to case.

-n, --newest
    Select the newest matching process.

-o, --oldest
    Select the oldest matching process.

-O, --older=SECONDS
    Match only processes older than SECONDS.

-p, --pid=PID,...
    Match process identifiers.

-P, --parent=PPID,...
    Match parent process identifiers.

-s, --session=SID,...
    Match session identifiers.

-t, --terminal=TTY,...
    Match controlling terminals.

-u, --euid=EUID,...
    Match effective user identifiers or names.

-U, --uid=UID,...
    Match real user identifiers or names.

-x, --exact
    Require an exact command-name match.

-F, --pidfile=FILE
    Read a process identifier from FILE. A value of - reads standard input.

-L, --logpidfile
    Require the selected pidfile to be locked.

-r, --runstates=STATE
    Restrict matches by Linux-style process state.

-A, --ignore-ancestors
    Exclude ancestors of this command.

--signal=SIGNAL
    Select the signal used by signal-sensitive matching and by pkill.

-H, --require-handler
    Match only processes that catch the selected signal.

--cgroup=NAME,...
    Match cgroup v2 paths when the host provider exposes them.

--ns=PID
    Match the namespaces of PID.

--nslist=LIST
    Restrict namespace comparison to names in LIST.

--env=NAME[=VALUE]
    Match an observed process environment entry.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

GNU extended regular-expression matching is supplied by `Icod.CommandFramework`.

## EXIT STATUS

```text
0    At least one selected process was successfully acted upon.
1    No process matched, or no selected operation succeeded.
2    Command-line or regular-expression syntax error.
3    Fatal observation or control error.
```

A failure for one target does not necessarily make the command fail when another selected target was successfully signalled.

## PLATFORM NOTES

Process signalling is capability-driven. Ordinary termination/signalling works where the host process provider can represent the requested operation. POSIX signal numbers, queued signal values, signal-disposition inspection, and `process_mrelease` have platform-specific availability. Unsupported operations are reported rather than emulated with different semantics.

## AUTHORS

Inspired by `pgrep` and `pkill`, originally written by Kjetil Torgrim Homme. Albert Cahalan and Roberto Polli contributed later implementation work, and Craig Small contributed to the modern procps-ng command and manual lineage.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pkill.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pkill(1)`, `pgrep(1)`, `pidwait(1)`, `kill(1)`, `signal(7)`
