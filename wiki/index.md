# Bienvenue

**LedManager** fait vivre les boutons et panneaux LED de votre borne au rythme de vos jeux RetroBat : chaque jeu colore vos boutons selon ses propres contrôles, les lampes MAME s'allument comme sur la borne d'origine, et les effets réagissent en temps réel à ce qui se passe à l'écran.

![Plan de câblage Pico](assets/pico_wiring_diagram_fr.png)

## Ce que fait LedManager

- **Panels par jeu** : à la sélection d'un jeu, vos boutons prennent les couleurs des contrôles réels du jeu (fournies par APIExpose).
- **Lampes arcade MAME** : les sorties natives (`READY_LAMP`, `TORP_LAMP_1`…) allument les bons boutons, comme sur la borne d'origine.
- **Effets ingame** : flashs, pulses Start/Select, scores sur matrice LED, réactions aux événements du jeu.
- **Matériel ouvert** : Raspberry Pi Pico prêt à l'emploi (plan de câblage fourni), et adaptable à d'autres cartes LED (PacLED64, LED-Wiz, WLED…).

## Par où commencer ?

<div class="grid cards" markdown>

- **[Premiers pas](premiers-pas.md)** — installer LedManager en 5 minutes.
- **[Matériel](materiel.md)** — câbler votre Raspberry Pi Pico et flasher le firmware.
- **[Configuration](configuration.md)** — décrire votre panel dans les fichiers `.ini`.
- **[Dépannage](depannage.md)** — les solutions aux problèmes courants.

</div>

!!! tip "Vous êtes plutôt du genre à tout brancher d'abord ?"
    Commencez par la page [Matériel](materiel.md) : une fois le Pico câblé et flashé, l'installation logicielle ne prend que quelques minutes.

## Comment ça marche

```text
APIExpose (événements des jeux)
   → LedManager.exe (décide quoi afficher)
      → commandes génériques (SLOT 1 RED, FLASH 6 YELLOW 80…)
         → PicoCommandSender.exe (adapte à votre matériel)
            → Raspberry Pi Pico → vos LEDs
```

LedManager fait partie de la famille de plugins RetroBat avec [APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose) (le moteur de données, **requis**) et [MarqueeManager](https://github.com/Nelfe80/RetroBat-Marquee-Manager) (écrans marquee et DMD).
