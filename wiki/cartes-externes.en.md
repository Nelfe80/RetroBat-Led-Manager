# External LED boards

LedManager is not married to the Raspberry Pi Pico. Its principle: **it decides what to display, never how** — GPIOs, USB channels, vendor SDKs and network APIs all live in each board's *sender*.

```text
APIExpose
  → LedManager.exe
     → generic commands (SLOT 1 RED, ALL WHITE…)
        → sender adapted to your board
           → SDK / USB / serial / HTTP / UDP
```

## Two integration methods

=== "Method A — Direct templates"

    If your LED program already accepts simple text commands, only edit `[CommandTemplates]` in `LedManager.ini`:

    ```ini
    [CommandTemplates]
    SetSlot=slot {slot} {color}
    All=all {color}
    Clear=clear
    ```

    Rare, but very clean when the target program already speaks a close dialect.

=== "Method B — Adapter sender (recommended)"

    For USB/SDK/API boards, write a small executable that reads commands on its standard input and translates them for the hardware:

    ```ini
    [CommandSender:P1]
    Name=External LED Board P1
    Executable=ExternalLedSender.exe
    Arguments=daemon --ini "ExternalLedSender.ini" --sender P1
    UseStdIn=true
    ```

    The sender receives `SLOT 1 RED`, looks up its mapping (`RGB:1,2,3`) and applies the values to the board.

## Realistic target boards

These integrations are not shipped ready-made: they are realistic targets for an adapted sender.

| Board | Strengths | Typical integration |
|---|---|---|
| [Ultimarc PacLED64](https://www.ultimarc.com/output/led-and-output-controllers/pacled64/) | 64 channels, per-channel brightness | sender → Ultimarc SDK |
| Ultimarc PAC-Drive | Simple ON/OFF: START, coin lamps | sender → Ultimarc SDK |
| [Ultimarc I-PAC Ultimate I/O](https://www.ultimarc.com/control-interfaces/i-pacs/i-pac-ultimate-i-o/) | Combined inputs + LED outputs | sender → Ultimarc SDK |
| [LED-Wiz](https://groovygamegear.com/webstore/index.php?main_page=product_info&products_id=239) | 32 PWM outputs, a cabinet classic | sender → LED-Wiz API |
| [WLED / ESP32](https://kno.wled.ge/interfaces/json-api/) | Strips, matrices, addressable effects | sender → HTTP JSON |
| Arduino / Teensy / custom serial Pico | Closest to the current architecture | minimal serial sender |

!!! tip "WLED: for ambience, not for buttons"
    WLED shines for `STRIP`, `MATRIX`, topper and cabinet lighting. For individual arcade buttons needing very low latency, prefer a direct serial link.

## The minimum sender contract

Your sender must understand (or cleanly ignore while logging) the following:

```text
SLOT 1 RED
SET START ORANGE
BATCH SLOT 1 RED;SLOT 2 BLUE;SLOT 3 BLACK
ALL WHITE
CLEAR
FLASH 6 YELLOW 80
MATRIXSCORE MATRIX1 12345 GREEN
MATRIXTEXT MATRIX1 WHITE READY
```

And its mapping file describes the hardware:

```ini
[Slots]
1=RGB:1,2,3
2=RGB:4,5,6

[Outputs]
START=LED:25

[Addressable]
MATRIX1=MATRIX:16x16:60-315
```

Design rule: **if a logic depends on the hardware, it belongs in the sender**. The color table (`RED=255,0,0`…) lives in the board's sender, never in `LedManager.ini` — every board interprets intensities its own way.

## Integration checklist

1. Identify the control mode: serial, USB SDK, HID, HTTP, UDP.
2. Create the sender and its mapping INI.
3. Plug the sender into `LedManager.ini`.
4. Test in order: `SLOT 1 RED` → `SLOT 1 BLACK` → `BATCH` → `START`/`SELECT` → `ALL WHITE` → `CLEAR`.
5. Test MAME lamps if the board drives lamps.
6. Measure latency; add batching/dedup in the sender if needed.
