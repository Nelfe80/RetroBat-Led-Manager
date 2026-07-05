# Troubleshooting

## Nothing lights up

Check in order:

1. **The COM port** — in `PicoCommandSender.ini`, section `[Serial]`, the `Port=` value must match the port Windows assigned (Device Manager → COM ports).
2. **APIExpose is running** — LedManager has nothing to display without it. Check that the APIExpose plugin is installed and started.
3. **The firmware answers** — connect to the Pico over serial (115200 bauds) and send `VERSION`. Expected answer: `VERSION DYNAMIC_PANEL_ADDR <date>`. Otherwise, reflash with `tools\deploy-pico-fw.ps1`.
4. **The startup delay** — `StartupDelayMs` in `LedManager.ini` gives the Pico time to initialize. If it is too short, the first commands are sent into the void.

!!! tip "Testing without hardware"
    Set `DryRun=true` in the `[CommandSender:P1]` section of `LedManager.ini`: commands are logged instead of sent, letting you validate the whole logic chain without a Pico plugged in.

## LEDs freeze mid-game (COM timeout)

When Windows puts the COM port in timeout, the serial bridge **reopens it automatically** and retries the command. `PicoCommandSender.exe` then detects `SERIAL REOPENED` and replays the full panel initialization. You should not have to do anything; if it happens often, raise `WriteTimeoutMs` in `[Serial]` and check the USB cable.

## The COM port stays busy after a crash

An old process sometimes keeps `COM3` open. Two solutions:

- double-click **`stop.bat`** — it closes LedManager, its senders and the serial bridge;
- on the next start, `LedManager.exe` automatically closes older instances found under the same plugin folder.

For debugging sessions where this automatic cleanup gets in the way:

```powershell
.\LedManager.exe --no-kill-previous
```

## A color renders poorly

Not every LED renders every color faithfully. Declare a substitute in `PicoCommandSender.ini`:

```ini
[ColorPolicy.Fallbacks]
GOLD=YELLOW
```

See [Configuration — the color policy](configuration.md#the-color-policy).

## Where are the logs?

In the plugin's `.log\` folder. It is the first thing to attach when asking for help: it shows generated commands, Pico answers and any ignored commands.

## Resetting the Pico

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\reset-pico.ps1
```

Then restart LedManager (or RetroBat).
