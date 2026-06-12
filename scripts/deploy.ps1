param(
    [string] $Configuration = "Release",
    [string] $GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")

& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -GameDir $GameDir

$SourceDll = Join-Path $Root "src\LostCastle2.CrispChineseFont\bin\$Configuration\LostCastle2.CrispChineseFont.dll"
$PluginDir = Join-Path $GameDir "BepInEx\plugins\LostCastle2.CrispChineseFont"
New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item -LiteralPath $SourceDll -Destination (Join-Path $PluginDir "LostCastle2.CrispChineseFont.dll") -Force

$FontSourceDir = Join-Path $Root "assets\fonts"
if (Test-Path -LiteralPath $FontSourceDir) {
    $FontTargetDir = Join-Path $PluginDir "fonts"
    New-Item -ItemType Directory -Force -Path $FontTargetDir | Out-Null
    Copy-Item -Path (Join-Path $FontSourceDir "*") -Destination $FontTargetDir -Force
}

Write-Host "Deployed to $PluginDir"
