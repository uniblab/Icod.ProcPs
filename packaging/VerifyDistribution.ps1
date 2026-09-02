param(
    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Project,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    foreach ($group in $Project.Project.PropertyGroup) {
        $property = $group.SelectSingleNode($Name)
        if ($null -ne $property -and 0 -lt $property.InnerText.Length) {
            return $property.InnerText
        }
    }

    throw "Project property '$Name' was not found."
}

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

function Get-ExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    $fileName = if ($IsWindowsPlatform) {
        "$CommandName.exe"
    } else {
        $CommandName
    }

    $path = Join-Path $Directory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected executable '$path' was not created."
    }

    return $path
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string[]]$Arguments = @(),

        [int]$ExpectedExitCode = 0,

        [string]$ExpectedOutput = ''
    )

    Write-Host "> $Path $($Arguments -join ' ')"
    $output = @(& $Path @Arguments)
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host $line
    }
    if ($ExpectedExitCode -ne $exitCode) {
        throw "Tool '$Path' exited with status $exitCode; expected $ExpectedExitCode."
    }
    if (0 -lt $ExpectedOutput.Length -and ($output -join "`n").Trim() -ne $ExpectedOutput) {
        throw "Tool '$Path' reported '$($output -join ' ')'; expected '$ExpectedOutput'."
    }
}

function Read-ToolSettingsFromPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$TargetFramework
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $settingsPath = "tools/$TargetFramework/any/DotnetToolSettings.xml"
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $settingsPath } | Select-Object -First 1
        if ($null -eq $entry) {
            throw "Package '$PackagePath' does not contain '$settingsPath'."
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            [xml]$settings = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }

        $commands = @($settings.DotNetCliTool.Commands.Command)
        if (0 -eq $commands.Count) {
            throw "Package '$PackagePath' declares no .NET tool commands."
        }

        return @{
            Archive = $archive
            Commands = $commands
        }
    } catch {
        $archive.Dispose()
        throw
    }
}

function Assert-ToolPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$TargetFramework,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCommand,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedReadmeEntry,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedReadmeSourcePath,

        [Parameter(Mandatory = $true)]
        [string[]]$RequiredAssemblies,

        [string[]]$RequiredPackageEntries = @()
    )

    $result = Read-ToolSettingsFromPackage -PackagePath $PackagePath -TargetFramework $TargetFramework
    $archive = $result.Archive
    try {
        if (1 -ne $result.Commands.Count) {
            throw "Package '$PackagePath' declares $($result.Commands.Count) commands; expected exactly one."
        }

        $command = $result.Commands[0]
        if ($ExpectedCommand -ne "$($command.Name)") {
            throw "Package '$PackagePath' declares command '$($command.Name)'; expected '$ExpectedCommand'."
        }
        if ('dotnet' -ne "$($command.Runner)") {
            throw "Command '$($command.Name)' in '$PackagePath' uses unexpected runner '$($command.Runner)'."
        }

        foreach ($assembly in $RequiredAssemblies) {
            $entryPath = "tools/$TargetFramework/any/$assembly"
            if (-not ($archive.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1)) {
                throw "Package '$PackagePath' does not contain '$entryPath'."
            }
        }

        foreach ($entryPath in $RequiredPackageEntries) {
            if (-not ($archive.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1)) {
                throw "Package '$PackagePath' does not contain '$entryPath'."
            }
        }

        $nuspecEntries = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.EndsWith(
                        '.nuspec',
                        [System.StringComparison]::OrdinalIgnoreCase
                    )
                }
        )
        if (1 -ne $nuspecEntries.Count) {
            throw "Package '$PackagePath' contains $($nuspecEntries.Count) nuspec files; expected exactly one."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }

        $readmeNode = $nuspec.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='readme']"
        )
        if ($null -eq $readmeNode -or [string]::IsNullOrWhiteSpace($readmeNode.InnerText)) {
            throw "Package '$PackagePath' does not declare NuGet readme metadata."
        }

        $readmeEntryPath = $readmeNode.InnerText.Trim().Replace('\', '/')
        if ($ExpectedReadmeEntry -ne $readmeEntryPath) {
            throw "Package '$PackagePath' declares readme '$readmeEntryPath'; expected '$ExpectedReadmeEntry'."
        }

        $readmeEntry = $archive.Entries |
            Where-Object { $_.FullName -eq $readmeEntryPath } |
            Select-Object -First 1
        if ($null -eq $readmeEntry) {
            throw "Package '$PackagePath' does not contain declared readme '$readmeEntryPath'."
        }

        $reader = [System.IO.StreamReader]::new($readmeEntry.Open())
        try {
            $actualReadme = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }

        $expectedReadme = [System.IO.File]::ReadAllText($ExpectedReadmeSourcePath)
        if ($expectedReadme -ne $actualReadme) {
            throw "Package '$PackagePath' readme does not match '$ExpectedReadmeSourcePath'."
        }
    } finally {
        $archive.Dispose()
    }
}

function Write-LocalNuGetConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $escapedPath = [System.Security.SecurityElement]::Escape($PackageDirectory)
    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedPath" />
  </packageSources>
