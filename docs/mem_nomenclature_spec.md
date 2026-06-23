# MEM Nomenclature Specification (V11.25)

## Overview

This document defines the complete naming, structure, and normalization rules for `.MEM` files. These files are the cornerstone of the automated DOFLinx integration, bridging raw game memory to hardware effects (LEDs, Solenoids, Shakers).

The entire system is **Keyword-Centric**. If a memory address is not successfully identified and mapped to a recognized keyword, it will have **no effect** on the arcade cabinet.

The goal is to ensure that all generated `.MEM` files are:
- consistent across systems
- machine-readable
- **ACTION-oriented**
- suitable for automated parsing and real-time hardware reaction

### Pipeline V11.25 Standards:
- **Four Source Pillars:** ra, doflinx, datacrystal, gamehacking.
- **Title-Centric Naming:** `rom.name` stores the official game title.
- **Industrial Cleanliness:** Suppression of debug fields (`comment`) in production.

---

## 1. Core Philosophy: No Keyword, No Effect

The nomenclature follows a strict hierarchy of identification, reinforced by the **Pipeline V11 Strict Mode**:

1.  **The Keyword is King:** Identifying a memory address through a normalized keyword (e.g., `health`, `score`, `invincibility`) is the primary objective.
2.  **Semantic Evidence (V11):** For automated parsing, if the description contains no explicit keywords, the action is suppressed and the entry is stashed in `system.memory`.
3.  **Action Mapping:** Once a keyword is identified, it must be mapped to a standard `ACTION` payload.
4.  **Fallback (Technical):** Any identified address that does **not** match a known keyword or semantic family is relegated to the `system.memory` or `memory.variables` section.
5.  **Multi-Source Provenance:** Every address carries a `source` tag identifying its origin (ra, doflinx, datacrystal, or gamehacking).

---

## 2. Data Provenance (The Four Pillars)

The automated pipeline (V11.25) merges data from four specific sources in order of priority:

| Priority | Source Tag      | Directory Path              | Description                                      |
|----------|-----------------|-----------------------------|--------------------------------------------------|
| 1 (Base) | `"ra"`          | `sources/ra/`               | RetroAchievements JSON (Primary Authority)      |
| 2 (Native)| `"doflinx"`     | `sources/doflinx/`          | Native DOFLinx Arcade MEM files (Industry)       |
| 3 (Comm) | `"datacrystal"` | `sources/datacrystal/`      | Community memory maps (.MEM format)             |
| 4 (Cheat)| `"gamehacking"` | `sources/gamehacking/`      | Standardized GameHacking JSON (Completion)      |

---

## 2. File Format & Structure

Each `.MEM` file must be a valid Lua table. Due to the priority of events over raw technical data, the section ordering in the file reflects this hierarchy.

### 2.1 Required top-level structure

```lua
return {
  game = { ... },
  rom = { ... },
  events = { ... },
  memory = { ... }
}
```

### 2.2 Top-level sections

The sections must appear in this order:
1.  **`game`**: Metadata about the game title and target system.
2.  **`rom`**: Technical binary identity (hashes).
3.  **`events`**: **The Heart.** Categorized gameplay events mapped to keywords.
4.  **`memory`**: **The Fallback.** Only contains technical variables that failed to be classified into `events`.

---

## 3. Runtime ACTION Event Mapping

To simplify external hardware and frontend integration, the Lua API automatically translates categorized `.MEM` memory changes into standard universal `ACTION` string payloads. The mapping connects the raw memory category (`category.event`) plus the logic direction (e.g., `increase` vs `decrease`) into single commands.

### 3.1 Standard Mapped Actions

| UDP Payload Key | Value | Trigger Conditions (from `.MEM` category) | Use Cases |
|---|---|---|---|
| `ACTION:` | `1UP` | `[POSITIVE] lives` | The player gains an extra life. Flash Green / Play 1UP sound. |
| `ACTION:` | `DEAD` | `[NEGATIVE] lives` or `[CRITICAL] health` | The player dies. Flash Red / Play Death sound. |
| `ACTION:` | `HEAL` | `[POSITIVE] health` or `[POSITIVE] hp` or `[POSITIVE] energy` | Player restores some health or energy. |
| `ACTION:` | `HIT` | `[NEGATIVE] health` or `[NEGATIVE] hp` or `[NEGATIVE] energy` | Player takes damage without dying. Flash Red / Shaker motor. |
| `ACTION:` | `BOSS_HIT` | `[NEGATIVE] boss_hit` | Enemy or Boss takes damage. Flash White / Short Shaker motor. |
| `ACTION:` | `BOSS_HEAL`| `[POSITIVE] boss_hit` | Enemy or Boss regenerates energy. |
| `ACTION:` | `ITEM_GET` | `[POSITIVE] inventory.*` family | Player picks up an item, key, or weapon. Play item collect sound. |
| `ACTION:` | `ITEM_USE` | `[NEGATIVE] inventory.*` family | Player drops, loses, or consumes an item (e.g. Bombs in Arcade). Play explosion/use sound. |
| `ACTION:` | `SCORE` | `scoring.*` family or `is_score=true` flags | Points or currency collected. Can flash score digits or strobe lights. |
| `STATE:` | `NEW_LEVEL`| `stage`, `level`, `world` changes | Lifecycle event. **Note:** Must include the mapped name or numeric value as the event payload. |
| `STATE:` | `TITLE_SCREEN`| `flow.*` with map values containing 'title', 'menu', 'select' | Transitions to Title screens / Main Menus. |
| `STATE:` | `DEMO_MODE`| `flow.*` with map values containing 'demo', 'attract', 'intro' | Transitions to Demo or Attract Mode. |
| `STATE:` | `GAMEPLAY`| `flow.*` with map values containing 'game', 'play', 'normal' | Transitions to standard active Gameplay. |
| `STATE:` | `GAME_OVER`| `flow.*` with map values containing 'over', 'end', 'credit' | Transitions to Game Over or Ending sequences. |
| `STATE:` | `PAUSED`| `flow.*` with map values containing 'pause' | Transitions to Paused state. |
| `ANIM:` | `{custom verb}`| `state.*` or `player_state` combined with `action_map` | Character animations (JUMP, RUN, CROUCH) if exposed in UDP. |
| `ACTION:` | `UPDATE` | All other variables | Generic status update triggers (e.g., Invincibility flag changed). |

