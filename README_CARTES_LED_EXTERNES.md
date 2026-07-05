# Brancher Une Autre Carte LED

Ce document explique comment adapter `LedManager` a une carte LED autre que le
Pico actuellement utilise.

Le principe important : `LedManager` reste agnostique. Il decide quoi afficher,
mais il ne connait pas les GPIO, les canaux USB, les SDK constructeurs ou les
API reseau. Toute la traduction materielle doit vivre dans le sender.

```text
APIExpose
  -> LedManager.exe
     -> commandes generiques
        -> sender adapte a la carte LED
           -> SDK / USB / serie / HTTP / UDP / firmware cible
```

## Contrat Minimum

Une carte LED externe doit recevoir les intentions suivantes, directement ou via
un petit adaptateur :

```text
SLOT 1 RED
SET START ORANGE
SET SELECT BLACK
BATCH SLOT 1 RED;SLOT 2 BLUE;SLOT 3 BLACK
ALL WHITE
CLEAR
FLASH 6 YELLOW 80
MATRIXSCORE MATRIX1 12345 GREEN
MATRIXTEXT MATRIX1 WHITE READY
```

Le sender peut choisir de ne supporter qu'une partie des commandes. Dans ce cas,
il doit ignorer proprement les commandes inconnues et loguer ce qui manque.

## Deux Methodes D'Integration

### Methode A : Templates Directs

Si le programme cible accepte deja des commandes texte simples, il suffit de
modifier `LedManager.ini`.

```ini
[CommandTemplates]
SetSlot=slot {slot} {color}
SetOutput=output {target} {color}
All=all {color}
Clear=clear
Batch=batch {items}
Flash=flash {target} {color} {durationMs}
MatrixScore=score {target} {value} {color}
MatrixText=text {target} {color} {text}
```

Cette methode est rare, mais tres propre quand le programme LED sait deja lire
des commandes proches de celles de `LedManager`.

### Methode B : Sender Adaptateur

C'est la methode recommandee pour les cartes USB/SDK/API.

Dans `LedManager.ini`, on remplace ou ajoute un sender :

```ini
[CommandSender:P1]
Name=External LED Board P1
Enabled=true
Player=1
Executable=ExternalLedSender.exe
Mode=daemon
Arguments=daemon --ini "ExternalLedSender.ini" --sender P1
UseStdIn=true
LineEnding=\n
DryRun=false
StartupDelayMs=2000
```

Le sender lit stdin :

```text
BATCH SLOT 1 RED;SLOT 2 BLUE
SET START ORANGE
SLOT 3 BLACK
```

Puis il traduit vers le materiel :

```text
SLOT 1 RED -> channels 1,2,3 -> RGB value
START ORANGE -> channel 25 -> on/off ou RGB selon config
ALL WHITE -> repaint global
```

## Mapping Materiel

Chaque carte doit avoir un fichier de mapping clair. Exemple generique :

```ini
[Board]
Type=PacLED64
DeviceId=1
DryRun=false

[Colors]
RED=255,0,0
GREEN=0,255,0
BLUE=0,0,255
YELLOW=255,255,0
ORANGE=255,80,0
WHITE=255,255,255
GRAY=80,80,80
BLACK=0,0,0

[Slots]
1=RGB:1,2,3
2=RGB:4,5,6
3=RGB:7,8,9
4=RGB:10,11,12
5=RGB:13,14,15
6=RGB:16,17,18
7=RGB:19,20,21
8=RGB:22,23,24

[Outputs]
START=LED:25
SELECT=LED:26
JOY1=RGB:27,28,29

[Addressable]
STRIP1=PIXELS:0-59
MATRIX1=MATRIX:16x16:60-315
```

Regles :

- `RGB:x,y,z` : trois canaux pour rouge, vert, bleu.
- `LED:x` : un canal simple ON/OFF ou intensite.
- `PIXELS:a-b` : plage adressable.
- `MATRIX:WxH:a-b` : matrice adressable.
- `BLACK` signifie eteint.
- `GRAY` est le nom canonique pour gris.

## Cartes Et Systemes Connus

Ces exemples ne sont pas des integrations deja codees ici. Ce sont des cibles
realistes pour un sender adapte.

### Ultimarc PacLED64

Carte USB orientee LEDs, avec 64 canaux et controle de luminosite. Une LED RGB
utilise 3 canaux. C'est une bonne candidate pour un sender via SDK Ultimarc.

Source constructeur :
<https://www.ultimarc.com/output/led-and-output-controllers/pacled64/>

Integration typique :

```text
LedManager -> PacLedSender.exe -> DLL/SDK Ultimarc -> PacLED64
```

### Ultimarc PAC-Drive

Carte plus simple, plutot ON/OFF. Utile pour START, SELECT, coin lamps,
lampes cabinet ou sorties simples. Moins adaptee aux boutons RGB nuancees.

