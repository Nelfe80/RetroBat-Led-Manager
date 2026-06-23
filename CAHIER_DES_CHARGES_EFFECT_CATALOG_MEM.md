# Gestion des effets LedManager — Catalogue par défaut basé sur les `.MEM`

## 1. Principe

Les effets du plugin ne doivent pas être codés au hasard. Ils doivent partir des **actions normalisées** présentes dans les fichiers `.MEM`.

Un `.MEM` observe la RAM du jeu et transforme une variation mémoire en action standardisée, par exemple :

```text
COIN_GAIN
HIT
LOSE_LIFE
GAIN_LIFE
INVINCIBILITY_START
LEVEL_CLEAR
GAME_OVER
SCORE_STATE
```

LedManager reçoit ces actions via APIExpose, cherche l'action dans un catalogue d'effets par défaut, puis déclenche un effet LED.  
Les fichiers `.evt` servent ensuite à surcharger ou spécialiser ces effets pour un jeu précis.

```text
.MEM → APIExpose → event mem.action → LedManager → default.mem.effects.json → effet LED → sender externe → Pico
```

## 2. Pourquoi un catalogue par défaut

Le catalogue par défaut garantit qu'un jeu fonctionne immédiatement dès qu'il dispose d'un `.MEM`, même sans fichier `.evt` spécifique.

Exemple :

```text
COIN_GAIN → flash jaune
HIT → pulse rouge
HEAL → flash vert
INVINCIBILITY_START → rainbow
LEVEL_CLEAR → animation de célébration
SCORE_STATE → affichage matrice
```

Le `.evt` du jeu ne sert qu'à améliorer le rendu.

Exemple Sonic :

```text
catalogue par défaut : COIN_GAIN = flash jaune
sonic.evt : COIN_GAIN = flash jaune uniquement sur boutons actifs + ring sur circle LED
```

## 3. Source des actions

Le catalogue est alimenté par les actions standardisées du lexique `.MEM`, par exemple `LIVES_STATE`, `HEALTH_STATE`, `COIN_GAIN`, `HIT`, `LEVEL_CLEAR`, `GAME_OVER`, `PAUSE_ON`, `PAUSE_OFF`, `BOSS_HIT`, `INVINCIBILITY_START`, `SPEED_START`, `SHIELD_GAIN`, `KEY_GET`, `TREASURE`, `DOOR_OPENED`, `BOMB_TIMER`, etc.

Le fichier `api_lexicon.py` associe déjà ces actions à des descriptions standardisées.  
Le générateur `.MEM` applique ensuite des règles de familles, de conditions et d'anti-spam.

## 4. Chaîne de résolution

Pour chaque event runtime reçu :

1. APIExpose publie un événement issu du `.MEM`.
2. LedManager lit :
   - `action`
   - `family`
   - `condition`
   - `value`
   - `player`
   - `system`
   - `rom`
3. LedManager cherche une règle jeu dans `.evt`.
4. Si aucune règle jeu ne correspond, il cherche une règle système.
5. Si aucune règle système ne correspond, il applique `default.mem.effects.json`.
6. Si aucune action exacte n'existe, il applique un fallback par famille.
7. Si aucun fallback ne correspond, l'événement est ignoré.

## 5. Priorité des règles

```text
1. game layout .evt
2. game .evt
3. system .evt
4. default.mem.effects.json action exacte
5. default.mem.effects.json famille
6. fallback silencieux
```

## 6. Layers d'effets

Le moteur garde plusieurs couches indépendantes :

```text
0  hardware
10 base_layout
20 selected
30 running
40 mame_outputs
50 mem_action
60 input_feedback
70 alert
90 error
```

Un effet `COIN_GAIN` temporaire ne détruit donc pas le layout de base.  
Il se joue par-dessus, puis restaure l'état précédent.

## 7. Catalogue par familles

### flow.lifecycle

| Action | Effet par défaut |
|---|---|
| TITLE_SCREEN | ambiance attract mode |
| DEMO_MODE | lightshow doux |
| GAME_PLAYING | restauration gameplay |
| PAUSE_ON | dim global |
| PAUSE_OFF | restauration |
| GAME_OVER | strobe rouge lent |
| CONTINUE_SCREEN | blink START / SELECT |
| LEVEL_CLEAR | célébration |

### resources.lives / resources.health

| Action | Effet par défaut |
|---|---|
| HIT | pulse rouge |
| HEAL | flash vert |
| LOSE_LIFE | flash rouge fort |
| GAIN_LIFE | flash vert / blanc |
| LOW_HEALTH_WARN | heartbeat rouge |
| DROWNING | pulse bleu accéléré |

