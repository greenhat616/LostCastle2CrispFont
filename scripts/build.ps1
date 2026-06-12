param(
    [string] $Configuration = "Release",
    [string] $GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$CoreDir = Join-Path $GameDir "BepInEx\core"
$InteropDir = Join-Path $GameDir "BepInEx\interop"

if (-not (Test-Path -LiteralPath (Join-Path $CoreDir "BepInEx.Unity.IL2CPP.dll"))) {
    throw "BepInEx IL2CPP core files were not found. Install BepInEx 6 IL2CPP into '$GameDir' first."
}

if (-not (Test-Path -LiteralPath (Join-Path $InteropDir "Unity.TextMeshPro.dll"))) {
    throw "BepInEx interop assemblies were not found. Start the game once after installing BepInEx so it can generate BepInEx\interop."
}

dotnet build (Join-Path $Root "LostCastle2.CrispChineseFont.slnx") `
    -c $Configuration `
    /p:GameDir="$GameDir"
