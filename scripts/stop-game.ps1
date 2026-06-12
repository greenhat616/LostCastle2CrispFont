param(
    [string] $ProcessName = "LostCastle2"
)

$ErrorActionPreference = "Stop"
$Processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $Processes) {
    Write-Host "No $ProcessName process is running."
    exit 0
}

$Processes | ForEach-Object {
    Write-Host "Stopping $($_.ProcessName) pid=$($_.Id)"
    Stop-Process -Id $_.Id -Force
}
