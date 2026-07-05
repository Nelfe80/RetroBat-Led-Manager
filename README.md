# RetroBat LedManager

**LedManager** pilote vos boutons LED RGB et panneaux WS2812 (Raspberry Pi Pico) au rythme de vos jeux RetroBat : couleurs par jeu, lampes MAME, effets temps réel — alimenté par [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose).

## 📖 Documentation

**➡ [Wiki en français](https://nelfe80.github.io/RetroBat-Led-Manager/)** · **[English wiki](https://nelfe80.github.io/RetroBat-Led-Manager/en/)**

Installation, plan de câblage Pico, firmware, configuration et cartes LED externes (PacLED64, LED-Wiz, WLED…).

## ⬇ Installation rapide

1. Installez d'abord [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases) (requis) et le [runtime .NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Téléchargez `LedManager-x.y.z-full.7z` depuis les [Releases](https://github.com/Nelfe80/RetroBat-Led-Manager/releases).
3. Décompressez dans `RetroBat\plugins\` → `RetroBat\plugins\LedManager\`.
4. RetroBat fermé, double-cliquez `install-es-start-hook.bat`, puis relancez RetroBat.
5. Indiquez le port COM de votre Pico dans `PicoCommandSender.ini` — le câblage et le firmware sont expliqués dans le wiki.

## 📄 Licence

Usage personnel et non commercial libre ; utilisation commerciale sous licence écrite — voir [LICENSE.md](LICENSE.md) (schéma commun avec APIExpose).

---

# RetroBat LedManager

**LedManager** drives your RGB LED buttons and WS2812 panels (Raspberry Pi Pico) in sync with your RetroBat games: per-game colors, MAME lamps, real-time effects — powered by [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose).

## 📖 Documentation

**➡ [English wiki](https://nelfe80.github.io/RetroBat-Led-Manager/en/)** · **[Wiki en français](https://nelfe80.github.io/RetroBat-Led-Manager/)**

## ⬇ Quick install

1. First install [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases) (required) and the [.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Download `LedManager-x.y.z-full.7z` from the [Releases](https://github.com/Nelfe80/RetroBat-Led-Manager/releases).
3. Extract into `RetroBat\plugins\` → `RetroBat\plugins\LedManager\`.
4. With RetroBat closed, double-click `install-es-start-hook.bat`, then start RetroBat.
5. Set your Pico's COM port in `PicoCommandSender.ini` — wiring and firmware are explained in the wiki.

## 📄 Licensing

Free for personal, non-commercial use; commercial use under written license — see [LICENSE.md](LICENSE.md) (same scheme as APIExpose).
