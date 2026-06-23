# DOFLinx .MEM Generation: The Supreme Technical Compendium (V11.25)

This document is the absolute, all-in-one **Source of Truth** for the DOFLinx API project. It consolidates every philosophical, strategic, technical, and structural element required to generate standardized `.MEM` files.

---

## PART I: CORE PHILOSOPHY & DOFLinx ARCHITECTURE

### 1.1 State Observation (The "Logic of Observation")
A `.MEM` file is an **Active Observer**, not a game script. Its purpose is to bridge the virtual world (RAM) with the physical world (Arcade Cabinet Hardware).
- **The Observation Flow**: `Physical RAM` → `DOFLinx Poll` → `Action String` → `Cabinet Hardware (LED/Motor/DMD/Fan)`.
- **The Action Bridge**: When a memory value matches a `condition`, it broadcasts a standard **Action String**.

### 1.2 "Appropriating" Memory Logic from Hacks
We use GameHacking XMLs and DOFLinx MEM files not to "cheat", but to find **Active Addresses**.
- **Cheat Logic (Hack)**: "Infinite Health" (Address `0x0110` = `0xFF`).
- **DOFLinx Interpretation**: Observe address `0x0110` for `decrease`. When a hit occurs, the RAM value drops momentarily, triggering the physical `HIT` effect.

---

## PART II: TECHNICAL DATA SOURCING & ORGANIZATION

### 2.1 Les Quatre Piliers de Données (Merged Sources — V11.25)

Chaque `.MEM` est le produit d'une fusion quadripartite selon cette **priorité absolue** :

| Priorité | Dossier source              | Clé `source=`   | Format  | Rôle                                           |
|----------|-----------------------------|-----------------|---------|------------------------------------------------|
| 1 (Base) | `sources/ra/{system}/`      | `"ra"`          | JSON    | Données RetroAchievements — base obligatoire   |
| 2 (Enrich)| `sources/doflinx/`         | `"doflinx"`     | `.MEM`  | MEM natifs DOFLinx arcade — enrichissement     |
| 3 (Communauté)| `sources/datacrystal/{system}/` | `"datacrystal"` | `.MEM` | Maps mémoire communautaires DataCrystal |
| 4 (Complétion)| `sources/gamehacking/{system}/` | `"gamehacking"` | JSON | GameHacking standardisé — complétion uniquement |

> **Règle de non-écrasement** : Si l'adresse a déjà été fournie par une source plus prioritaire, la source suivante ne peut pas écraser (`overwrite=False`).

### 2.2 Organisation & Routage (Production Pipeline)
- **Dossier cible** : `API/mem_gen/<system_slug>/`
- **Clean-Slug Naming Rule** : Les fichiers `.MEM` portent le nom épuré du jeu (**Clean Slug**) sans ID numérique (ex: `sonic-the-hedgehog.MEM`, pas `sonic-the-hedgehog-1.MEM`).
- **Le Dictionnaire d'Alias (`alias.json`)** : Chaque dossier système contient un `alias.json` pour la résolution dynamique.
  - Couches de mapping : **Label ROM** → **MD5 Hash** → **Titre Officiel**
  - Valeur : le Clean Slug sans extension.

### 2.3 Localisation des Dossiers Sources (Paths Absolus)

```
API/sources/
├── ra/              ← RetroAchievements JSON (ex-RAfolders)
│   ├── nes/
│   ├── snes/
│   ├── megadrive/
│   └── ...
├── doflinx/         ← MEM natifs DOFLinx arcade (fichiers .MEM)
│   ├── (fichiers à la racine pour arcade : pacman.MEM, sf2.MEM, ...)
│   └── (sous-dossiers pour consoles si présents)
├── datacrystal/     ← Maps mémoire communautaires (fichiers .MEM par système)
│   ├── nes/
│   ├── snes/
│   ├── megadrive/
│   └── ...
├── gamehacking/     ← GameHacking JSON standardisé (ex-gamehacking_json)
│   ├── nes/
│   ├── snes/
│   └── ...
├── cheat/           ← Cheats bruts (lecture seule)
├── databases/       ← Bases indexées
└── rdb/             ← ROM Databases
```

