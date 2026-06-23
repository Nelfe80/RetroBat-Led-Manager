# Event Keyword Coverage

Etat au 2026-06-14.

Ce document consolide les mots-clefs d'evenements issus de :

- `docs/mem_nomenclature_spec.md`
- `docs/superefficient_prompt.md`
- `default.mem.effects.json`

Objectif : avoir une base lisible pour decider ensuite quels effets, couleurs,
directions et temporisations appliquer a chaque famille d'evenements.

## Statuts

| Statut | Sens |
|---|---|
| `OK` | Action presente dans `default.mem.effects.json`. |
| `A ajouter` | Action autorisee par les specs/prompts, sans regle dediee; elle reste visible via le fallback `*`. |
| `Alias a mapper` | Action proche d'une action existante; visible via fallback, mais a mapper proprement. |
| `Wildcard a gerer` | Famille dynamique autorisee mais encore sans regle specifique; depuis le rework, le fallback `*` reste visible. |
| `A ignorer runtime` | Mot-clef utile pour extraction/filtrage, mais ne doit pas produire d'effet LED direct. |

## Couverture Generale

Le catalogue actuel couvre une grande partie de la nomenclature standard, mais
il est moins complet que le vocabulaire de `superefficient_prompt.md`.

Points importants :

- `COIN_GAIN`, `COIN_LOSE`, `OBJECT_DESTROYED`, `OBJECT_INTERACTION`,
  `BOSS_HIT`, `BOSS_DEFEATED`, `HIT`, `HEAL`, `LOSE_LIFE`, `GAIN_LIFE`
  sont presents et disposent maintenant d'effets plus visibles pour les cas
  recurrents.
- Les actions avec suffixe dynamique (`TRANSFORMATION_SUPER`,
  `HIT_KNOCKDOWN`, `PLAYER_STATE_SMALL`, etc.) sont resolues par wildcard
  catalogue quand une famille specifique existe.
- Un fallback `*` couvre les actions `mem` non mappees avec un `FLASH`
  court sur bouton aleatoire, pour eviter les evenements totalement muets.
- `SLOT:${event.slot}` peut maintenant tomber sur `fallbackTargets` quand
  l'evenement n'a pas de slot; le catalogue utilise surtout `RANDOM_BUTTON`
  ou `RANDOM_COLUMN` pour rester visible sans saturer le Pico.

## Panel Physique

Disposition reelle du panel :

```text
Haut : 4 3 5 7
Bas  : 1 2 6 8
```

Colonnes physiques gauche vers droite :

```text
[4,1] -> [3,2] -> [5,6] -> [7,8]
```

Sequences directionnelles a respecter :

| Nom logique | Ordre attendu |
|---|---|
| `left_to_right_columns` | `4+1`, puis `3+2`, puis `5+6`, puis `7+8` |
| `right_to_left_columns` | `7+8`, puis `5+6`, puis `3+2`, puis `4+1` |
| `bottom_to_top_bar` | barre `1+2+6+8`, puis barre `4+3+5+7` |
| `top_to_bottom_bar` | barre `4+3+5+7`, puis barre `1+2+6+8` |
| `top_row_left_to_right` | `4`, `3`, `5`, `7` |
| `bottom_row_left_to_right` | `1`, `2`, `6`, `8` |
| `all_buttons` | `1+2+3+4+5+6+7+8` |

Implementation : les effets `sweep` et `chase` acceptent maintenant `pattern`
et generent les slots par groupes physiques, au lieu de parcourir `1..8`.

## Actions Standard

