# Runtime Panel Et Latence

Etat au 2026-06-13.

Ce document decrit le flux temps reel utilise pour rendre le panel physique
reactif quand EmulationStation change de systeme ou de jeu.

## Flux Cible

```text
APIExpose events.ini / ES
  -> /ws/panel panel.state + Sequence
  -> LedManager.exe
  -> CommandRouter
  -> PicoCommandSender.exe daemon
  -> firmware Pico MicroPython
```

LedManager ne choisit pas le panel. APIExpose resout le systeme, le jeu, le
layout et les couleurs. LedManager applique le dernier `panel.state` recu.

## Latest Wins

Chaque `panel.state` peut porter un champ `Sequence` monotone emis par
APIExpose. LedManager conserve la derniere sequence appliquee :

- si une sequence plus ancienne arrive apres une sequence plus recente, elle est
  ignoree;
- si plusieurs changements systeme arrivent en rafale, seul le dernier etat doit
  rester visible;
- LedManager ne doit pas rejouer toute une vieille file d'etats panel.

Cette logique evite le defilement visible de panels intermediaires quand
l'utilisateur traverse rapidement plusieurs systemes.

La `Sequence` est maintenant transportee jusque dans les commandes routees vers
le sender. Si un replay apres `READY` ou une commande deja presente dans la
file contient une sequence inferieure a la derniere sequence connue pour ce
sender, LedManager la jette avant l'ecriture serie. Cela evite qu'un etat N-1
repasse derriere le bon panel pendant une reconnexion ou un changement rapide.

## Completer Les Slots Eteints

Un panel recu depuis APIExpose peut ne contenir que les sorties allumees. Pour
un vrai panel physique, cette representation est insuffisante : les anciennes
LEDs resteraient allumees si on ne les eteint pas explicitement.

`PanelState` complete donc les slots physiques attendus (`SLOT 1` a `SLOT 8`)
avec `BLACK` quand ils sont absents. Le firmware recoit ainsi un etat complet,
pas seulement un delta partiel.

## Ordre Des Batches

Les commandes `BATCH` sont ordonnees avec les sorties eteintes en premier :

```text
BATCH SLOT 3 BLACK;SLOT 4 BLACK;SLOT 1 GRAY;SLOT 2 BLUE
```

La raison est materielle : certains profils firmware ont une logique de
securite PWM / conflit de GPIO. Si une couleur faible comme `GRAY` est appliquee
avant que les slots concurrents soient eteints, le firmware peut la refuser ou
la transformer en noir. En envoyant d'abord `BLACK`, on libere les lignes avant
d'appliquer les nouvelles couleurs.

La meme regle existe dans le firmware Pico pour `BATCH` : il traite les sorties
`OFF`, `BLACK` ou `0` avant les sorties colorees.

## Regle Anti-Spam All Buttons

Ne jamais allumer les 8 slots boutons un par un quand ils doivent tous recevoir
la meme couleur. Ce cas doit toujours utiliser une commande globale :

```text
ALL BLUE
```

et non :

```text
SLOT 1 BLUE
SLOT 2 BLUE
...
SLOT 8 BLUE
```

La raison est materielle : huit ecritures serie successives pour un meme etat
visuel surchargent inutilement `PicoCommandSender` et le firmware Pico. Dans
les cas observes, ce spam peut provoquer un decrochage/reboot du Pico.

Regle de conception :

- meme couleur sur tous les boutons de jeu => `ALL <COLOR>`;
- couleurs differentes par slot => `BATCH SLOT ...`;
- animation progressive volontaire => a reserver aux effets rares et courts,
  jamais pour poser un etat uniforme;
- restore uniforme des boutons => `ALL <COLOR>`, pas huit `SLOT`.

LedManager coalesce donc un `BATCH` couvrant exactement `SLOT 1..8` avec la
meme couleur en `ALL <COLOR>` avant l'entree dans la file sender. Le cache de
deduplication traite aussi `ALL <COLOR>` comme l'etat connu des 8 slots.

## Session Ingame Et Panel De Base

Au `game-start`, LedManager sauvegarde le dernier `panel.state` connu et le
garde comme panel de base de la session ingame. Ce panel represente les touches
actives du systeme ou du jeu MAME; il doit rester la reference lisible pour
l'utilisateur pendant la partie.

