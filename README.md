# RetroBat LedManager

**LedManager** pilote vos boutons LED RGB et panneaux WS2812 (Raspberry Pi Pico) au rythme de vos jeux RetroBat : couleurs par jeu, lampes MAME, effets temps réel - alimenté par [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose).

## 📖 Documentation

**➡ [Wiki en français](https://nelfe80.github.io/RetroBat-Led-Manager/)** · **[English wiki](https://nelfe80.github.io/RetroBat-Led-Manager/en/)**

Installation, plan de câblage Pico, firmware, configuration et cartes LED externes (PacLED64, LED-Wiz, WLED…).

## ⬇ Installation rapide

1. Installez d'abord [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe) (requis) et le [runtime .NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Téléchargez et lancez **[`LedManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Led-Manager/releases/latest/download/LedManager-Setup.exe)** : il installe le plugin dans `RetroBat\plugins\LedManager\` et enregistre le hook de démarrage EmulationStation.
3. Indiquez le port COM de votre Pico dans `PicoCommandSender.ini` (ou via **LedManagerSetup** que l'installateur propose d'ouvrir) - le câblage et le firmware sont expliqués dans le wiki.

## 📄 Licence

Usage personnel et non commercial libre ; utilisation commerciale sous licence écrite - voir [LICENSE.md](LICENSE.md) (schéma commun avec APIExpose).

---

# RetroBat LedManager

**LedManager** drives your RGB LED buttons and WS2812 panels (Raspberry Pi Pico) in sync with your RetroBat games: per-game colors, MAME lamps, real-time effects - powered by [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose).

## 📖 Documentation

**➡ [English wiki](https://nelfe80.github.io/RetroBat-Led-Manager/en/)** · **[Wiki en français](https://nelfe80.github.io/RetroBat-Led-Manager/)**

## ⬇ Quick install

1. First install [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe) (required) and the [.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Download and run **[`LedManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Led-Manager/releases/latest/download/LedManager-Setup.exe)**: it installs the plugin into `RetroBat\plugins\LedManager\` and registers the EmulationStation start hook.
3. Set your Pico's COM port in `PicoCommandSender.ini` (or via **LedManagerSetup**, which the installer offers to open) - wiring and firmware are explained in the wiki.

## 📄 Licensing

Free for personal, non-commercial use; commercial use under written license - see [LICENSE.md](LICENSE.md) (same scheme as APIExpose).
