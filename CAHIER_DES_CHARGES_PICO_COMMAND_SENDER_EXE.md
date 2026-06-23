# Cahier des charges — PicoCommandSender.exe multi-instance

## 1. Objectif

PicoCommandSender.exe reste un sender simple vers un seul Pico.

Pour gérer plusieurs Picos, LedManager lance plusieurs instances de PicoCommandSender.exe, chacune avec son propre fichier `.ini`.

```text
PicoCommandSender.exe daemon --ini PicoCommandSender.p1.ini
PicoCommandSender.exe daemon --ini PicoCommandSender.p2.ini
PicoCommandSender.exe daemon --ini PicoCommandSender.global.ini
```

PicoCommandSender n'a pas besoin de gérer plusieurs ports dans un même processus pour le MVP.

---

## 2. Principe

Une instance = un Pico = un port COM.

```text
Instance P1 -> COM8 -> Pico joueur 1
Instance P2 -> COM9 -> Pico joueur 2
Instance GLOBAL -> COM10 -> Pico global
```

LedManager fait le routage. PicoCommandSender exécute uniquement les commandes reçues.

---

## 3. Variables d'identité

Chaque `.ini` peut déclarer :

```ini
[Identity]
SenderId=P1
Player=1
Role=player
Name=Pico Player 1
```

ou :

```ini
[Identity]
SenderId=GLOBAL
Player=0
Role=global
Name=Global LED Pico
```

PicoCommandSender doit inclure cette identité dans ses logs et dans ses réponses.

Exemple :

```text
OK sender=P1 player=1 cmd="SET B1 RED"
ERR sender=P2 player=2 SERIAL_DISCONNECTED
```

---

## 4. Exemple PicoCommandSender.p1.ini

```ini
[Identity]
SenderId=P1
Player=1
Role=player
Name=Pico Player 1

[Serial]
Port=COM8
BaudRate=115200
AutoReconnect=true
ReconnectDelayMs=1000

[Pico]
InitCommands=PING|HW GPIO_8B_SS_GPIO|ONOFFINVERT ON|GET
ShutdownCommands=CLEAR
```

## 5. Exemple PicoCommandSender.p2.ini

```ini
[Identity]
SenderId=P2
Player=2
Role=player
Name=Pico Player 2

[Serial]
Port=COM9
BaudRate=115200
AutoReconnect=true
ReconnectDelayMs=1000

[Pico]
InitCommands=PING|HW GPIO_8B_SS_GPIO|ONOFFINVERT ON|GET
ShutdownCommands=CLEAR
```

## 6. Exemple PicoCommandSender.global.ini

```ini
[Identity]
SenderId=GLOBAL
Player=0
Role=global
Name=Global Matrix Pico

[Serial]
Port=COM10
BaudRate=115200
AutoReconnect=true
ReconnectDelayMs=1000

[Pico]
InitCommands=PING|HW ADDR_MATRIX_16X16|GET
ShutdownCommands=CLEAR
```

---

## 7. Commandes

Les commandes reçues restent identiques :

```text
SET B1 RED
SLOT 1 BLUE
START ON
MATRIXSCORE MATRIX1 12345 GREEN
```

Le sender n'a pas besoin de recevoir `player` dans la commande, car LedManager a déjà choisi la bonne instance.

Optionnellement, pour debug, il peut accepter :

```text
#PLAYER 1
#SENDER P1
```

Ces lignes sont ignorées côté Pico mais loggées côté sender.

---

## 8. Déconnexion partielle

Si le Pico P2 est débranché :

- l'instance P2 tente de se reconnecter ;
- l'instance P1 continue de fonctionner ;
- LedManager reçoit l'état P2 disconnected ;
- les commandes P2 sont mises en file courte ou ignorées selon configuration.

---

## 9. Statut

Chaque instance doit pouvoir répondre :

```text
STATUS
```

Réponse :

```json
{
  "senderId": "P1",
  "player": 1,
  "role": "player",
  "connected": true,
  "port": "COM8",
  "lastPong": "2026-06-10T18:00:00Z"
}
```

---

## 10. Critères d'acceptation

- On peut lancer deux instances simultanées de PicoCommandSender.exe.
- Chaque instance ouvre un port COM différent.
- Chaque instance applique son `InitCommands`.
- Une commande envoyée à P1 n'arrive pas sur P2.
- Les logs affichent `senderId` et `player`.
- Si P2 se déconnecte, P1 continue.
- LedManager peut piloter P1, P2 et GLOBAL indépendamment.