| Action | Famille | Statut | Notes |
|---|---|---|---|
| `1UP` | resources.lives | Alias a mapper | Mapper vers `GAIN_LIFE` ou ajouter une regle dediee. |
| `DEAD` | resources.lives | OK | Deja rouge/pulse. |
| `HEAL` | resources.health | OK | Deja vert/flash. |
| `HIT` | resources.health | OK | Deja rouge/pulse. |
| `BOSS_HIT` | combat.boss | OK | Deja blanc/flash. |
| `BOSS_HEAL` | combat.boss | OK | Present. |
| `ITEM_GET` | inventory.items | OK | Present, proche de `INVENTORY_ITEM`. |
| `ITEM_USE` | inventory.items | OK | Present. |
| `SCORE` | scoring.points | OK | Present, route matrix score. |
| `SCORE_STATE` | scoring.points | OK | Present, route matrix score. |
| `NEW_LEVEL` | progression.level | OK | Present. |
| `TITLE_SCREEN` | flow.lifecycle | OK | Present. |
| `DEMO_MODE` | flow.lifecycle | OK | Present. |
| `GAMEPLAY` | flow.lifecycle | OK | Present. |
| `GAME_PLAYING` | flow.lifecycle | OK | Present. |
| `GAME_OVER` | flow.lifecycle | OK | Present. |
| `PAUSED` | flow.lifecycle | OK | Present. |
| `PAUSE_ON` | flow.lifecycle | OK | Present. |
| `PAUSE_OFF` | flow.lifecycle | OK | Present. |
| `UPDATE` | system.memory | OK | Present, mais effet direct a reconsiderer. |
| `UNKNOWN` | system.unmapped | OK | Present, effet direct a reconsiderer. |

## Progression Et Etats Globaux

| Action | Statut | Notes |
|---|---|---|
| `CORPORATE_SCREEN` | OK | Present. |
| `TITLE_SCREEN` | OK | Present. |
| `DEMO_MODE` | OK | Present. |
| `GAME_PLAYING` | OK | Present. |
| `GAMEPLAY` | OK | Present. |
| `GAME_OVER` | OK | Present. |
| `CREDITS` | OK | Present. |
| `CREDITS_SCREEN` | OK | Present. |
| `SAVING_ACTIVE` | OK | Present. |
| `SAVING_END` | OK | Present. |
| `PAUSE_ON` | OK | Present. |
| `PAUSE_OFF` | OK | Present. |
| `CONTINUE_SCREEN` | OK | Present. |
| `LEVEL_CLEAR` | OK | Present. |
| `QUEST_COMPLETE` | OK | Present. |
| `RANK_ACHIEVED` | OK | Present. |
| `RANK_STATE` | OK | Present. |
| `PLAYER_THEME` | OK | Present. |
| `LOADOUT_SELECTED` | OK | Present. |
| `SELECT_SCREEN` | OK | Present. |
| `STAGE_SELECT` | OK | Present. |
| `WORLD_MAP` | OK | Present. |
| `MAP_SCREEN` | A ajouter | Present dans le prompt, absent du catalogue. |
| `HOWTOPLAY_SCREEN` | A ajouter | Present dans les regles du prompt, absent du catalogue. |
| `LOADING_SCREEN` | OK | Present. |
| `START_GAME` | OK | Present. |
| `INTRO_SCREEN` | OK | Present. |
| `SETTINGS_CHANGED` | OK | Present. |
| `MODE_STATE` | A ajouter | Prompt : mode/settings, absent du catalogue. |
| `MODE_*` | Wildcard a gerer | Exemples : `MODE_TIMETRIAL`, `MODE_ENDLESS`, `MODE_VS`. |
| `DIFFICULTY_*` | Wildcard a gerer | Absent du catalogue actuel. |
| `PLAYER_SELECTED_*` | Wildcard a gerer | Absent du catalogue actuel. |
| `OPPONENT_SELECTED_*` | Wildcard a gerer | Absent du catalogue actuel. |
| `VEHICLE_SELECTED_*` | Wildcard a gerer | Absent du catalogue actuel. |

## Ressources Et Survie