### 3.2 Comprehensive Generic Action Groups (Mapped to `UDP_OUT`)

To push generic universal behaviors across completely different games, creators should explicitly use the following standard Action Strings inside their `.MEM` definitions (via `action` or `action_map`). These groups are aligned with the keyword detection philosophy.

#### A. Core Progression & Global States (Mapped to `STATE:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `CORPORATE_SCREEN` | Boot logo (Sega, Capcom, SNK) | Fades to Corporate colors |
| `TITLE_SCREEN` | Main Menu / Title Screen | Attract Mode lighting / Ambient music |
| `DEMO_MODE` | Attract Mode / CPU Gameplay Playback | Muted inputs / Light show |
| `GAME_PLAYING` | Active standard gameplay | Playfield fully lit |
| `GAME_OVER` | Game Over / Continue prompt | Slow pulse / Somber lighting |
| `CREDITS` | Game credits rolling | Theatrical lighting / Celebration |
| `SAVING_ACTIVE` | Writing to Disk/Memory Card | Disk spin sound / Blue rotating pattern |
| `SAVING_END` | Save completed | Green success pulse |
| `PAUSE_ON` | Player hits Start/Pause | Playfield dims |
| `PAUSE_OFF` | Player resumes play | Playfield restores |
| `CONTINUE_SCREEN` | Choice to continue or quit | Blinking countdown LEDs |
| `LEVEL_CLEAR` | Act, Zone, or Match finished | Fanfare lighting / Fireworks |
| `RANK_ACHIEVED` | Score Screen / Final Grade S,A | Gold/Silver strobe depending on rank |
| `QUEST_COMPLETE` | Mission or Quest finished | Short happy chime / Ambient flash |
| `PLAYER_THEME` | Character/Vehicle color/skin selected | Overall panel color sync |
| `LOADOUT_SELECTED` | Character/Weapon/Magic selection confirmed | Confirm chime / Flash color |
| `RESOURCE_USED` | Secondary resources (MP, Stamina, Air) | Button pulse / Color flash |
| `OBJECT_INTERACTION` | World triggers (Doors, Rocks, Traps) | Haptic Solenoid / Shaker |
| `EVENT_TRIGGER` | Generic Script/Event Flags | Short strobe flash |
| `NEW_LEVEL` | World/Stage/Level change | Transition flash / Background change |
| `SETTINGS_CHANGED` | Options adjusted | Subtle menu flash |

#### B. Resources & Survival (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `LOSE_LIFE` | Explicit override for Death/Life loss | Flash Red / Death sound |
| `GAIN_LIFE` | Explicit override for 1UP/Extend | Flash Green / 1UP Chime |
| `HIT` | Non-lethal damage taken | Short Shaker Motor / Red Strobe |
| `HEAL` | Health or Energy recovered | Soft Green/Blue glow |
| `LOW_HEALTH_WARN` | Health drops below critical threshold | Continuous Red heartbeat pulse |
| `TIMER_LOW` | Time almost out (e.g., 10 seconds left) | Fast Yellow blinking |
| `DROWNING` | Underwater air running out | Blue ambient pulse accelerating |
| `LOW_RESOURCES_WARN` | Stamina/Air/Oxygen critical | Strobe or Haptic alarm |

#### C. Power-ups & Temporary States (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `INVINCIBILITY_START` | Star / Invincibility mode starts | Strobes pulsate White/Gold |
| `INVINCIBILITY_STOP` | Immortality buff ends | Strobes turn OFF |
| `SPEED_START` | Speed Shoes / Boost activated | Fan turns ON, RGB strips accelerate |
| `SPEED_STOP` | Temporary speed buff ends | Fan turns OFF |
| `SHIELD_GAIN` | Character acquires armor/forcefield | Single Green LED sweep |
| `SHIELD_LOST` | Character loses their shield | Single Red LED sweep |
| `STEALTH_START` | Shadow Mode / Invisibility active | Dimmed Cyan ambiance pulse |
| `STEALTH_STOP` | Invisible state lost / Spotted | Red flash / "Thud" effect |

#### D. Collectibles & Inventory (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `COIN_GAIN` | Currency picked up | Coin sound / Yellow LED blink |
| `COIN_LOSE` | Currency lost/dropped | Coin scatter sound / Heavy Shaker |
| `KEY_GET` / `PASS_GET` | Key item or access card found | Mechanical click / Green flash |
| `POWERUP_GET` | Generic player ability upgrade | Rising pitch chime / White flash |
| `TREASURE` | Big collectible (Emerald, Key) | Sustained Gold flash / Fanfare |
| `WEAPON_UPGRADE` | Firepower increased | White flash / Power-up sound |
| `BOMB_FIRED` | Screen-clearing item used | Mega Solenoid fire / Blinding Flash |

#### E. Action & Combat (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `FIRE_SIDEARM` | Auxiliary gun fired | Solenoid click / Pistol flash |
| `BATTLE_START` | Combat encounter begins | Sharp Red strobe / Aggressive tone |
| `BATTLE_END` | Combat resolves | Lighting fades back to normal |
| `COMBO_HIT` | Hit combo counter increase | Strobe scales with combo length |
| `FATALITY` | Finisher moves | Dark Red/Black lighting |
| `PARRY_SUCCESS` | Successful block/reflect | Metallic "ping" / Sharp White spark |
| `CRITICAL_HIT` | Weak spot hit / Headshot | Intense Yellow flash / Heavy Shaker |
| `BOSS_HIT` | Boss enemy takes damage | White Strobe |
| `BOSS_DEFEATED` | Boss dies | Massive Shaker / Explosions |