Les events ingame et outputs MAME sont appliques par-dessus ce panel :

- une sortie active peut remplacer temporairement la couleur d'un slot;
- une fin d'effet `BLACK`, `OFF` ou `0` restaure la couleur effective dessous;
- si aucun override ingame n'est actif sur le slot, la couleur restauree est
  celle du panel snapshot au `game-start`;
- si un override MAME reste actif, un effet temporaire revient vers cet override
  au lieu de revenir directement au panel de base;
- un `ALL BLACK` en session ne declenche un restore complet qu'une seule fois,
  au premier all-off utile de la session; les all-off suivants identiques sont
  deduplicques pour eviter les rafales de gros `BATCH`;
- les fins d'effets suivantes doivent restaurer uniquement la sortie ciblee
  (`SLOT`, `START`, `SELECT`, etc.) plutot que le panel complet.
- apres 2 secondes sans nouvelle activite ingame, LedManager renvoie les slots
  boutons du panel systeme snapshot au `game-start`, en excluant `START` et
  `SELECT` qui restent des sorties independantes;
- les animations `START`/`SELECT` ne repoussent pas ce timer : elles peuvent
  continuer sans empecher les boutons de jeu de revenir au panel systeme;
- ce restore idle force l'envoi des slots snapshot meme si le cache de dedup
  pensait deja connaitre ces couleurs, puis ces couleurs deviennent les
  nouvelles references de deduplication.

La couleur absente dans un evenement n'est pas consideree comme un `off`; seuls
les mots explicites `BLACK`, `OFF`, `0`, `FALSE` ou `NO` declenchent cette
restauration.

## Sender Et Bridge Serie

Le flux live doit garder le port COM ouvert :

- `LedManager.exe` lance `PicoCommandSender.exe daemon`;
- `PicoCommandSender` garde une session serie persistante;
- les commandes arrivent via stdin;
- le bridge PowerShell `tools/serial-bridge.ps1` peut etre utilise quand le CDC
  Pico est plus fiable via `System.IO.Ports` que via l'ecriture native directe.

Le delai apres envoi cote bridge est volontairement court pour le panel live.
Il ne doit pas redevenir un gros debounce, sinon les changements systeme se
mettent a trainer.

LedManager garde aussi le dernier etat envoye par sortie et par sender. Une
commande qui ne change rien, par exemple `SLOT 8 BLUE` alors que `SLOT 8` est
deja connu en `BLUE`, est ignoree avant d'entrer dans la file serie. Pour un
`BATCH`, les items deja identiques sont retires et seuls les changements reels
sont envoyes. Les commandes impulsionnelles comme `FLASH` ne sont pas
dedupliquees.

Le bridge ouvre le port avec `DTR=false` et `RTS=false`, puis ecrit les lignes
avec `SerialPort.WriteLine`. Ce mode correspond au smoke test direct stable sur
le Pico et evite qu'une ligne reste visible seulement a la fermeture du port.

Le firmware renvoie maintenant un retour de traitement pour les commandes non
heartbeat :

```text
RX BATCH SLOT 1 YELLOW;SLOT 2 RED
DONE BATCH SLOT 1 YELLOW;SLOT 2 RED
```

Ces lignes remontent via `PicoCommandSender` dans les logs LedManager. Si
`[route] ... BATCH ...` apparait sans `RX/DONE` cote sender, le probleme est
entre LedManager et le firmware. Si `RX/DONE` apparait mais que le visuel ne
change pas, le probleme est firmware/profil hardware/GPIO.

## Reprise Apres Decrochage Pico

Si le Pico reboot, se debranche brievement ou perd le lien serie pendant un
jeu, la reprise doit etre automatique :

1. Si le bridge serie meurt ou si l'ecriture echoue, `PicoCommandSender`
   relance le bridge.
2. Apres reconnexion, il rejoue les commandes d'initialisation du `.ini`
   (`PING`, `HW ...`, `ONOFFINVERT ...`, `GET`).
3. Si le firmware annonce `READY DYNAMIC PANEL...` alors que le sender tourne
   deja, `PicoCommandSender` considere que le Pico a reboot et rejoue la meme
   initialisation.
