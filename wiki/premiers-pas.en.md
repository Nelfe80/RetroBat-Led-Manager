# Getting started

Installing LedManager requires **no installer**: download, extract, activate. Five minutes, tops.

## Before you begin

You will need:

- a working **RetroBat** installation;
- the **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases)** plugin installed and running — it feeds game data to LedManager;
- the **[.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**;
- an LED panel: a wired Raspberry Pi Pico (see [Hardware](materiel.md)) or a compatible board (see [External LED boards](cartes-externes.md)).

## Installation

1. Download **`LedManager-x.y.z-full.7z`** from the [releases page](https://github.com/Nelfe80/RetroBat-Led-Manager/releases).
2. Extract the archive into your `RetroBat\plugins\` folder — you get:

    ```text
    RetroBat\plugins\LedManager\
    ```

3. Close RetroBat if it is running, then double-click **`install-es-start-hook.bat`**. A window confirms the hook installation.
4. Start RetroBat as usual: LedManager now starts automatically with EmulationStation.

!!! note "What does the hook do?"
    It simply installs this file on the EmulationStation side, without touching anything else in RetroBat:

    ```text
    emulationstation\.emulationstation\scripts\start\LedManager-start.bat
    ```

## Check that it works

When RetroBat starts, your buttons should light up after a few seconds (the Pico needs time to initialize). Browse the systems: colors change with each system's panel. Launch an arcade game with lamps (e.g. `seawolf` under MAME) to see native outputs come alive.

If nothing lights up, head to [Troubleshooting](depannage.md) — the first reflex is to check the COM port in `PicoCommandSender.ini`.

## Stop or uninstall

| Action | How |
|---|---|
| Stop LedManager (and its senders) | Double-click `stop.bat` |
| Remove the automatic startup | Double-click `uninstall-es-start-hook.bat` |
| Uninstall completely | Remove the hook, then delete the `LedManager` folder |

!!! tip "Updating"
    To update, replace the folder contents with the new archive — back up your customized `.ini` files first.