#### F. Racing & Vehicles (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `LAP_COMPLETE` | Crossed the finish line | Chequered flag lighting / Roar |
| `GEAR_SHIFT` | Upshift or Downshift | Force feedback clunk / Solenoid |
| `TURBO_BOOST` | Nitro or Boost pad hit | Fan max speed / Backward sweep |
| `MOUNT_START` | Riding Yoshi/Mech/Horse/Tank | Sustained vibration / Weighty feels |
| `MOUNT_STOP` | Dismounting mount / Ejecting | High-pitch sound / Weight loss flash |
| `CRASH` / `COLLISION` | Vehicle collision | Extremely heavy Shaker / Flash Red |

#### G. Hardware & Interaction (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `KEY_PRESSED` | Raw button input | Haptic click / Short flash |
| `CAMERA_MOVE` | Screen scrolling / Pan | Subtle positional lighting shift |

#### H. Cutscenes & Interactivity (Mapped to `STATE:` or `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `CINEMATIC_PLAYING` | Non-interactive sequence | Lights dim / Theatrical ambiance |
| `CINEMATIC_END` | Cinematics finish | Playfield restores |
| `DIALOGUE_SCENE` | Characters conversing | steady front light / Focus effect |
| `DIALOGUE_END` | Characters conversing ends | Ambient lighting restored |
| `CHOICE_PROMPT` | Waiting for player choice | Alternating buttons blinking |
| `CHOICE_END` | Choice made | Confirmation flash |
| `MAP_VIEWING` | Viewing level/world map | Cool blue glow |
| `MAP_CLOSED` | Map closed | Blue glow fades out |

#### I. Exploration & Secrets (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `DOOR_OPENED` | Key door or secret door opened | Low haptic clunk / Quick flash |
| `CHEST_OPENED` | Loot container or chest opened | Short chime / Twinkling light |
| `PUZZLE_SOLVED` | Mechanism or riddle solved | Success melody / Global white flash |
| `HACKING_START` | Terminal interaction / Coding | Scrolling Green LED patterns |
| `LOCKPICK_START` | Mechanical lock picking | Precision vibration / Tick feedback |
| `ROOM_DISCOVERED` | Entering a new area | Sweeping ambient reveal |
| `SECRET_REVEALED` | Hidden path or Warp Zone found | Tension buildup / Swirling RGB |

#### J. Simulation & Environment (Mapped to `STATE:` or `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `DAY_TIME` | Clock moves to day / morning | Warm orange/yellow ambiance |
| `NIGHT_TIME` | Clock moves to night | Deep purple/dark blue ambiance |
| `WEATHER_EFFECT` | Rain, Storm, Snow, Fog, Darkness | Atmospheric strobe / Color ambiance |
| `WEATHER_CLEAR` | Weather stops / Clear sky | Bright sunshine light |
| `ENVIRONMENT_FORCE` | Gravity/Wind/Stream modifier | Continuous directional haptic |
| `ENVIRONMENT_MOVE` | Moving platforms / Elevators | Vertical haptic oscillation |
| `CRAFTING_START` | Forge / Cook starts | Workstation light / Anvil clinks |
| `CRAFTING_END` | Crafting finishes | Sparkle effect / Final forge struck |
| `FUNDS_SPENT` | Purchase made | Cash register sound / Quick Red |
| `FUNDS_GAINED` | Income / Sale | Cash register sound / Quick Green |
| `FUNDS_LOW_WARN` | No money / Out of Cash | Muted clink / Red pulse warning |

#### K. Construction & Destruction (Mapped to `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `OBJECT_DESTROYED` | Object broken (Sign, Wall) | Wood or Stone break sound / Shaker |
| `OBJECT_BUILT` | Object constructed | Construct sound / Gear clink |
| `OBJECT_REPAIRED` | Damaged object fixed | Metallic clink / Soft glow |

#### L. Status Effects (Mapped to `STATE:` or `ACTION:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `STATUS_EFFECT_START` | Stunned/Frozen/Paralyzed | Flickering Yellow/Blue ambiance |
| `POISON_START` | Character poisoned | Pulsing Purple glow |
| `TRANSFORMATION_START` | Morphing (Toad, Curse) | Magic puff flash |
| `STEALTH_ALERT` | Suspicion / Caution / Alert | Sudden Red flash / Alarm siren |
| `STATUS_EFFECT_STOP` | Recovery from an ailment | Returning to normal |

#### M. Mini-Games (Mapped to `STATE:`)
| Generic Action String | Context / Source | Expected Pincab Effect |
|---|---|---|
| `MINIGAME_ACTIVE` | Casino, Slots, or Minigame | Chase lights / RGB rainbow |

| `GENERAL_TIMER` | Main game countdown clock | Background ticking / Progress lighting |
| `BOMB_TIMER` | Countdown for explosion | Accelerating heartbeat Red strobe |
| `LEVEL_TIMER` | Special Zone, Bonus Room, Warp | Alternating color flash / Transition ambiance |
| `TIMER_LOW_WARN` | General time critical threshold | Rapid Red blinking / Alarm chime |
| `ENVIRONMENT_TIMER` | Air/Oxygen/Breath/Gas/Vacuum | Position-dependent ambiance (Blue/White/Green) |
| `INVINCIBILITY_TIMER` | Remaining star/invincible duration | Flashing rainbow cycle |
| `SPEED_TIMER` | Remaining speed/boost duration | Fast forward white strobe sweeps |
| `SHIELD_TIMER` | Remaining shield/barrier duration | Soft green/blue protective glow pulse |
| `STEALTH_TIMER` | Remaining cloak duration | Slow Cyan pulse |
| `STATUS_EFFECT_TIMER` | Duration of poison, stun, etc. | Status-colored ambiance (Purple/Yellow/Ice) |
| `COMBO_TIMER` | Time remaining to keep a combo | Strobe heartbeat / Progressive pitch |
| `COOLDOWN_TIMER` | Ability/Refill/Weapon ready in X | Pulsing amber / Single ding when ready |

#### Movement & Animations (Action Map Verbs)
The following are typically used in `action_map` for raw visceral movements, usually mapped to haptics:
- `RUN`, `JUMP`, `SKID`, `SPIN`, `CROUCH`, `SPECIAL_STAGE_ENTER`.

