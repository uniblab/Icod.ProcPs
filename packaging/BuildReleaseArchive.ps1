param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)
$IsLinuxPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Linux
)
$IsMacOSPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX
)

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

function Get-ExecutableFileName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [Parameter(Mandatory = $true)]
        [string]$Rid
    )

    if ($Rid.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
        return "$CommandName.exe"
    }

    return $CommandName
}

function Get-CurrentRuntimeIdentifier {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $architectureName = if ([System.Runtime.InteropServices.Architecture]::X64 -eq $architecture) {
        'x64'
    } elseif ([System.Runtime.InteropServices.Architecture]::Arm64 -eq $architecture) {
        'arm64'
    } else {
        return ''
    }

    if ($IsWindowsPlatform) {
        return "win-$architectureName"
    }
    if ($IsLinuxPlatform) {
        return "linux-$architectureName"
    }
    if ($IsMacOSPlatform) {
        return "osx-$architectureName"
    }

    return ''
}

function Invoke-Executable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ExpectedOutput = ''
    )

    Write-Host "> $Path --version"
    $output = @(& $Path --version)
    if (0 -ne $LASTEXITCODE) {
        throw "Executable '$Path' exited with status $LASTEXITCODE."
    }
    foreach ($line in $output) {
        Write-Host $line
    }

    if (0 -lt $ExpectedOutput.Length -and ($output -join "`n").Trim() -ne $ExpectedOutput) {
        throw "Executable '$Path' reported '$($output -join ' ')'; expected '$ExpectedOutput'."
    }
}

function Assert-ArchiveContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [string]$RootDirectoryName,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedFileNames
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\\', '/') })
        foreach ($fileName in $ExpectedFileNames) {
            $expectedEntry = "$RootDirectoryName/$fileName"
            if ($entries -notcontains $expectedEntry) {
                throw "Archive '$ArchivePath' does not contain '$expectedEntry'."
            }
        }
    } finally {
        $archive.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw 'RuntimeIdentifier must not be empty.'
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Version must not be empty.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot 'artifacts/release'
$publishRoot = Join-Path $releaseRoot "publish/$RuntimeIdentifier"
$stageDirectoryName = "Icod.ProcPs-$Version-$RuntimeIdentifier"
$stageParent = Join-Path $releaseRoot 'stage'
$stageDirectory = Join-Path $stageParent $stageDirectoryName
$archivePath = Join-Path $releaseRoot "$stageDirectoryName.zip"
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

$projects = [ordered]@{
    'free' = 'free/Icod.ProcPs.Free.csproj'
    'pgrep' = 'pgrep/Icod.ProcPs.Pgrep.csproj'
    'pidof' = 'pidof/Icod.ProcPs.PidOf.csproj'
    'pidwait' = 'pidwait/Icod.ProcPs.PidWait.csproj'
    'pkill' = 'pkill/Icod.ProcPs.Pkill.csproj'
    'pmap' = 'pmap/Icod.ProcPs.Pmap.csproj'
    'ps' = 'ps/Icod.ProcPs.Ps.csproj'
    'pwdx' = 'pwdx/Icod.ProcPs.Pwdx.csproj'
    'sysctl' = 'sysctl/Icod.ProcPs.Sysctl.csproj'
    'uptime' = 'uptime/Icod.ProcPs.Uptime.csproj'
    'vmstat' = 'vmstat/Icod.ProcPs.Vmstat.csproj'
    'w' = 'w/Icod.ProcPs.W.csproj'
    'watch' = 'watch/Icod.ProcPs.Watch.csproj'
    'procps' = 'procps/Icod.ProcPs.Router.csproj'
}

foreach ($path in @($publishRoot, $stageDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    foreach ($commandName in $projects.Keys) {
        $projectPath = Join-Path $repositoryRoot $projects[$commandName]
        $publishDirectory = Join-Path $publishRoot $commandName
        New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

        Invoke-DotNet -Arguments @(
            'publish',
            $projectPath,
            '-c', $Configuration,
            '-r', $RuntimeIdentifier,
            '--self-contained', $selfContainedValue,
            "-p:PublishSelfContained=$selfContainedValue",
            '-p:PublishSingleFile=true',
            '-p:PublishTrimmed=false',
            '-p:DebugType=None',
            '-p:DebugSymbols=false',
            '-p:ContinuousIntegrationBuild=true',
            '-o', $publishDirectory
        )

        $executableFileName = Get-ExecutableFileName -CommandName $commandName -Rid $RuntimeIdentifier
        $publishedExecutable = Join-Path $publishDirectory $executableFileName
        if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
            throw "Publish did not produce '$publishedExecutable'."
        }

        Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $stageDirectory $executableFileName)
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $stageDirectory 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $stageDirectory 'README.md')

    $currentRid = Get-CurrentRuntimeIdentifier
    if ($RuntimeIdentifier -eq $currentRid) {
        foreach ($commandName in $projects.Keys) {
            $executableFileName = Get-ExecutableFileName -CommandName $commandName -Rid $RuntimeIdentifier
            $stagedExecutable = Join-Path $stageDirectory $executableFileName
            if (-not $IsWindowsPlatform) {
                & chmod +x $stagedExecutable
                if (0 -ne $LASTEXITCODE) {
                    throw "chmod failed for '$stagedExecutable'."
                }
            }
            $expectedOutput = if ('procps' -eq $commandName) {
                "procps (Icod.ProcPs) $Version"
            } else {
                ''
            }
            Invoke-Executable -Path $stagedExecutable -ExpectedOutput $expectedOutput
        }
    } else {
        Write-Host "Skipping executable smoke tests because host RID '$currentRid' does not match '$RuntimeIdentifier'."
    }

    if ($RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
        Compress-Archive -LiteralPath $stageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    } else {
        $zipCommand = Get-Command zip -ErrorAction SilentlyContinue
        if ($null -eq $zipCommand) {
            throw "The 'zip' command is required to preserve executable permissions for '$RuntimeIdentifier' archives."
        }

        Push-Location $stageParent
        try {
            & $zipCommand.Source -r -q $archivePath $stageDirectoryName
            if (0 -ne $LASTEXITCODE) {
                throw "zip exited with status $LASTEXITCODE."
            }
        } finally {
            Pop-Location
        }
    }

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Release archive '$archivePath' was not produced."
    }

    $expectedFileNames = @('LICENSE', 'README.md')
    foreach ($commandName in $projects.Keys) {
        $expectedFileNames += Get-ExecutableFileName -CommandName $commandName -Rid $RuntimeIdentifier
    }
    Assert-ArchiveContents `
        -ArchivePath $archivePath `
        -RootDirectoryName $stageDirectoryName `
        -ExpectedFileNames $expectedFileNames

    Write-Host ''
    Write-Host "Created release archive: $archivePath"
    Write-Host "  Runtime identifier: $RuntimeIdentifier"
    Write-Host "  Self-contained:     $selfContainedValue"
} finally {
    Pop-Location
}
