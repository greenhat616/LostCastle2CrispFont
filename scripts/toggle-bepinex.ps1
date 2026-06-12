param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("enable", "disable")]
    [string] $Mode,
    [string] $GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
)

$ErrorActionPreference = "Stop"
$ConfigPath = Join-Path $GameDir "doorstop_config.ini"
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "doorstop_config.ini was not found in '$GameDir'."
}

$EnabledValue = if ($Mode -eq "enable") { "true" } else { "false" }
$Text = Get-Content -LiteralPath $ConfigPath -Raw
$Text = [regex]::Replace($Text, "(?m)^enabled\s*=\s*(true|false)\s*$", "enabled = $EnabledValue")
Set-Content -LiteralPath $ConfigPath -Value $Text -Encoding UTF8

Write-Host "Doorstop/BepInEx is now $Mode`d in $ConfigPath"
