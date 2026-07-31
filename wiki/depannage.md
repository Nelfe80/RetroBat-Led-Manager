# Dépannage

## Rien ne s'allume

Vérifiez dans l'ordre :

1. **Le port COM** — dans `PicoCommandSender.ini`, section `[Serial]`, la valeur `Port=` doit correspondre au port attribué par Windows (Gestionnaire de périphériques → Ports COM).
2. **APIExpose tourne** — LedManager n'a rien à afficher sans lui. Vérifiez que le plugin APIExpose est installé et démarré.
3. **Le firmware répond** — connectez-vous au Pico en série (115200 bauds) et envoyez `VERSION`. Réponse attendue : `VERSION DYNAMIC_PANEL_ADDR <date>`. Sinon, reflashez avec `tools\deploy-pico-fw.ps1`.
4. **Le délai de démarrage** — `StartupDelayMs` dans `LedManager.ini` laisse le temps au Pico de s'initialiser. S'il est trop court, les premières commandes partent dans le vide.

!!! tip "Tester sans matériel"
    Passez `DryRun=true` dans la section `[CommandSender:P1]` de `LedManager.ini` : les commandes sont journalisées au lieu d'être envoyées, ce qui permet de vérifier toute la chaîne logique sans Pico branché.

## Les LED se figent en cours de partie (timeout COM)

Quand Windows met le port COM en timeout, le pont série le **rouvre automatiquement** et rejoue la commande. `PicoCommandSender.exe` détecte alors `SERIAL REOPENED` et renvoie l'initialisation complète du panel. Vous ne devriez rien avoir à faire ; si le problème revient souvent, augmentez `WriteTimeoutMs` dans `[Serial]` et vérifiez le câble USB.

## Le port COM reste occupé après un plantage

Un ancien processus garde parfois `COM3` ouvert. Deux solutions :

- double-cliquez sur **`stop.bat`** — il ferme LedManager, ses senders et le pont série ;
- au prochain démarrage, `LedManager.exe` ferme de lui-même les anciennes instances trouvées dans le même dossier plugin.

Pour du débogage où ce nettoyage automatique gêne :

```powershell
.\LedManager.exe --no-kill-previous
```

## Une couleur s'affiche mal

Toutes les LED ne rendent pas fidèlement toutes les couleurs. Déclarez un remplacement dans `PicoCommandSender.ini` :

```ini
[ColorPolicy.Fallbacks]
GOLD=YELLOW
```

Voir [Configuration — la policy couleur](configuration.md#la-policy-couleur).

## Un bouton déclenche la mauvaise action en jeu

La bonne LED s'allume, mais appuyer déclenche autre chose (deux boutons inversés, par exemple) ? C'est le **câblage des contacts**, indépendant de celui des LEDs. Lancez l'[assistant](assistant.md) → **Juste la cartographie** : appuyez sur chaque bouton allumé, puis **« Écrire la cartographie & régénérer »**. Les remaps RetroArch et les configs MAME sont réécrits d'après ce que vos boutons envoient réellement — et l'opération est réversible.

Si l'assistant affiche **« Manette illisible »**, vérifiez qu'une manette/encodeur est bien branché et qu'EmulationStation est fermé.

## Où sont les logs ?

Dans le dossier `.log\` du plugin. C'est la première chose à joindre si vous demandez de l'aide : on y voit les commandes générées, les réponses du Pico et les éventuelles commandes ignorées.

## Réinitialiser le Pico

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File tools\reset-pico.ps1
```

Puis relancez LedManager (ou RetroBat).

## Toujours bloqué ?

Ouvrez un ticket sur le [suivi des problèmes de LedManager](https://github.com/Nelfe80/RetroBat-Led-Manager/issues) en joignant les logs du dossier `.log\`.
