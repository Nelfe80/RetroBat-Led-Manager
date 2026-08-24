mame-repair - reparation des cfg MAME
======================================

Le bouton « Reparer ce jeu » de LedManager Setup (carte Controles) utilise ces
fichiers via l'endpoint APIExpose POST /api/v1/panels/controls/mamecfg/repair.

Principe : MAME jette silencieusement les ports d'un cfg dont la signature
(type/tag/mask/defvalue) ne correspond pas a SA version. dump_ports.lua demarre
le jeu 2 secondes sans video ni son et exporte la liste reelle des ports de la
version MAME installee. La reparation realigne ensuite le cfg deploye sur ces
signatures : ports perdus restaures, tags/masks derives corriges, sequences de
l'utilisateur conservees.

Ces fichiers sont autonomes : ils ne dependent d'aucun script present a la
racine de l'emulateur MAME (ceux-ci disparaissent aux mises a jour RetroBat).
