# L'assistant de configuration

`LedManagerSetup.exe`, à la racine du plugin, est l'outil visuel qui vous accompagne du branchement à la mise en service — sans éditer un seul fichier à la main.

Il propose deux modes, dans la barre latérale :

- **Panel virtuel** : un miroir en temps réel de votre panneau. Quand LedManager tourne (en jeu ou dans les menus), il affiche exactement les couleurs envoyées à vos LEDs. Idéal pour vérifier que tout réagit, ou déboguer sans avoir les yeux sur la borne.
- **Assistant matériel** : le parcours guidé pour configurer et tester votre Pico.

## Le panel virtuel

Ouvrez `LedManagerSetup.exe` pendant que RetroBat tourne : la pastille devient verte et le panneau virtuel s'anime en même temps que le vrai. Vous y retrouvez la [disposition standard conseillée](materiel.md#la-disposition-standard-conseillee-pour-retrobat) (SELECT/START en haut à gauche, puis les deux rangées).

!!! note "Un léger ralentissement en jeu est normal"
    L'outil tourne en priorité basse pour ne pas gêner l'émulation, mais gardez-le fermé pendant vos parties sérieuses — c'est un outil de réglage.

## L'assistant matériel

L'assistant prend le **contrôle direct** de votre Pico pour le tester. Comme LedManager occupe le port du Pico, l'assistant l'arrête automatiquement au démarrage du test (vos LEDs s'éteignent le temps de la configuration, c'est normal).

### 1. Préparation

Branchez votre Pico en USB (câble **data**, pas un câble de charge seul), puis cliquez **Détecter le Pico**. L'assistant :

- arrête LedManager pour libérer le port ;
- cherche votre Pico sur les ports série et lit sa version de firmware ;
- relance le pilote et allume tout le panneau en blanc.

Si rien n'est détecté : vérifiez le câble, et que le [firmware est installé](materiel.md#flasher-le-firmware).

### 2. Test du panneau

Vos boutons doivent être **tous allumés en blanc**. C'est la confirmation que l'alimentation et le firmware fonctionnent. Si certains restent éteints, c'est un problème de câblage ou d'alimentation (voir [Dépannage](depannage.md)).

### 3. Test du câblage

C'est l'étape maligne : un bouton s'allume en **vert** sur votre vrai panneau, un par un. À chaque fois, **cliquez sur le bouton virtuel qui correspond** au bouton allumé en vrai. Un clignotement cyan confirme votre clic, sur l'écran comme sur le panneau.

START et SELECT sont testés en fin de séquence.

L'assistant compare ainsi votre câblage réel à la disposition attendue. À la fin, il vous dit si tout correspond, ou liste les différences éventuelles — pratique pour repérer une inversion de fils sans tout démonter.

!!! tip "À venir"
    La correction automatique du mapping (l'assistant réécrit le câblage logiciel au lieu de vous faire ressouder), le test des canaux couleur et la génération complète de la configuration arrivent dans les prochaines versions.
