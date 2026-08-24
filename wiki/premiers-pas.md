# Premiers pas

Installer LedManager tient en un **installateur** : on télécharge, on lance, on active. Comptez cinq minutes.

## Avant de commencer

Vous aurez besoin de :

- une installation **RetroBat** fonctionnelle ;
- le plugin **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe)** installé et fonctionnel - c'est lui qui fournit les données des jeux à LedManager ;
- le **[runtime .NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0)** ;
- un panel LED : un Raspberry Pi Pico câblé (voir [Matériel](materiel.md)) ou une carte compatible (voir [Cartes LED externes](cartes-externes.md)).

## Installation

1. Téléchargez **[`LedManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Led-Manager/releases/latest/download/LedManager-Setup.exe)** depuis la page des releases.
2. Lancez l'installateur : il installe le plugin dans `RetroBat\plugins\` et enregistre le hook de démarrage EmulationStation - vous obtenez :

    ```text
    RetroBat\plugins\LedManager\
    ```

3. Relancez RetroBat normalement : LedManager démarre désormais automatiquement avec EmulationStation.

!!! note "Que fait le hook ?"
    L'installateur enregistre simplement ce fichier côté EmulationStation, sans toucher au reste de RetroBat :

    ```text
    emulationstation\.emulationstation\scripts\start\LedManager-start.bat
    ```

## Vérifier que ça fonctionne

Au lancement de RetroBat, vos boutons doivent s'allumer après quelques secondes (le temps que le Pico s'initialise). Naviguez entre les systèmes : les couleurs changent selon le panel de chaque système. Lancez un jeu arcade avec des lampes (par exemple `seawolf` sous MAME) pour voir les sorties natives s'animer.

Si rien ne s'allume, direction [Dépannage](depannage.md) - le premier réflexe est de vérifier le port COM dans `PicoCommandSender.ini`.

## Arrêter ou désinstaller

| Action | Comment |
|---|---|
| Arrêter LedManager (et ses senders) | Double-clic sur `stop.bat` |
| Retirer le lancement automatique | Double-clic sur `uninstall-es-start-hook.bat` |
| Désinstaller complètement | Retirer le hook, puis supprimer le dossier `LedManager` |

!!! tip "Mise à jour"
    Pour mettre à jour, remplacez le contenu du dossier par celui de la nouvelle archive - vos fichiers `.ini` personnalisés méritent une copie de sauvegarde avant.
