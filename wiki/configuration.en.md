# Configuration

Two `.ini` files drive everything, each with a precise role:

| File | Role | In one sentence |
|---|---|---|
| `LedManager.ini` | The conductor | *What* to display and *who* to send it to |
| `PicoCommandSender.ini` | The hardware adapter | *How* your board understands the orders |

!!! tip "Graphical tools are coming"
    A visual configuration tool is planned. Meanwhile, this page covers the settings a user actually changes — the rest can stay as it is.

## LedManager.ini — the orchestrator

### Declaring your senders

Each physical panel (one per player, for instance) is a *sender*:

```ini
[CommandSenders]
Default=P1
Global=GLOBAL

[CommandSender:P1]
Name=Pico Player 1
Enabled=true
Player=1
Executable=PicoCommandSender.exe
Arguments=daemon --ini "PicoCommandSender.ini" --sender P1
StartupDelayMs=18000
```

The setting worth knowing: **`StartupDelayMs`** gives the Pico time to initialize at startup. If your LEDs light up late, resist shrinking it too much — a Pico that is not ready ignores commands.

### Routing players and targets

```ini
[PlayerRouting]
1=P1
2=P2

[TargetRouting]
MATRIX1=GLOBAL
STRIP1=GLOBAL
```

Cabinet-wide effects (score matrix, strips) go to the `GLOBAL` sender, player panels to `P1`/`P2`.

## PicoCommandSender.ini — your hardware

### The serial port

```ini
[Serial]
Port=COM3
BaudRate=115200
```

**`Port`** is the first setting to check after installation: open Windows Device Manager and find the COM port assigned to your Pico.

### Your panel

The `[Hardware:P1]` and `[GPIO:P1]` sections describe buttons and wiring — see [Hardware](materiel.md#describing-your-panel).

### The color policy

Not every LED renders every color faithfully. `[ColorPolicy]` lets you allow, deny or substitute colors **without recompiling anything**:

```ini
[ColorPolicy.Fallbacks]
GOLD=YELLOW
PURPLE=VIOLET
GRAY=WHITE
```

Here, if a game asks for gold, the panel shows yellow instead. `GRAY` is the canonical name for grey; `BLACK` means off.

## Adapting to another LED program

LedManager does not know your hardware's final protocol: it fills in text templates defined in `[CommandTemplates]`. To drive another program, you only change those templates:

```ini
[CommandTemplates]
SetSlot=SLOT {slot} {color}
Flash=FLASH {target} {color} {durationMs}
All=ALL {color}
Clear=CLEAR
```

Available variables: `{slot}`, `{target}`, `{color}`, `{durationMs}`, `{value}`, `{text}`, `{player}`, `{system}`, `{rom}`… Integration methods are detailed in [External LED boards](cartes-externes.md).

## During a game

A few behaviours worth knowing (no settings required):

- when a game starts, the system panel is memorized (*snapshot*);
- in-game effects are targeted overlays; an `OFF` on a button restores it from the snapshot;
- after 2 seconds without button activity, the system panel is restored;
- START and SELECT live their own life, independent of buttons B1–B8;
- MAME lamps keep their own state: a panel restore never relights a lamp the game turned off.