---

## 4. `game` Header

The `game` block identifies the logical game entity.

### 4.1 Structure

```lua
game = {
  title = "Super Mario Bros.",
  system = "nes",
  system_name = "NES/Famicom"
}
```

### 4.2 Fields

#### `title`
- type: string
- required
- human-readable title of the game

#### `system`
- type: string
- required
- must use the RetroBat folder name slug

#### `system_name`
- type: string
- required
- full human-readable name of the system (e.g. "Genesis/Mega Drive")

#### `genre`
- type: string
- optional (standard in V11.26)
- primary gameplay genre (e.g. "Platform", "Racing", "Shooter")
- automated via LaunchBox database (`lb_genres.json`)
- `gb`
- `mame`
- `megadrive`
- `mastersystem`
- `psx`

#### `system_name`
- type: string or `nil`
- recommended
- human-readable system name

---

## 5. `rom` Header

The `rom` block identifies the technical ROM target(s).

### 5.1 Structure

```lua
rom = {
  name = "Super Mario Bros.",
  file = "super-mario-bros.zip",
  hashes = {
    {
      hash = "8e3630186e35d477231bf8fd50e54cdd",
      label = "Super Mario Bros. (World).nes"
    }
  }
}
```

### 5.2 Fields

#### `name`
- type: string
- required
- official human-readable game title (from RA metadata)
- Title Case preferred
- includes spaces and punctuation

#### `file`
- type: string
- required
- target ZIP name exported by RetroBat
- Clean slug format (snake_case or kebab-case) + `.zip` extension

#### `hashes`
- type: array
- recommended
- list of compatible ROM binary identities (md5 or system-specific)

### 5.3 Hash entry structure

```lua
{
  hash = "8e3630186e35d477231bf8fd50e54cdd",
  label = "Super Mario Bros. (World).nes",
  tags = { "nointro" }
}
```

#### `hash`
- type: string
- required inside a hash entry
- hash string exactly as provided by the source database

#### `label`
- type: string or `nil`
- optional
- human-readable ROM filename or variant label

#### `tags`
- type: array of strings
- optional
- source tags such as:
  - `nointro`
  - `redump`
  - `retrobat`
  - `translated`
  - `prototype`
  - `beta`

### 5.4 Header naming rules

- `system` must always reflect the RetroBat folder, not the source wording.
- `name` must remain stable across documentation updates.
- `hashes` may include multiple variants for the same game.
- Hash identity should never replace gameplay identity.

---

---

## 6. `events` Section

The `events` block defines interpreted gameplay events, state changes, flow transitions, and resource updates. This is the **primary** section where identified keywords live. Any entry here results in a potential feedback effect on the hardware.

### 6.1 Required structure

```lua
events = {
  flow = { ... },
  progression = { ... },
  resources = { ... },
  inventory = { ... },
  combat = { ... },
  scoring = { ... },
  state = { ... },
  world_interaction = { ... },
  system = { ... }
}
```

### 6.2 Allowed event families

- `flow`
- `progression`
- `resources`
- `inventory`
- `combat`
- `scoring`
- `state`
- `world_interaction`
- `system`

---

## 7. Event Entry Format

Each event entry must be a Lua table representing a keyword identification success mapped to a specific hardware action.

### 7.1 Canonical event entry

```lua
{ address=0x075A, type="u8", condition="decrease", action="LOSE_LIFE", desc="Player loses a life" }
```

### 7.2 Allowed fields

#### Required (Core)
- `address`: Uppercase Hexadecimal address (e.g., `0X075A`).
- `type`: Data type (e.g., `u8`).
- `condition`: Logic trigger (e.g., `eq`, `increase`, `decrease`, `bit_true`, `bit_false`).
- `desc`: Human-readable version of the event.

#### Recommended (DOF Impact)
- **`action`**: The Generic Action String (e.g., `HIT`, `WEATHER`, `RESOURCE_USED`) as defined in Section 3.2.
- **`value`**: The specific sub-type or parameter associated with the action (e.g., `0XFF`, `0X0C`, `RAIN`). **Mandatory for `eq` conditions**.
- `min`, `max`, `map`, `factor`, `is_score`, `format`, `no_log`, `no_survey`, `source`.

### 7.3 Canonical field order

1. `address`
2. `type`
3. `condition`
4. `action`
5. `value`
6. `mask`
7. `bit`
8. `min`
9. `max`
10. `no_log`
11. `no_survey`
12. `desc`
13. `source`

> **Note on `comment`**: The field `comment` is an internal generator artifact used for debugging provenance. It is **suppressed** in final production `.MEM` files as of V11.25.

---

## 8. Primitive Types

### 8.1 Allowed types

Unsigned:
- `u8`
- `u16be`
- `u16le`
- `u24be`
- `u24le`
- `u32be`
- `u32le`

Signed:
- `s8`
- `s16be`
- `s16le`
- `s32be`
- `s32le`

### 8.2 Type rules

- `u8` is the default fallback when an entry is clearly byte-sized.
- Multi-byte types must only be used if width and endian are known.
- **Auto-Byteswap (MegaDrive)**: For systems like MegaDrive (Genesis Plus GX), the Wrapper automatically performs a byteswap (XOR 1). MEM files MUST provide original RetroAchievements addresses; the hardware layer handles the alignment.

---

## 9. Conditions

### 11.1 Allowed conditions

- `change`
- `increase`
- `decrease`
- `eq`
- `neq`
- `bit_true`
- `bit_false`
- `any`

### 11.2 Meaning

#### `change`
Use when a value changes without reliable directional semantics.

Examples:
- current level
- game mode
- room id
- player state

#### `increase`
Use when a value is expected to rise meaningfully.

Examples:
- score
- coins
- rings
- experience
- combo count

#### `decrease`
Use when a value is expected to fall meaningfully.

Examples:
- lives
- health
- timer
- oxygen
- ammo if consumed

#### `eq` (Equal)
Use when a specific state or threshold is the event trigger. Requires the `value` field.