| Action | Statut | Notes |
|---|---|---|
| `LIVES_STATE` | OK | Present. |
| `LOSE_LIFE` | OK | Present. |
| `GAIN_LIFE` | OK | Present. |
| `1UP` | Alias a mapper | Mapper vers `GAIN_LIFE`. |
| `HEALTH_STATE` | OK | Present. |
| `HIT` | OK | Present. |
| `HIT_*` | Wildcard a gerer | Exemples : `HIT_KNOCKDOWN`, `HIT_HURT`. |
| `HEAL` | OK | Present. |
| `DEAD` | OK | Present. |
| `DROWNING` | OK | Present. |
| `LOW_HEALTH_WARN` | OK | Present. |
| `LOW_RESOURCES_WARN` | OK | Present. |
| `RESOURCE_STATE` | OK | Present. |
| `RESOURCE_GAIN` | OK | Present. |
| `RESOURCE_LOSE` | OK | Present. |
| `RESOURCE_USED` | OK | Present. |
| `AMMO_STATE` | OK | Present. |
| `AMMO_GAIN` | OK | Present. |
| `AMMO_LOSE` | OK | Present. |
| `UNIT_COUNT` | OK | Present. |
| `UNIT_GAIN` | OK | Present. |
| `UNIT_LOSE` | OK | Present. |
| `STATS_UPDATE` | OK | Present. |

## Score, Monnaie Et Collectibles

| Action | Statut | Notes |
|---|---|---|
| `SCORE` | OK | Present. |
| `SCORE_STATE` | OK | Present. |
| `MONEY_STATE` | OK | Present. |
| `COIN_GAIN` | OK | Present mais trop discret actuellement (`flash_restore` 80 ms). |
| `COIN_LOSE` | OK | Present. |
| `RING_GAIN` | OK | Present. |
| `FUNDS_GAINED` | OK | Present. |
| `FUNDS_SPENT` | OK | Present. |
| `FUNDS_LOW_WARN` | OK | Present. |
| `EXPERIENCE_STATE` | OK | Present. |

## Inventaire Et Powerups

| Action | Statut | Notes |
|---|---|---|
| `INVENTORY_ITEM` | OK | Present. |
| `ITEM_GET` | OK | Present. |
| `ITEM_USE` | OK | Present. |
| `KEY_GET` | OK | Present. |
| `PASS_GET` | OK | Present. |
| `TREASURE` | OK | Present. |
| `TREASURE_*` | Wildcard a gerer | Autorise par prompt, absent en resolution prefixe. |
| `POWERUP_GET` | OK | Present. |
| `WEAPON_UPGRADE` | OK | Present. |
| `WEAPON_STATE` | A ajouter | Autorise par prompt, absent du catalogue. |
| `WEAPON_DAMAGED` | A ajouter | Autorise par prompt, absent du catalogue. |
| `DYNAMIC_INVENTORY` | OK | Present. |

## Combat

| Action | Statut | Notes |
|---|---|---|
| `BATTLE_START` | OK | Present. |
| `BATTLE_END` | OK | Present. |
| `BOSS_HIT` | OK | Present. |
| `BOSS_HEAL` | OK | Present. |
| `BOSS_DEFEATED` | OK | Present. |
| `ENEMY_HIT` | A ajouter | Vu dans le flux Mario 3; absent du catalogue. |
| `ENEMY_DEFEATED` | A ajouter | Autorise par prompt. |
| `ENEMY_PROXIMITY` | A ajouter | Autorise par prompt. |
| `KO` | A ajouter | Autorise par prompt. |
| `CRITICAL_HIT` | OK | Present. |
| `FATALITY` | OK | Present. |
| `FIRE_SIDEARM` | OK | Present. |
| `BOMB_FIRED` | OK | Present. |
| `PARRY_SUCCESS` | OK | Present. |
| `COMBO_HIT` | OK | Present. |
| `COMBO_CHAIN_HIT` | A ajouter | Autorise par prompt. |
| `ATTACKING` | A ajouter | Autorise par prompt. |
| `SPECIAL_ACTION` | OK | Present. |

## Mouvements Et Etats Joueur

