# Configuration

Two `.ini` files drive everything, each with a precise role:

| File | Role | In one sentence |
|---|---|---|
| `LedManager.ini` | The conductor | *What* to display and *who* to send it to |
| `PicoCommandSender.ini` | The hardware adapter | *How* your board understands the orders |

!!! tip "Graphical tools are coming"
    A visual configuration tool is planned. Meanwhile, this page covers the settings a user actually changes - the rest can stay as it is.

## LedManager.ini - the orchestrator

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

The setting worth knowing: **`StartupDelayMs`** gives the Pico time to initialize at startup. If your LEDs light up late, resist shrinking it too much - a Pico that is not ready ignores commands.

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

## PicoCommandSender.ini - your hardware

### The serial port

```ini
[Serial]
Port=COM3
BaudRate=115200
```

**`Port`** is the first setting to check after installation: open Windows Device Manager and find the COM port assigned to your Pico.

### Your panel

The `[Hardware:P1]` and `[GPIO:P1]` sections describe buttons and wiring - see [Hardware](materiel.md#describing-your-panel).

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

Want Rainbow Road colors for Super Mario Kart? Create a **sparse patch** - only what you change goes in it, everything else keeps coming from APIExpose and its updates:

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
described in `default.mem.effects.json`, at the plugin root - an editable
catalogue, no code involved:

- **Family rules** (`genericRules`): one rule per family of moments
  (`resources.lives`, `scoring.collectibles`, `combat.enemies`…) - it applies
  to **every game**, no per-game mapping.
- **Effects**: `flash_restore`, `sweep`, `pulse`, `sparkle`,
  `health_feedback`, `matrix_score`… with targets (`ALL_BUTTONS`, `STRIP1`,
  `RANDOM_COLUMN`, `MATRIX1`), colors, durations and anti-spam (`throttleMs`).
- **Game color**: when the event carries its own color (arcade score deltas:
  1944's orange plane…), it **wins** over the rule color - the effect takes
  the tint of the destroyed target.
- **Per player**: events carrying a player index are routed to that player's
  panel (`playerField`), otherwise to `GLOBAL`.
- **Layers** (`effectLayers`): ingame effects live above the game panel and
  below alerts - every source has its priority, nothing gets clobbered.

!!! example "Example: golden flash on every treasure"
    In `genericRules`, the `inventory.items` family triggers a golden
    `sparkle` on `ALL_BUTTONS` + `STRIP1` with automatic restore: grab a key
    in Zelda or an emerald in Sonic and the cabinet sparkles - two games,
    zero configuration.


## Score verdict and MULTIPLAYER matches

With APIExpose 1.9.185 or newer, LedManager also opens `/ws/scoring` and
`/ws/multiplayer`, only when `GET /api/v1/ws/streams` announces them (an older
API keeps the usual five streams). Same API key, same reconnection.

- The certification verdict (`scoring.verdict`) becomes a catalogue moment:
  `SCORING_PUBLISHED`, `SCORING_HELD`, `SCORING_QUARANTINED`, `SCORING_REJECTED`.
- A MULTIPLAYER match gives `MULTIPLAYER_MATCH_STARTED` then
  `MULTIPLAYER_MATCH_ENDED`.
- These moments only light up when `default.mem.effects.json` names them
  (`actionRules`): never the generic `*` flash. The shipped effects are flash
  sweeps the board restores by itself, in game as in ES.
- Empty `[APIExpose] ScoringPath=` or `MultiplayerPath=`: that stream is not opened.

## Signal meanings: ammo, bombs, lives

During a game, APIExpose (1.9.185 or newer) publishes what the game signals
mean (`deck.semantics`, read from its `.SMEM` file) on `/ws/ingame` and
`/ws/arcade`. LedManager keeps it for the running launch, tracks the value of
each signal and forgets it when the game ends.

One generic behaviour per role, for any game with a `.SMEM`: when a stock
falls to zero (`players.N.bombs`, `players.N.ammo`, `players.N.lives`), the
button whose function matches blinks until the value comes back. The button
function comes from the game's dynpanel (`"function": "Bomb"`); the `.SMEM`
starting value never triggers anything.

```ini
[Instruments]
Enabled=true          ; read the signal meanings
ZeroBlink=true        ; blink the button of an empty stock
BlinkColor=BLACK      ; the button goes dark for a moment, the board relights it
BlinkOnMs=250
BlinkIntervalMs=700

[InstrumentButtons]   ; one role per key: words looked for in the button function
bombs=bomb,grenade
ammo=ammo,reload
lives=                ; no button by default: the state is tracked and logged
```
