# PGREP(1)

## NAME

**pgrep** — find processes by name and other attributes

## SYNOPSIS

```text
pgrep [options] <pattern>
```

## DESCRIPTION

`Icod.ProcPs.Pgrep` is a managed .NET implementation of procps-ng `pgrep(1)`, modeled on procps-ng 4.0.6.

`pgrep` enumerates processes through `Icod.ProcPs.Shared`, applies GNU extended regular-expression and attribute selection, and writes the identifiers of matching processes. Selection can be based on process identity, parentage, groups, sessions, users, terminals, age, state, cgroup, namespaces, environment, and signal disposition where the host can observe those properties.

By default the pattern is matched against the short command name. Use `--full` to match the complete command line.

## OUTPUT OPTIONS

```text
-d, --delimiter=STRING
    Separate results with STRING instead of the host newline.

-l, --list-name
    Print the PID followed by the process name.

-a, --list-full
    Print the PID followed by the full command line.

--quiet
    Suppress normal output and communicate the match through exit status.

-v, --inverse
    Invert the match.

-w, --lightweight
    Include Linux lightweight tasks when procfs task enumeration is available.

-Q
    Shell-quote full command-line output.
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
0    One or more processes matched.
1    No processes matched.
2    Command-line or regular-expression syntax error.
3    Fatal observation or control error.
```

## PLATFORM NOTES

Basic process enumeration and common identity selectors are cross-platform. Linux procfs supplies additional semantics such as lightweight tasks, namespaces, cgroups, detailed process states, environment observation, and signal-disposition data. A selector that requires information the host cannot safely observe may fail rather than guess.

## AUTHORS

Inspired by `pgrep` and `pkill`, originally written by Kjetil Torgrim Homme. Albert Cahalan and Roberto Polli contributed later implementation work, and Craig Small contributed to the modern procps-ng command and manual lineage.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pgrep.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pgrep(1)`, `pkill(1)`, `pidwait(1)`, `ps(1)`, `regex(7)`
