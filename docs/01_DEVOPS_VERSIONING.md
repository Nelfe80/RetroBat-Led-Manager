# DevOps, Versioning & Deploiement

Etat au 2026-06-12.

Ce document aligne LedManager sur le workflow local d'APIExpose, avec les
adaptations propres au plugin LED: deux executables (`LedManager.exe` et
`PicoCommandSender.exe`), plusieurs fichiers `.ini`, des fichiers `.MEM` de
test et un firmware Pico.

## 1. Processus De Snapshots (Local Versioning)

Le projet utilise un systeme de versioning local `.versioning/commits/` car il
n'est pas garanti d'etre sous Git.

Le dossier de travail courant ne doit pas dependre d'un workflow GitHub pour
retrouver les changements, les tests et les releases. Les snapshots locaux sont
donc la source d'audit principale pour LedManager.

| Etape | Commande PowerShell (via `tools\new-version-snapshot.ps1`) | Fichiers captures type |
|---|---|---|
| **Avant modif.** | `-Label "before-my-change" -Files "docs\LOGS.md","src\...", "LedManager.ini"` | Securite avant developpement |
| **Apres modif.** | `-Label "my-change-validated" -Files "Directory.Build.props","docs\...", "src\...", "tests\..." -Tests "dotnet build...", "dotnet run..." -Notes "..."` | Source + docs + validation |
| **Release** | `-Label "release-X" -Release -Files "LedManager.exe","PicoCommandSender.exe","Directory.Build.props","docs\LOGS.md",... -Tests "..." -Notes "..."` | Binaires publies + manifest |

Commande de base :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\new-version-snapshot.ps1 `
  -Label "before-my-change" `
  -Files "docs\LOGS.md","docs\01_DEVOPS_VERSIONING.md","src\LedManager\Program.cs" `
  -Notes "Snapshot avant correction"
```

Si la politique PowerShell locale bloque les scripts (`PSSecurityException`),
utiliser explicitement `powershell -NoProfile -ExecutionPolicy Bypass -File`
pour les scripts projet connus, notamment `tools\new-version-snapshot.ps1`.

Note de syntaxe : `-Files` accepte des entrees separees par virgules. Pour
`-Tests`, l'usage recommande reste une entree par commande; le script evite de
couper les virgules internes typiques comme `Stop-Process -Id 1,2 -Force`.

## 2. Workflow Projet Attendu

Le cycle normal de travail est separe du cycle release. Une modification de code
ne doit pas remplacer les executables racine tant que le test utilisateur n'est
pas valide.

| Phase | Action | Regle |
|---|---|---|
| **Avant changement** | Creer un snapshot `.versioning/commits/` avec les fichiers concernes. | Toujours garder un point de retour lisible avant une modification risquee. |
| **Developpement** | Modifier le code, stopper les process Debug actifs, compiler en Debug, relancer LedManager ou PicoCommandSender depuis `src`. | Ne pas copier de binaire publie a la racine pendant un test simple. |
| **Test dev** | Executer les tests, puis tester en `DryRun=true` ou avec Pico reel si demande. | Les fichiers `.ini` de dev restent la reference de test. |
| **Validation** | Creer un snapshot post-test avec sources, docs, configs et tests executes. | Le snapshot doit contenir le contexte de correction et les commandes de validation. |
| **Release** | Publier en `win-x64`, puis copier explicitement les executables a la racine si c'est le contrat runtime choisi. | Seulement apres demande/validation utilisateur. Le runtime publie est trace par un snapshot `-Release`. |

Regles importantes pour les versions :

- `Directory.Build.props` centralise `Version`, `AssemblyVersion`,
  `FileVersion` et `InformationalVersion`.
- `Directory.Build.props` peut avancer pendant le dev pour identifier un build
  de test via les proprietes de fichier Windows.
- Les executables racine gardent la derniere version release tant qu'une
  publication n'a pas ete demandee et validee.
- Une version Debug plus recente que les binaires racine est donc normale entre
  deux publications.

## 3. Build Debug Et Tests

Depuis `E:\RetroBat\plugins\LedManager` :

```powershell
Get-Process LedManager,PicoCommandSender -ErrorAction SilentlyContinue |
  Stop-Process -Force
dotnet build LedManager.sln
dotnet run --project tests\LedManager.Tests\LedManager.Tests.csproj
```

En developpement, Codex doit stopper les process `LedManager` et
`PicoCommandSender` actifs avant tout build Debug normal s'ils verrouillent
`src\...\bin\Debug\net8.0-windows\*.dll` ou `*.exe`. C'est obligatoire quand un
test precedent laisse un daemon Pico ou un replay LedManager en cours.

