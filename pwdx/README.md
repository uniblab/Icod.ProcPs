# PWDX(1)

## NAME

**pwdx** — report the current working directory of a process

## SYNOPSIS

```text
pwdx [options] pid...
```

## DESCRIPTION

`Icod.ProcPs.Pwdx` is a managed .NET implementation of procps-ng `pwdx(1)`, modeled on procps-ng 4.0.6.

For each supplied process identifier, the command obtains a stable process observation and asks the shared path provider for the process's current working directory. Output has the form:

```text
PID: /observed/current/directory
```

Operands of the form `/proc/PID` are also accepted for compatibility.

## OPTIONS

```text
-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

Multiple PIDs may be supplied. Failure to observe one process does not prevent later operands from being attempted.

## EXIT STATUS

```text
0    All requested working directories were reported.
1    An operand was invalid or at least one requested directory could not be
     observed.
```

## PLATFORM NOTES

Linux obtains current-directory information from procfs and macOS uses native process-path facilities. The current Windows provider does not expose another process's working directory with equivalent semantics; such requests produce a controlled unsupported diagnostic rather than a guessed path.

## AUTHORS

Nicholas Miell wrote the procps `pwdx` command in 2004. Its manual page was based on Albert Cahalan's `pmap(1)` documentation, and the command has subsequently been maintained within procps-ng.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pwdx.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pwdx(1)`, `ps(1)`, `pgrep(1)`