---

## PART III: SEMANTIC MAPPING — FAMILY ROUTING

### 3.1 DC_CATEGORY_MAP (Catégories DOFLinx MEM → Familles V11)

| Catégorie Source    | Famille Cible        |
|---------------------|----------------------|
| `level`             | `progression.zone`   |
| `coins_rings`       | `scoring.collectibles` |
| `powerup_state`     | `state.temporary`    |
| `star_invincibility`| `state.temporary`    |
| `enemy_state`       | `combat.enemies`     |
| `mode_state`        | `flow.lifecycle`     |
| `x_position`        | `system.movement`    |
| `y_position`        | `system.movement`    |
| `memory`            | `system.memory`      |
| `events`            | `flow.lifecycle`     |
| `stage`             | `progression.stage`  |
| `zone`              | `progression.zone`   |
| `game_state`        | `flow.lifecycle`     |
| `settings`          | `flow.settings`      |
| `oxygen`            | `resources.lives`    |
| `lives`             | `resources.lives`    |
| `scoring`           | `scoring.points`     |

### 3.2 FAMILY_ROUTING (Actions → Familles V11)

| Action(s)                                      | Famille Cible              |
|------------------------------------------------|----------------------------|
| `TITLE_SCREEN`, `GAME_PLAYING`, `GAME_OVER`, `PAUSE_ON/OFF`, `DEMO_MODE`, `CONTINUE_SCREEN`, `CORPORATE_SCREEN`, `SAVING_ACTIVE`, `SELECT_SCREEN`, `STARE_UPDATE`, `MINIGAME_ACTIVE` | `flow.lifecycle` |
| `SETTINGS_CHANGED`                             | `flow.settings`            |
| `NEW_LEVEL`, `LEVEL_CLEAR`, `STATS_UPDATE`, `SECRET_REVEALED` | `progression.level` |
| `HIT`, `HEAL`, `LOSE_LIFE`, `GAIN_LIFE`, `LIVES_STATE`, `HEALTH_STATE`, `RESOURCE_STATE`, `DROWNING`, `UNIT_COUNT`, `DYNAMIC_INFINITE`, `DYNAMIC_MAX`, `DYNAMIC_START`, `DYNAMIC_ZERO` | `resources.lives` |
| `COIN_GAIN`, `COIN_LOSE`, `MONEY_STATE`, `AMMO_STATE`, `FUNDS_SPENT`, `FUNDS_GAINED` | `scoring.collectibles` |
| `SCORE_STATE`                                  | `scoring.points`           |
| `INVENTORY_ITEM`, `TREASURE`, `KEY_GET`, `DYNAMIC_INVENTORY` | `inventory.items` |
| `INVINCIBILITY_START/STOP`, `SPEED_START/STOP`, `SHIELD_GAIN/LOST`, `STATUS_EFFECT_START/STOP`, `POISON_START`, `TRANSFORMATION`, `SPECIAL_ACTION`, `DYNAMIC_ALWAYS` | `state.temporary` |
| `JUMPING`, `RUNNING`                           | `state.player`             |
| `MOUNT_START`, `MOUNT_STOP`, `MOUNT_STATE`     | `state.mount`              |
| `BOSS_HIT`, `BOSS_DEFEATED`, `BOMB_FIRED`      | `combat.enemies`           |
| `GENERAL_TIMER`, `BOMB_TIMER`, `LEVEL_TIMER`   | `system.timer`             |
| `OBJECT_INTERACTION`, `OBJECT_DESTROYED`, `DOOR_OPENED`, `ENVIRONMENT_FORCE` | `world_interaction.objects` |
| `DYNAMIC_MODIFIER`                             | `flow.lifecycle`           |
| `UNKNOWN`                                      | `system.unmapped`          |
| `IGNORE`                                       | `system.internal`          |

---

