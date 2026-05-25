param(
    [Parameter(Mandatory = $true)]
    [string]$Port,

    [int]$Baud = 921600
)

$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    Write-Host "[AnyPTZ] Flashing ESP32 on port $Port with baud $Baud"

    $esptool = Get-Command esptool.py -ErrorAction SilentlyContinue
    if (-not $esptool) {
        $esptool = Get-Command esptool -ErrorAction SilentlyContinue
    }
    if (-not $esptool) {
        throw "esptool not found. Install it: python -m pip install --upgrade esptool"
    }

    $toolPath = $esptool.Source

    & $toolPath --chip esp32 --port $Port erase_flash

    & $toolPath --chip esp32 --port $Port --baud $Baud --before default_reset --after hard_reset write_flash -z --flash_mode dio --flash_freq 40m --flash_size detect `
        0x1000 bootloader.bin `
        0x8000 partitions.bin `
        0x10000 firmware.bin `
        0x310000 littlefs.bin

    Write-Host "[AnyPTZ] Flash completed successfully."
}
finally {
    Pop-Location
}
