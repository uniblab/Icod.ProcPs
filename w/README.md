# W(1)

## NAME

**w** — show who is logged in and what they are doing

## SYNOPSIS

```text
w [options] [user]
```

## DESCRIPTION

`Icod.ProcPs.W` is a managed .NET implementation of procps-ng `w(1)`, modeled on procps-ng 4.0.6.

The header reports the current local time, system/container uptime, login count, and load averages. Each session row can show the user, terminal, origin, login time, idle time, cumulative CPU used by the session, CPU used by the selected current process, and the command being run.

The optional `user` operand restricts output to sessions belonging to that login name.

## OPTIONS

```text
-c, --container
    Use container uptime.

-h, --no-header
    Suppress the uptime/load header and column headings.

-u, --no-current
    Do not require the selected current process to belong to the login user.

-s, --short
    Use the short format and omit login-time, JCPU, and PCPU columns.

-t, --terminal
    Add observable terminal sessions that are absent from login accounting.

-f, --from
    Toggle display of the FROM/origin column.

-o, --old-style
    Use the old idle-time formatting rules.

-i, --ip-addr
    Prefer the accounting provider's numeric/address origin.

-p, --pids
    Prefix WHAT with the login/current process identifiers.

--help
    Display help and exit.

-V, --version
    Display version information and exit.
```

## ENVIRONMENT

```text
PROCPS_CONTAINER
    When set, behave as though --container was selected.

PROCPS_USERLEN
    Override the username-column width. The default is 8.

PROCPS_FROMLEN
    Override the origin-column width. The default is 16.

COLUMNS
    Limit the overall terminal-width presentation when valid.
```

Invalid ProcPs width environment values are diagnosed and the implementation falls back to its defaults.

## EXIT STATUS

```text
0    Report completed successfully.
1    Invalid options or required login/process/system observations failed.
130  Operation was cancelled.
```

## PLATFORM NOTES

`w` requires a trustworthy login-session accounting source in addition to process and system metrics. The command runs where `Icod.ProcPs.Shared` can provide those observations; hosts without supported login accounting receive a controlled error rather than synthetic sessions. Linux exposes the closest procps semantics.

## AUTHORS

The procps `w` command was rewritten almost entirely by Charles Blake, based on the earlier version by Larry Greenfield and Michael K. Johnson.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `w.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`w(1)`, `uptime(1)`, `ps(1)`, `who(1)`, `utmp(5)`
