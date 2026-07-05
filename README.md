# LedManager

LedManager transforme les etats et evenements exposes par APIExpose en commandes
LED generiques.

## Installation

Le dossier doit être installé ici :

```text
E:\RetroBat\plugins\LedManager
```

### Scripts disponibles

| Script | Description |
|--------|-------------|
| `install-es-start-hook.bat` | Installe le lancement automatique au démarrage d'EmulationStation. |
| `uninstall-es-start-hook.bat` | Retire uniquement le hook LedManager. Ne touche pas `updatestores.bat`. |
| `stop.bat` | Arrête le process LedManager (et ses senders). |

Le hook installe ce fichier côté EmulationStation :

```text
emulationstation\.emulationstation\scripts\start\LedManager-start.bat
``` Le projet est volontairement decoupe en trois responsabilites :

- `APIExpose` publie les faits : panel courant, debut/fin de jeu, signaux
  ingame, outputs arcade, scores.
- `LedManager.exe` orchestre les intentions : snapshot panel, session ingame,
  overlays, restore, deduplication et routage vers un sender.
- `PicoCommandSender.exe` adapte ces commandes generiques a un materiel donne :
  port serie, init Pico, protocole firmware, optimisations propres au panel.

Le firmware Pico doit rester simple : il execute des commandes. Il ne porte pas
la logique ingame, les palettes full-panel ou les choix de routage.

## Flux

```text
APIExpose websocket events
  -> LedManager.exe
     -> commandes generiques configurees par LedManager.ini
        -> PicoCommandSender.exe ou autre sender
           -> init materielle du firmware depuis [Hardware:<sender>] + [GPIO:<sender>]
           -> adaptation materielle optionnelle configuree par PicoCommandSender.ini
              -> firmware / programme LED cible
```

## Outputs Arcade MAME

Le flux `/ws/arcade` transporte les sorties MAME natives, par exemple
`mame.output.changed` avec des signaux comme `READY_LAMP`, `RELOAD_LAMP` ou
`TORP_LAMP_1`. Ces signaux ne sont pas des effets ingame classiques : ce sont
des etats de lampes.

LedManager les mappe via les `system_outputs` du panel courant ou du fichier
dynpanel du jeu :

```text
signal MAME -> output panel -> slot physique -> couleur de reference
```

Regle appliquee :

- `Value != 0` allume le slot avec la couleur de reference du panel.
- `Value == 0` eteint le slot en `BLACK`.
- l'etat courant des outputs arcade est conserve separement des effets
  ingame, afin qu'un restore du panel ne rallume pas une lampe eteinte par
  MAME.

Exemple `seawolf` :

```text
TORP_LAMP_1=1 -> SLOT 1 RED
TORP_LAMP_1=0 -> SLOT 1 BLACK
READY_LAMP=1  -> SLOT 4 WHITE
RELOAD_LAMP=1 -> SLOT 7 YELLOW
```

## Commandes Generiques LedManager

LedManager ne connait pas le protocole final du materiel. Il remplit des
templates INI dans la section `[CommandTemplates]`.

```ini
[CommandTemplates]
SetOutput=SET {target} {color}
SetSlot=SLOT {slot} {color}
SetSystem={target} {state}
Clear=CLEAR
All=ALL {color}
Batch=BATCH {items}
MatrixScore=MATRIXSCORE {target} {value} {color}
MatrixText=MATRIXTEXT {target} {color} {text}
Flash=FLASH {target} {color} {durationMs}
```

Variables disponibles :

- `{target}` : cible normalisee, par exemple `START`, `SELECT`, `MATRIX1`.
- `{slot}` : numero de slot physique, par exemple `1`.
- `{color}` : couleur normalisee en majuscules, par exemple `CYAN`.
- `{state}` : etat brut d'un evenement systeme.
- `{value}` : valeur numerique ou texte court, par exemple un score.
- `{text}` : texte a afficher.
- `{durationMs}` : duree d'un flash.
- `{player}` : joueur logique.
- `{sender}` : identifiant du sender choisi.
- `{senderName}` : nom lisible du sender.
- `{targetPlayer}` : joueur extrait de la cible si disponible.
- `{payload}` : payload JSON brut.
- `{system}` : systeme APIExpose.
- `{rom}` : rom APIExpose.

Commandes produites par defaut :

- `SLOT 1 RED` : applique une couleur a un bouton/slot.
- `SLOT 1 PCT:100,25,25` : applique une valeur PWM explicite si le firmware cible la supporte.
- `SET START ORANGE` : applique une couleur/etat a une sortie nommee.
- `FLASH 6 YELLOW 80` : overlay bref, le firmware restaure ensuite.
- `BATCH SLOT 1 CYAN;SLOT 2 BLUE` : panel ou restore groupe.
- `ALL GREEN` : effet global.
- `CLEAR` : extinction globale.
- `MATRIXSCORE MATRIX1 12345 GREEN` : score live.
- `MATRIXTEXT MATRIX1 WHITE READY` : texte matrice.

