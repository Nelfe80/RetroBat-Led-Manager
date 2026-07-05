# Configuration

Deux fichiers `.ini` pilotent tout, chacun avec un rôle précis :

| Fichier | Rôle | En une phrase |
|---|---|---|
| `LedManager.ini` | Le chef d'orchestre | *Quoi* afficher et *vers qui* l'envoyer |
| `PicoCommandSender.ini` | L'adaptateur matériel | *Comment* votre carte comprend les ordres |

!!! tip "Des outils graphiques arrivent"
    Un outil de configuration visuel est prévu. En attendant, cette page couvre les réglages qu'un utilisateur modifie réellement — le reste peut rester tel quel.

## LedManager.ini — l'orchestrateur

### Déclarer vos senders

Chaque panel physique (un par joueur, par exemple) est un *sender* :

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

Le réglage à connaître : **`StartupDelayMs`** laisse au Pico le temps de s'initialiser au démarrage. Si vos LED s'allument trop tard, ne le réduisez pas trop — un Pico pas prêt ignore les commandes.

### Router les joueurs et les cibles

```ini
[PlayerRouting]
1=P1
2=P2

[TargetRouting]
MATRIX1=GLOBAL
STRIP1=GLOBAL
```

Les effets « cabinet » (matrice de score, bandeaux) partent vers le sender `GLOBAL`, les panels joueurs vers `P1`/`P2`.

## PicoCommandSender.ini — votre matériel

### Le port série

```ini
[Serial]
Port=COM3
BaudRate=115200
```

**`Port`** est le premier réglage à vérifier après l'installation : ouvrez le Gestionnaire de périphériques Windows et repérez le port COM attribué à votre Pico.

### Votre panel

Les sections `[Hardware:P1]` et `[GPIO:P1]` décrivent boutons et branchements — voir [Matériel](materiel.md#decrire-votre-panel).

### La policy couleur

Toutes les LED ne rendent pas toutes les couleurs fidèlement. La `[ColorPolicy]` permet d'autoriser, refuser ou remplacer des couleurs **sans recompiler quoi que ce soit** :

```ini
[ColorPolicy.Fallbacks]
GOLD=YELLOW
PURPLE=VIOLET
GRAY=WHITE
```

Ici, si un jeu demande du doré, le panel affichera du jaune. `GRAY` est le nom canonique du gris ; `BLACK` signifie éteint.

## Adapter à un autre programme LED

LedManager ne connaît pas le protocole final de votre matériel : il remplit des gabarits texte définis dans `[CommandTemplates]`. Pour piloter un autre programme, on ne change que ces gabarits :

```ini
[CommandTemplates]
SetSlot=SLOT {slot} {color}
Flash=FLASH {target} {color} {durationMs}
All=ALL {color}
Clear=CLEAR
```

Variables disponibles : `{slot}`, `{target}`, `{color}`, `{durationMs}`, `{value}`, `{text}`, `{player}`, `{system}`, `{rom}`… Le détail des méthodes d'intégration est dans [Cartes LED externes](cartes-externes.md).

## Pendant une partie

Quelques comportements utiles à connaître (aucun réglage requis) :

- au lancement d'un jeu, le panel du système est mémorisé (*snapshot*) ;
- les effets ingame sont des surcouches ciblées ; un `OFF` sur un bouton le restaure depuis le snapshot ;
- après 2 secondes sans activité, le panel du système est restauré ;
- START et SELECT vivent leur vie indépendamment des boutons B1–B8 ;
- les lampes MAME gardent leur propre état : une restauration du panel ne rallume pas une lampe éteinte par le jeu.