Sorties Debug principales :

```text
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe
```

Verifier les versions Debug avant un test utilisateur :

```powershell
Get-Item src\LedManager\bin\Debug\net8.0-windows\LedManager.exe |
  Select-Object -ExpandProperty VersionInfo
Get-Item src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe |
  Select-Object -ExpandProperty VersionInfo
```

Lancer LedManager en mode console/stdin, sans APIExpose :

```powershell
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini
```

Lancer LedManager avec un fichier d'evenements de test :

```powershell
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-events.jsonl
```

Lancer LedManager avec des evenements `.MEM` resolus par
`default.mem.effects.json` :

```powershell
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-mem-events.jsonl
```

Showcase des effets du catalogue :

```powershell
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-effect-showcase.jsonl --event-delay-ms 700
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-coin-random.jsonl --event-delay-ms 250
```

Lancer LedManager connecte aux flux APIExpose :

```powershell
# Dans LedManager.ini : [APIExpose] Enabled=true
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini
```

Flux utilises :

- `/ws/frontend` : `game-start` / `game-end`, sauvegarde et restauration panel.
- `/ws/panel` : `panel.state`, couleurs/layout deja resolus par APIExpose.
- `/ws/ingame` : score live pendant la partie.
- `/ws/arcade` : outputs MAME/FBNeo.
- `/ws/hiscore` : high score / score durable, pas score live.

Lancer un sender Pico en mode daemon :

```powershell
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe daemon --ini PicoCommandSender.p1.ini
```

Regle importante : pour le live, utiliser le mode `daemon`. Il ouvre le port COM
une seule fois, initialise le Pico une seule fois, puis recoit les commandes sur
`stdin`. Le mode `send` sert seulement au depannage ponctuel; il ne doit pas
etre utilise pour un flux panel/LED continu.

Scanner les ports pour trouver un Pico :

```powershell
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe probe --ini PicoCommandSender.p1.ini
```

## 4. Test Sans Materiel

Les configs exemples sont en `DryRun=true`.

Smoke test complet sans materiel :

```powershell
dotnet build LedManager.sln
dotnet run --project tests\LedManager.Tests\LedManager.Tests.csproj
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-events.jsonl
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-mem-events.jsonl --event-delay-ms 1200
src\LedManager\bin\Debug\net8.0-windows\LedManager.exe --ini LedManager.ini --event-file docs\test-effect-showcase.jsonl --event-delay-ms 700
```

Resultat attendu :

- un `panel.state` route un batch vers P1 et P2;
- `ui.game.started.raw` sauvegarde `state\panel-before-game.json`;
- `ingame.score.changed` route un `MATRIXSCORE` vers GLOBAL;
- `hiscore.updated` est observe mais ne pilote pas le score live;
- `ui.game.ended.raw` restaure le panel sauvegarde;
- les actions `.MEM` connues appliquent les effets du catalogue;
- les actions inconnues peuvent utiliser un fallback famille.

## 5. Test Avec Pico Reel

Avant test reel :

1. Compiler.
2. Mettre `DryRun=false` dans le ou les fichiers `.ini` concernes.
3. Fixer `Port=COMx` ou conserver `Port=auto`.
4. Tester le sender seul.

Test progressif recommande :

```powershell
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe probe --ini PicoCommandSender.p1.ini
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "PING"
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "HW GPIO_8B_SS_GPIO"
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "SET B1 RED"
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "SLOT 2 BLUE"
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "BATCH SLOT 1 RED;SLOT 2 BLUE;SLOT 3 GREEN"
src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe send --ini PicoCommandSender.p1.ini "CLEAR"
```

La detection automatique reprend l'idee de l'ancien projet Python :

- scanner les ports serie;
- configurer le baudrate;
- envoyer `PING`;
- accepter une reponse `PONG`, `READY` ou `DYNAMIC PANEL`.

## 6. Retention Des Snapshots

Le nettoyage n'est **jamais automatique**. Il necessite une action explicite
basee sur un audit manuel.

| Type de snapshot | Regle de retention | Remarque |
|---|---|---|
| **Release** | **100% conservees** | Flag `"release": true` dans le `manifest.json`. |
| **Non-release recents** | **50 derniers** | Utilises comme historique de travail. |
| **Non-release obsoletes** | **Suppression manuelle** | Apres audit via `Get-ChildItem`. Deplacer vers cold storage si besoin. |
| **Sans manifest.json** | **Conserves** | Jusqu'a conversion ou archivage explicite. |

