# PMAP(1)

## NAME

**pmap** — report the memory map of one or more processes

## SYNOPSIS

```text
pmap [options] PID [PID ...]
```

## DESCRIPTION

`Icod.ProcPs.Pmap` is a managed .NET implementation of procps-ng `pmap(1)`, modeled on procps-ng 4.0.6.

The command reports mapped address ranges for selected processes. The default view shows mapping address, size, mode, and mapping name. Extended modes expose RSS/dirty data or the detailed fields supplied by Linux `smaps`.

The implementation reads maps through `Icod.ProcPs.Shared` and revalidates process identity around observation so PID reuse is not silently accepted.

## OPTIONS

```text
-x, --extended
    Show extended size, RSS, dirty, mode, and mapping information.

-X
    Show a dynamic extended set of kernel fields.

-XX
    Show all detailed kernel fields exposed by the provider, including
    VmFlags when available.

-d, --device
    Show mapping offset and device information.

-q, --quiet
    Suppress the normal header/footer material.

-p, --show-path
    Show full mapping paths instead of basenames.

-k, --use-kernel-name
    Preserve kernel pseudo-mapping names.

-A, --range=LOW[,HIGH]
    Restrict output to mappings overlapping the hexadecimal address range.

-r
    Accepted and ignored for SunOS compatibility.

-c, --read-rc
-C, --read-rc-from=FILE
-n, --create-rc
-N, --create-rc-to=FILE
    Recognized for procps compatibility, but rc-file modes are deliberately
    not implemented by this portability profile and return a controlled error.

-h, --help
    Display help and exit.

-V, --version
    Display version information and exit.
```

## EXIT STATUS

```text
0    Requested maps were reported.
1    A controlled usage, capability, or map-observation error occurred.
42   A requested process vanished or its PID was reused; this status is
     combined with other failure bits where applicable.
```

## PLATFORM NOTES

Operational memory-map reporting is currently Linux-only because Linux procfs provides the complete mapping and `smaps` semantics required by this profile. On Windows and macOS the command reports the unsupported capability rather than approximating a procps map from incomplete APIs.

## AUTHORS

Inspired by the procps `pmap` implementation begun by Albert Cahalan, with later substantial maintenance and development by Craig Small, Jim Warner, and Sami Kerola.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

See `pmap.LICENSE.txt` and the repository `LICENSE` file for licensing terms and notices applicable to this project.

## SEE ALSO

`pmap(1)`, `ps(1)`, `proc(5)`
