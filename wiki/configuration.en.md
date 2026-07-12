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

## Customizing a game or a system: overrides

Want Rainbow Road colors for Super Mario Kart? Create a **sparse patch** — only what you change goes in it, everything else keeps coming from APIExpose and its updates:

```text
overrides\systems\snes.json          → every SNES game
overrides\games\snes\smk.json        → just Super Mario Kart (wins over the system)
```

```json
{
  "schema": "ledmanager.panel-override.v1",
  "slots":   { "1": { "color": "GREEN" }, "2:3": { "color": "RED" } },
  "outputs": { "VR1 Lamp": { "slot": 1 } }
}
```

`"1"` = player 1 slot 1, `"2:3"` = player 2 slot 3; `outputs` keys are the game's output names (arcade lamps). The plugin's `overrides\README.txt` contains full examples, and the LedManagerSetup app will generate these files for you.

## During a game

A few useful behaviours (no setting required):

- when a game starts, the system panel is memorised (*snapshot*);
- ingame effects are targeted overlays; an `OFF` on a button restores it from the snapshot;
- after 2 seconds without activity, the system panel is restored;
- START and SELECT live their own life, independent of buttons B1–B8;
- MAME lamps keep their own state: a panel restore never relights a lamp the game turned off.

## Ingame effects: `default.mem.effects.json`

Reactions to **game moments** (life lost, boss hit, coin collected…) are
described in `default.mem.effects.json`, at the plugin root — an editable
catalogue, no code involved:

- **Family rules** (`genericRules`): one rule per family of moments
  (`resources.lives`, `scoring.collectibles`, `combat.enemies`…) — it applies
  to **every game**, no per-game mapping.
- **Effects**: `flash_restore`, `sweep`, `pulse`, `sparkle`,
  `health_feedback`, `matrix_score`… with targets (`ALL_BUTTONS`, `STRIP1`,
  `RANDOM_COLUMN`, `MATRIX1`), colors, durations and anti-spam (`throttleMs`).
- **Game color**: when the event carries its own color (arcade score deltas:
  1944's orange plane…), it **wins** over the rule color — the effect takes
  the tint of the destroyed target.
- **Per player**: events carrying a player index are routed to that player's
  panel (`playerField`), otherwise to `GLOBAL`.
- **Layers** (`effectLayers`): ingame effects live above the game panel and
  below alerts — every source has its priority, nothing gets clobbered.

!!! example "Example: golden flash on every treasure"
    In `genericRules`, the `inventory.items` family triggers a golden
    `sparkle` on `ALL_BUTTONS` + `STRIP1` with automatic restore: grab a key
    in Zelda or an emerald in Sonic and the cabinet sparkles — two games,
    zero configuration.

