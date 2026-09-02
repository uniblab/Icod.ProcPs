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

function Get-MSBuildProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $value = (& dotnet msbuild $ProjectPath -nologo "-getProperty:$Name").Trim()
    if (0 -ne $LASTEXITCODE) {
        throw "Unable to read MSBuild property '$Name' from '$ProjectPath'."
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "MSBuild property '$Name' is empty in '$ProjectPath'."
    }

    return $value
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
    $packageId = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'PackageId'
    $packageVersion = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'PackageVersion'
    $targetFramework = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'TargetFramework'
    $toolCommandName = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'ToolCommandName'
    $assemblyName = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'AssemblyName'
    $packageReadme = Get-MSBuildProperty -ProjectPath $routerProjectPath -Name 'PackageReadmeFile'

    if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
        $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
    }
    $ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
    if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
        throw "Artifact directory '$ArtifactDirectory' does not exist."
    }

    $packagePath = Join-Path $ArtifactDirectory "$packageId.$packageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Package '$packagePath' was not produced by the pack stage."
    }

    Write-Host ''
    Write-Host "=== Verify exact package artifact ($Configuration) ==="
    Write-Host "Package: $packagePath"

    $toolRoot = "tools/$targetFramework/any"
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

        $nuspecPackageId = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        $nuspecVersion = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        if ($packageId -ne $nuspecPackageId) {
            throw "Package ID '$nuspecPackageId' does not match expected ID '$packageId'."
        }
        if ($packageVersion -ne $nuspecVersion) {
            throw "Package version '$nuspecVersion' does not match expected version '$packageVersion'."
        }

        $readmeNode = $metadata.SelectSingleNode("*[local-name()='readme']")
        if ($null -eq $readmeNode -or $packageReadme -ne $readmeNode.InnerText.Trim().Replace('\', '/')) {
            throw "Package must declare '$packageReadme' as its NuGet readme."
        }

        $requiredEntries = @(
            $packageReadme,
            'procps/README.md',
            "$toolRoot/DotnetToolSettings.xml",
            "$toolRoot/$assemblyName.dll",
            "$toolRoot/free.dll",
            "$toolRoot/pgrep.dll",
            "$toolRoot/pidof.dll",
            "$toolRoot/pidwait.dll",
            "$toolRoot/pkill.dll",
            "$toolRoot/pmap.dll",
            "$toolRoot/ps.dll",
            "$toolRoot/pwdx.dll",
            "$toolRoot/slabtop.dll",
            "$toolRoot/hugetop.dll",
            "$toolRoot/sysctl.dll",
            "$toolRoot/tload.dll",
            "$toolRoot/top.dll",
            "$toolRoot/uptime.dll",
            "$toolRoot/vmstat.dll",
            "$toolRoot/w.dll",
            "$toolRoot/watch.dll"
        )
        foreach ($entryPath in $requiredEntries) {
            if (-not ($archive.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1)) {
                throw "Package does not contain required entry '$entryPath'."
            }
        }

        $readmeEntry = $archive.Entries | Where-Object { $_.FullName -eq $packageReadme } | Select-Object -First 1
        $packagedReadme = Get-ZipEntryText -Entry $readmeEntry
        $repositoryReadme = [System.IO.File]::ReadAllText($repositoryReadmePath)
        if ($repositoryReadme -ne $packagedReadme) {
            throw "Packaged '$packageReadme' does not exactly match the repository README.md."
        }

        $toolSettingsEntry = $archive.Entries | Where-Object { $_.FullName -eq "$toolRoot/DotnetToolSettings.xml" } | Select-Object -First 1
        [xml]$toolSettings = Get-ZipEntryText -Entry $toolSettingsEntry
        $commands = @($toolSettings.DotNetCliTool.Commands.Command)
        if (1 -ne $commands.Count) {
            throw "Package declares $($commands.Count) tool commands; expected exactly one."
        }
        if ($toolCommandName -ne "$($commands[0].Name)" -or 'dotnet' -ne "$($commands[0].Runner)") {
            throw "Package tool settings do not declare the expected '$toolCommandName'/dotnet command."
        }
    } finally {
        $archive.Dispose()
    }

    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "$packageId-package-smoke-$([Guid]::NewGuid().ToString('N'))"
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
            'tool', 'install', $packageId,
            '--version', $packageVersion,
            '--tool-path', $toolPath,
            '--configfile', $nugetConfigPath,
            '--no-cache'
        )

        $shimName = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
            "$toolCommandName.exe"
        } else {
            $toolCommandName
        }
        $routerShim = Join-Path $toolPath $shimName
        if (-not (Test-Path -LiteralPath $routerShim -PathType Leaf)) {
            throw "Installed tool shim '$routerShim' was not created."
        }

        Invoke-Tool -Path $routerShim -Arguments @('--version') -ExpectedOutput "$toolCommandName ($packageId) $packageVersion"

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
