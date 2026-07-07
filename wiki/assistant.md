# L'assistant de configuration

`LedManagerSetup.exe`, à la racine du plugin, est l'outil visuel qui vous accompagne du branchement à la mise en service — sans éditer un seul fichier à la main.

Il propose deux modes, dans la barre latérale :

- **Panel virtuel** : un miroir en temps réel de votre panneau. Quand LedManager tourne (en jeu ou dans les menus), il affiche exactement les couleurs envoyées à vos LEDs. Idéal pour vérifier que tout réagit, ou déboguer sans avoir les yeux sur la borne.
- **Assistant matériel** : le parcours guidé pour configurer et tester votre Pico.

!!! note "Français ou anglais"
    L'outil s'affiche dans la langue de RetroBat (réglage EmulationStation), sinon celle de Windows. Pour forcer : `LedManagerSetup.exe --lang fr` ou `--lang en`.

## L'accueil : état de l'installation

![Accueil](assets/setup/setup-home.png)

L'onglet d'ouverture vérifie chaque maillon de la chaîne : LedManager (avec boutons Démarrer/Arrêter), le Pico (port configuré, détection à la demande quand LedManager est arrêté), APIExpose et le miroir du panel virtuel, le Data Pack (systèmes et jeux curatés disponibles) et vos personnalisations. Un maillon rouge = le point à régler en premier.

## Le panel virtuel

![Panel virtuel](assets/setup/setup-monitor.png)

Ouvrez `LedManagerSetup.exe` pendant que RetroBat tourne : la pastille devient verte et le panneau virtuel s'anime en même temps que le vrai. Vous y retrouvez la [disposition standard conseillée](materiel.md#la-disposition-standard-conseillee-pour-retrobat) (SELECT/START en haut à gauche, puis les deux rangées).

!!! note "Un léger ralentissement en jeu est normal"
    L'outil tourne en priorité basse pour ne pas gêner l'émulation, mais gardez-le fermé pendant vos parties sérieuses — c'est un outil de réglage.

## Mes jeux : personnaliser les couleurs

![Mes jeux](assets/setup/setup-games.png)

L'onglet **Mes jeux** affiche le panel de chaque système tel que le pack le définit (voir [Panels par système](systemes.md)), et vous laisse le repeindre : cliquez un bouton, choisissez sa couleur dans la palette du firmware (19 couleurs), enregistrez. Votre personnalisation est écrite en **patch épars** dans `overrides\systems\<système>.json` — le pack n'est jamais modifié, et LedManager applique le patch dès la prochaine sélection de jeu, sans redémarrage.

- Le sélecteur **Panel** sert d'aperçu : 2/4/6/8 boutons et variantes historiques (Score Master, Fighting Stick…). L'override s'applique au système entier.
- **Jeu arcade** : tapez un nom de rom (mslug, chasehq, seawolf…) pour éditer un jeu précis parmi les 3280 jeux curatés — le panel affiché est exactement celui que le runtime résout (pack + patch système), et votre peinture est écrite dans `overrides\games\<système>\<rom>.json`, prioritaire sur le patch système.
- **« Couleur d'origine »** dans la palette retire l'override d'un bouton ; **« Revenir aux couleurs du pack »** supprime tout le patch.
- **« Tester sur le panneau réel »** arrête LedManager le temps du test et envoie vos couleurs au Pico : elles suivent vos clics en direct sur les vrais boutons.

## L'assistant matériel

![Assistant matériel](assets/setup/setup-wizard.png)

L'assistant prend le **contrôle direct** de votre Pico pour le tester. Comme LedManager occupe le port du Pico, l'assistant l'arrête automatiquement au démarrage du test (vos LEDs s'éteignent le temps de la configuration, c'est normal).

### 1. Préparation

Branchez votre Pico en USB (câble **data**, pas un câble de charge seul), puis cliquez **Détecter le Pico**. L'assistant :

- arrête LedManager pour libérer le port ;
- cherche votre Pico sur les ports série et lit sa version de firmware ;
- relance le pilote et allume tout le panneau en blanc.

Si rien n'est détecté : vérifiez le câble, et que le [firmware est installé](materiel.md#flasher-le-firmware).

### 2. Test du panneau

Vos boutons doivent être **tous allumés en blanc**. C'est la confirmation que l'alimentation et le firmware fonctionnent. Si certains restent éteints, c'est un problème de câblage ou d'alimentation (voir [Dépannage](depannage.md)).

### 3. Test des couleurs

L'assistant allume chaque canal l'un après l'autre : tout le panneau en rouge, puis en vert, puis en bleu. Le panneau virtuel montre la couleur attendue ; si le vrai panneau affiche autre chose (les fils R/G/B ont été croisés au montage), indiquez la couleur réellement vue.

L'assistant en déduit l'ordre réel des fils et **corrige l'ordre des canaux dans la configuration** — sans ressouder. Le test se relance ensuite pour confirmer.

### 4. Test du câblage

C'est l'étape maligne : un bouton s'allume en **vert** sur votre vrai panneau, un par un. À chaque fois, **cliquez sur le bouton virtuel qui correspond** au bouton allumé en vrai. Un clignotement cyan confirme votre clic, sur l'écran comme sur le panneau.

START et SELECT sont testés en fin de séquence.

L'assistant compare ainsi votre câblage réel à la disposition attendue. Si des différences apparaissent (deux fils inversés, par exemple), le bouton **« Corriger automatiquement »** réécrit le câblage logiciel (`[GPIO:P1]` de `PicoCommandSender.ini`, avec sauvegarde `.bak`) pour que chaque bouton réponde à sa place — sans rien démonter. Le test se relance pour confirmer.

### 5. Enregistrer la configuration

Quand tout correspond, **« Enregistrer la configuration »** écrit dans `PicoCommandSender.ini` ce que l'assistant a vérifié sur votre matériel : le port COM qui a répondu, la composition du panneau (nombre de boutons, START/SELECT), et un délai d'initialisation **mesuré** sur votre Pico plutôt que la valeur prudente livrée par défaut — LedManager démarre d'autant plus vite.