Examples:
- title screen active
- invincibility active
- boss defeated flag
- continue screen shown

#### `any`
Use only as a fallback when the observation is useful but not semantically directional.

---

## 10. Range Multiplexing (min, max)

When the same memory address triggers different actions based on its value or delta, use `min` and `max` fields to filter the entry.

- **For `condition="equal"`**: The event fires if the absolute value is within `[min, max]`.
- **For `condition="change"`**: The event fires if the **delta** (amount changed) is within `[min, max]`. This is common in Arcade scores to identify which enemy was hit.

Example:
```lua
{ address=0x0840, type="u32be", condition="change", min=100, max=100, action="SCORE", desc="Destroyed Small Plane" },
{ address=0x0840, type="u32be", condition="change", min=1000, max=1000, action="SCORE", desc="Destroyed Boss Section" }
```

---

## 11. `map` Normalization

`map` translates numeric values into human-readable semantics.

### 12.1 Structure

```lua
map = {
  [0] = "off",
  [1] = "on"
}
```

### 12.2 Rules

- keys must be numeric
- values must be strings
- values must be short, stable, lowercase where appropriate
- state values should prefer canonical terminology

### 12.3 Preferred canonical values

Binary:
- `off`
- `on`

State:
- `none`
- `idle`
- `jumping`
- `falling`
- `hurt`
- `dying`
- `dead`
- `paused`
- `active`

Powerups:
- `small`
- `big`
- `fire`
- `super`
- `hyper`
- `shield`
- `invincible`

Flow:
- `boot`
- `attract`
- `title`
- `menu`
- `options`
- `in_game`
- `pause`
- `game_over`
- `continue`
- `ending`
- `credits`

---

## 11. Description (`desc`) Rules

### 13.1 General rules

- English only
- concise
- gameplay-oriented when possible
- Title Case or sentence-style stability must be consistent across the project; recommended style is sentence-like with first capital letter only
- no trailing period
- no address in the text
- no source markup
- no HTML remnants
- no parenthetical noise unless necessary for disambiguation

### 13.2 Good examples

- `Player lives`
- `Current level`
- `Collected rings`
- `Invincibility active`
- `Boss hit counter`
- `Player powerup state`

### 13.3 Bad examples

- `ram address for number of lives`
- `Current amount of player's remaining lives.`
- `0x075A - Lives`
- `Player lives (actual current variable)`

---

## 12. Section Ordering

### 12.1 Top-level order

1. `game`
2. `rom`
3. `events` (Nested Hierarchy)
4. `memory`

### 12.3 Automatic Category Deduction
The Arcade Wrapper V4 uses the **Lua Indentation/Table Structure** to deduce the `category.event` path. 
Example: Providing an entry inside `events.flow.game_state` will automatically tag the UDP source as `flow.game_state` without additional mapping. This allows for an infinite, flexible event hierarchy.

### 12.2 `events` family order

1. `flow`
2. `progression`
3. `resources`
4. `inventory`
5. `combat`
6. `scoring`
7. `state`
8. `system`

---

## 13. `flow` Event Family

`flow` describes where the player is in the game lifecycle.

### 13.1 Canonical sub-keys

Allowed normalized keys:
- `boot`, `attract_mode`, `intro`, `title_screen`, `main_menu`, `options_menu`, `save_menu`, `load_menu`, `character_select`, `file_select`, `map_screen`, `pause`, `in_game`, `continue_screen`, `game_over`, `ending`, `credits`, `demo_play`, `settings`.

### 13.2 Examples

```lua
flow = {
  menu = {
    { address=0X0010, type="u8", condition="eq", action="TITLE_SCREEN", value=0X01, desc="Title screen" }
  },
  lifecycle = {
    { address=0X0010, type="u8", condition="eq", action="GAME_PLAYING", value=0X04, desc="Gameplay active" }
  }
}
```

---

## 14. `progression` Event Family

`progression` describes content traversal.

### 14.1 Canonical sub-keys

Allowed normalized keys:
- `world`, `zone`, `level`, `act`, `stage`, `area`, `room`, `mission`, `chapter`, `map`, `floor`, `checkpoint`, `lap`, `quest`.

### 14.2 Normalization rules

Normalize source terms as follows:
- `Current phase`, `chapter`, `episode` → best matching canonical progression key
- `Dungeon room`, `current room` → `room`
- `Current map`, `overworld map` → `map`
- `Stage number`, `round`, `scene` → `stage`

### 14.3 Examples

```lua
progression = {
  world = {
    { address=0x075F, type="u8", condition="change", action="NEW_LEVEL", desc="Current world" }
  },
  level = {
    { address=0x0760, type="u8", condition="change", action="NEW_LEVEL", desc="Current level" }
  }
}
```

### 16.4 Progression Hierarchy (Hiérarchie)

The system supports multi-layered progression. Source labels should be normalized into this hierarchy:

1.  **Macro (world, zone):** High-level environment groupings (Super Map, Group of Acts).
2.  **Micro (level, stage, round, scene, phase, mission, chapter, floor):** Discrete traversable units.
3.  **Sub (act, area, room, map, checkpoint):** Subdivision of a micro-unit or small exploration zone.

### 16.5 Display and Mapping Rules (Règles d'affichage)

When a `NEW_LEVEL` or progression update is emitted:

- **Mapped Name Priority:** If a `map = { ... }` exists, the API MUST display the human-readable name of the new unit (e.g., `ACTION: NEW_LEVEL | Green Hill Zone`).
- **Numeric Fallback:** If no mapping is present, the API MUST display the numeric value (e.g., `ACTION: NEW_LEVEL | World 3`).
- **State Association:** Any variable capable of clarifying an event's state (e.g., specifying *which* boss was hit or *which* item was collected) MUST be enriched with a `map` dictionary to ensure the UI/Log reflects the gameplay reality rather than raw numbers.

---

## 15. `resources` Event Family

`resources` covers values that the player gains, loses, consumes, or refills.

### 15.1 Canonical sub-keys

