param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if (0 -ne $LASTEXITCODE) {
        throw "dotnet exited with status $LASTEXITCODE."
    }
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedOutput
    )

    Write-Host "> $Path $($Arguments -join ' ')"
    $output = @(& $Path @Arguments)
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host $line
    }

    if (0 -ne $exitCode) {
        throw "Tool '$Path' exited with status $exitCode."
    }

    $actualOutput = ($output -join "`n").Trim()
    if ($ExpectedOutput -ne $actualOutput) {
        throw "Tool '$Path' reported '$actualOutput'; expected '$ExpectedOutput'."
    }
}

function Get-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $reader = [System.IO.StreamReader]::new($Entry.Open())
    try {
        return $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$routerProjectPath = Join-Path $repositoryRoot 'procps/Icod.ProcPs.Router.csproj'
$repositoryReadmePath = Join-Path $repositoryRoot 'README.md'

Push-Location $repositoryRoot
try {
    $packageVersion = (& dotnet msbuild $routerProjectPath -nologo -getProperty:PackageVersion).Trim()
    if (0 -eq $packageVersion.Length) {
        throw 'Unable to determine PackageVersion.'
    }

    if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
        $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
    }
    $ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
    if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
        throw "Artifact directory '$ArtifactDirectory' does not exist."
    }

    $packagePath = Join-Path $ArtifactDirectory "Icod.ProcPs.$packageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Icod.ProcPs package '$packagePath' was not produced by the pack stage."
    }

    Write-Host ''
    Write-Host "=== Verify exact package artifact ($Configuration) ==="
    Write-Host "Package: $packagePath"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [System.StringComparison]::OrdinalIgnoreCase) })
        if (1 -ne $nuspecEntries.Count) {
            throw "Package contains $($nuspecEntries.Count) nuspec files; expected exactly one."
        }

        [xml]$nuspec = Get-ZipEntryText -Entry $nuspecEntries[0]
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $metadata) {
            throw 'Package nuspec does not contain metadata.'
        }

        $packageId = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        $nuspecVersion = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        if ('Icod.ProcPs' -ne $packageId) {
            throw "Package ID '$packageId' does not match Icod.ProcPs."
        }
        if ($packageVersion -ne $nuspecVersion) {
            throw "Package version '$nuspecVersion' does not match expected version '$packageVersion'."
        }

        $readmeNode = $metadata.SelectSingleNode("*[local-name()='readme']")
        if ($null -eq $readmeNode -or 'README.md' -ne $readmeNode.InnerText.Trim().Replace('\', '/')) {
            throw 'Package must declare repository README.md as its NuGet readme.'
        }

        $requiredEntries = @(
            'README.md',
            'procps/README.md',
            'tools/net10.0/any/DotnetToolSettings.xml',
            'tools/net10.0/any/procps.dll',
            'tools/net10.0/any/free.dll',
            'tools/net10.0/any/pgrep.dll',
            'tools/net10.0/any/pidof.dll',
            'tools/net10.0/any/pidwait.dll',
            'tools/net10.0/any/pkill.dll',
            'tools/net10.0/any/pmap.dll',
            'tools/net10.0/any/ps.dll',
            'tools/net10.0/any/pwdx.dll',
            'tools/net10.0/any/slabtop.dll',
            'tools/net10.0/any/hugetop.dll',
            'tools/net10.0/any/sysctl.dll',
            'tools/net10.0/any/tload.dll',
            'tools/net10.0/any/top.dll',
            'tools/net10.0/any/uptime.dll',
            'tools/net10.0/any/vmstat.dll',
            'tools/net10.0/any/w.dll',
            'tools/net10.0/any/watch.dll'
        )
        foreach ($entryPath in $requiredEntries) {
            if (-not ($archive.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1)) {
                throw "Package does not contain required entry '$entryPath'."
            }
        }

        $readmeEntry = $archive.Entries | Where-Object { $_.FullName -eq 'README.md' } | Select-Object -First 1
        $packagedReadme = Get-ZipEntryText -Entry $readmeEntry
        $repositoryReadme = [System.IO.File]::ReadAllText($repositoryReadmePath)
        if ($repositoryReadme -ne $packagedReadme) {
            throw 'Packaged README.md does not exactly match the repository README.md.'
        }

        $toolSettingsEntry = $archive.Entries | Where-Object { $_.FullName -eq 'tools/net10.0/any/DotnetToolSettings.xml' } | Select-Object -First 1
        [xml]$toolSettings = Get-ZipEntryText -Entry $toolSettingsEntry
        $commands = @($toolSettings.DotNetCliTool.Commands.Command)
        if (1 -ne $commands.Count) {
            throw "Package declares $($commands.Count) tool commands; expected exactly one."
        }
        if ('procps' -ne "$($commands[0].Name)" -or 'dotnet' -ne "$($commands[0].Runner)") {
            throw "Package tool settings do not declare the expected procps/dotnet command."
        }
    } finally {
        $archive.Dispose()
    }

    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "Icod.ProcPs-package-smoke-$([Guid]::NewGuid().ToString('N'))"
    $toolPath = Join-Path $smokeRoot 'tool'
    $nugetConfigPath = Join-Path $smokeRoot 'NuGet.Config'
    New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

    try {
        $escapedArtifactDirectory = [System.Security.SecurityElement]::Escape($ArtifactDirectory)
        $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedArtifactDirectory" />
  </packageSources>
</configuration>
"@
        [System.IO.File]::WriteAllText($nugetConfigPath, $nugetConfig, [System.Text.UTF8Encoding]::new($false))

        Invoke-DotNet -Arguments @(
            'tool', 'install', 'Icod.ProcPs',
            '--version', $packageVersion,
            '--tool-path', $toolPath,
            '--configfile', $nugetConfigPath,
            '--no-cache'
        )

        $shimName = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
            'procps.exe'
        } else {
            'procps'
        }
        $routerShim = Join-Path $toolPath $shimName
        if (-not (Test-Path -LiteralPath $routerShim -PathType Leaf)) {
            throw "Installed tool shim '$routerShim' was not created."
        }

        Invoke-Tool -Path $routerShim -Arguments @('--version') -ExpectedOutput "procps (Icod.ProcPs) $packageVersion"

        $products = [ordered]@{
            'free' = 'Icod.ProcPs.Free'
            'pgrep' = 'Icod.ProcPs.Pgrep'
            'pidof' = 'Icod.ProcPs.PidOf'
            'pidwait' = 'Icod.ProcPs.PidWait'
            'pkill' = 'Icod.ProcPs.Pkill'
            'pmap' = 'Icod.ProcPs.Pmap'
            'ps' = 'Icod.ProcPs.Ps'
            'pwdx' = 'Icod.ProcPs.Pwdx'
            'slabtop' = 'Icod.ProcPs.SlabTop'
            'hugetop' = 'Icod.ProcPs.HugeTop'
            'sysctl' = 'Icod.ProcPs.Sysctl'
            'tload' = 'Icod.ProcPs.Tload'
            'top' = 'Icod.ProcPs.Top'
            'uptime' = 'Icod.ProcPs.Uptime'
            'vmstat' = 'Icod.ProcPs.Vmstat'
            'w' = 'Icod.ProcPs.W'
            'watch' = 'Icod.ProcPs.Watch'
        }

        foreach ($commandName in $products.Keys) {
            $expectedVersion = "$($products[$commandName]) ($packageVersion) inspired by procps-ng 4.0.6"
            Invoke-Tool -Path $routerShim -Arguments @($commandName, '--version') -ExpectedOutput $expectedVersion
        }
    } finally {
        if (Test-Path -LiteralPath $smokeRoot) {
            Remove-Item -LiteralPath $smokeRoot -Recurse -Force
        }
    }

    Write-Host ''
    Write-Host 'Exact package verification completed successfully.'
    Write-Host "  Package: $packagePath"
} finally {
    Pop-Location
}
