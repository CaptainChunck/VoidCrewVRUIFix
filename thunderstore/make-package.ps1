# Builds the Thunderstore package, dist\Void_Crew_VR_UI_Fix-<version>.zip, from the Release build of the plugin:
# manifest.json, icon.png, README.md and CHANGELOG.md at the root of the zip, and the DLL in plugins\.
# Build the plugin in Release first, then run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File thunderstore\make-package.ps1
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path -Parent $here

$manifest = Get-Content (Join-Path $here "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $manifest.version_number
if ($manifest.name -notmatch '^[A-Za-z0-9_]+$') { throw "manifest.json name '$($manifest.name)' may only use letters, digits and underscores." }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "manifest.json version_number '$version' must be Major.Minor.Patch." }
if ($manifest.description.Length -gt 250) { throw "manifest.json description is $($manifest.description.Length) characters; Thunderstore allows 250." }

$dll = Join-Path $root "VoidCrewVRUIFix\bin\Release\VoidCrewVRUIFix.dll"
if (-not (Test-Path $dll)) { throw "Build the plugin in Release first: $dll is missing." }
$dllVersion = (Get-Item $dll).VersionInfo.FileVersion
if ($dllVersion -ne "$version.0") { throw "The DLL is version $dllVersion but manifest.json says $version. Update one of them." }

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $here "icon.png"))
$iconSize = "$($icon.Width) x $($icon.Height)"
$icon.Dispose()
if ($iconSize -ne "256 x 256") { throw "icon.png is $iconSize; Thunderstore needs 256 x 256." }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zipPath = Join-Path $dist ("Void_Crew_VR_UI_Fix-" + $version + ".zip")
if (Test-Path $zipPath) { Remove-Item $zipPath }

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    # Entry names with forward slashes, which every mod manager reads correctly.
    $files = [ordered]@{
        "manifest.json" = Join-Path $here "manifest.json"
        "icon.png" = Join-Path $here "icon.png"
        "README.md" = Join-Path $here "README.md"
        "CHANGELOG.md" = Join-Path $root "CHANGELOG.md"
        "plugins/VoidCrewVRUIFix.dll" = $dll
    }
    foreach ($name in $files.Keys) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$name], $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}
Write-Output ("Thunderstore package: " + $zipPath)