Allowed normalized keys:
- `lives`, `health`, `energy`, `hp`, `mp`, `stamina`, `ammo`, `magic`, `air`, `oxygen`.
- **Special Timers:** `timer`, `level_timer`, `environment_timer`, `speed_timer`, `shield_timer`, `invincibility_timer`, `status_timer`, `combo_timer`, `cooldown_timer`.

### 15.2 Normalization rules

Preferred mappings:
- `Life`, `Lives`, `Current lives` → `lives`
- `HP`, `Current HP`, `Health` → `health` unless an RPG convention requires `hp`
- `MP`, `Mana`, `Magic points` → `mp`
- `Air`, `Oxygen`, `Breath` → `oxygen` or `air`

### 15.3 Examples

```lua
resources = {
  lives = {
    { address=0x075A, type="u8", condition="decrease", action="LOSE_LIFE", desc="Player loses a life" }
  },
  health = {
    { address=0x00A0, type="u8", condition="decrease", action="HIT", desc="Player takes damage" },
    { address=0x00A0, type="u8", condition="increase", action="HEAL", desc="Player recovers health" }
  }
}
```

---

## 16. `inventory` Event Family

`inventory` covers collectible, held, equipped, and usable objects.

### 16.1 Canonical sub-keys

Allowed normalized keys:
- `inventory`, `items`, `keys`, `equipment`, `weapon`, `armor`, `powerup`, `quest_items`, `held_object`.
- **Generic Actions:** `key_get`, `powerup_get`.

### 16.2 Normalization rules

- Held but not persistent objects should prefer `held_object`.
- Inventory tables should prefer `items` or `inventory` depending on source detail.
- Equipment slots should prefer `equipment`, with `weapon` or `armor` when explicit.

### 16.3 Examples

```lua
inventory = {
  held_object = {
    { address=0x001D, type="u8", condition="change", action="ITEM_GET", desc="Held object changed" }
  },
  keys = {
    { address=0x0310, type="u8", condition="increase", action="ITEM_GET", desc="Key obtained" }
  }
}
```

---

## 17. `combat` Event Family

`combat` covers offensive and defensive interactions.

### 17.1 Canonical sub-keys

Allowed normalized keys:
- `enemy_state`, `boss_state`, `boss_hit`, `damage_taken`, `damage_dealt`, `weapon_charge`, `shield_state`, `invulnerability_frames`.

### 17.2 Examples

```lua
combat = {
  boss_hit = {
    { address=0x0400, type="u8", condition="increase", action="BOSS_HIT", desc="Boss hit count" }
  },
  enemy_state = {
    { address=0x0410, type="u8", condition="change", action="BOSS_HIT", desc="Enemy state changed", no_log=true }
  }
}
```

---

## 18. `scoring` Event Family

`scoring` covers performance and reward values.

### 18.1 Canonical sub-keys

Allowed normalized keys:
- `score`, `coins_rings`, `bonus`, `currency`, `experience`, `combo`, `chain`, `multiplier`.

### 18.2 Normalization rules

- `Coins`, `rings`, or similar arcade pick-ups should prefer `coins_rings` unless the game clearly uses a money economy.
- `Gold`, `money`, `gil`, `rupees`, `zenny` should normalize to `currency` unless game-specific tooling requires otherwise.

### 18.3 Examples

```lua
scoring = {
  score = {
    { address=0x0840, type="u24be", condition="increase", action="SCORE", desc="Score increased", is_score=true }
  },
  coins_rings = {
    { address=0x075E, type="u8", condition="increase", action="RING_GAIN", desc="Collected rings" }
  }
}
```

---

## 19. `state` Event Family

`state` is the normalized namespace for player forms, temporary effects, status conditions, and general state machines.

### 19.1 Canonical sub-keys

Allowed normalized keys:
- `player_state`, `powerup_state`, `temporary_state`, `status_effect`, `mode_state`, `game_state`, `vehicle_state`, `environment_state`.

### 19.2 `player_state`

Use for current player action or posture states (e.g., idle, jumping, dying).

Example:

```lua
player_state = {
  { address=0x1234, type="u8", condition="change", action_map={ [5]="JUMP", [10]="RUN" }, desc="Player state", no_log=true }
}
```

### 19.3 `powerup_state`

Use for durable or semi-durable player form changes.

Example:

```lua
powerup_state = {
  { address=0x0756, type="u8", condition="change", action="WEAPON_UPGRADE", desc="Player powerup state", map={
    [0]="small",
    [1]="big",
    [2]="fire"
  } }
}
```

### 19.4 `temporary_state`

Use for temporary player conditions like invincibility.

Example:

```lua
temporary_state = {
  { address=0x079F, type="u8", condition="equal", action="INVINCIBILITY_START", min=1, max=1, desc="Invincibility active", no_log=true }
}
```

### 21.5 `status_effect`

Use for RPG-like or explicit status ailment systems.

Examples:
- poison
- burn
- freeze
- sleep
- curse
- silence
- haste
- slow

Example:

```lua
status_effect = {
  { address=0x4000, type="u8", condition="change", desc="Status effect changed" }
}
```

### 21.6 `mode_state`

Use for gameplay mode sub-states not covered by flow.

Examples:
- overworld mode
- battle mode
- puzzle mode
- vehicle mode

### 21.7 `game_state`

Use for internal or high-level game state machines when meaningful to runtime tooling.

Examples:
- active gameplay state
- loading state
- scripted state

### 21.8 `mini_game_state`

Use for discrete secondary gameplay phases (Simon says, etc.).

---

---

## 20. `world_interaction` Event Family

Covers changes to the physical or interactive environment that are not character-specific actions.

### 22.1 Canonical sub-keys
Allowed normalized keys:
- `object_state`
- `construction`
- `destruction`
- `environmental_interactive`

---

## 21. `system` Event Family

`system` stores technical or low-semantic entries that still matter.

### 23.1 Canonical sub-keys

Allowed normalized keys:
- `memory`
- `flags`
- `prng`
- `internal_state`
- `debug`
- `input`
- `display`

### 22.2 Rules

Use `system` only when the value is:
- valid
- potentially useful
- not clearly gameplay-facing

