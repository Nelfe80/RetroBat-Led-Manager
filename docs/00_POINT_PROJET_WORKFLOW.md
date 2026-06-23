# Point Projet Et Workflow

Etat au 2026-06-12.

LedManager est un plugin C# local pour RetroBat. Sa mission est de transformer
les etats panel, signaux runtime, outputs arcade et evenements `.MEM` exposes
par APIExpose en commandes LED envoyees a un ou plusieurs Raspberry Pi Pico.

## Architecture Cible

```text
APIExpose /ws/frontend  -> game start/end, cycle de session
APIExpose /ws/panel     -> panel.state, layout deja resolu par ES/APIExpose
APIExpose /ws/ingame    -> score live et signaux runtime pendant la partie
APIExpose /ws/arcade    -> outputs MAME/FBNeo et lampes cabinet
APIExpose /ws/hiscore   -> score durable/high score, pas score live

LedManager.exe
  -> routage player/sender
  -> layers et effets
  -> batching par Pico

PicoCommandSender.exe instances
  -> P1 / P2 / GLOBAL
  -> port COM dedie
  -> mode daemon obligatoire pour le live
  -> firmware Pico dynamique
```

Le choix du panel ne se fait pas dans LedManager. ES/APIExpose resolvent le
panel actif. LedManager consomme `panel.state` et applique les couleurs/targets.

## Regles Produit

- `/ws/frontend` est la source canonique pour `game-start` et `game-end`.
- Au `game-start`, LedManager sauvegarde le dernier `panel.state` connu.
- Au `game-end`, LedManager peut restaurer ce snapshot si
  `RestoreOnGameEnd=true`.
- `/ws/ingame` transporte le score live pendant la partie; il peut piloter une
  matrice via `MATRIXSCORE`.
- `/ws/hiscore` transporte les scores durables/high scores; il ne doit pas etre
  confondu avec le score live.
- Le sender Pico reste mono-Pico. Le multi-Pico est obtenu par plusieurs
  instances `PicoCommandSender.exe`.
- En live, chaque instance sender garde son COM ouvert en mode `daemon`; on
  evite absolument d'ouvrir/fermer le port a chaque commande.
- Les commandes batch ne melangent jamais deux senders.
- Le flux panel live suit une logique `latest wins` : si APIExpose envoie des
  sequences panel rapides, LedManager applique seulement l'etat le plus recent.
- Un `panel.state` doit produire un etat physique complet : les slots absents
  sont eteints explicitement et les commandes `BLACK/OFF` sortent avant les
  couleurs dans les batches.

Detail latence/panel : voir `02_PANEL_RUNTIME_LATENCY.md`.

## Structure Dev

```text
LedManager.sln
src/LedManager.Core         logique commune
src/LedManager              exe principal
src/PicoCommandSender       exe serie mono-Pico
tests/LedManager.Tests      runner de tests sans dependance externe
fw/                         firmware MicroPython Pico
docs/                       specs et workflow
```

## Cycle De Travail

1. Creer un snapshot local avant une modification importante :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\new-version-snapshot.ps1 `
  -Label "before-my-change" `
  -Files "docs\LOGS.md","src\LedManager\Program.cs" `
  -Notes "Snapshot avant correction"
```

2. Modifier dans `src/`, `tests/`, `docs/` ou les configs racine.
3. Compiler en Debug :

```powershell
dotnet build LedManager.sln
```

4. Lancer les tests :

```powershell
dotnet run --project tests\LedManager.Tests\LedManager.Tests.csproj
```

5. Tester sans Pico avec `DryRun=true` :

```powershell
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-events.jsonl
```

6. Tester avec Pico reel uniquement apres verification du port COM ou de
   `Port=auto`, puis `DryRun=false`.
7. Creer un snapshot post-test avec les sources, docs, configs et tests
   executes.

Commandes detaillees : voir `01_DEVOPS_VERSIONING.md`.

## Etat Initial Code

Le socle C# contient :

- parser INI leger;
- routage player -> sender;
- routage GLOBAL et targets globales;
- persistance panel `game-start` / `game-end`;
- client WebSocket multi-flux APIExpose;
- sender Pico avec `Port=auto`, `PING`, init commands et mode daemon;
- tests de routage P1/P2/GLOBAL, score ingame vs hiscore et batch panel.
