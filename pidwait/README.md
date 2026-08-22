# PIDWAIT(1)

## NAME

**pidwait** — wait for processes selected by name and other attributes

## SYNOPSIS

```text
pidwait [options] <pattern>
```

## DESCRIPTION

`Icod.ProcPs.PidWait` is a managed .NET implementation of procps-ng `pidwait(1)`, modeled on procps-ng 4.0.6.

The command uses the shared `pgrep`/`pkill` selection engine to locate matching processes and then waits for the selected process identities through `Icod.CommandFramework` process-wait contracts. Identity/reuse information is retained so a recycled PID is not silently treated as the original target.

## WAIT OPTIONS

```text
-e, --echo
    Print each selected PID before waiting for it.
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
0    At least one selected process was successfully waited for.
1    No usable process matched.
2    Command-line or regular-expression syntax error.
3    Fatal observation, wait, or cancellation error in this command profile.
```

A target that vanishes before it can be waited for is treated as no longer usable; other matched targets may still complete normally.

## PLATFORM NOTES

Waiting is performed through the host process-control provider. Linux can use the strongest native identity/wait primitives exposed by the framework; other hosts use their corresponding process-wait mechanisms. Linux-only selectors such as namespaces or lightweight tasks remain dependent on procfs.

## AUTHORS

`pidwait` shares the procps `pgrep`/`pkill` lineage established by Kjetil Torgrim Homme, with later procps-ng work by contributors including Craig Small. The modern procps-ng manual treats `pgrep`, `pkill`, and `pidwait` as one command family.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pidwait.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pidwait(1)`, `pgrep(1)`, `pkill(1)`, `pidfd_open(2)`