Examples:
- PRNG
- technical counters
- unknown but stable flags
- internal state machine values

---

## 22. Global Variable and Event Name Dictionary

This section defines normalized names that should be preferred whenever matching source descriptions.

### 23.1 Progression dictionary

| Source patterns | Normalized key |
|---|---|
| world | `world` |
| zone | `zone` |
| act | `act` |
| stage, round, scene | `stage` |
| area | `area` |
| room | `room` |
| chapter | `chapter` |
| floor | `floor` |
| checkpoint | `checkpoint` |
| lap | `lap` |
| mission | `mission` |
| map | `map` |

### 23.2 Resource dictionary

| Source patterns | Normalized key |
|---|---|
| life, lives | `lives` |
| health | `health` |
| hp | `hp` or `health` |
| mp, mana | `mp` |
| stamina | `stamina` |
| ammo, bullets, shots | `ammo` |
| oxygen, air, breath | `oxygen` or `air` |
| timer, time left | `timer` |
| countdown | `countdown` |

### 23.3 Scoring dictionary

| Source patterns | Normalized key |
|---|---|
| score, points | `score` |
| coin, ring | `coins_rings` |
| gold, money, gil, rupees | `currency` |
| exp, xp, experience | `experience` |
| bonus | `bonus` |
| combo | `combo` |
| chain | `chain` |
| multiplier | `multiplier` |

### 23.4 Inventory dictionary

| Source patterns | Normalized key |
|---|---|
| inventory | `inventory` |
| item | `items` |
| key | `keys` |
| weapon | `weapon` |
| armor, armour | `armor` |
| equipment, equipped | `equipment` |
| held object | `held_object` |
| powerup | `powerup` |

### 23.5 State dictionary

| Source patterns | Normalized key |
|---|---|
| player state, status | `player_state` |
| form, powerup, transformation | `powerup_state` |
| invincible, shield, star, underwater | `temporary_state` |
| poison, sleep, burn, curse | `status_effect` |
| mode | `mode_state` |
| game state | `game_state` |
| vehicle state | `vehicle_state` |

---

## 23. Standardized Event Semantics

This section defines common normalized event intents.

### 24.1 Resource event patterns

#### Lives
- variable: `Player lives`
- decrease event: `Player loses a life`
- increase event: `Player gains a life`

#### Health
- variable: `Player health`
- decrease event: `Player takes damage`
- increase event: `Player recovers health`

#### Score
- variable: `Player score`
- event: `Score increased`

#### Coins/Rings
- variable: `Collected rings` or `Collected coins`
- increase event: `Collected rings`
- decrease event: `Rings lost`

### 24.2 Progression event patterns

#### Level/Stage/Room
- variable: `Current level`, `Current stage`, `Current room`
- event: `Current level`, `Current stage`, `Current room`
- condition: `change`

### 24.3 State event patterns

#### Invincibility
- section: `state.temporary_state`
- preferred desc: `Invincibility active`
- preferred condition: `equal`
- map or threshold should resolve active/inactive clearly

#### Powerup form
- section: `state.powerup_state`
- preferred desc: `Player powerup state`
- preferred condition: `change`

#### Hurt/Dying action state
- section: `state.player_state`
- preferred desc: `Player state`
- preferred condition: `change`

---

## 24. Fallback Strategy

### 25.1 If a valid address exists but gameplay classification is unclear
Preserve it under:
- `memory.variables`
- and optionally `events.system.memory` if runtime observation is still useful

### 25.2 If a page only exposes technical values
Generate a `.MEM` anyway if addresses are valid.

### 25.3 If parsing succeeds but no canonical event family fits
Use the nearest family or fallback to `system.memory`.

### 25.4 If an entry is pure PRNG or heap data
Preserve only if useful to advanced tooling; otherwise omit from `events` but it may remain in `memory.variables`.

---

## 25. Noise Rejection Rules

The following should not be promoted to gameplay-facing sections unless there is a clear reason:

- heap markers
- buffer markers
- unknown pointer tables
- raw object structure offsets with no semantic labeling
- duplicate display mirrors when an authoritative variable exists

These can remain in:
- `memory.variables`
- `events.system.memory`

---

## 26. Duplicate Resolution Rules

When multiple source entries point to similar meanings:

1. prefer the most authoritative variable
2. prefer gameplay value over display mirror
3. prefer the clearest source description
4. preserve both only if one is display and one is actual game logic

Examples:
- `Lives display` and `Lives actual` may coexist if both are useful
- `Displayed score` and `Internal score` may coexist if the runtime needs both

---

## 27. Naming Style Rules

### 27.1 All keys
- lowercase only
- snake_case only
- no spaces
- no punctuation other than underscore

### 27.2 Allowed examples
- `player_state`
- `temporary_state`
- `coins_rings`
- `boss_hit`
- `game_over`

### 27.3 Forbidden examples
- `playerState`
- `PlayerState`
- `coins/rings`
- `game-over`

---

## 28. Example Full File

```lua
return {
  game = {
    title = "Super Mario Bros.",
    system = "nes",
    system_name = "NES/Famicom"
  },

  rom = {
    name = "super_mario_bros",
    source_name = nil,
    hashes = {
      {
        hash = "8e3630186e35d477231bf8fd50e54cdd",
        label = "Super Mario Bros. (World).nes",
        tags = { "nointro" }
      }
    }
  },

  events = {
    flow = {
      in_game = {
        { address=0x0010, type="u8", condition="equal", action="GAME_PLAYING", min=4, max=4, desc="Gameplay active" }
      }
    },

    progression = {
      world = {
        { address=0x075F, type="u8", condition="change", action="NEW_LEVEL", desc="Current world" }
      },
      level = {
        { address=0x0760, type="u8", condition="change", action="NEW_LEVEL", desc="Current level" }
      }
    },

    resources = {
      lives = {
        { address=0x075A, type="u8", condition="decrease", action="LOSE_LIFE", desc="Player loses a life" }
      },
      timer = {
        { address=0x07F8, type="u8", condition="decrease", action="TIMER_LOW", desc="Timer countdown", no_log=true }
      }
    },

    scoring = {
      coins_rings = {
        { address=0x075E, type="u8", condition="increase", action="COIN_GAIN", desc="Collected coins" }
      }
    },

    state = {
      powerup_state = {
        { address=0x0756, type="u8", condition="change", action="WEAPON_UPGRADE", desc="Player powerup state", map={
          [0]="small",
          [1]="big",
          [2]="fire"
        } }
      },
      temporary_state = {
        { address=0x079F, type="u8", condition="equal", action="INVINCIBILITY_START", min=1, max=1, desc="Invincibility active", no_log=true }
      }
    },

    system = {
      memory = {
        { address=0x00FF, type="u8", condition="change", action="UPDATE", desc="PRNG value", no_log=true }
      }
    }
  },

  memory = {
    variables = {
      lives = {
        address = 0x075A,
        type = "u8",
        desc = "Player lives"
      },
      world = {
        address = 0x075F,
        type = "u8",
        desc = "Current world"
      },
      unidentified_technical_1 = {
        address = 0x07A0,
        type = "u8",
        desc = "Unidentified technical variable"
      }
    }
  }
}
```

