# Matériel

Le montage de référence utilise un **Raspberry Pi Pico** qui pilote 8 boutons RGB et des LED START/SELECT. C'est le matériel le mieux supporté ; pour d'autres cartes, voir [Cartes LED externes](cartes-externes.md).

## Le câblage

![Plan de câblage Raspberry Pi Pico — 8 boutons RGB + START/SELECT](assets/pico_wiring_diagram_fr.png)

Les points essentiels du plan :

- Chaque bouton RGB utilise **3 GPIO** (fils jaune, blanc, rouge) : B1 = GP0/GP1/GP2, B2 = GP3/GP4/GP5, et ainsi de suite.
- Le fil **noir** de chaque bouton est le commun, relié au **3.3V** du Pico — les GND ne sont pas câblés.
- **START** utilise GP27 et **SELECT** GP28, avec une LED simple (une seule couleur parmi les trois fils, au choix).
- Cas particulier : GP23 n'étant pas disponible sur le connecteur, le rouge de B8 est câblé sur **GP26**.

!!! warning "Important"
    Seul le **3V3(OUT)** alimente les fils communs des boutons et LED. Ne câblez pas les GND du Pico vers les boutons.

## Flasher le firmware

Le firmware du Pico est fourni dans le dossier `fw\` du plugin. Pour le déployer :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy-pico-fw.ps1
```

Le script copie `main.py`, `profiles_db.py` et `hardware_profiles.py` sur le Pico (MicroPython requis). En cas de Pico récalcitrant, `tools\reset-pico.ps1` force un redémarrage propre.

## Vérifier le Pico

Le firmware répond à deux commandes de diagnostic sur le port série (115200 bauds) :

```text
VERSION  →  VERSION DYNAMIC_PANEL_ADDR 2026.06.20
CAPS     →  CAPS PING,INIT,PTR,BUS,HW,GPIO,SLOT,SLOTPWM,...
```

C'est le moyen le plus rapide de confirmer que le firmware est en place et à la bonne version avant d'accuser la configuration.

## Décrire votre panel

Une fois le matériel branché, vous le décrivez **avec des mots simples** dans `PicoCommandSender.ini` — nombre de boutons, type de LED, GPIO utilisés :

```ini
[Hardware:P1]
PanelButtons=8
PanelButtonType=RGBLED
Start=LED
Select=LED

[GPIO:P1]
B1=0,1,2
B2=3,4,5
START=27
SELECT=28
```

Types possibles : `NONE` (absent), `LED` (simple ON/OFF, 1 GPIO), `RGBLED` (3 GPIO), `ADDRLED` (WS2812/NeoPixel adressable). Le sender se charge de toute l'initialisation du firmware — vous n'avez aucun nom de profil interne à connaître.

La suite de la configuration (routage, couleurs, port série) est détaillée dans [Configuration](configuration.md).
