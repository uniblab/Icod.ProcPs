# PROCPS(1)

## NAME

`procps` — multi-command wrapper for the managed Icod.ProcPs suite

## SYNOPSIS

```text
procps COMMAND [args...]
procps --help
procps --version
```

## DESCRIPTION

`procps` is the installable .NET tool entry point for `Icod.ProcPs`. It routes
its first operand in-process to the corresponding managed ProcPs command:

```text
procps free    [args...]
procps pgrep   [args...]
procps pidof   [args...]
procps pidwait [args...]
procps pkill   [args...]
procps pmap    [args...]
procps ps      [args...]
procps pwdx    [args...]
procps slabtop [args...]
procps sysctl  [args...]
procps top     [args...]
procps uptime  [args...]
procps vmstat  [args...]
procps w       [args...]
procps watch   [args...]
```

The router does not launch another process. It invokes the same managed command
implementations used by the standalone executables and returns the selected
command's exit status unchanged.

Command-specific option parsing remains with the selected command. Use
`procps COMMAND --help` for the complete option list for that command.

## ROUTER OPTIONS

`-h`, `--help`
: Display the router help and exit successfully.

`-v`, `--version`
: Display the `procps` package version and exit successfully.

An omitted or unknown command is a router usage error and returns status 2.

## DISTRIBUTION MODES

### Conventional .NET tool

The `Icod.ProcPs` NuGet tool package installs exactly one command:

```text
procps
```

The package does not install fifteen additional command shims. Select a ProcPs
command through the router's first argument.

### Traditional executables

Version-tagged GitHub releases also provide conventional ZIP archives for
Windows, Linux, and macOS on both x64 and ARM64. Each archive contains the
fifteen historical command entry points plus `procps`, the repository `LICENSE`,
and the repository `README.md`. These archives are framework-dependent and
require the .NET 10 runtime.

See `packaging/README.md` for the supported packaging, verification, and release
workflow.

## LICENSE

`procps` is an executable work in the Icod.ProcPs suite and is distributed under
the GNU General Public License version 3 or later. The reusable
`Icod.ProcPs.Shared` library retains its separate LGPL-3.0-or-later licensing.

## SEE ALSO

`free(1)`, `pgrep(1)`, `pidof(1)`, `pidwait(1)`, `pkill(1)`, `pmap(1)`,
`ps(1)`, `pwdx(1)`, `slabtop(1)`, `sysctl(8)`, `top(1)`, `uptime(1)`,
`vmstat(8)`, `w(1)`, `watch(1)`