</configuration>
"@

    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$suitePropertiesPath = Join-Path $repositoryRoot 'Directory.Build.props'
$routerProjectPath = Join-Path $repositoryRoot 'procps/Icod.ProcPs.Router.csproj'
$solutionPath = Join-Path $repositoryRoot 'Icod.ProcPs.sln'
$commandNames = @(
    'free',
    'pgrep',
    'pidof',
    'pidwait',
    'pkill',
    'pmap',
    'ps',
    'pwdx',
    'slabtop',
    'hugetop',
    'sysctl',
    'tload',
    'top',
    'uptime',
    'vmstat',
    'w',
    'watch'
)

$productNames = [ordered]@{
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

[xml]$suiteProperties = Get-Content -LiteralPath $suitePropertiesPath -Raw
[xml]$routerProject = Get-Content -LiteralPath $routerProjectPath -Raw
$targetFramework = Get-ProjectProperty -Project $routerProject -Name 'TargetFramework'
$routerPackageId = Get-ProjectProperty -Project $routerProject -Name 'PackageId'
$routerPackageReadme = Get-ProjectProperty -Project $routerProject -Name 'PackageReadmeFile'
$routerVersion = Get-ProjectProperty -Project $suiteProperties -Name 'IcodProcPsSuiteVersion'
$repositoryReadmePath = Join-Path $repositoryRoot 'README.md'

$validationRoot = Join-Path $repositoryRoot 'artifacts/distribution-validation'
$packageDirectory = Join-Path $validationRoot 'packages'
$routerToolPath = Join-Path $validationRoot 'router-tool'
$nugetConfigPath = Join-Path $validationRoot 'NuGet.Config'
$standaloneOutputPath = Join-Path $repositoryRoot "bin/$Configuration/$targetFramework"

if (Test-Path -LiteralPath $validationRoot) {
    Remove-Item -LiteralPath $validationRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-DotNet -Arguments @('restore', $solutionPath)
    Invoke-DotNet -Arguments @(
        'build',
        $solutionPath,
        '-c', $Configuration,
        '--no-restore',
        '-p:ContinuousIntegrationBuild=true'
    )
    Invoke-DotNet -Arguments @(
        'test',
        $solutionPath,
        '-c', $Configuration,
        '--no-build',
        '--no-restore',
        '--logger', 'trx'
    )

    foreach ($commandName in $commandNames) {
        $standaloneExecutable = Get-ExecutablePath `
            -Directory $standaloneOutputPath `
            -CommandName $commandName
        $expectedVersion = "$($productNames[$commandName]) ($routerVersion) inspired by procps-ng 4.0.6"
        Invoke-Tool `
            -Path $standaloneExecutable `
            -Arguments @('--version') `
            -ExpectedOutput $expectedVersion
    }

    Invoke-DotNet -Arguments @(
        'pack',
        $routerProjectPath,
        '-c', $Configuration,
        '--no-build',
        '--no-restore',
        '-o', $packageDirectory,
        '-p:ContinuousIntegrationBuild=true'
    )

    $routerPackagePath = Join-Path $packageDirectory "$routerPackageId.$routerVersion.nupkg"
    if (-not (Test-Path -LiteralPath $routerPackagePath -PathType Leaf)) {
        throw "Router package '$routerPackagePath' was not produced."
    }

    $commandAssemblies = @('procps.dll')
    foreach ($commandName in $commandNames) {
        $commandAssemblies += "$commandName.dll"
    }
    Assert-ToolPackage `
        -PackagePath $routerPackagePath `
        -TargetFramework $targetFramework `
        -ExpectedCommand 'procps' `
        -ExpectedReadmeEntry $routerPackageReadme `
        -ExpectedReadmeSourcePath $repositoryReadmePath `
        -RequiredAssemblies $commandAssemblies `
        -RequiredPackageEntries @('procps/README.md')

    Write-LocalNuGetConfig -PackageDirectory $packageDirectory -Path $nugetConfigPath

    Invoke-DotNet -Arguments @(
        'tool', 'install', $routerPackageId,
        '--version', $routerVersion,
        '--tool-path', $routerToolPath,
        '--configfile', $nugetConfigPath,
        '--no-cache'
    )

    $routerShim = Get-ExecutablePath -Directory $routerToolPath -CommandName 'procps'
    Invoke-Tool `
        -Path $routerShim `
        -Arguments @('--version') `
        -ExpectedOutput "procps (Icod.ProcPs) $routerVersion"
    foreach ($commandName in $commandNames) {
        $expectedVersion = "$($productNames[$commandName]) ($routerVersion) inspired by procps-ng 4.0.6"
        Invoke-Tool `
            -Path $routerShim `
            -Arguments @($commandName, '--version') `
            -ExpectedOutput $expectedVersion
    }

    Write-Host ''
    Write-Host 'Distribution verification completed successfully.'
    Write-Host "  Tool package: $routerPackagePath"
    Write-Host "  Standalone executables: $standaloneOutputPath"
} finally {
    Pop-Location
}
