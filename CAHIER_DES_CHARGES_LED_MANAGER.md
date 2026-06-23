# Cahier des charges — LedManager v4 multi-Pico / multi-player

## 1. Objectif de la mise à jour

LedManager doit pouvoir gérer plusieurs contrôleurs physiques en parallèle.

Cas typique :

```text
Pico 1 = panel joueur 1
Pico 2 = panel joueur 2
Pico 3 = éléments globaux optionnels : topper, bande LED, matrice, start/coin central
```

LedManager doit donc connaître la notion de **player matériel**, router les commandes vers le bon exécutable/sender, et rendre la variable `{player}` disponible partout :

- dans les `.evt` ;
- dans les templates de commande ;
- dans le moteur d'effets ;
- dans la résolution des targets ;
- dans les logs ;
- dans les commandes envoyées à l'exécutable.

---

## 2. Architecture multi-cibles

```text
APIExpose
   ↓ WebSocket / HTTP
LedManager
   ↓ résolution player + targets
CommandSender[P1] → PicoCommandSender.exe → Pico joueur 1
CommandSender[P2] → PicoCommandSender.exe → Pico joueur 2
CommandSender[GLOBAL] → PicoCommandSender.exe ou autre exe → LEDs globales
```

Chaque sender est une instance configurée dans `LedManager.ini`.

LedManager ne doit pas supposer qu'il y a un seul Pico.

---

## 3. Principes de routage

### 3.1. Routage par player

Si une action concerne un bouton avec `Player = 1`, LedManager route vers le sender déclaré pour `Player 1`.

Exemple :

```json
{
  "action": "setSlot",
  "player": 1,
  "slot": 1,
  "color": "RED"
}
```

devient :

```text
Sender = P1
Commande = SLOT 1 RED
```

Si une action concerne `Player = 2` :

```json
{
  "action": "setSlot",
  "player": 2,
  "slot": 1,
  "color": "BLUE"
}
```

devient :

```text
Sender = P2
Commande = SLOT 1 BLUE
```

### 3.2. Routage global

Si `player = 0`, `player = null` ou si l'événement est global, LedManager utilise le sender global :

```text
GLOBAL
CABINET
SHARED
```

Exemples :

- bande LED autour de la borne ;
- topper ;
- matrice score centrale ;
- coin central ;
- START commun ;
- effet attract mode ;
- erreur système.

### 3.3. Fallback

Si le player demandé n'a pas de sender dédié :

1. utiliser le sender `GLOBAL` si configuré ;
2. sinon utiliser `DEFAULT` ;
3. sinon ignorer l'action avec log Debug ou Warning selon criticité.

---

## 4. Configuration LedManager.ini multi-Pico

Exemple avec un Pico par joueur :

```ini
[CommandSenders]
Default=P1
Global=GLOBAL

[CommandSender:P1]
Name=Pico Player 1
Enabled=true
Player=1
Executable=E:\RetroBat\plugins\LedManager\PicoCommandSender.exe
Mode=daemon
Arguments=daemon --ini "E:\RetroBat\plugins\LedManager\PicoCommandSender.ini" --sender P1
UseStdIn=true
LineEnding=\n

[CommandSender:P2]
Name=Pico Player 2
Enabled=true
Player=2
Executable=E:\RetroBat\plugins\LedManager\PicoCommandSender.exe
Mode=daemon
Arguments=daemon --ini "E:\RetroBat\plugins\LedManager\PicoCommandSender.ini" --sender P2
UseStdIn=true
LineEnding=\n

[CommandSender:GLOBAL]
Name=Global LEDs
Enabled=true
Player=0
Executable=E:\RetroBat\plugins\LedManager\PicoCommandSender.exe
Mode=daemon
Arguments=daemon --ini "E:\RetroBat\plugins\LedManager\PicoCommandSender.ini" --sender GLOBAL
UseStdIn=true
LineEnding=\n
```

---

## 5. Configuration des templates avec variable player

