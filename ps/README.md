# PS(1)

## NAME

**ps** — report a snapshot of current processes

## SYNOPSIS

```text
ps [options]
```

## DESCRIPTION

`Icod.ProcPs.Ps` is a managed .NET implementation of procps-ng `ps(1)`, modeled on procps-ng 4.0.6.

The command supports Unix-style options, BSD option words, numeric PID operands, custom output formats, sorting, process forests, thread views, personality selection, user/group/terminal/process relationship selectors, and a broad ProcPs field catalog. Process and system observations come from `Icod.ProcPs.Shared`.

As with procps `ps`, option styles can change both selection and the default output presentation. Use `ps L` to list the format fields implemented by this command.

## SELECTION

```text
-A, -e
    Select all processes.

-a
    Select processes with a terminal except session leaders.

a
    Lift the current-user restriction.

x
    Include processes without a controlling terminal.

-d
    Select all processes except session leaders.

-N, --deselect
    Invert the selection.

-p, --pid=PIDLIST
    Select process IDs.

-q, --quick-pid=PIDLIST
    Select process IDs and preserve the supplied order.

--ppid=PIDLIST
    Select parent process IDs.

-g GROUPLIST
    Select numeric sessions or named effective groups.

--pgroup=PIDLIST
    Select process groups.

--group=GROUPLIST
    Select effective groups.

-s, --sid=PIDLIST
    Select sessions.

-t, --tty=TTYLIST
    Select terminals.

-u, --user=USERLIST
    Select effective users.

-U, --User=USERLIST
    Select real users.

-G, --Group=GROUPLIST
    Select real groups.

-C, --command=LIST
    Select short command names.

r
    Restrict the selection to running tasks.
```

## OUTPUT AND FORMAT

```text
L
    List implemented format specifiers.

-o, --format=FORMAT
    Select a user-defined comma-separated output format. Headings and field
    widths may be overridden with the supported procps syntax.

-f, -F, -l
    Select full, extra-full, or long preset formats.

j, l, u, v
    Select BSD jobs, long, user, or virtual-memory preset formats.

--sort=SPEC
    Sort using comma-separated [+|-]field keys.

--forest, -H, f
    Display processes in hierarchy order.

-L, -T, -m, H, m
    Show threads where the process provider can enumerate them.

e
    Append the observed environment to the command.

c
    Prefer the short command name rather than arguments.

--headers
--no-headers
    Force or suppress headings.

--cols=N, --columns=N, --width=N
    Set the output width.

w
    Widen output; repeat for effectively unlimited width.

--personality=NAME
    Select linux, posix, bsd, sunos4, digital, hp, or aix compatibility
    presentation.

--help
    Display help and exit.

--version
    Display version information and exit.
```

## EXIT STATUS

```text
0    Report completed successfully.
1    Invalid options or a controlled observation/rendering error occurred.
130  Operation was cancelled.
```

## PLATFORM NOTES

The project targets .NET 10 on Windows, Linux, and macOS. Linux procfs exposes the richest ProcPs field set, including namespaces, cgroups, signal masks, capabilities, security labels, and lightweight tasks. Native Windows and macOS providers expose the process information that can be represented faithfully; unavailable fields remain unavailable rather than being invented.

## AUTHORS

The procps `ps` lineage credits Branko Lankester with the original implementation; Michael K. Johnson with a major `/proc` rewrite; Michael Shields with PID-list selection; Charles Blake with multi-level sorting and substantial infrastructure; David Mosberger-Tang with generic BFD support for `psupdate`; and Albert Cahalan with the rewrite for full Unix98 and BSD support.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `ps.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`ps(1)`, `pgrep(1)`, `pmap(1)`, `w(1)`, `proc(5)`
