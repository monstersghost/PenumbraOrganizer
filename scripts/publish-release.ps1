$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $root 'PenumbraOrganizer.sln'
$appProject = Join-Path $root 'PenumbraOrganizer.App\PenumbraOrganizer.App.csproj'
$releaseRoot = Join-Path $root 'artifacts\release'
$publishDir = Join-Path $releaseRoot 'publish'
$packageDir = Join-Path $releaseRoot 'package'
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PenumbraOrganizer-release-smoke-' + [guid]::NewGuid().ToString('N'))
$extractDir = Join-Path $smokeRoot 'extracted'
$logsRoot = Join-Path $env:LOCALAPPDATA 'PenumbraOrganizer\Logs'

[xml]$projectXml = Get-Content -Path $appProject
$version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not determine the application version from $appProject."
}

$zipName = "PenumbraOrganizer-v$version-win-x64.zip"
$zipPath = Join-Path $releaseRoot $zipName
$exeName = 'PenumbraOrganizer.exe'
$exePath = Join-Path $packageDir $exeName
$releaseSummaryPath = Join-Path $releaseRoot 'release-summary.json'

if (Test-Path $releaseRoot) {
    Remove-Item -Recurse -Force $releaseRoot
}
if (Test-Path $smokeRoot) {
    Remove-Item -Recurse -Force $smokeRoot
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

dotnet build $solutionPath -c Release
dotnet test $solutionPath -c Release --no-build
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -o $publishDir

Get-ChildItem -Path $publishDir -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $packageDir $_.Name) -Force
}

Get-ChildItem -Path $publishDir -Directory | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $packageDir $_.Name) -Recurse -Force
}

Copy-Item (Join-Path $root 'README_FOR_USERS.txt') $packageDir -Force
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.txt') $packageDir -Force
Copy-Item (Join-Path $root 'LICENSE') $packageDir -Force

if (-not (Test-Path $exePath)) {
    throw "Publish failed: $exeName was not created in the package directory."
}

$userFacingFiles = @(
    Join-Path $packageDir 'README_FOR_USERS.txt'
    Join-Path $packageDir 'THIRD_PARTY_NOTICES.txt'
    Join-Path $packageDir 'LICENSE'
)
foreach ($file in $userFacingFiles) {
    if (-not (Test-Path $file)) {
        throw "Required packaged file is missing: $file"
    }

    $content = Get-Content -Raw -Path $file
    if ($content -match 'C:\\Users\\' -or $content -match [regex]::Escape($env:APPDATA)) {
        throw "User-facing file contains a machine-specific absolute path: $file"
    }
}

if (Get-ChildItem -Path $packageDir -Recurse -File | Where-Object {
    $_.FullName -match '\\AppData\\Roaming\\XIVLauncher\\pluginConfigs\\Penumbra\\' -or
    $_.Name -eq 'mod_data.db'
}) {
    throw "Package content unexpectedly includes live Penumbra configuration data."
}

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath

Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
$extractedExe = Join-Path $extractDir $exeName
if (-not (Test-Path $extractedExe)) {
    throw "Smoke-test extraction failed: $exeName was not found in the extracted package."
}

$preExistingLogs = @{}
if (Test-Path $logsRoot) {
    Get-ChildItem -Path $logsRoot -Filter 'startup-*.log' -File | ForEach-Object {
        $preExistingLogs[$_.FullName] = $_.LastWriteTimeUtc
    }
}

$process = Start-Process -FilePath $extractedExe -WorkingDirectory $extractDir -PassThru -WindowStyle Hidden
$smokePassed = $false
$smokeMessage = 'Startup did not reach the completion checkpoint.'
$latestLog = $null

try {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500

        if (Test-Path $logsRoot) {
            $latestLog = Get-ChildItem -Path $logsRoot -Filter 'startup-*.log' -File |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
        }

        if ($latestLog -and (-not $preExistingLogs.ContainsKey($latestLog.FullName) -or $latestLog.LastWriteTimeUtc -gt $preExistingLogs[$latestLog.FullName])) {
            $logContent = Get-Content -Raw -Path $latestLog.FullName
            if ($logContent -match 'ERROR:' -or $logContent -match 'startup failed') {
                throw "Startup log reported a fatal error: $($latestLog.FullName)"
            }
            if ($logContent -match 'STAGE: startup completed') {
                $smokePassed = $true
                $smokeMessage = "Startup completed successfully. Log: $($latestLog.FullName)"
                break
            }
        }

        if ($process.HasExited) {
            if ($process.ExitCode -ne 0) {
                throw "Smoke-test launch exited with code $($process.ExitCode)."
            }
        }
    } while ((Get-Date) -lt $deadline)
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
    $process.Dispose()
}

if (-not $smokePassed) {
    throw $smokeMessage
}

$zipInfo = Get-Item $zipPath
$summary = [ordered]@{
    version = $version
    zipPath = $zipPath
    zipSizeBytes = $zipInfo.Length
    publishDirectory = $publishDir
    packageDirectory = $packageDir
    extractedDirectory = $extractDir
    smokeTest = [ordered]@{
        succeeded = $smokePassed
        message = $smokeMessage
        logPath = if ($latestLog) { $latestLog.FullName } else { $null }
    }
}

$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $releaseSummaryPath -Encoding utf8

$hashes = @()
foreach ($file in @($zipPath, $extractedExe)) {
    $hash = Get-FileHash $file -Algorithm SHA256
    $hashes += "{0} *{1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $file)
}
$hashes | Set-Content -Path (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Release package created:"
Write-Host "  $zipPath"
Write-Host "Smoke test:"
Write-Host "  $smokeMessage"
Write-Host "Summary:"
Write-Host "  $releaseSummaryPath"