| Action | Statut | Notes |
|---|---|---|
| `PLAYER_STATE` | OK | Present. |
| `PLAYER_STATE_*` | Wildcard a gerer | Exemples : `PLAYER_STATE_SMALL`, `PLAYER_STATE_MORPH_BALL`. |
| `PLAYER_STATE_SMALL` | A ajouter | Autorise par prompt. |
| `IDLE` | A ajouter | Autorise par prompt. |
| `WALKING` | A ajouter | Autorise par prompt. |
| `RUNNING` | OK | Present. |
| `RUN` | OK | Present, ancien verbe d'action map. |
| `JUMPING` | OK | Present. |
| `JUMP` | OK | Present, ancien verbe d'action map. |
| `CROUCHING` | A ajouter | Autorise par prompt. |
| `CROUCH` | OK | Present, ancien verbe d'action map. |
| `FALLING` | A ajouter | Autorise par prompt. |
| `SPINNING` | A ajouter | Autorise par prompt. |
| `SPIN` | OK | Present, ancien verbe d'action map. |
| `SWIMMING` | A ajouter | Autorise par prompt. |
| `CLIMBING` | A ajouter | Autorise par prompt. |
| `DIVING` | A ajouter | Autorise par prompt. |
| `SKIDDING` | A ajouter | Autorise par prompt. |
| `SKID` | OK | Present, ancien verbe d'action map. |
| `BUMPER` | A ajouter | Autorise par prompt. |
| `SPRING` | A ajouter | Autorise par prompt. |
| `SPECIAL_STAGE_ENTER` | OK | Present. |

## Etats Temporaires Et Transformations

| Action | Statut | Notes |
|---|---|---|
| `INVINCIBILITY_START` | OK | Present. |
| `INVINCIBILITY_STOP` | OK | Present. |
| `INVINCIBILITY_TIMER` | OK | Present. |
| `SPEED_START` | OK | Present. |
| `SPEED_STOP` | OK | Present. |
| `SPEED_STATE` | OK | Present. |
| `SPEED_TIMER` | OK | Present. |
| `SHIELD_GAIN` | OK | Present. |
| `SHIELD_LOST` | OK | Present. |
| `SHIELD_TIMER` | OK | Present. |
| `STEALTH_START` | OK | Present. |
| `STEALTH_STOP` | OK | Present. |
| `STEALTH_ALERT` | OK | Present. |
| `STEALTH_TIMER` | OK | Present. |
| `STATUS_EFFECT_START` | OK | Present. |
| `STATUS_EFFECT_STOP` | OK | Present. |
| `STATUS_EFFECT_TIMER` | OK | Present. |
| `POISON_START` | OK | Present. |
| `TRANSFORMATION` | OK | Present. |
| `TRANSFORMATION_START` | OK | Present. |
| `TRANSFORMATION_*` | OK | Couvert par wildcard prefixe. |
| `TRANSFORMATION_SUPER` | OK | Vu dans le flux Mario 3; resolu par `TRANSFORMATION_*`. |
| `TRANSFORMATION_OLD` | OK | Resolu par `TRANSFORMATION_*`. |
| `TRANSFORMATION_NORMAL` | OK | Resolu par `TRANSFORMATION_*`. |
| `TRANSFORMATION_POWERUP` | OK | Resolu par `TRANSFORMATION_*`. |
| `DYNAMIC_ALWAYS` | OK | Present. |
| `DYNAMIC_INFINITE` | OK | Present. |
| `DYNAMIC_MAX` | OK | Present. |
| `DYNAMIC_MODIFIER` | OK | Present. |
| `DYNAMIC_START` | OK | Present. |
| `DYNAMIC_ZERO` | OK | Present. |

## Objets, Monde Et Secrets

| Action | Statut | Notes |
|---|---|---|
| `OBJECT_INTERACTION` | OK | Present avec `fallbackTargets` si pas de slot. |
| `OBJECT_INTERACTION_CHECKPOINT` | OK | Couvert par fallback `*` ou a specialiser plus tard. |
| `OBJECT_DESTROYED` | OK | Present, mais cible actuelle fragile si pas de slot. |
| `OBJECT_BUILT` | OK | Present. |
| `OBJECT_REPAIRED` | OK | Present. |
| `BUILD_START` | A ajouter | Autorise par prompt. |
| `BUILD_END` | A ajouter | Autorise par prompt. |
| `DOOR_OPENED` | OK | Present. |
| `CHEST_OPENED` | OK | Present. |
| `PUZZLE_SOLVED` | OK | Present. |
| `SECRET_REVEALED` | OK | Present. |
| `ROOM_DISCOVERED` | OK | Present. |
| `GOAL_REACHED` | A ajouter | Autorise par prompt. |
| `EVENT_TRIGGER` | OK | Present. |
| `CAMERA_MOVE` | OK | Present. |
| `KEY_PRESSED` | OK | Present, mais attention au spam input. |

