Overrides utilisateur / User overrides
======================================

FR - Vos personnalisations de panel, jamais ecrasees par les mises a jour.
Ce sont des patchs epars : seul ce que vous changez y figure, le reste
continue de venir d'APIExpose (et de profiter de ses ameliorations).

  overrides\systems\<systeme>.json        tous les jeux d'un systeme
  overrides\games\<systeme>\<rom>.json    un jeu precis (gagne sur le systeme)

Exemple - couleurs Rainbow Road pour Super Mario Kart :
  fichier : overrides\games\snes\smk.json
  {
    "schema": "ledmanager.panel-override.v1",
    "slots": {
      "1": { "color": "GREEN" },
      "2": { "color": "YELLOW" },
      "3": { "color": "BLUE" },
      "4": { "color": "RED" }
    }
  }

Exemple - mapper une lampe de jeu arcade sur un bouton :
  fichier : overrides\games\mame\daytona.json
  {
    "schema": "ledmanager.panel-override.v1",
    "outputs": {
      "VR1 Lamp": { "slot": 1 },
      "VR2 Lamp": { "slot": 2 }
    }
  }

Cles de slots : "1" = slot 1 joueur 1, "2:3" = slot 3 joueur 2.
Cles d'outputs : le nom de la sortie du jeu (voir le panel virtuel ou les logs).
L'application LedManagerSetup generera ces fichiers pour vous.

EN - Your panel customizations, never clobbered by updates. These are sparse
patches: only what you change goes here, everything else keeps coming from
APIExpose. Slot keys: "1" = slot 1 player 1, "2:3" = slot 3 player 2.
Output keys: the game's output name. The LedManagerSetup app will generate
these files for you.
