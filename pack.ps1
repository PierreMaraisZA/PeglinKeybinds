<#
.SYNOPSIS
    Builds the Release DLL and the Thunderstore package (dist\PierreMarais-PeglinKeybinds-<version>.zip).

.DESCRIPTION
    1. Checks the versions in src\Plugin.cs and PeglinKeybinds.csproj match thunderstore\manifest.json, and that
       CHANGELOG.md has a section for that version.
    2. Runs the unit tests.
    3. Builds Release (this also copies the DLL into the game's BepInEx\plugins folder).
    4. Zips manifest.json, icon.png, README.md, CHANGELOG.md and the DLL, at the top level of the zip.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File pack.ps1
    powershell -ExecutionPolicy Bypass -File pack.ps1 -GamePath "D:\Games\Peglin"
#>
param(
    # Peglin install folder, if it isn't the default Steam location.
    [string]$GamePath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$team = 'PierreMarais'

$manifest = Get-Content (Join-Path $root 'thunderstore\manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number

# 1. Versions must agree, or Thunderstore and the in-game log would disagree about what's installed.
$pluginVersion = (Select-String -Path (Join-Path $root 'src\Plugin.cs') -Pattern 'const string Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($pluginVersion -ne $version) {
    throw "Version mismatch: src\Plugin.cs says $pluginVersion but thunderstore\manifest.json says $version."
}
$projectVersion = ([xml](Get-Content (Join-Path $root 'PeglinKeybinds.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($projectVersion -ne $version) {
    throw "Version mismatch: PeglinKeybinds.csproj says $projectVersion but thunderstore\manifest.json says $version."
}
if (-not (Select-String -Path (Join-Path $root 'CHANGELOG.md') -Pattern "^## $([regex]::Escape($version))\s*$" -Quiet)) {
    throw "CHANGELOG.md has no '## $version' section."
}
Write-Host "Packing $($manifest.name) $version" -ForegroundColor Cyan

# 2. Tests.
dotnet test (Join-Path $root 'tests\PeglinKeybinds.Tests.csproj') -nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not packing.' }

# 3. Release build.
$buildArgs = @((Join-Path $root 'PeglinKeybinds.csproj'), '-c', 'Release', '-nologo', '--verbosity', 'quiet')
if ($GamePath) { $buildArgs += "-p:GamePath=$GamePath" }
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$dll = Join-Path $root 'bin\Release\net472\PeglinKeybinds.dll'


# 4. Stage and zip. Files must sit at the top level of the zip, not inside a folder.
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $root 'thunderstore\manifest.json'), (Join-Path $root 'icon.png'), (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md'), $dll -Destination $stage

$zip = Join-Path $dist "$team-$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Package ready: $zip" -ForegroundColor Green
