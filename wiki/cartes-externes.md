# Cartes LED externes

LedManager n'est pas marié au Raspberry Pi Pico. Son principe : **il décide quoi afficher, jamais comment** — les GPIO, canaux USB, SDK constructeurs et API réseau vivent dans le *sender* de chaque carte.

```text
APIExpose
  → LedManager.exe
     → commandes génériques (SLOT 1 RED, ALL WHITE…)
        → sender adapté à votre carte
           → SDK / USB / série / HTTP / UDP
```

## Deux méthodes d'intégration

=== "Méthode A — Templates directs"

    Si votre programme LED accepte déjà des commandes texte simples, modifiez seulement `[CommandTemplates]` dans `LedManager.ini` :

    ```ini
    [CommandTemplates]
    SetSlot=slot {slot} {color}
    All=all {color}
    Clear=clear
    ```

    Rare, mais très propre quand le programme cible parle déjà un langage proche.

=== "Méthode B — Sender adaptateur (recommandée)"

    Pour les cartes USB/SDK/API, on écrit un petit exécutable qui lit les commandes sur son entrée standard et les traduit pour le matériel :

    ```ini
    [CommandSender:P1]
    Name=External LED Board P1
    Executable=ExternalLedSender.exe
    Arguments=daemon --ini "ExternalLedSender.ini" --sender P1
    UseStdIn=true
    ```

    Le sender reçoit `SLOT 1 RED`, consulte son mapping (`RGB:1,2,3`) et applique les valeurs à la carte.

!!! tip "Votre sender peut être n'importe quel programme"
    `Executable=` et `Arguments=` sont libres : un exe compilé, un script Python (`Executable=python.exe`), Node, ou même PowerShell si vous y tenez. Seule règle : lire les commandes ligne par ligne sur l'entrée standard. Préférez toutefois un exécutable compilé — les antivirus surveillent de près les scripts PowerShell qui tournent en continu (heuristiques « ClickFix »), c'est d'ailleurs pourquoi le sender Pico officiel utilise un accès série natif.

## Cartes cibles réalistes

Ces intégrations ne sont pas fournies clé en main : ce sont des cibles réalistes pour un sender adapté.

| Carte | Points forts | Intégration type |
|---|---|---|
| [Ultimarc PacLED64](https://www.ultimarc.com/output/led-and-output-controllers/pacled64/) | 64 canaux, luminosité par canal | sender → SDK Ultimarc |
| Ultimarc PAC-Drive | Simple ON/OFF : START, coin lamps | sender → SDK Ultimarc |
| [Ultimarc I-PAC Ultimate I/O](https://www.ultimarc.com/control-interfaces/i-pacs/i-pac-ultimate-i-o/) | Entrées + sorties LED combinées | sender → SDK Ultimarc |
| [LED-Wiz](https://groovygamegear.com/webstore/index.php?main_page=product_info&products_id=239) | 32 sorties PWM, classique des bornes | sender → API LED-Wiz |
| [WLED / ESP32](https://kno.wled.ge/interfaces/json-api/) | Rubans, matrices, effets adressables | sender → HTTP JSON |
| Arduino / Teensy / Pico série custom | Le plus proche de l'architecture actuelle | sender série minimal |

!!! tip "WLED : pour l'ambiance, pas pour les boutons"
    WLED excelle pour `STRIP`, `MATRIX`, topper et éclairage cabinet. Pour des boutons arcade individuels à latence très basse, préférez une liaison série directe.

## Le contrat minimum d'un sender

Votre sender doit comprendre (ou ignorer proprement en le journalisant) :

```text
SLOT 1 RED
SET START ORANGE
BATCH SLOT 1 RED;SLOT 2 BLUE;SLOT 3 BLACK
ALL WHITE
CLEAR
FLASH 6 YELLOW 80
MATRIXSCORE MATRIX1 12345 GREEN
MATRIXTEXT MATRIX1 WHITE READY
```

Et son fichier de mapping décrit le matériel :

```ini
[Slots]
1=RGB:1,2,3
2=RGB:4,5,6

[Outputs]
START=LED:25

[Addressable]
MATRIX1=MATRIX:16x16:60-315
```

Règle de conception : **si une logique dépend du matériel, elle va dans le sender**. La table des couleurs (`RED=255,0,0`…) vit dans le sender de la carte, jamais dans `LedManager.ini` — chaque carte interprète les intensités à sa façon.

## Checklist d'intégration

1. Identifier le mode de pilotage : série, USB SDK, HID, HTTP, UDP.
2. Créer le sender et son INI de mapping.
3. Brancher le sender dans `LedManager.ini`.
4. Tester dans l'ordre : `SLOT 1 RED` → `SLOT 1 BLACK` → `BATCH` → `START`/`SELECT` → `ALL WHITE` → `CLEAR`.
5. Tester les lampes MAME si la carte pilote des lampes.
6. Mesurer la latence ; ajouter du batch/dedup côté sender si besoin.