```ini
[CommandTemplates]
SetOutput=SET {target} {color}
SetSlot=SLOT {slot} {color}
SetSystem={target} {state}
SetJoy={target} {color}
Clear=CLEAR
All=ALL {color}
Batch=BATCH {items}
MatrixScore=MATRIXSCORE {target} {value} {color}
MatrixText=MATRIXTEXT {target} {color} {text}
```

Les variables disponibles dans les templates deviennent :

```text
{player}
{sender}
{senderName}
{target}
{targetPlayer}
{slot}
{color}
{state}
{value}
{text}
{payload}
{row}
{system}
{rom}
{emulator}
{core}
{layout}
```

Même si le template Pico n'utilise pas `{player}`, cette variable doit être disponible pour les outils externes.

Exemple pour un outil custom :

```ini
SetSlotArgs=--player "{player}" --slot "{slot}" --color "{color}"
SetOutputArgs=--player "{player}" --target "{target}" --color "{color}"
```

---

## 6. Configuration PicoCommandSender

Le process `PicoCommandSender` utilise un seul fichier :
`PicoCommandSender.ini`. Les sections suffixees `:P1`, `:P2` et `:GLOBAL`
decrivent chaque instance.

### Pico joueur 1

```ini
[Serial:P1]
Port=COM8
BaudRate=115200

[Pico:P1]
AutoInitFromHardware=true

[Hardware:P1]
PanelButtons=8
PanelButtonType=RGBLED
Start=LED
Select=LED
Joystick1=NONE
Joystick2=NONE
OnOffInvert=true

[GPIO:P1]
B1=0,1,2
B2=3,4,5
START=27
SELECT=28
```

`AutoInitFromHardware=true` construit l'init firmware depuis la topologie user.
L'utilisateur n'ecrit plus de noms internes comme `GPIO_8B_SS_GPIO`.

Types utilisateur :

```text
NONE   -> absent
LED    -> LED simple ON/OFF, 1 GPIO
RGBLED -> LED RGB directe, 3 GPIO
ADDRLED -> LED adressable WS2812/NeoPixel
```

Le sender genere ensuite `PING`, `INIT`, `PTR`, `COMMIT`, `ONOFFINVERT` et
`GET`. Les gros setups adressables ou experimentaux peuvent utiliser :

```ini
[Advanced:P1]
InitCommandsOverride=
```

La policy couleur est dans le meme fichier :

```ini
[ColorPolicy]
; panel 6 boutons RGB
RgbTargets=1,2,3,4,5,6
IgnoredTargets=START,SELECT

; panel 6 boutons + 2 joysticks RGB
RgbTargets=1,2,3,4,5,6,JOY1,JOY2
IgnoredTargets=START,SELECT

[ColorPolicy.Fallbacks]
GRAY=WHITE
```

`GRAY` est le nom canonique unique pour le gris.

### Pico joueur 2

```ini
# PicoCommandSender.ini
[Serial:P2]
Port=COM9
BaudRate=115200

[Pico:P2]
InitCommands=PING|HW GPIO_8B_SS_GPIO|ONOFFINVERT ON|GET
```

### Pico global

```ini
# PicoCommandSender.ini
[Serial:GLOBAL]
Port=COM10
BaudRate=115200

[Pico:GLOBAL]
InitCommands=PING|HW ADDR_MATRIX_16X16|GET
```

---

## 7. Résolution des données APIExpose

Dans le flux APIExpose, les données contiennent déjà des notions de player :

```json
{
  "Player": 1,
  "Slot": 1,
  "Color": "Red"
}
```

LedManager doit utiliser :

```text
ActiveLayout.Players[].Index
ActiveLayout.Players[].Buttons[].Player
ActivePanel.Slots[].Inputs[].Player
ActivePanel.ControlMap.Inputs[].Player
ActivePanel.ControlMap.Outputs[].Player
ActivePanel.ExternalOutputs[].Player
ActivePanel.ExternalAxes[].Player
```

Règles :

