# RetroBat LedManager

**LedManager** est un gestionnaire et orchestrateur d'effets LED dynamiques conçu pour fonctionner en harmonie avec **APIExpose** pour RetroBat. Il convertit les événements système (sélection d'un jeu, lancement d'une ROM, appui sur des boutons, scores de jeux d'arcade) en animations lumineuses interactives sur vos boutons et dalles LED.

Ce dépôt contient l'exécutable compilé, les scripts de firmware pour le matériel Raspberry Pi Pico, la documentation utilisateur et le code source complet de la solution pour compilation par l'utilisateur.

---

## ⚠️ Licences et Protection (IMPORTANT)

Ce projet et ses fichiers de configuration sont protégés par le modèle de licence APIExpose :
1.  **Logiciel / Code Source** : Distribué sous licence **personnelle et non-commerciale** (voir `LICENSE.md` et `PERSONAL-LICENSE.md`). L'utilisation commerciale, l'intégration payante ou la revente matérielle/logicielle sans accord de licence commerciale écrit préalable est strictement interdite (voir `COMMERCIAL-LICENSE.md`).
2.  **Pack de Données d'Effets et Mappings** : Les effets LED prédéfinis (`default.mem.effects.json`) et les configurations associées sont protégés par la licence **`DATA-LICENSE.md`**.

---

## 🏗️ Architecture et Responsabilités

Le projet est scindé en trois couches pour rester agnostique de votre matériel LED final :

1.  **APIExpose** (Service API de RetroBat) : Diffuse les faits système bruts (le jeu actif, les outputs de l'émulateur MAME en cours, les scores).
2.  **LedManager.exe** (L'Orchestrateur) : Se connecte par WebSocket à APIExpose, gère la mémoire des états de jeu, applique les règles d'effet d'animations (comme les flashes d'action), déduplique les signaux et transmet les instructions génériques à un envoyeur de commandes matériel.
3.  **PicoCommandSender.exe** (Le Traducteur Matériel) : Reçoit les commandes génériques de `LedManager.exe`, les adapte aux contraintes électriques de vos ports GPIO déclarés et les envoie sur le port série USB du Raspberry Pi Pico.

---

## 📁 Structure du Dépôt

*   `LedManager.exe` : Orchestrateur principal précompilé (Windows x64).
*   `PicoCommandSender.exe` : Outil de transmission série USB précompilé.
*   `LedManager.ini` / `PicoCommandSender.ini` : Fichiers de configuration de routage des ports et de comportement.
*   `apiexpose-curator-ledmanager.ini` : Profil d'intégration.
*   `default.mem.effects.json` : Base de données des effets lumineux prédéfinis.
*   `stop.bat` : Script de fermeture propre de toutes les instances et libération du port COM.
*   `docs/` : Guides d'utilisation, spécifications et schémas.
    *   `docs/PICO_WIRING_AND_CONFIG.md` : Guide complet de câblage physique et de flashage du Pico (à consulter en priorité !).
*   `fw/` : Code source du firmware MicroPython à copier sur votre Raspberry Pi Pico.
*   `src/` : Code source C# complet de LedManager et PicoCommandSender.

---

## ⚡ Câblage Physique et Configuration du Pico

Pour savoir comment raccorder votre Raspberry Pi Pico à vos boutons d'arcade, à vos rubans ou dalles de LED WS2812B (NeoPixel) et comment charger le firmware MicroPython, consultez notre guide détaillé :

👉 **[Guide de Câblage et Configuration du Raspberry Pi Pico](docs/PICO_WIRING_AND_CONFIG.md)**

Un schéma technique de câblage est également inclus dans ce guide (`docs/pico_wiring_diagram.png`) pour vous assister.

### ⚡ Schémas de Câblage (Wiring Diagrams)

#### Version Française :
![Schéma de Câblage Pico FR](docs/pico_wiring_diagram_fr.png)

#### English Version :
![Pico Wiring Diagram EN](docs/pico_wiring_diagram.png)

---

## 🔧 Compilation des Sources

Si vous désirez compiler les binaires `LedManager.exe` et `PicoCommandSender.exe` par vous-même :

1.  Installez le **SDK .NET 8.0** ou supérieur.
2.  Ouvrez une invite de commande ou PowerShell à la racine du dépôt.
3.  Compilez la solution complète en mode Release :
    ```powershell
    dotnet build LedManager.sln -c Release
    ```
4.  Les exécutables compilés se situeront dans :
    *   `src/LedManager/bin/Release/net8.0/LedManager.exe`
    *   `src/PicoCommandSender/bin/Release/net8.0/PicoCommandSender.exe`
    *   (Copiez-les à la racine du dépôt pour remplacer les binaires par défaut).