Audit type :

```powershell
Get-ChildItem .versioning\commits -Directory |
  Sort-Object Name |
  Select-Object -Last 50
```

## 7. Cycle De Vie Release & Runtime

Le runtime utilisateur doit etre explicite. En developpement, les binaires
Debug restent dans `src\...\bin\Debug`. En release, les binaires publies sont
archives dans `artifacts\release\...`, puis copies a la racine seulement si le
contrat runtime choisi le demande.

| Composant / Action | Emplacement / Commande | Description / Role |
|---|---|---|
| **LedManager Debug** | `src\LedManager\bin\Debug\net8.0-windows\LedManager.exe` | Test local rapide. |
| **Pico sender Debug** | `src\PicoCommandSender\bin\Debug\net8.0-windows\PicoCommandSender.exe` | Test sender mono-Pico. |
| **Configs runtime** | `LedManager.ini`, `PicoCommandSender.*.ini`, `default.mem.effects.json` | Dependances reelles du comportement LED. |
| **Fichiers test** | `docs\test-*.jsonl`, `docs\*.MEM` | Fixtures de validation manuelle et sans materiel. |
| **Firmware Pico** | `fw\` | Firmware MicroPython et profils hardware. |
| **Release archivee** | `artifacts\release\<release-dir>` | Sortie `dotnet publish`, capturee par snapshot `-Release`. |

Generation framework-dependent, utile si .NET 8 est installe sur la machine :

```powershell
dotnet publish src\LedManager\LedManager.csproj -c Release -r win-x64 --self-contained false -o artifacts\release\LedManager-win-x64
dotnet publish src\PicoCommandSender\PicoCommandSender.csproj -c Release -r win-x64 --self-contained false -o artifacts\release\PicoCommandSender-win-x64
```

Generation self-contained, plus lourde mais autonome :

```powershell
dotnet publish src\LedManager\LedManager.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  -o artifacts\release\LedManager-win-x64-selfcontained

dotnet publish src\PicoCommandSender\PicoCommandSender.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  -o artifacts\release\PicoCommandSender-win-x64-selfcontained
```

Copie racine seulement apres validation utilisateur :

```powershell
Copy-Item -LiteralPath artifacts\release\LedManager-win-x64-selfcontained\LedManager.exe `
  -Destination .\LedManager.exe `
  -Force

Copy-Item -LiteralPath artifacts\release\PicoCommandSender-win-x64-selfcontained\PicoCommandSender.exe `
  -Destination .\PicoCommandSender.exe `
  -Force
```

Snapshot release type :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\new-version-snapshot.ps1 `
  -Label "release-1.0.0" `
  -Release `
  -Files "Directory.Build.props","LedManager.exe","PicoCommandSender.exe","LedManager.ini","PicoCommandSender.p1.ini","PicoCommandSender.p2.ini","PicoCommandSender.global.ini","default.mem.effects.json","docs\LOGS.md","docs\01_DEVOPS_VERSIONING.md" `
  -Tests "dotnet build LedManager.sln","dotnet run --project tests\LedManager.Tests\LedManager.Tests.csproj" `
  -Notes "Release LedManager 1.0.0 validee utilisateur"
```

## 8. Documentation Associee Au Cycle De Dev

| Action de code | Regle documentaire |
|---|---|
| Changement metier | Mettre a jour `docs\LOGS.md`, `docs\DOCS_INDEX.md` si besoin et les specs concernees. |
| Changement de flux APIExpose | Mettre a jour `00_POINT_PROJET_WORKFLOW.md`, les exemples `docs\test-*.jsonl` et les contrats WebSocket. |
| Changement `.MEM` / effets | Mettre a jour `default.mem.effects.json`, `docs\mem_*.md`, les `.MEM` exemples et les tests showcase. |
| Changement sender/Pico | Mettre a jour les `.ini`, `fw\README_DYNAMIC_PANEL_WS2812.md` et les commandes de test. |
| Versioning assembly | Mettre a jour `Directory.Build.props` sans hardcoder la version ailleurs. |

Documents de reference :

- [00_POINT_PROJET_WORKFLOW.md](00_POINT_PROJET_WORKFLOW.md)
- [DOCS_INDEX.md](DOCS_INDEX.md)
- [LOGS.md](LOGS.md)
- [mem_nomenclature_spec.md](mem_nomenclature_spec.md)
- [mem_generation_file.md](mem_generation_file.md)