- `Player = 1` → sender P1 ;
- `Player = 2` → sender P2 ;
- `Player = 0` → GLOBAL ;
- `Player = null` → GLOBAL ;
- output sans player mais avec `usage=start_select` → GLOBAL ou sender défini par mapping ;
- output avec player explicite → sender du player.

---

## 8. Mapping avancé des outputs

Certains outputs sont globaux mais doivent allumer des éléments d'un joueur.

Exemple :

```json
{
  "name": "lamp1",
  "usage": "view",
  "player": 1,
  "color": "Red"
}
```

Routage :

```text
sender P1
target OUTPUT:lamp1 ou SLOT assigné par layout
```

Exemple global :

```json
{
  "name": "EXP_LAMP_0",
  "usage": "explosion",
  "player": null
}
```

Routage :

```text
GLOBAL si disponible
sinon ALL players si règle .evt le demande
```

Un `.evt` peut forcer le routage :

```json
{
  "effect": "flash",
  "targets": ["ALL_BUTTONS"],
  "players": [1, 2],
  "color": "ORANGE"
}
```

---

## 9. Extension du format `.evt`

### 9.1. Filtrer par player

```json
{
  "id": "p1_button_press",
  "when": {
    "trigger": "input.press",
    "player": 1,
    "input": "B1"
  },
  "then": [
    {
      "effect": "flash",
      "players": [1],
      "targets": ["B1"],
      "color": "WHITE",
      "restore": true
    }
  ]
}
```

### 9.2. Utiliser le player de l'événement

```json
{
  "id": "any_player_button_feedback",
  "when": {
    "trigger": "input.press",
    "input": "*"
  },
  "then": [
    {
      "effect": "flash",
      "players": ["${event.player}"],
      "targets": ["${event.target}"],
      "color": "WHITE",
      "restore": true
    }
  ]
}
```

### 9.3. Effet sur tous les joueurs

```json
{
  "id": "global_explosion",
  "when": {
    "trigger": "mame.output.changed",
    "name": "EXP_LAMP_*",
    "value": true
  },
  "then": [
    {
      "effect": "pulse",
      "players": ["all"],
      "targets": ["ALL_BUTTONS"],
      "color": "ORANGE",
      "durationMs": 250,
      "restore": true
    }
  ]
}
```

### 9.4. Effet sur le sender global

```json
{
  "id": "score_matrix_global",
  "when": {
    "trigger": "score.changed"
  },
  "then": [
    {
      "effect": "matrix_score",
      "sender": "GLOBAL",
      "target": "MATRIX1",
      "value": "${event.score}",
      "color": "GREEN"
    }
  ]
}
```

---

## 10. Variables disponibles dans `.evt`

```text
${event.player}
${event.players}
${event.target}
${event.slot}
${event.input}
${event.output}
${event.value}
${event.color}
${context.system}
${context.rom}
${context.core}
${context.emulator}
${context.layout}
${sender.name}
${sender.player}
```

Ces variables doivent aussi pouvoir alimenter les templates de commandes.

---

## 11. Cibles LED multi-player

Les cibles doivent pouvoir être qualifiées :

```text
B1
P1:B1
P2:B1
PLAYER:1:B1
PLAYER:2:SLOT:1
SLOT:1
P1:SLOT:1
P2:SLOT:1
ALL_PLAYERS
ALL_BUTTONS
P1:ALL_BUTTONS
P2:ALL_BUTTONS
GLOBAL:MATRIX1
GLOBAL:STRIP1
```

Règles :

- `B1` sans player utilise le player de l'événement si disponible.
- `P1:B1` force player 1.
- `GLOBAL:MATRIX1` force sender global.
- `ALL_PLAYERS` duplique l'effet sur tous les senders player activés.

---

## 12. Batching multi-Pico

Le batching doit être fait par sender.

Exemple d'actions :

```text
P1 B1 RED
P1 B2 BLUE
P2 B1 YELLOW
P2 B2 GREEN
GLOBAL MATRIXSCORE 12345 GREEN
```

LedManager doit produire :

