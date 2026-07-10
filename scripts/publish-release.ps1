# Before running this script for a new release: confirm docs/HOW_TO_USE.pdf and its source
# markdown are the latest edited versions. The improved HOW_TO_USE.pdf as of 2026-07-10 was
# authored on a different machine than the one that normally runs this script — pull that
# updated file into this checkout first, or this script will silently bundle a stale one.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'PenumbraOrganizer.App\PenumbraOrganizer.App.csproj'
$releaseRoot = Join-Path $root 'artifacts\release'
$publishDir = Join-Path $releaseRoot 'publish'
$packageDir = Join-Path $releaseRoot 'package'
$zipPath = Join-Path $releaseRoot 'PenumbraOrganizer-v0.3.3-beta-win-x64.zip'
$exePath = Join-Path $publishDir 'PenumbraOrganizer.exe'

if (Test-Path $releaseRoot) {
    Remove-Item -Recurse -Force $releaseRoot
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

if (-not (Test-Path $exePath)) {
    throw "Publish failed: PenumbraOrganizer.exe was not created."
}

Copy-Item $exePath $packageDir
Copy-Item (Join-Path $root 'README_FOR_USERS.txt') $packageDir
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.txt') $packageDir
Copy-Item (Join-Path $root 'LICENSE') $packageDir

# Reminder: this copies whatever docs/HOW_TO_USE.pdf is on THIS machine right now. See the header
# comment at the top of this file before proceeding if you're not sure it's current.
$howToUsePdf = Join-Path $root 'docs\HOW_TO_USE.pdf'
if (-not (Test-Path $howToUsePdf)) {
    throw "How-to-use PDF not found at $howToUsePdf. Run docs\build_how_to_use_pdf.py first."
}
Copy-Item $howToUsePdf $packageDir

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath

$hashes = @()
foreach ($file in @($zipPath, (Join-Path $packageDir 'PenumbraOrganizer.exe'))) {
    $hash = Get-FileHash $file -Algorithm SHA256
    $hashes += "{0} *{1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $file)
}
$hashes | Set-Content -Path (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Release package created:"
Write-Host "  $zipPath"
Write-Host "Checksums:"
Write-Host "  $(Join-Path $releaseRoot 'SHA256SUMS.txt')"
