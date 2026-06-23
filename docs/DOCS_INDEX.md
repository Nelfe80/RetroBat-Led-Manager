# LedManager Docs Index

Etat au 2026-06-12.

Documents principaux :

- `../README.md` : perimetre APIExpose / LedManager / PicoCommandSender,
  commandes generiques et adaptation par INI.
- `00_POINT_PROJET_WORKFLOW.md` : reprise rapide du projet, architecture et flux de dev.
- `01_DEVOPS_VERSIONING.md` : snapshots `.versioning`, version assembly, build, test et release.
- `02_PANEL_RUNTIME_LATENCY.md` : flux panel live, latest-wins, ordre des batches, tests de latence et firmware.
- `LOGS.md` : journal court des changements valides.
- `mem_nomenclature_spec.md` et `mem_generation_file.md` : specifications `.MEM`.

Workflow local :

- `../.versioning/README.md` : fonctionnement des snapshots locaux.
- `../tools/new-version-snapshot.ps1` : script de capture avant/apres modif et release.
- `../Directory.Build.props` : version commune des assemblies LedManager.

Contrats techniques :

- `../CAHIER_DES_CHARGES_LED_MANAGER.md` : cible multi-Pico / multi-player.
- `../CAHIER_DES_CHARGES_PICO_COMMAND_SENDER_EXE.md` : sender mono-Pico multi-instance.
- `../CAHIER_DES_CHARGES_EFFECT_CATALOG_MEM.md` : catalogue d'effets `.MEM`.
- `../fw/README_DYNAMIC_PANEL_WS2812.md` : protocole firmware Pico.