Pour adapter LedManager a un autre programme LED, modifier uniquement
`[CommandTemplates]`. Exemple :

```ini
[CommandTemplates]
SetSlot=button:{slot}:color:{color}
SetOutput=output:{target}:color:{color}
Flash=flash:{target}:{color}:{durationMs}
Batch=batch {items}
MatrixScore=score {target} {value} {color}
```

Pour brancher une carte LED differente du Pico actuel, voir
[`README_CARTES_LED_EXTERNES.md`](README_CARTES_LED_EXTERNES.md).

## Routage Des Senders

Les senders sont declares dans `LedManager.ini`.

```ini
[CommandSenders]
Default=P1
Global=GLOBAL

[PlayerRouting]
0=P1
1=P1
2=P2

[TargetRouting]
MATRIX1=GLOBAL
STRIP1=GLOBAL
CIRCLE1=GLOBAL

[CommandSender:P1]
Name=Pico Player 1
Enabled=true
Player=1
Executable=PicoCommandSender.exe
Mode=daemon
Arguments=daemon --ini "PicoCommandSender.ini" --sender P1
UseStdIn=true
LineEnding=\n
DryRun=false
StartupDelayMs=18000
QueueCapacity=16
MaxQueueAgeMs=150
SendIntervalMs=10
```

`LedManager.exe` peut donc piloter un Pico, un autre executable local, un pont
reseau, ou un programme tiers tant que celui-ci accepte les commandes definies
par les templates.

## Adaptation Materielle Dans PicoCommandSender

`PicoCommandSender.exe` est le bon endroit pour les contraintes specifiques au
Pico et a son firmware. Par defaut, il transmet la commande recue telle quelle.

Avant de transmettre ou d'adapter les commandes runtime, chaque instance du
sender initialise son Pico depuis `PicoCommandSender.ini`. L'utilisateur decrit
son materiel avec des mots simples :

```ini
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
...
START=27
SELECT=28
```

Types utilisateur :

- `NONE` : element absent.
- `LED` : LED simple ON/OFF, 1 GPIO.
- `RGBLED` : LED RGB directe, 3 GPIO.
- `ADDRLED` : LED adressable WS2812/NeoPixel. Les gros setups adressables
  peuvent temporairement utiliser `[Advanced:<sender>] InitCommandsOverride`.

Le sender genere ensuite `PING`, `INIT`, les `PTR`, `COMMIT`,
`ONOFFINVERT ON` et `GET`. L'utilisateur n'a donc pas besoin d'ecrire un nom de
profil firmware interne.

Le firmware expose aussi deux commandes de diagnostic pour que le sender puisse
verifier la compatibilite du Pico branche :

```text
VERSION -> VERSION DYNAMIC_PANEL_ADDR 2026.06.20
CAPS    -> CAPS PING,INIT,PTR,BUS,HW,GPIO,SLOT,SLOTPWM,...
```

Un adaptateur peut etre active dans le INI du sender :

```ini
[CommandAdapter]
Enabled=true
Mode=PicoFullPanelPwm
# Convertit uniquement un BATCH complet et uniforme des slots 1..8.
FullPanelSlots=8
FullPanelCommand=ALLPCT {r} {g} {b}

[CommandAdapter.FullPanelColors]
WHITE=0,0,0
PINK=100,0,50
CYAN=0,100,0
YELLOW=0,0,100
BLUE=100,100,0
RED=100,0,100
GREEN=0,100,100
BLACK=100,100,100
LIME=25,100,100
VIOLET=100,75,0
PURPLE=100,25,25
GRAY=75,75,75
TURQUOISE=25,100,0
AQUA=25,100,25
MAGENTA=100,0,0
LEMON=0,25,100

[CommandAdapter.FullPanelAliases]
ORANGE=YELLOW
GOLD=YELLOW
TEAL=TURQUOISE
```

Regle importante : l'adaptateur ne doit modifier que les commandes qu'il sait
traduire sans ambiguite. La policy couleur externe peut aussi adapter les
commandes unitaires et les lots partiels :

```text
SLOT 4 ORANGE          -> SLOTPWM 4 ORANGE, si ORANGE est valide en one-slot
SLOT 4 GOLD            -> SLOTPWM 4 YELLOW, si GOLD est refuse et a un fallback
FLASH 6 YELLOW 80     -> FLASH 6 YELLOW 80
BATCH SLOT 1 CYAN;... -> ALLPCT 0 100 0, seulement si slots 1..8 sont CYAN
ALL WHITE              -> ALLPCT 0 0 0
ALL ORANGE             -> ALLPCT 0 0 100, via alias ORANGE=YELLOW
```

Les commandes `ALL` ne sont pas dedupliquees par LedManager : elles servent de
repaint global et doivent pouvoir etre renvoyees meme si la derniere couleur
logique etait identique.

Les alias full-panel evitent qu'un effet couvre les 8 boutons avec une couleur
validee seulement en one-slot. La substitution reste locale au sender Pico et
LedManager conserve des couleurs logiques agnostiques.

### Policy Couleur