## PART IV: RÈGLES DE LOGGING (Whitelist V11.18 / Anti-Spam V11.24)

### 4.1 Principe Fondamental
Par défaut, **tout est silencieux** (`no_log=true`, `no_survey=true`). Seule la whitelist ouvre le log/survey.

### 4.2 Whitelist — Actions Visibles (`no_log=false`, `no_survey=false`)
- **Survie** : `HIT`, `HEAL`, `LIVES_STATE`, `GAIN_LIFE`, `LOSE_LIFE`
- **Combat** : `BOSS_HIT`, `BOSS_DEFEATED`, `WEAPON_UPGRADE`
- **Ressources** : `COIN_GAIN`, `COIN_LOSE`, `MONEY_STATE`, `SCORE_STATE`, `EXPERIENCE_STATE`
- **Butins** : `TREASURE`, `KEY_GET`
- **États** : `INVINCIBILITY_START/STOP`, `SHIELD_GAIN/LOST`, `TRANSFORMATION_*`, `MOUNT_*`, `PLAYER_STATE*`
- **Progression** : `PROGRESSION_ZONE`, `PROGRESSION_STAGE`
- **Lifecycle** (unicité) : `TITLE_SCREEN`, `LEVEL_CLEAR`, `GAME_OVER`, `NEW_LEVEL`, `SELECT_SCREEN`, `GAME_PLAYING`, `PAUSE_ON/OFF`, `DEMO_MODE`, `CONTINUE_SCREEN`, `CREDITS_SCREEN`, `CORPORATE_SCREEN`, `START_GAME`, `INTRO_SCREEN`, `LOADING_SCREEN`, `CHARACTER_SELECT`, `STAGE_SELECT`, `WORLD_MAP`

### 4.3 Règles Spéciales Anti-Spam (V11.24)
- **Timers/Compteurs** : `no_log=true`, `no_survey=false` — silencieux en log, actif pour le hardware survey.
- **Sons/Musiques** : `no_log=true`, `no_survey=false` — surveille la valeur, ne log pas.
- **Lifecycle (double)** : Si l'action lifecycle est déjà apparue (lifecycle_tracker), les occurrences suivantes sont silencieuses (`no_log=true`, `no_survey=true`).

### 4.4 Noise Map (V11.24 — Isolation)
- Fichier : `mem_gen/{system}/noise_map.json`
- Adresses marquées `"ignore": true` → skip total pendant la génération.
- **Sibling Wake-up** : Si une adresse gelée était la seule source active d'une action, le premier candidat silencieux du même groupe est réveillé.

---

## PART V: STRUCTURE DU FICHIER .MEM GÉNÉRÉ

### 5.1 Ordre des Sections (Obligatoire)

```lua
return {
  game  = { ... },   -- Métadonnées du jeu
  rom   = { ... },   -- Identité binaire (hashes)
  events = { ... }   -- Événements classifiés (hiérarchie familles)
}
```

### 5.2 Bloc `game`

```lua
game = {
  title       = "Sonic The Hedgehog",   -- Titre officiel (from RA metadata)
  system      = "megadrive",             -- Clé système RetroBat
  system_name = "Genesis/Mega Drive",    -- Nom humain du système
  genre       = "Platform"               -- Genre (par défaut: "Platform")
}
```

### 5.3 Bloc `rom`

```lua
rom = {
  name  = "sonic-the-hedgehog",          -- Clean slug (sans extension, snake_case ou kebab-case)
  file  = "sonic-the-hedgehog.zip",      -- Fichier ZIP cible RetroBat
  hashes = {
    { hash = "1bc674be034e43c96b86487ac69d9293", label = "Sonic The Hedgehog (USA, Europe).md" },
    { hash = "abc123...", label = "Sonic (Japan).md" }
  }
}
```

> **Note V11.25** : Le champ `name` a été réintroduit. Il est distinct de `file` et représente le clean slug stable utilisé pour le routing d'alias.

### 5.4 Bloc `events` — Hiérarchie des Familles

