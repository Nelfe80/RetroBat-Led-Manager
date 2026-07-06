# The setup assistant

`LedManagerSetup.exe`, at the root of the plugin, is the visual tool that guides you from wiring to going live — without editing a single file by hand.

It offers two modes in the sidebar:

- **Virtual panel**: a real-time mirror of your panel. While LedManager is running (in a game or the menus), it shows exactly the colors sent to your LEDs. Great to check everything reacts, or to debug without staring at the cabinet.
- **Hardware assistant**: the guided flow to configure and test your Pico.

## The virtual panel

Open `LedManagerSetup.exe` while RetroBat is running: the dot turns green and the virtual panel animates in sync with the real one. It shows the [recommended standard arrangement](materiel.md#the-standard-arrangement-recommended-for-retrobat) (SELECT/START top-left, then the two rows).

!!! note "A slight in-game slowdown is normal"
    The tool runs at low priority so it doesn't disturb emulation, but keep it closed during serious play — it's a tuning tool.

## The hardware assistant

The assistant takes **direct control** of your Pico to test it. Since LedManager holds the Pico's port, the assistant stops it automatically when the test starts (your LEDs go dark during configuration — that's normal).

### 1. Preparation

Plug your Pico in over USB (a **data** cable, not a charge-only one), then click **Detect the Pico**. The assistant:

- stops LedManager to free the port;
- searches the serial ports for your Pico and reads its firmware version;
- restarts the driver and lights the whole panel white.

If nothing is detected: check the cable, and that the [firmware is installed](materiel.md#flashing-the-firmware).

### 2. Panel test

Your buttons should all be **lit white**. That confirms power and firmware work. If some stay dark, it's a wiring or power issue (see [Troubleshooting](depannage.md)).

### 3. Wiring test

This is the clever step: one button lights up **green** on your real panel, one at a time. Each time, **click the virtual button that matches** the one lit for real. A cyan blink confirms your click, both on screen and on the panel.

START and SELECT are tested at the end of the sequence.

The assistant thus compares your real wiring to the expected arrangement. At the end, it tells you whether everything matches, or lists any differences — handy to spot a swapped wire without dismantling anything.

!!! tip "Coming soon"
    Automatic mapping correction (the assistant rewrites the software wiring instead of making you re-solder), the color-channel test and full config generation are coming in future versions.
