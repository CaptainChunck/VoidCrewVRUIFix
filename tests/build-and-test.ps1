# Builds the plugin (Release) and runs the offline checks in tests\OfflineChecks, without starting the game:
#  - applies the plugin's Harmony patches to the real game, HDRP and UUVR assemblies,
#  - resolves every game, Unity, HDRP and UUVR member the plugin's code uses,
#  - runs the plugin's by-name lookups against the game's types,
#  - checks the defaults a new install gets and the VR tab's RESTORE (tests\OfflineChecks\tested-setup.cfg).
# Prints ALL CHECKS PASSED and exits with 0 when everything is fine.
# Needs Visual Studio 2022 or newer (or its Build Tools), Void Crew, and UUVR installed with Rai Pal. It only reads the
# game's and Rai Pal's folders.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tests\build-and-test.ps1
#      [-VoidCrewDir <Void Crew folder>] [-BepInExDir <Rai Pal's BepInEx folder for Void Crew>] [-SkipBuild]
param(
    [string]$VoidCrewDir = "G:\SteamLibrary\steamapps\common\Void Crew",
    [string]$BepInExDir = (Join-Path $env:APPDATA "raicuparta\rai-pal\data\installed-mods\3743495372862092943\bepinex\BepInEx"),
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio (or its Build Tools) is needed: vswhere.exe was not found." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
$csc = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe" | Select-Object -First 1
if (-not $msbuild -or -not $csc) { throw "MSBuild or the C# compiler was not found with vswhere." }

$managed = Join-Path $VoidCrewDir "Void Crew_Data\Managed"
$core = Join-Path $BepInExDir "core"
$uuvr = Join-Path $BepInExDir "plugins\uuvr-mono-modern"
foreach ($dir in @($managed, $core, $uuvr)) {
    if (-not (Test-Path $dir)) { throw "Not found: $dir. Pass -VoidCrewDir and -BepInExDir for your PC." }
}

if (-not $SkipBuild) {
    Write-Output "Building the plugin (Release)..."
    & $msbuild (Join-Path $root "VoidCrewVRUIFix\VoidCrewVRUIFix.csproj") -p:Configuration=Release "-p:VoidCrewDir=$VoidCrewDir" "-p:BepInExDir=$BepInExDir" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
}

$tests = Join-Path $PSScriptRoot "OfflineChecks"
$out = Join-Path $tests "bin"
New-Item -ItemType Directory -Force $out | Out-Null
& $csc -nologo -target:exe -platform:x64 "-r:$(Join-Path $core 'Mono.Cecil.dll')" "-out:$(Join-Path $out 'PatchTest.exe')" (Join-Path $tests "PatchTest.cs")
if ($LASTEXITCODE -ne 0) { throw "The offline checks did not compile." }
Copy-Item (Join-Path $tests "tested-setup.cfg") $out -Force

Write-Output "Running the offline checks..."
& (Join-Path $out "PatchTest.exe") (Join-Path $root "VoidCrewVRUIFix\bin\Release") $core $managed $uuvr
exit $LASTEXITCODE
