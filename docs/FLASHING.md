# AnyPTZ Firmware Flashing Guide

This guide explains how to flash AnyPTZ on a **new ESP32 Lolin32 Lite** and how to update existing devices.

## Contents

- [1. Files in this repository](#1-files-in-this-repository)
- [2. First flash for a new MCU (recommended)](#2-first-flash-for-a-new-mcu-recommended)
- [3. Full manual flashing with esptool](#3-full-manual-flashing-with-esptool)
- [4. OTA update for already working devices](#4-ota-update-for-already-working-devices)
- [5. First boot after flashing](#5-first-boot-after-flashing)
- [6. Troubleshooting](#6-troubleshooting)

## 1. Files in this repository

### Full flash files (for new microcontroller)

Located in `firmware/fullflash`:

- `bootloader.bin`
- `partitions.bin`
- `firmware.bin`
- `littlefs.bin`
- `flash_full_windows.bat`
- `flash_full_windows.ps1`

### GUI uploader package

Located in `firmware/uploader`:

- `Biolumos FW Uploader.exe`
- `firmware.bin`
- `bootloader.bin`
- `partitions.bin`
- `littlefs.bin`
- `tools/esptool.exe`

### OTA files (for updates through web UI)

Located in `firmware/ota`:

- `AnyPTZ_v0.1.6_OTA_bundle_20260525_152617.bin`
- `AnyPTZ_v0.1.6_OTA_bundle_20260525_152617.ota`
- `AnyPTZ_v0.1.6_OTA_web_only_20260525_152617.bin`

### Integrity checks

- `firmware/SHA256SUMS.txt`

## 2. First flash for a new MCU (recommended)

### Windows GUI uploader

1. Connect ESP32 by USB.
2. Open `firmware/uploader`.
3. Run `Biolumos FW Uploader.exe`.
4. Select your COM port.
5. Leave the default `firmware.bin` selected.
6. Click `Flash`.

The adapted uploader flashes:

- `0x1000` -> `bootloader.bin`
- `0x8000` -> `partitions.bin`
- `0x10000` -> `firmware.bin`
- `0x310000` -> `littlefs.bin`

### Windows quick method

Prerequisites:

1. Install Python 3.
2. Install esptool:

```powershell
python -m pip install --upgrade esptool
```

3. Verify esptool is available:

```powershell
esptool.py version
```

1. Connect ESP32 by USB.
2. Open terminal in `firmware/fullflash`.
3. Run:

```powershell
.\flash_full_windows.bat COM5
```

Replace `COM5` with your real COM port.

The script will:

- erase flash
- write bootloader, partitions, firmware and LittleFS
- reset the board

## 3. Full manual flashing with esptool

Use this if you want full control or are on Linux/macOS.

### Install esptool

```bash
python -m pip install --upgrade esptool
```

### Erase flash (recommended)

```bash
esptool.py --chip esp32 --port COM5 erase_flash
```

### Write full image set

Run this command from `firmware/fullflash`:

```bash
esptool.py --chip esp32 --port COM5 --baud 921600 --before default_reset --after hard_reset write_flash -z --flash_mode dio --flash_freq 40m --flash_size detect 0x1000 bootloader.bin 0x8000 partitions.bin 0x10000 firmware.bin 0x310000 littlefs.bin
```

Flash offsets used by this release:

- `0x1000` -> bootloader
- `0x8000` -> partitions
- `0x10000` -> firmware (app)
- `0x310000` -> littlefs

## 4. OTA update for already working devices

If your AnyPTZ is already running and web UI is available:

1. Open AnyPTZ web panel.
2. Go to OTA upload section.
3. Upload one of these files from `firmware/ota`:

- `AnyPTZ_v0.1.6_OTA_bundle_20260525_152617.bin` or `.ota` for full OTA (firmware + web files)
- `AnyPTZ_v0.1.6_OTA_web_only_20260525_152617.bin` for web UI filesystem only

## 5. First boot after flashing

1. Power on AnyPTZ.
2. Device starts AP mode if Wi-Fi is not configured.
3. Connect to AnyPTZ AP network.
4. Open `http://192.168.1.1`.
5. Configure your Wi-Fi and camera profiles.

## 6. Troubleshooting

### Board not detected

- Install USB-UART drivers (CP210x/CH340 depending on your board batch).
- Try another USB cable (data cable, not charge-only).
- Check COM port in Device Manager.

### Flash failed / timed out

- Lower speed to 115200 and retry.
- Hold BOOT button while starting flash if needed.
- Run erase command and flash again.

### Device boots but web UI missing

- `littlefs.bin` was not flashed or was corrupted.
- Flash full set again (including `littlefs.bin`).

### Integrity check

To verify files before flashing, compare hashes:

```powershell
Get-ChildItem .\firmware -Recurse -File | Where-Object { $_.Extension -in '.bin', '.ota' } | Get-FileHash -Algorithm SHA256
```

Compare with values in `firmware/SHA256SUMS.txt`.