4. Pendant cette reinitialisation, les ecritures stdin sont serializees pour ne
   pas envoyer d'effet sur un firmware encore `CONFIG UNCOMMITTED`.
5. Quand le sender annonce de nouveau `READY sender=<id>`, LedManager renvoie
   le dernier `panel.state` connu pour ce sender.

Ce dernier point est important : apres un reset firmware, le Pico peut etre de
nouveau joignable mais ne plus connaitre son profil hardware ou ses couleurs
courantes. Rejouer `HW GPIO_8B_SS_GPIO` evite les erreurs `ERR OUTPUT START`,
`ERR SLOT 7` ou `OUTPUTS NONE` observees apres reboot; rejouer le dernier
panel remet ensuite les touches actives visibles.

Si la reprise ne se fait pas, verifier :

- le port COM est revenu dans Windows;
- `PicoCommandSender.p1.ini` contient le bon `Port` ou `Port=auto`;
- le bridge ne reste pas lance seul sans `PicoCommandSender`;
- LedManager affiche un nouveau `[init] sender=P1 reason=firmware-ready` puis
  `READY sender=P1`, puis `replay latest panel after READY`.

### Diagnostic Reboot USB

La sequence suivante indique un reset ou une disparition USB du Pico, pas une
simple commande LED invalide :

```text
[bridge] write failed, reopening COM3: ...
[bridge] open attempt ... failed for COM3: ... Le port 'COM3' n'existe pas.
...
READY DYNAMIC PANEL ADDRESSABLE DRIVER
CONFIG UNCOMMITTED
OUTPUTS NONE
```

`skip stale command` est un mecanisme LedManager volontaire : une commande live
trop vieille est jetee pour garder le panel reactif. Elle peut apparaitre juste
avant le decrochage, mais elle ne provoque pas le reboot firmware.

## Mesurer La Reactivite

L'outil de stress test actuel est :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& 'E:\RetroBat\plugins\LedManager\tools\measure-panel-latency.ps1' -Systems @('megadrive','gamegear','sega32x','gamegear','snes','gamegear') -GapMs 220 -TimeoutMs 2500"
```

Options utiles :

- `-FrontendOnly` mesure `/ws/frontend`.
- sans option, l'outil mesure `/ws/panel`.
- `-HttpContext` mesure l'endpoint HTTP de contexte APIExpose, mais ce n'est
  pas le meilleur thermometre pour la reactivite panel car le contexte peut
  attendre des enrichissements.
- `-GapMs` simule la vitesse de navigation entre systemes.
- `-PollMs` regle la frequence de polling HTTP quand `-HttpContext` est utilise.

Pour des mesures propres, lancer APIExpose en mode test :

```powershell
E:\RetroBat\plugins\APIExpose\RetroBat.Api.exe --urls http://127.0.0.1:12345 --test-mode
```

Ce mode coupe les travaux de demarrage interactifs cote APIExpose, notamment la
modale de migration media, afin de ne mesurer que le flux `events.ini` /
WebSocket / panel.

## Firmware Pico

Le firmware est dans `fw/`. Quand le firmware evolue, utiliser le script de
deploiement depuis le PC :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\RetroBat\plugins\LedManager\tools\deploy-pico-fw.ps1 -Port COM3
```

Le script coupe les process susceptibles de verrouiller le port, passe par le
REPL serie, uploade `main.py`, `hardware_profiles.py` et `profiles_db.py`, puis
redemarre le Pico.

Avant un deploiement firmware, fermer LedManager ou PicoCommandSender si le COM
est occupe. Un message `PermissionError(13, Acces refuse)` signifie presque
toujours qu'un process garde encore le port ouvert.

## Tests A Conserver

Les tests de routage doivent couvrir :

- un `panel.state` complet route en batch vers le bon sender;
- les slots absents sont forces a `BLACK`;
- les commandes noires sortent avant les commandes colorees dans `BATCH`;
- une sequence panel obsolette est ignoree;
- un `game-start` sauvegarde le dernier panel connu;
- un `game-end` restaure le snapshot si `RestorePanelOnGameEnd=true`.
