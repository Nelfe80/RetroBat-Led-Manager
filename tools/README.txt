LedManager tools / Outils LedManager
====================================

EN
--
This directory is reserved for local development, serial, latency, firmware
deployment, and color-analysis helper tools. Its contents are intentionally not
tracked by Git, except for this README.

Common local tools include:

- serial bridge and smoke-test scripts
- Pico firmware deployment scripts
- latency measurement scripts
- color sweep and calibration helpers

If a tool is organized as a subfolder, keep a README.txt inside that subfolder.
Git tracks those notes while ignoring the actual local binaries, caches, and
scratch files.

Installation:

1. Restore the helper scripts from your local toolbox, backup, or release
   package when you need them.
2. Place them directly under LedManager/tools.
3. Keep generated caches such as __pycache__ in this folder; Git will ignore
   them.
4. Firmware files that are part of the plugin source of truth belong in
   LedManager/fw, not in tools.

FR
--
Ce dossier est reserve aux outils locaux de developpement, serie, mesure de
latence, deploiement firmware et analyse/calibrage des couleurs. Son contenu
n'est volontairement pas suivi par Git, sauf ce README.

Outils locaux typiques :

- scripts de pont serie et de smoke-test
- scripts de deploiement du firmware Pico
- scripts de mesure de latence
- helpers de sweep et de calibration couleur

Si un outil est organise en sous-dossier, garder un README.txt dans ce
sous-dossier. Git suit ces notes tout en ignorant les binaires locaux, caches et
fichiers temporaires.

Installation :

1. Restaurer les scripts depuis ta boite a outils locale, une sauvegarde ou un
   package de release lorsque tu en as besoin.
2. Les placer directement dans LedManager/tools.
3. Garder les caches generes comme __pycache__ dans ce dossier ; Git les
   ignorera.
4. Les fichiers firmware faisant partie de la source de verite du plugin doivent
   rester dans LedManager/fw, pas dans tools.