## Vehicules, Course Et Montures

| Action | Statut | Notes |
|---|---|---|
| `LAP_STATE` | OK | Present. |
| `LAP_COMPLETE` | OK | Present. |
| `ROUND_STATE` | OK | Present. |
| `GEAR_SHIFT` | OK | Present. |
| `TURBO_BOOST` | OK | Present. |
| `CRASH` | OK | Present. |
| `COLLISION` | OK | Present. |
| `MOUNT_START` | OK | Present. |
| `MOUNT_STOP` | OK | Present. |
| `MOUNT_STATE` | OK | Present. |
| `MOUNT_*` | Wildcard a gerer | Autorise par prompt. |

## Cutscenes, Dialogues Et Menus

| Action | Statut | Notes |
|---|---|---|
| `CINEMATIC_PLAYING` | OK | Present. |
| `CINEMATIC_END` | OK | Present. |
| `DIALOGUE_SCENE` | OK | Present. |
| `DIALOGUE_END` | OK | Present. |
| `CHOICE_PROMPT` | OK | Present. |
| `CHOICE_END` | OK | Present. |
| `MAP_VIEWING` | OK | Present. |
| `MAP_CLOSED` | OK | Present. |
| `MAP_SCREEN` | A ajouter | Prompt audio/screen states. |
| `HOWTOPLAY_SCREEN` | A ajouter | Prompt tutorial/help states. |
| `MINIGAME_ACTIVE` | OK | Present. |

## Environnement Et Simulation

| Action | Statut | Notes |
|---|---|---|
| `ENVIRONMENT_FORCE` | OK | Present. |
| `ENVIRONMENT_MOVE` | OK | Present. |
| `ENVIRONMENT_TIMER` | OK | Present. |
| `ENVIRONMENT_*` | Wildcard a gerer | Exemples : `ENVIRONMENT_UNDERWATER`, `ENVIRONMENT_SPACE`, `ENVIRONMENT_ICE`. |
| `DAY_TIME` | OK | Present. |
| `NIGHT_TIME` | OK | Present. |
| `WEATHER` | OK | Present. |
| `WEATHER_EFFECT` | OK | Present. |
| `WEATHER_CLEAR` | OK | Present. |
| `RAIN` | OK | Present. |
| `CRAFTING_START` | OK | Present. |
| `CRAFTING_END` | OK | Present. |
| `HACKING_START` | OK | Present. |
| `LOCKPICK_START` | OK | Present. |

## Timers

| Action | Statut | Notes |
|---|---|---|
| `GENERAL_TIMER` | OK | Present. |
| `TIMER_LOW` | OK | Present. |
| `TIMER_LOW_WARN` | OK | Present. |
| `BOMB_TIMER` | OK | Present. |
| `LEVEL_TIMER` | OK | Present. |
| `INVINCIBILITY_TIMER` | OK | Present. |
| `SPEED_TIMER` | OK | Present. |
| `SHIELD_TIMER` | OK | Present. |
| `STEALTH_TIMER` | OK | Present. |
| `STATUS_EFFECT_TIMER` | OK | Present. |
| `COMBO_TIMER` | OK | Present. |
| `COOLDOWN_TIMER` | OK | Present. |
| `ENVIRONMENT_TIMER` | OK | Present. |

## Actions Techniques Ou Legacy