```text
Sender P1:
BATCH B1 RED;B2 BLUE

Sender P2:
BATCH B1 YELLOW;B2 GREEN

Sender GLOBAL:
MATRIXSCORE MATRIX1 12345 GREEN
```

Ne jamais mélanger dans un même batch des commandes destinées à deux Picos différents.

---

## 13. États et layers par player

Le moteur d'état doit être partitionné par sender/player :

```text
State[P1].Layer[base]
State[P1].Layer[input]
State[P2].Layer[base]
State[P2].Layer[input]
State[GLOBAL].Layer[matrix]
```

Un effet temporaire sur P1 ne doit pas restaurer ou modifier P2.

---

## 14. Cas d'usage

### 14.1. Deux panels identiques

```text
Pico P1 -> 8 boutons RGB + start/select
Pico P2 -> 8 boutons RGB + start/select
```

Un jeu 2 joueurs allume les boutons utiles pour chaque joueur.

### 14.2. Jeu alterné

Si `alternating = 1`, LedManager peut :

- garder les deux panels allumés ;
- ou n'allumer que le joueur actif si APIExpose expose l'information runtime.

### 14.3. Sonic single player sur borne 2 joueurs

Le player 1 reçoit le layout principal.

Le player 2 peut :

- rester éteint ;
- afficher une ambiance système ;
- reproduire l'effet global selon configuration.

### 14.4. Explosion globale

Un événement explosion peut flasher :

```text
P1 ALL_BUTTONS
P2 ALL_BUTTONS
GLOBAL STRIP1
```

### 14.5. Score sur matrice globale

Même si le score appartient au player 1, l'affichage score peut être routé vers `GLOBAL:MATRIX1`.

---

## 15. Évolutions LedManager.ini

Option de mapping player :

```ini
[Players]
Enabled=true
PlayerCount=2
DefaultPlayer=1
GlobalSender=GLOBAL
MissingPlayerFallback=GLOBAL
BroadcastGlobalEffects=true

[PlayerRouting]
1=P1
2=P2
0=GLOBAL
GLOBAL=GLOBAL
```

Option d'affectation de targets globales :

```ini
[TargetRouting]
MATRIX1=GLOBAL
STRIP1=GLOBAL
CIRCLE1=GLOBAL
START=P1
SELECT=P1
COIN=GLOBAL
```

Ou pour deux sets start/select :

```ini
[TargetRouting:P1]
START=P1
SELECT=P1

[TargetRouting:P2]
START=P2
SELECT=P2
```

---

## 16. Critères d'acceptation multi-Pico

- LedManager lance plusieurs instances de sender.
- Chaque sender possède son propre `.ini`.
- `{player}` est disponible dans `.evt`.
- `{player}` est disponible dans les templates de commandes.
- Un bouton P1 ne s'envoie pas au Pico P2.
- Un effet global peut être broadcast sur P1 et P2.
- Les batches sont séparés par Pico.
- Une déconnexion du Pico P2 ne coupe pas le Pico P1.
- Le sender GLOBAL peut piloter une matrice ou une bande LED.
- Les outputs MAME avec `Player = null` sont routés vers GLOBAL ou broadcast selon règle.
- Les logs indiquent toujours le sender et le player.

---

## 17. Logs attendus

```text
[LedManager] Event input.press player=1 target=B1
[LedManager] Route player=1 sender=P1 command="SET B1 WHITE"
[LedManager] Event input.press player=2 target=B1
[LedManager] Route player=2 sender=P2 command="SET B1 WHITE"
[LedManager] Event score.changed player=1 value=12345
[LedManager] Route sender=GLOBAL command="MATRIXSCORE MATRIX1 12345 GREEN"
```

---

## 18. Résumé

LedManager doit devenir multi-cibles :

```text
un événement -> une ou plusieurs actions -> un ou plusieurs senders -> un ou plusieurs Picos
```

La variable `{player}` devient une donnée centrale du système.

Elle doit être disponible :

- dans les events normalisés ;
- dans les règles `.evt` ;
- dans les templates `.ini` ;
- dans le routing ;
- dans les logs ;
- dans les commandes externes.