---

## 29. Runtime API Behavior Flags

To prevent overloading the emulator runtime or the networking output, the following flags MUST be used to control how and when events are observed.

### 29.1 `no_log = true`
Use `no_log` on events that change frequently to prevent flooding text consoles or network streams.
- **Mandatory for:** `timer`, `countdown`, `score`, `experience`, and all internal counters or cooldowns.
- **Exceptions (Force Logging):** 
    1. Resource variables representing "money" (e.g., `coins_rings`, `currency`).
    2. Any entry in a `scoring.*` family using `min` or `max` filters (Targeted score detection).
    3. `flow.game_state` transitions containing the "game state" label.
    4. Explicit `action` keywords: `COIN_GAIN`, `LOSE_LIFE`.

### 29.2 `no_survey = true`
Use `no_survey` for memory addresses that the emulator should completely skip reading by default.
- **Mandatory for:** 
    1. Any entry identified but not associated with a recognized gameplay keyword (Fallback/System).
    2. Verbose technical entries that do not trigger a specific DOF `ACTION`.
- **Example (from Sonic the Hedgehog):**
```lua
{ address=0xFFD008, type="u16be", condition="change", action="NEW_LEVEL", desc="Stage track", no_survey=true }
```

---

## 30. ROM Dictionary Aliasing (`alias.json`)

To centralize `.MEM` files (e.g., `sonic_the_hedgehog.MEM`) without renaming them for each region or romhack, a system folder should contain an `alias.json` file mapping raw ROM filenames to their canonical `.MEM` name.

### 31.1 Structure
```json
{
  "Sonic The Hedgehog (USA, Europe)": "sonic_the_hedgehog",
  "Sonic The Hedgehog (Japan)": "sonic_the_hedgehog",
  "Sonic 1 - Boomed (QoL Fix)": "sonic_the_hedgehog"
}
```

---

---

---

## 31. States vs Counters (États et Compteurs)

The nomenclature distinguishes between a discrete **State** and a progressing **Counter**, even if they relate to the same gameplay effect.

### 31.1 Definitions
- **State (État):** A binary or enumerated condition (On/Off, Active/Inactive).
- **Counter (Compteur):** A numerical value representing duration or accumulation.

### 31.2 Example: REFERENCE_sonic-the-hedgehog.MEM
These entries coexist to serve different logic layers:

```lua
-- The State (Binary toggle)
{ address=0xFFFE2D, type="u8", condition="bit_true", action="INVINCIBILITY_START", action_map={[1]="START"}, desc="Invincibility", map={[0]="Not active", [1]="Active"} },

-- The Counter (Frequent update, no_log mandatory)
{ address=0xFFD032, type="u16be", condition="bit_true", action="INVINCIBILITY_START", desc="Invincibility counter", no_log=true }
```

---

## 32. Official Genre Taxonomy

The .MEM file genre field must map EXCLUSIVELY to one of the following official standards. Any granular arcade genres or fallback heuristic inferences must be normalized to this exact spelling:

- Action
- Adventure
- Beat 'em Up
- Board Game
- Casino
- Compilation
- Construction and Management Simulation
- Education
- Fighting
- Flight Simulator
- Horror
- Life Simulation
- MMO
- Music
- Party
- Pinball
- Platform
- Puzzle
- Quiz
- Racing
- Role-Playing
- Sandbox
- Shooter
- Sports
- Stealth
- Strategy
- Vehicle Simulation
- Visual Novel

---

## 33. Compliance Checklist

A `.MEM` file is compliant if:
- it uses the canonical top-level structure
- `game.system` uses the RetroBat folder name
- `rom.name` is normalized and stable
- `hashes` preserve variant identity when available
- `memory.variables` describes raw observed variables
- `events` uses only approved event families
- state-related entries are separated into standardized sub-keys
- descriptions are normalized and readable
- field order is canonical
- **Logging rules** (`no_log`) are applied to all non-currency counters
- **Survey rules** (`no_survey`) are applied to all fallback entries

---

## 34. `memory` Section (The Fallback Bucket)

The `memory` block is the final section of the `.MEM` file. It describes memory entries that **failed to be identified** through the keyword master structure.

### 34.1 Purpose

Use `memory` only to preserve addresses that:
- have a valid/reliable location but no known gameplay meaning
- belong to heap data or technical PRNG with no associated keyword

### 34.2 Structure

```lua
memory = {
  variables = {
    unknown_flag_1 = {
      address = 0x075A,
      type = "u8",
      desc = "Unidentified technical variable"
    }
  }
}
```

### 34.3 Fields

- `address` (required)
- `type` (required)
- `desc` (required): Should state clearly that the variable is unidentified or technical.

---

## 35. Final Recommendation

For all new generation logic: **Keyword First**. If a line doesn't match our `analyzed_keywords.md`, it belongs to the `memory` section at the end. Continuous improvement of the keyword dictionary is the only way to move technical memory into the active `events` family and bring the arcade cabinet to life.