| Action | Statut | Notes |
|---|---|---|
| `ACTION` | OK | Placeholder generique du catalogue. A reconsiderer. |
| `UDP_OUT` | OK | Placeholder/provenance. A reconsiderer. |
| `DC_CATEGORY_MAP` | OK | Ne devrait probablement pas produire d'effet runtime. |
| `FAMILY_ROUTING` | OK | Ne devrait probablement pas produire d'effet runtime. |
| `IGNORE` | OK | A ignorer runtime. |
| `UNKNOWN` | OK | Fallback; effet direct a discuter. |
| `STARE_UPDATE` | OK | Probable typo de `STATE_UPDATE`; a auditer. |
| `UPDATE` | OK | Fallback generique; effet direct a discuter. |

## Effets Actuels Disponibles

Ces effets sont implementes dans `DefaultEffectCatalog`.

| Effet | Role actuel | Variables actuelles |
|---|---|---|
| `pulse` | Couleur temporaire puis restore optionnel. | `targets`, `color`, `durationMs`, `restore` |
| `flash` | Alternance couleur/noir. | `targets`, `color`, `times`, `onMs`, `offMs`, `restore` |
| `flash_restore` | Flash firmware avec restore local Pico. | `target`, `color`, `onMs` ou `durationMs` |
| `blink` | Clignotement repete sur duree. | `targets`, `color`, `intervalMs`, `durationMs`, `restore`, `throttleMs` |
| `timer_warning` | Alias comportemental de `blink`. | idem `blink` |
| `strobe` | Cycle rapide de couleurs. | `targets`, `colors`, `intervalMs`, `durationMs`, `restore` |
| `celebrate` | Fanfare multi-couleurs. | `targets`, `colors`, `durationMs`, `restore` |
| `rainbow` | Sequence arc-en-ciel fixe. | `targets`, `durationMs`, `restore` |
| `sparkle` | Scintillement sur quelques slots aleatoires. | `targets`, `color`, `durationMs`, `restore` |
| `sweep` | Balayage par slots ou groupes physiques si `pattern` est fourni. | `targets`, `pattern`, `color`, `durationMs`, `intervalMs`, `restore` |
| `chase` | Balayage repete par slots ou groupes physiques si `pattern` est fourni. | `targets`, `pattern`, `color`, `durationMs`, `intervalMs`, `restore` |
| `column_scan` / `column_wipe` | Barre horizontale qui avance de colonne en colonne en effacant/restaurant la precedente. | `targets`, `pattern`, `color`, `intervalMs`, `durationMs`, `restore` |
| `column_bounce` | Variante aller-retour du scan horizontal. | `targets`, `pattern`, `color`, `intervalMs`, `restore` |
| `column_prism` / `column_bars` | Bandes de couleurs decalees par colonne, style raster/copper bars. | `targets`, `pattern`, `colors`, `times`, `intervalMs`, `restore` |
| `ambient` | Couleur fixe selon palette. | `targets`, `palette` |
| `dim` | Noir logique. | `targets` |
| `restore` | Noir logique pour declencher restore overlay. | `targets` |
| `matrix_score` | Score vers matrice. | `target`, `value`, `color`, `throttleMs` |

## Contraintes Firmware Pico

Reference : `fw/main.py`.

Le firmware impose des contraintes importantes a respecter quand on choisit les
effets et les couleurs.

### Couleurs

Sur les sorties GPIO RGB directes, les couleurs ne sont pas des valeurs RGB
classiques. Ce sont des pourcentages d'extinction par canal :

```text
0   = canal pleinement allume
100 = canal eteint
```

Couleurs primaires firmware :

| Couleur | Valeurs firmware |
|---|---|
| `WHITE` | `0,0,0` |
| `PINK` | `100,0,0` |
| `CYAN` | `0,100,0` |
| `YELLOW` | `0,0,100` |
| `BLUE` | `100,100,0` |
| `RED` | `100,0,100` |
| `GREEN` | `0,100,100` |
| `BLACK` | `100,100,100` |

Couleurs nuancees declarees :

```text
ORANGE, LIME, VIOLET, PURPLE, GRAY/GREY, GOLD, TURQUOISE, AQUA,
TEAL, MAGENTA, LEMON
```

