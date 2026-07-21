# Before running this script for a new release: confirm docs/HOW_TO_USE.pdf and its source
# markdown are the latest edited versions. The improved HOW_TO_USE.pdf as of 2026-07-10 was
# authored on a different machine than the one that normally runs this script — pull that
# updated file into this checkout first, or this script will silently bundle a stale one.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'PenumbraOrganizer.App\PenumbraOrganizer.App.csproj'
$updaterProject = Join-Path $root 'PenumbraOrganizer.Updater\PenumbraOrganizer.Updater.csproj'
$releaseRoot = Join-Path $root 'artifacts\release'
$appPublishDir = Join-Path $releaseRoot 'publish-app'
$updaterPublishDir = Join-Path $releaseRoot 'publish-updater'
$packageDir = Join-Path $releaseRoot 'package'
$zipPath = Join-Path $releaseRoot 'PenumbraOrganizer-v0.3.4.1-beta-win-x64.zip'
$exePath = Join-Path $appPublishDir 'PenumbraOrganizer.exe'
$updaterExePath = Join-Path $updaterPublishDir 'PenumbraOrganizer.Updater.exe'

if (Test-Path $releaseRoot) {
    Remove-Item -Recurse -Force $releaseRoot
}

New-Item -ItemType Directory -Force -Path $appPublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $updaterPublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

# PublishSingleFile is deliberately off: a self-extracting single-file exe is a strong
# heuristic trigger for AV false positives (e.g. Defender flagging v0.3.4-beta as
# Trojan:Win32/Tecabans.STV!cl on 2026-07-19). Plain multi-file self-contained publish
# triggers this far less often. App and Updater are published to separate directories
# (not merged during publish) since each is self-contained and their runtime dependency
# trees shouldn't be interleaved before the final package copy.
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $appPublishDir

if (-not (Test-Path $exePath)) {
    throw "Publish failed: PenumbraOrganizer.exe was not created."
}

Copy-Item (Join-Path $appPublishDir '*') $packageDir -Recurse -Force -Exclude '*.pdb'

dotnet publish $updaterProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $updaterPublishDir

if (-not (Test-Path $updaterExePath)) {
    throw "Publish failed: PenumbraOrganizer.Updater.exe was not created."
}

# App and Updater both self-contain the .NET runtime, and a few shared-name framework
# assemblies (WindowsBase.dll, System.Drawing.dll, Microsoft.VisualBasic.dll) resolve to
# different actual DLLs for each: App's are the real Windows Desktop (WPF) implementations,
# Updater's are stub reference assemblies (empirically confirmed 2026-07-19 -- App's
# WindowsBase.dll is ~2.2MB, Updater's is ~16KB). Blindly overwriting would silently ship a
# broken WPF runtime, so only files NOT already present from the App copy are added here.
Get-ChildItem $updaterPublishDir -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $relativePath = $_.FullName.Substring($updaterPublishDir.Length).TrimStart('\')
    $destPath = Join-Path $packageDir $relativePath
    if (-not (Test-Path $destPath)) {
        $destDir = Split-Path -Parent $destPath
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }
        Copy-Item $_.FullName $destPath
    }
}
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
foreach ($file in @($zipPath, (Join-Path $packageDir 'PenumbraOrganizer.exe'), (Join-Path $packageDir 'PenumbraOrganizer.Updater.exe'))) {
    $hash = Get-FileHash $file -Algorithm SHA256
    $hashes += "{0} *{1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $file)
}
$hashes | Set-Content -Path (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Release package created:"
Write-Host "  $zipPath"
Write-Host "Checksums:"
Write-Host "  $(Join-Path $releaseRoot 'SHA256SUMS.txt')"
