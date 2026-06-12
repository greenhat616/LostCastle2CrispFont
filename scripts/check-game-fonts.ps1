param(
    [string] $GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
)

$ErrorActionPreference = "Stop"
$PackageDir = Join-Path $GameDir "LostCastle2_Data\StreamingAssets\yoo\DefaultPackage"
$CurrentVersion = Get-Content -LiteralPath (Join-Path $PackageDir "DefaultPackage.version")
$Manifest = Join-Path $PackageDir "DefaultPackage_$CurrentVersion.bytes"

Write-Host "Current YooAsset manifest: $Manifest"
Write-Host "Known font bundles in current build:"
rg -a -i -o "defaultpackage_font[A-Za-z0-9_&(). -]+\.bundle|Alibaba[-A-Za-z0-9_ &().]+|AlibabaPuHuiTi[-A-Za-z0-9_ &().]+|SourceHanSansCN[-A-Za-z0-9_ &().]+" $Manifest |
    Sort-Object -Unique