Attention : si une nuance n'est pas PWM-safe a cause d'un conflit GPIO, le
firmware peut la rabattre via `FALLBACK` vers une couleur plus simple
(`GOLD -> YELLOW`, `PURPLE -> BLUE`, `GRAY -> BLACK`, etc.).

### Commandes Visibles Sur Le Panel Actuel

Commandes firmware utiles :

| Commande | Role | Remarques |
|---|---|---|
| `ALL <color>` | applique une couleur a toutes les sorties declarees | Force la couleur, evite des conflits PWM. |
| `SLOT <n> <color>` | applique une couleur a un bouton physique | Slots valides selon mapping Pico. |
| `FLASH <target> <color> <ms>` | flash une sortie puis restaure sa couleur precedente | `target` doit etre un slot ou un nom de sortie existant. |
| `BATCH a;b;c` | applique plusieurs commandes | Le firmware traite les OFF/BLACK avant les couleurs. |
| `ALLPCT r g b` | applique un pourcentage RGB aux boutons B1..B8 uniquement | N'inclut pas START/SELECT. |
| `ALLPCTPANEL r g b` | variante calibration avec controles | Inclut certains controles selon type. |
| `CLEAR` | eteint tout | Pour adressables, coupe aussi les bus. |

Dans `PicoCommandSender.p1.ini`, l'adaptateur `PicoFullPanelPwm` traduit
certains `ALL <color>` et certains `BATCH` uniformes vers `ALLPCT`.
Actuellement :

- `ALL RED` peut devenir `ALLPCT 100 0 100`.
- `ALL YELLOW` peut devenir `ALLPCT 0 0 100`.
- `ALL BLACK` peut devenir `ALLPCT 100 100 100`.
- Un `BATCH` complet des 8 slots avec la meme couleur peut aussi devenir
  `ALLPCT`.
- Les `FLASH` ne sont pas traduits par cet adaptateur et partent tels quels
  vers le firmware.

### Contraintes De Ciblage

- `FLASH` ne sait flasher qu'une sortie/slot existant.
- Un target vide ou invalide produit `ERR FLASH`.
- Une regle comme `SLOT:${event.slot}` ne marche que si l'evenement fournit
  vraiment `slot`.
- Si l'evenement n'a pas de slot, prevoir `fallbackTargets`, par exemple
  `RANDOM_BUTTON` ou `RANDOM_COLUMN`.
- Les cibles `STRIP1`, `MATRIX1`, `CIRCLE1`, `JOY1` doivent exister dans le
  hardware et ne sont pas visibles si `LedManager.ini` declare le nombre a 0.

### Contraintes De Temporisation

- `FLASH` restaure cote firmware via `pending_restores`.
- Si plusieurs flashs se superposent sur la meme sortie, le firmware conserve
  la couleur de depart du premier flash comme couleur de retour.
- Des flashs trop courts (`80ms`) peuvent etre quasiment invisibles sur le
  panel physique.
- Les effets PC multi-etapes (`flash`, `blink`, `sparkle`, `sweep`, `chase`)
  passent par la file serie. Il faut eviter les rafales trop longues, surtout
  sur des events frequents (`COIN_GAIN`, timers, score).
- Le firmware et LedManager ont deja une logique anti-backlog :
  derniers panels gagnants, dedupe d'etat, queues bornees.

### Implications Pour Les Futurs Effets

- La philosophie par defaut devient horizontale : raisonner en colonnes
  physiques `[4,1]`, `[3,2]`, `[5,6]`, `[7,8]`, avec effacement/restauration
  court de la colonne precedente pour creer l'illusion de mouvement.
- Les effets rares peuvent utiliser plusieurs colonnes colorees en meme temps
  (`column_prism`) pour produire des illusions type demo scene / copper bars.
- La contrainte d'intensite ne signifie pas reduire toutes les couleurs :
  les couleurs fortes (`WHITE`, `GOLD`, `ORANGE`, `PURPLE`) restent utiles
  si elles sont rares, isolees sur un slot/une colonne, ou associees a un
  evenement important.