```lua
events = {
  flow         = { lifecycle = {...}, settings = {...} },
  progression  = { level = {...}, zone = {...}, stage = {...} },
  resources    = { lives = {...}, health = {...}, secondary = {...}, environmental = {...} },
  inventory    = { items = {...}, weapon = {...} },
  scoring      = { points = {...}, collectibles = {...}, experience = {...} },
  combat       = { enemies = {...}, tactical = {...} },
  state        = { temporary = {...}, player = {...}, mount = {...} },
  world_interaction = { objects = {...} },
  system       = { timer = {...}, memory = {...} }
}
```

### 5.5 Format d'une Entrée (Strict — V11)

```lua
{ address=0X075A, type="u8", condition="decrease", action="LOSE_LIFE", no_log=false, no_survey=false, desc="Life lost" }
```

**Règle de formatage** :
1. Assignments sans espaces dans les accolades.
2. Une entrée = une ligne.
3. Hex en majuscules : `0X075A`, `0XFF0000`.
4. Ordre des champs : `address`, `type`, `condition`, `action`, `value`?, `bit`?, `mask`?, `min`?, `max`?, `no_log`?, `no_survey`?, `desc`, `comment`? (debug seulement).

**Définition Stricte des Champs :**
- `address` : L'adresse RAM en hexadécimal.
- `type` : `u8`, `u16le`/`u16be`, `u24le`/`u24be`, ou `u32le`/`u32be`. **Interdiction d'omettre `le` ou `be` pour $>8$ bits** (un `u32` nu forcera un *fallback* d'erreur à `u8`).
- `action` : Le nom standardisé V11 (ex: `LOSE_LIFE`, `OBJECT_DESTROYED`).
- `value` : Utilisé par `equal/eq`. **DOIT ETRE UN ENTIER (ex: `value=0x01`), JAMAIS UNE CHAINE (`value="0x01"`)**. Sans quoi le wrapper l'évaluera à 0 et causera des boucles infinies.

> **Note sur `comment`** : Présent en génération/debug pour tracer l'origine (description DC brute). **Non présent dans les MEM finaux produits.**

---

## PART VI: NORMALISATION DES CONDITIONS

| Condition    | Usage                                              |
|--------------|----------------------------------------------------|
| `change`     | Toute transition de valeur (mode, état, salle)     |
| `increase`   | Hausse significative (score, pièces, XP)           |
| `decrease`   | Baisse significative (vie, HP, timer, O2)          |
| `eq`         | Valeur exacte (état lifecycle, flag boss)          |
| `neq`        | Différent d'une valeur de référence                |
| `bit_true`   | Bit spécifique = 1 (flags compressés)              |
| `bit_false`  | Bit spécifique = 0 (flags compressés)              |
| `any`        | Fallback non-directionnel                          |

---

## PART VII: LEXICON — RÈGLE "KEYWORD ONLY" (Strict Mode V11)

### 7.1 Principe
En mode `is_dc=True` (source doflinx), seuls les **mots-clés extraits** de la description sont conservés comme `desc`. Si aucun mot-clé n'est trouvé → pas d'action, entrée routée vers `system.memory`.

### 7.2 Score Framing (Delta Logic)
`min`/`max` sont **uniquement** utilisés pour identifier des deltas de score précis :
```lua
{ address=0XFE10, type="u32be", condition="change", action="SCORE_STATE", min=100, max=100, desc="score" }
```

### 7.3 Bitmask Multiplexing
Pour les flags compressés, le générateur décompose en couples `bit_true`/`bit_false` :
```lua
{ address=0X00A1, type="u8", condition="bit_true",  bit=6, mask=0X40, action="INVINCIBILITY_START", desc="invincibility" }
{ address=0X00A1, type="u8", condition="bit_false", bit=6, mask=0X40, action="INVINCIBILITY_STOP",  desc="invincibility stopped" }
```

---

## PART VIII: MASTER BUILD RULE
All generated `.MEM` variables MUST be transformed into **Standardized Actions**. Logic is universal; Rendering is hardware-specific.
