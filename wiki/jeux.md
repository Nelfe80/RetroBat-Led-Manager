# Panels par jeu et câblage

L'onglet **Mes jeux** de LedManager Setup personnalise le panel d'un jeu précis : ses
couleurs, et pour l'arcade son **câblage** complet - quelles actions du jeu tombent sur
quels boutons, quelles lampes s'allument où.

![Vue Mes jeux](assets/setup/setup-games.png)

## Choisir un jeu

1. Sélectionnez le **système** (arcade : `mame`… ; consoles : `snes`, `megadrive`…).
2. Le **gabarit** suit votre panel réel ; vous pouvez prévisualiser les dispositions
   2/4/6/8 boutons et les gabarits spéciaux d'un système.
3. Tapez dans la recherche (nom du jeu ou de la rom) puis choisissez dans la liste.

Le panel s'affiche avec les couleurs finales (pack + votre configuration système + votre
configuration jeu). Cliquez un bouton pour changer sa couleur - l'enregistrement crée un
patch léger dans `overrides\`, le Data Pack n'est jamais modifié.

## La baie de câblage (arcade)

Sous le panel, la **baie de câblage** montre le jeu comme une borne :

- à gauche, les **actions du jeu** (cyan) ;
- au centre, le **panel** : SELECT/START et les boutons, aux couleurs du jeu ;
- à droite, les **lampes** natives (MAME), groupées par famille - les familles non
  câblées sont repliées, cliquez l'en-tête pour les ouvrir ;
- en bas, les **périphériques** (joystick à voies, volant, pédale…) avec une prise par
  axe.

Gestes essentiels :

- **Tirez une prise vers un bouton** : le câble s'aimante. Une action peut être posée
  sur plusieurs boutons ; une lampe se re-domicilie (y compris sur START/SELECT).
- **Supprimer une liaison** : redéposez sur le bouton déjà branché, ou lâchez dans le
  vide pour revenir au réglage d'origine.
- **Cliquez** une puce, un bouton, un périphérique ou START/SELECT pour voir ses
  ramifications (écho sur le panel virtuel, et sur le vrai panneau pendant un test).
- **Double-clic** sur une puce : détails techniques du canal.
- Pointillé = réglage actuel · trait plein = votre modification.

« Enregistrer le câblage » sauvegarde votre configuration ; « Mettre à jour ce jeu »
(carte **Contrôles**) la pousse dans l'émulateur. Le bouton **Réparer ce jeu** réaligne
une configuration MAME abîmée par un changement de version, en interrogeant votre MAME
installé - vos réglages sont conservés.

## Tester mon système

Dans **Mes systèmes**, le bouton « Tester mon système » lance la rom de diagnostic des
contrôles du système sélectionné : appuyez sur chaque bouton du panel pour vérifier le
câblage de bout en bout. Les programmes utilisés viennent de la collection
[ES-Panels](https://github.com/Nelfe80/ES-Panels) (voir ses crédits et origines).