### scoring.collectibles / scoring.points

| Action | Effet par défaut |
|---|---|
| COIN_GAIN | flash jaune |
| COIN_LOSE | scatter jaune / rouge |
| MONEY_STATE | update doux |
| SCORE_STATE | matrice score si disponible |

### state.temporary

| Action | Effet par défaut |
|---|---|
| INVINCIBILITY_START | rainbow / blanc-or |
| INVINCIBILITY_STOP | restore layer state |
| SPEED_START | chase bleu |
| SPEED_STOP | restore |
| SHIELD_GAIN | sweep vert/bleu |
| SHIELD_LOST | flash rouge |
| POISON_START | pulse violet |

### combat.enemies

| Action | Effet par défaut |
|---|---|
| BOSS_HIT | flash blanc |
| BOSS_DEFEATED | célébration |
| CRITICAL_HIT | flash jaune intense |
| PARRY_SUCCESS | spark blanc |
| FATALITY | rouge/noir |

### inventory.items

| Action | Effet par défaut |
|---|---|
| KEY_GET | flash vert/or |
| TREASURE | sparkle gold |
| CHEST_OPENED | sparkle court |
| WEAPON_UPGRADE | flash blanc/or |

### system.timer

| Action | Effet par défaut |
|---|---|
| GENERAL_TIMER | mise à jour silencieuse |
| TIMER_LOW_WARN | blink orange/rouge |
| BOMB_TIMER | heartbeat rouge accéléré |
| INVINCIBILITY_TIMER | rainbow si actif |

## 8. Format du catalogue

Fichier :

```text
resources/effects/default.mem.effects.json
```

Exemple :

```json
{
  "schema": "ledmanager.default_effect_catalog.v1",
  "actionRules": {
    "COIN_GAIN": {
      "when": { "trigger": "mem.action", "action": "COIN_GAIN" },
      "then": [
        {
          "effect": "flash",
          "targets": ["ALL_BUTTONS"],
          "color": "YELLOW",
          "times": 2,
          "onMs": 50,
          "offMs": 50,
          "restore": true,
          "priority": 50
        }
      ]
    }
  }
}
```

## 9. Exemple avec Sonic

Event `.MEM` :

```text
action = COIN_GAIN
family = scoring.collectibles
desc = Collected rings
```

Sans `.evt`, LedManager applique :

```text
flash jaune sur ALL_BUTTONS
```

Avec `sonic.evt`, on peut spécialiser :

```json
{
  "id": "sonic_ring_collected",
  "when": {
    "trigger": "mem.action",
    "action": "COIN_GAIN"
  },
  "then": [
    {
      "effect": "flash",
      "targets": ["ALL_BUTTONS", "CIRCLE1"],
      "color": "YELLOW",
      "times": 2,
      "restore": true
    }
  ]
}
```

## 10. Anti-spam

Les `.MEM` peuvent contenir des timers, compteurs ou états très fréquents.  
Le générateur marque certains événements avec `no_log` ou `no_survey`.

LedManager doit respecter ces informations :

```text
no_survey=true → ne pas déclencher d'effet
no_log=true + événement fréquent → effet possible mais throttlé
```

Règles recommandées :

```text
score.changed        max 10-20 Hz
timer.changed        max 4 Hz
axis.changed         max 30 Hz
matrix score         max 10 Hz
flash transient      cooldown 50-100 ms par action
```

## 11. Interaction avec les `.evt`

Le `.evt` ne remplace pas le catalogue par défaut.  
Il le surcharge.

Exemple :

```json
{
  "extends": "default.mem.effects",
  "rules": [
    {
      "id": "override_coin_gain",
      "when": { "trigger": "mem.action", "action": "COIN_GAIN" },
      "then": [
        {
          "effect": "flash",
          "targets": ["SLOT:1", "SLOT:2"],
          "color": "YELLOW",
          "times": 3,
          "restore": true
        }
      ]
    }
  ]
}
```

## 12. Nombre d'actions extraites

Le catalogue généré contient actuellement **178 actions candidates** issues du lexique et des spécifications `.MEM`.

Le fichier JSON généré est une base de départ : il faudra ensuite l'affiner action par action selon les tests réels.

## 13. Fichiers générés

- `default.mem.effects.json`
- `CAHIER_DES_CHARGES_EFFECT_CATALOG_MEM.md`