- Les effets frequents ou generiques doivent plutot etre isoles
  (`RANDOM_BUTTON`, `RANDOM_COLUMN`) que rendus globaux. L'objectif est un
  rendu lisible sans saturer le Pico ni ecraser le panel de base.
- Ne pas transformer automatiquement un `ALL_BUTTONS` generique en 8 commandes
  `SLOT`: cela spamme le Pico. Les commandes par slot doivent rester reservees
  aux effets explicitement concus comme des bandes/colonnes (`column_flash_*`).
- Les effets globaux visibles doivent preferer `ALL_BUTTONS` ou des groupes de
  slots explicites plutot que `STRIP1`.
- Les effets rapides doivent rester simples : un `ALL` ou 2 a 4 commandes
  `SLOT`, pas une longue animation sur chaque pickup.
- Pour un effet de balayage coherent avec le panel, il faudra generer des
  `SLOT` par groupes physiques, pas `SLOT 1`, `SLOT 2`, ... `SLOT 8`.
- Les couleurs nuancees sont utilisables, mais les couleurs primaires sont plus
  fiables pour les feedbacks critiques.
- `GOLD` est semantiquement utile pour tresor/coin, mais peut etre mieux rendu
  en `YELLOW` sur le panel actuel selon calibration.

## Variables A Statuer Pour La Personnalisation

Variables utiles a introduire dans une prochaine evolution du catalogue :

| Variable | Pourquoi |
|---|---|
| `pattern` | Choisir `left_to_right_columns`, `bottom_to_top_bar`, `top_row_left_to_right`, etc. |
| `direction` | `left_to_right`, `right_to_left`, `bottom_to_top`, `top_to_bottom`, `in_out`, `out_in`. |
| `lanes` | Decrire des groupes de slots a allumer simultanement (`[[4,1],[3,2],[5,6],[7,8]]`). |
| `holdMs` | Temps de maintien apres la derniere etape. |
| `fade` | Prevoir plus tard un fondu ou une intensite progressive. |
| `intensity` | Couleur pleine ou attenuee, utile si le PWM est calibre. |
| `restoreMode` | `base_panel`, `black`, `previous`, `none`. |
| `fallbackTargets` | Cibles a utiliser si `${event.slot}` est absent ou si `STRIP1` est indisponible. |
| `minIntervalMs` | Anti-spam par action, plus lisible que `throttleMs` pour certains events. |
| `valueMode` | Declenchement selon `Value`, par exemple boss dead si `Value >= 3`. |

Variables deja introduites dans le rework :

| Variable | Etat |
|---|---|
| `pattern` | Actif sur `sweep` / `chase`. |
| `column_scan` | Actif pour les mouvements horizontaux avec effacement de colonne precedente. |
| `column_bounce` | Actif pour les mouvements horizontaux aller-retour. |
| `column_prism` | Actif pour les bandes de couleurs decalees par colonnes. |
| `fallbackTargets` | Actif sur les steps catalogue. |
| `RANDOM_BUTTON` | Cible virtuelle vers un `SLOT:1..8`. |
| `RANDOM_COLUMN` | Cible virtuelle vers une colonne physique parmi `[4,1]`, `[3,2]`, `[5,6]`, `[7,8]`. |
| Wildcards `PREFIX_*` | Actif dans `DefaultEffectCatalog`; la regle la plus specifique gagne. |
| Wildcard `*` | Actif comme fallback discret pour action `mem` non mappee. |

## Priorites Suite

1. Tester en live Mario 3 : `COIN_GAIN`, `OBJECT_DESTROYED`,
   `OBJECT_INTERACTION`, `BOSS_HIT`, `BOSS_DEFEATED`,
   `TRANSFORMATION_SUPER`, `DYNAMIC_INVENTORY`.
2. Ajuster les couleurs selon rendu reel Pico (`GOLD` peut tomber proche de
   `YELLOW`, `PURPLE` proche de `BLUE`).
3. Ajuster les tempos autour de `100ms` pour les actions frequentes, et garder
   les animations multi-commandes pour les evenements rares.
4. Decider quels placeholders techniques doivent rester visibles via `*` et
   lesquels doivent devenir explicitement silencieux.
