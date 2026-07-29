# Getting started

Installing LedManager is a single **installer**: download, run, activate. Five minutes, tops.

## Before you begin

You will need:

- a working **RetroBat** installation;
- the **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe)** plugin installed and running — it feeds game data to LedManager;
- the **[.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**;
- an LED panel: a wired Raspberry Pi Pico (see [Hardware](materiel.md)) or a compatible board (see [External LED boards](cartes-externes.md)).

## Installation

1. Download **[`LedManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Led-Manager/releases/latest/download/LedManager-Setup.exe)** from the releases page.
2. Run the installer: it installs the plugin into `RetroBat\plugins\` and registers the EmulationStation start hook — you get:

    ```text
    RetroBat\plugins\LedManager\
    ```

3. Start RetroBat as usual: LedManager now starts automatically with EmulationStation.

!!! note "What does the hook do?"
    The installer simply registers this file on the EmulationStation side, without touching anything else in RetroBat:

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