Source constructeur, voir la page PacLED64 qui compare aussi PAC-Drive :
<https://www.ultimarc.com/output/led-and-output-controllers/pacled64/>

### Ultimarc I-PAC Ultimate I/O

Interface arcade qui combine entrees et sorties LED. Peut etre interessante si
le panel utilise deja cette carte pour les controles.

Source constructeur :
<https://www.ultimarc.com/control-interfaces/i-pacs/i-pac-ultimate-i-o/>

### GroovyGameGear LED-Wiz

Carte USB historique pour bornes arcade. Elle expose 32 sorties et supporte des
niveaux PWM. Une LED RGB utilise 3 sorties.

Source constructeur :
<https://groovygamegear.com/webstore/index.php?main_page=product_info&products_id=239>

Integration typique :

```text
LedManager -> LedWizSender.exe -> ActiveX/DLL/API LED-Wiz -> LED-Wiz
```

### WLED / ESP32

Tres interessant pour rubans, matrices et effets adressables WS2812. WLED expose
une API JSON reseau. Ce n'est pas ideal pour chaque bouton arcade RGB individuel
si l'on veut une latence tres basse, mais c'est excellent pour `STRIP`, `CIRCLE`,
`MATRIX`, topper ou eclairage cabinet.

Source projet :
<https://kno.wled.ge/interfaces/json-api/>

Integration typique :

```text
LedManager -> WledSender.exe -> HTTP JSON -> ESP32/WLED
```

### Arduino / Teensy / Pico Custom Serial

C'est l'option la plus proche de notre architecture actuelle. Le microcontroleur
lit des lignes serie et applique les couleurs. Le sender peut rester tres simple,
voire reutiliser le protocole texte existant.

Integration typique :

```text
LedManager -> SerialSender.exe -> COMx -> firmware custom
```

## Ce Que Le Sender Doit Faire

Un sender externe doit :

1. Charger son INI.
2. Initialiser la carte.
3. Lire les lignes stdin.
4. Parser les commandes `SLOT`, `SET`, `BATCH`, `ALL`, `CLEAR`, etc.
5. Traduire couleur logique -> intensite/canaux/pixels.
6. Appliquer les commandes avec dedup si utile.
7. Loguer les commandes ignorees ou non supportees.
8. Liberer proprement la carte a la fermeture.

Pseudo-code :

```text
load ExternalLedSender.ini
open board
print READY sender=P1

while line = stdin.readLine():
    if line starts with "BATCH ":
        split by ";"
        apply all items
    elif line starts with "SLOT ":
        map slot to channels
        set RGB
    elif line starts with "SET ":
        map output to channel(s)
        set LED/RGB
    elif line starts with "ALL ":
        repaint all RGB slots
    elif line == "CLEAR":
        all off
```

## Couleurs Et Intensites

Ne pas supposer que toutes les cartes interpretent les couleurs pareil :

- Le Pico GPIO actuel utilise des pourcentages d'extinction.
- Une PacLED64 ou LED-Wiz utiliserait plutot des intensites par canal.
- WLED utilise du RGB classique.
- Certaines cartes ont des limites de courant par canal ou par groupe.

Donc la table `[Colors]` doit rester dans le sender de la carte, pas dans
`LedManager.ini`.

## Routage Recommande

Panel joueur :

```ini
[PlayerRouting]
1=P1
2=P2
```

Effets cabinet :

```ini
[TargetRouting]
MATRIX1=GLOBAL
STRIP1=GLOBAL
CIRCLE1=GLOBAL
```

Exemple mixte :

```text
P1 -> PicoCommandSender -> boutons joueur 1
GLOBAL -> WledSender -> bandeau + matrice score
```

## Checklist Pour Ajouter Une Carte

1. Identifier le mode de pilotage : serie, USB SDK, HID, HTTP, UDP.
2. Creer un sender dedie si necessaire.
3. Creer un INI de mapping canaux/pixels.
4. Brancher le sender dans `LedManager.ini`.
5. Tester `SLOT 1 RED`, `SLOT 1 BLACK`, puis `BATCH`.
6. Tester `START` et `SELECT`.
7. Tester `ALL WHITE` puis `CLEAR`.
8. Tester les outputs MAME si la carte pilote des lampes.
9. Tester la latence et ajouter du batch/dedup cote sender si besoin.

## Regle De Conception

Si une logique depend du materiel, elle va dans le sender.

```text
Bon :
LedManager -> SLOT 1 RED
Sender PacLED64 -> channels 1,2,3 = 255,0,0

Pas bon :
LedManager -> PACLED64 CHANNEL 1 255
```

Comme ca, on peut remplacer le Pico par une PacLED64, une LED-Wiz, une carte
WLED ou un firmware serie sans casser la logique ingame/panel/arcade.
