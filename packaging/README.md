# Icod.ProcPs distribution

This directory contains distribution verification and release tooling for
`Icod.ProcPs`. Command behavior remains in the seventeen historical command
projects and the `procps` router project.

The supported distribution model has two forms:

1. one installable .NET tool package exposing `procps`; and
2. traditional ZIP archives containing all eighteen executable entry points for
   a specific runtime identifier.

## .NET tool package

The SDK tool package is produced directly from the router project:

```text
dotnet pack procps/Icod.ProcPs.Router.csproj -c Release -o artifacts
```

The resulting package ID is `Icod.ProcPs`. It installs exactly one tool command,
`procps`, which routes in-process to:

```text
procps free [args...]
procps pgrep [args...]
procps pidof [args...]
procps pidwait [args...]
procps pkill [args...]
procps pmap [args...]
procps ps [args...]
procps pwdx [args...]
procps slabtop [args...]
procps hugetop [args...]
procps sysctl [args...]
procps tload [args...]
procps top [args...]
procps uptime [args...]
procps vmstat [args...]
procps w [args...]
procps watch [args...]
```

The .NET tool package does not install separate shims for the seventeen historical
commands. Current `dotnet tool` packaging supports one command per package, so
`procps` is the suite's single managed-tool entry point.

`Icod.ProcPs.Shared` remains a separately versioned and separately published
library package. It is not published by the suite release workflow described
here.

## Traditional executable archives

The historical executable projects are ordinary executable projects and are
non-packable through the repository's `Directory.Build.targets`. The `procps`
router is the only executable project explicitly packaged as a .NET tool.

`BuildReleaseArchive.ps1` publishes framework-dependent single-file apphosts and
creates a ZIP containing:

```text
free
pgrep
pidof
pidwait
pkill
pmap
ps
pwdx
slabtop
hugetop
sysctl
tload
top
uptime
vmstat
w
watch
procps
LICENSE
README.md
```

Windows archives use the normal `.exe` suffix. The published ZIPs require a
compatible .NET 10 runtime.

The automated release produces native apphosts for these runtime identifiers:

```text
win-x64
win-arm64
linux-x64
linux-arm64
osx-x64
osx-arm64
```

To build an archive locally with PowerShell 7:

```text
pwsh packaging/BuildReleaseArchive.ps1 -RuntimeIdentifier win-x64 -Version 1.0.1
pwsh packaging/BuildReleaseArchive.ps1 -RuntimeIdentifier linux-x64 -Version 1.0.1
pwsh packaging/BuildReleaseArchive.ps1 -RuntimeIdentifier osx-x64 -Version 1.0.1
```

The script smoke-tests all eighteen apphosts when the requested RID matches the
current host. On Unix-like hosts it uses the system `zip` command so executable
permissions are retained in the archive. A `-SelfContained` switch is available
for local experimentation, but automated GitHub releases are intentionally
framework-dependent.

## Distribution verification

Run:

```text
powershell packaging/VerifyDistribution.ps1
```

or, with PowerShell 7:

```text
pwsh packaging/VerifyDistribution.ps1
```

The verifier:

- restores, builds, and tests the solution;
- executes the seventeen built standalone command apphosts through `--version`;
- packs `Icod.ProcPs` from the `procps` router project;
- inspects the generated tool package and requires exactly one command named
  `procps`;
- verifies that the router and seventeen managed command assemblies are present in
  the package;
- installs the package from an isolated local NuGet source; and
- exercises `procps --version` and each routed command's `--version` path.

GitHub Actions runs the same verification natively on Windows, Linux, and macOS
for both x64 and ARM64.

## Automated releases

`.github/workflows/release.yaml` is triggered only by pushed tags beginning with
`v`. Before publishing anything, it requires all of the following:

- the tag has the form `v<semver>`;
- the tagged commit is contained in `main`;
- the tag version matches both `Version` and `PackageVersion` in
  `procps/Icod.ProcPs.Router.csproj`;
- distribution verification passes on Windows, Linux, and macOS on x64 and
  ARM64;
- all six RID-specific ZIPs build and smoke-test; and
- the `Icod.ProcPs` NuGet tool package is built successfully.

Only after those gates pass does the workflow publish the same `.nupkg` first to
NuGet.org and then to GitHub Packages. If both publications succeed, it creates
a GitHub Release for the existing tag and attaches:

```text
Icod.ProcPs-<version>-win-x64.zip
Icod.ProcPs-<version>-win-arm64.zip
Icod.ProcPs-<version>-linux-x64.zip
Icod.ProcPs-<version>-linux-arm64.zip
Icod.ProcPs-<version>-osx-x64.zip
Icod.ProcPs-<version>-osx-arm64.zip
Icod.ProcPs.<version>.nupkg
SHA256SUMS.txt
```

GitHub also supplies its normal source-code archives for the tagged commit.
Prerelease versions containing a hyphen are created as GitHub prereleases.

### Repository configuration

NuGet.org publication uses Trusted Publishing rather than a stored long-lived API
key. In the NuGet.org account that owns `Icod.ProcPs`, create a GitHub Actions
Trusted Publishing policy with these values:

```text
Repository owner: uniblab
Repository:       Icod.ProcPs
Workflow file:    release.yaml
Environment:      (leave empty)
```

Create an Actions repository secret named `NUGET_USER` containing the NuGet.org
profile name that owns or is authorized to publish `Icod.ProcPs`. Use the
profile name, not an email address. The workflow grants `id-token: write` only to
the NuGet.org publication job and uses `NuGet/login@v1` to exchange GitHub's OIDC
token for a short-lived NuGet credential immediately before publication. No
long-lived NuGet API key is stored in GitHub.

GitHub Packages and GitHub Release creation use the workflow-provided
`GITHUB_TOKEN`; no separate GitHub package token is stored in the repository. The
workflow grants `packages: write` only to the GitHub Packages publication job and
`contents: write` only to the GitHub Release job.

### Publishing a version

First update `IcodProcPsSuiteVersion` in `Directory.Build.targets` together with
`Version` and `PackageVersion` in the `procps` router project, merge that change
to `main`, and ensure normal CI is green. Then tag that exact commit and push the
tag:

```text
git switch main
git pull
git tag -a v1.0.1 -m "Icod.ProcPs 1.0.1"
git push origin v1.0.1
```

The tag is the release trigger and the immutable source identity for every
package and archive produced by that workflow. Package registries are immutable
for a published version, so use a new version for a new release. Both registry
pushes use `--skip-duplicate`, which makes publication jobs safe to rerun after a
later stage fails. Prefer GitHub Actions' "Re-run failed jobs" operation when
recovering a partially completed release.

## Versioning

The suite release version is recorded in two places:

```text
Directory.Build.targets                         IcodProcPsSuiteVersion
procps/Icod.ProcPs.Router.csproj                Version / PackageVersion
```

`IcodProcPsSuiteVersion` supplies assembly version metadata to every standalone
historical command. Their version output is therefore derived from the same
suite version used by release archives:

```text
Icod.ProcPs.X (VERSION) inspired by procps-ng 4.0.6
```

Keep all three values aligned when preparing a suite release. Distribution
verification and release-archive smoke tests compare every standalone and routed
command's version output against the router package version and fail on a
mismatch. The independent `Icod.ProcPs.Shared` package keeps its own package
version.

## Licensing

`procps` and the standalone executable commands are GPL-3.0-or-later. Every
traditional ZIP contains the repository GPLv3 `LICENSE`, and the corresponding
source is the tagged repository revision used to build the GitHub Release.

`Icod.ProcPs.Shared` retains its separately declared LGPL-3.0-or-later license.