La policy couleur vit dans `PicoCommandSender.ini`. Elle permet d'ajuster les
couleurs sans recompiler l'executable. `GRAY` est le nom canonique unique pour
le gris.

```ini
[ColorPolicy]
Enabled=true
Unknown=allow
OnDeny=fallback
SlotCommand=SLOTPWM
UseSlotCommandForAllowed=true

# Targets RGB comptes dans la charge couleur.
# Pour un panel 6 boutons, garder seulement 1..6.
# Pour des joysticks RGB, ajouter JOY1,JOY2 ou les noms du profil firmware.
# Pour des joysticks ON/OFF, ne pas les mettre ici.
RgbTargets=1,2,3,4,5,6,7,8
IgnoredTargets=START,SELECT

[ColorPolicy.Fallbacks]
GOLD=YELLOW
PURPLE=VIOLET
GRAY=WHITE

[ColorPolicy.Single]
ORANGE=1:allow,2:allow,4:allow,6:allow,8:allow
GOLD=1:deny,2:allow,4:allow,6:deny,8:deny

[ColorPolicy.Pair]
ORANGE|LIME=2+6:allow,4+4:allow,6+2:allow
GOLD|LEMON=2+6:deny,4+4:deny,6+2:deny
```

La policy raisonne sur les targets RGB declarees, pas sur un panel fixe. Elle
reste donc compatible avec 4, 6 ou 8 boutons, avec un ou deux joysticks RGB, et
avec des controles `START`/`SELECT` ON/OFF ignores de la charge RGB. Quand une
regle exacte manque, le sender prend la regle de count la plus proche ; si une
combinaison est inconnue, `Unknown=allow` laisse passer la commande.

Ainsi LedManager reste agnostique, tandis que le PicoCommandSender peut
optimiser un hardware precis.

## Transport Serie Pico

Quand `Transport=PowerShellBridge`, `PicoCommandSender.exe` lance
`tools/serial-bridge.ps1`. Les valeurs importantes sont dans le INI du sender :

```ini
[Serial]
Port=COM3
BaudRate=115200
BootDelayMs=800
PostInitDelayMs=9000
ProbeTimeoutMs=900
WriteTimeoutMs=15000
Transport=PowerShellBridge
BridgeScript=tools\serial-bridge.ps1
```

`WriteTimeoutMs` controle le timeout d'ecriture du bridge. En cas de timeout
Windows sur COM3, le bridge rouvre le port et retente la commande une fois au
lieu de quitter brutalement.

Le bridge ne force pas de reset logiciel du Pico apres un reopen serie. Quand
COM3 est deja en timeout, l'ecriture des octets de reset peut elle-meme bloquer
le port. La recuperation se fait donc en deux temps : reopen COM3, puis
`PicoCommandSender.exe` detecte `SERIAL REOPENED` et rejoue les commandes
d'initialisation du profil.

## Demarrage Et Instance Unique

Au demarrage, `LedManager.exe` ferme automatiquement les anciennes instances
`LedManager.exe` trouvees sous le meme dossier plugin. L'arbre de processus est
ferme aussi, afin de liberer les `PicoCommandSender.exe` et le bridge serie qui
peuvent garder `COM3` ouvert apres un ancien lancement Debug. Les
`PicoCommandSender.exe` orphelins du meme dossier sont egalement fermes avant de
relancer les senders courants.

Option de secours pour debug :

```powershell
.\LedManager.exe --no-kill-previous
```

## Session Ingame

Pendant une partie :

- le dernier panel systeme est snapshot au `game-start`;
- les effets ingame sont envoyes en overlays cibles;
- un `OFF/BLACK` cible restaure le bouton depuis le snapshot;
- un `ALL OFF` peut restaurer le panel snapshot une seule fois par session;
- apres 2 secondes sans activite bouton, les slots du panel systeme sont
  restaures par LedManager;
- Start et Select restent independants des slots B1-B8.

## Firmware Pico

Le firmware stable expose les commandes simples :

```text
PING
GET / SCAN
HW <profile>
ONOFFINVERT ON|OFF
SLOT <slot> <color>
SET <target> <color>
SLOT <slot> PCT:<r>,<g>,<b>
SET <target> PCT:<r>,<g>,<b>
FLASH <target> <color> <durationMs>
BATCH <cmd1>;<cmd2>;...
ALL <color>
ALLPCT <r> <g> <b>
ALLPCTPANEL <r> <g> <b>
CLEAR
MATRIXSCORE <target> <value> <color>
MATRIXTEXT <target> <color> <text>
```

Ces commandes sont un protocole Pico, pas un contrat impose a tous les systemes
LED. Un autre systeme doit etre branche via ses propres templates et/ou son
propre sender.

## Workflow Dev

Avant une modification importante :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\new-version-snapshot.ps1 `
  -Label "before-my-change" `
  -Files "LedManager.ini","PicoCommandSender.ini","src\PicoCommandSender\Program.cs"
```

Verification habituelle :

```powershell
dotnet build LedManager.sln
dotnet run --project tests\LedManager.Tests\LedManager.Tests.csproj
```
