# Versus en la mesa

Dos teléfonos juegan la misma batalla, uno contra otro, en la misma wifi: por ejemplo un Android y un iPhone. Cada quien usa su mazo actual y ve el tablero en AR sobre la mesa. El plan general del AR está en `docs/ar-plan.md`.

## Cómo se juega

1. **Los dos en la misma wifi.** No hace falta internet; basta con que los dos teléfonos estén en la misma red.
2. **En el menú principal** (la casa del árbol), a la izquierda, debajo de tu panel de jugador, toca **«Versus en la mesa»**.
3. **Elige el tablero de tu teléfono:** **«Con marcador»** o **«Sin marcador»** (ver más abajo). Cada quien elige lo suyo; los dos teléfonos no tienen que elegir lo mismo. Se guarda para la próxima vez.
4. **Uno toca «Crear partida».** Su teléfono muestra un **código grande**, que es el último número de su IP (por ejemplo «45» si es 192.168.1.45), y debajo la IP completa.
5. **El otro toca «Unirse»**, escribe ese número en el teclado y toca **«Conectar»**. Si la red no es la típica de casa (/24) y no conecta, escribe la IP completa con los puntos.
6. **Al conectar, los dos entran a la batalla:** el tablero de Candy Kingdom, con las reglas normales.
   - Con AR, primero se coloca el tablero (ver «Modos de tablero»).
   - Cada quien coloca sus 4 paisajes. Si el rival aún no ha colocado los suyos, sale «Esperando a que el rival coloque sus paisajes…».
   - Cada quien gira la botella de kétchup. Quién empieza se decide al azar, igual en los dos teléfonos, y la botella acaba apuntando a esa persona.
7. **Los turnos son los de siempre.** En tu turno juegas cartas, haces floop, usas el líder y terminas el turno. En el del rival ves sus jugadas en tu tablero y arriba sale «Turno del rival…».
8. **El anillo de batalla lo juega quien ataca.** Cuando atacan tus criaturas, tocas el anillo como siempre: fallo, golpe o crítico. Cuando ataca el rival, tu teléfono espera su tirada («El rival está atacando…») y la muestra.
9. **Al acabar** salen «¡Ganaste!» o «Perdiste», con **«Revancha»** (empieza quien perdió) y **«Volver al menú»**.

**No bloqueen el teléfono ni cambien de app durante la partida.** Un vistazo corto a otra app aguanta, pero si un teléfono pasa unos 35 s sin responder (o se sale de la wifi, o se cierra la app), el otro muestra «Se perdió la conexión con el rival» con el botón «Volver al menú».

## Modos de tablero

### Con marcador impreso: los dos ven el mismo tablero

- **Imprime** `docs/marcador/marcador-versus.pdf`.
  - Página 1: A4. Página 2: carta.
  - Imprime **al 100 %** (tamaño real, sin «ajustar a la página»). El cuadro del marcador debe medir **15 × 15 cm**; la regla de 5 cm de la hoja sirve para comprobarlo.
  - Si no hay PDF a mano: `docs/marcador/marcador-versus-A4.png` está a 300 ppp.
- **La hoja va plana en el centro de la mesa.** Los dos se sientan uno frente al otro:
  - **Quien crea la partida (anfitrión)** se sienta en el borde de abajo, el de la **muesca blanca**, donde dice «Aquí se sienta quien CREA la partida».
  - **Quien se une (invitado)** se sienta en el borde de enfrente.
- **Al colocar**, apunta el teléfono a la hoja. En cuanto la reconoce, el tablero se pega a ella a su tamaño normal (unos 50 cm, más grande que la hoja) y sale «Tablero sobre el marcador».
  - El anfitrión ve su lado hacia la muesca; el invitado, girado 180°. Así los dos miran **el mismo tablero físico**, cada quien con su lado hacia sí.
  - Mientras la hoja se ve, el tablero la sigue. Cuando deja de verse, se queda anclado a la mesa donde estaba.
- **«Ajustar»** sigue disponible. Si lo usas, el tablero deja de seguir al marcador y lo mueves tú. **«Recolocar»** vuelve a buscar el marcador.
- **Durante la colocación** hay un botón **«Sin marcador»** para ignorar la hoja y colocarlo tocando la mesa. Con «Usar marcador» se vuelve a buscar.

### Sin marcador: cada quien coloca su tablero

- No hace falta imprimir nada. Es la colocación por planos de siempre: mueve el teléfono sobre la mesa hasta que salgan los puntos y toca donde quieras el tablero. Tu lado (azul) queda hacia ti.
- **Para compartir mesa**, pongan algo en el centro (una moneda, un vaso) y que cada quien toque sobre ese punto desde su lado. Afinen con **«Ajustar»**: un dedo mueve, dos dedos giran y cambian el tamaño, y ▲/▼ cambian la altura.
- **También funciona en mesas distintas**, o en dos cuartos de la misma casa: cada tablero es independiente. Lo único que viaja por la red son las jugadas.

### Sin AR

- **En el build de escritorio** (o con `"ar_mode": false`) no hay colocación: el versus se juega con la cámara normal.
- **Para probar en la Mac**, ver «Prueba sin teléfonos».

## Qué cambia respecto a una misión

- **Anillo de batalla:** solo lo juega quien ataca. No hay anillo de bloqueo para el defensor, para que cada ataque tenga una sola tirada que los dos teléfonos aplican igual. Si quien ataca falla, no hay daño; con golpe, el daño normal; con crítico, el daño crítico del líder atacante.
- **Orden de ataque:** las criaturas atacan en el orden de los carriles de su dueño (en una misión, el ataque del rival iba al revés), para que el orden sea el mismo en los dos teléfonos.
- **Sin recompensas:** no hay botín de las criaturas, cofres, monedas, experiencia ni progreso de misiones, ni pantalla de «continuar por gemas». Tampoco se gasta energía.
- **Mazo vacío:** los dos siguen la misma regla. Al empezar un turno sin cartas en el mazo, la mano y el descarte vuelven al mazo, se baraja y se roban 5. Es la regla que tenía la IA; la del jugador, que dependía de la interfaz, no era simétrica.
- **Quién empieza:** al azar en la primera partida; en la revancha empieza quien perdió.

## Cómo funciona por dentro

- **Lockstep.** Los dos teléfonos simulan la misma batalla y solo viajan las elecciones, en orden:
  - los paisajes;
  - las cartas jugadas (posición en la mano + id + carril);
  - el floop (carril) y el líder;
  - los objetivos elegidos y las cartas tomadas del descarte;
  - el fin de turno;
  - el resultado del anillo de quien ataca.
- **Roles canónicos:** el anfitrión es **A** y el invitado **B**. Cada teléfono se ve a sí mismo como `PlayerType.User` (P1, su lado hacia él) y al rival como `PlayerType.Opponent`. Todo lo que los dos tienen que hacer igual usa A/B (`VersusMatch.Side`).
- **Al empezar** se intercambian el mazo (cartas en orden, con nivel), el líder (id y rango) y los paisajes. El anfitrión elige la semilla y quién empieza, y manda `START`.
- **Azar compartido** (`VersusRandom`): un generador xorshift128+ sembrado, con **un flujo por lado canónico**. Cada uso de azar de la batalla saca del flujo del dueño del efecto, así el orden no depende de quién es P1 en cada teléfono. Fuera del versus todo sigue con `UnityEngine.Random`, como antes.
- **El rival** entra por `CWOpponentActionSequencer`, donde la IA llamaba a `MakeDecision()`. Sus objetivos y cartas del descarte entran por el mismo camino que los de una persona: las cartas usan `ChoosesByHand` en vez de `Owner == User`, y `CardScript.StartTargetSelection`/`OpenDiscardPile` esperan la elección que llega por la red.
- **Lo propio se manda desde:**
  - `CWPlayerHandsController.Summon`/`PlayCardToLane` (cartas);
  - `CWFloopActionManager.PlayFloopAction`/`TriggerLeader` (floop y líder);
  - `GameState.SelectTarget` (objetivos) y `CWDiscardCard.OnClick` (descarte);
  - `CWP1SetupBranch.Advance` (fin de turno, una vez por turno);
  - `CWBattleSequenceController.StopRing` (anillo);
  - `LandscapeReadyButtonScript` (paisajes).
- **Hash por turno:** al empezar cada turno, cada teléfono escribe el estado en orden A, B:
  - vidas y magia;
  - enfriamiento del líder;
  - mano, mazo y descarte (ids en orden);
  - paisaje, criatura (id, ataque, defensa, daño) y edificio de cada carril;
  - turno y cuántos números ha sacado cada flujo de azar.

  Los dos se intercambian ese estado y lo comparan. Si no coincide, el registro dice `VERSUS DESYNC` con los dos estados y en pantalla sale un aviso rojo. La partida sigue.
- **Red:** TCP en el puerto **47123**; el anfitrión escucha y el invitado se conecta. Mensajes de texto por línea y un ping cada 2 s; si en 35 s no llega nada, se da por cortada (al volver del segundo plano se reinicia la cuenta). No usa broadcast ni multicast UDP (en iOS piden un permiso especial). En iOS sí sale el aviso de «red local» la primera vez: hay que aceptarlo.
- **Archivos:**
  - `VersusMatch.cs` (sesión, protocolo, hash, pantallas);
  - `VersusNet.cs` (sockets);
  - `VersusRandom.cs`;
  - `VersusMenu.cs` (lobby en OnGUI) y `VersusGui.cs`;
  - `VersusTestBot.cs` (modo de prueba);
  - `ARBattleRig.cs` (marcador);
  - `Assets/Editor/ARMarkerSetup.cs` (librería del marcador).

## Auditoría del azar

En `Assets/Scripts` hay unos 75 usos de `Random.Range`/`Random.value`/`System.Random`.

**Pasan al azar sembrado** (`VersusRandom`, flujo del dueño):

| Dónde | Qué decide |
| :-- | :-- |
| `Deck.Shuffle(owner)`, desde `GameState.Setup` (los dos mazos), `GameState.Reshuffle`, `ReturnHandsDrawCardsAddMP`, `ShuffleAndDraw`, `ShuffleHand` | Orden del mazo |
| `GameState.ReturnCardToHand` | Qué carta vuelve del descarte |
| `CWList.RandomItem(owner)` en `DamageRandom`, `HealRandom`, `DEFBonusRandom`, `RandomCard` | Objetivo o carta al azar de un efecto |
| `RandomDEFBonus` | Defensa extra al azar |
| `RandomDiscardCard` | Carta al azar del descarte |

**Se quitan en el versus:**

| Dónde | Por qué |
| :-- | :-- |
| `Deck.ShuffleLandscapes` del rival | Los paisajes del rival son los que él coloca. |
| Botín de `CreatureBattleScript` (`DetermineSideQuestToDropItem`, `CanDropCoins`, `CanDropCard`) | En el versus no hay botín. |

**Se quedan como estaban:**

| Dónde | Por qué |
| :-- | :-- |
| `CWBattleSequenceController.SetUpRing` (zona y velocidad del anillo) | Solo afecta al anillo de quien ataca, en su teléfono; el resultado viaja por la red. |
| `CWKetchupBottleScript` (giro de la botella) | La botella se guía hasta el jugador que dice la semilla. |
| IA: `WeightedList.TopCandidate`, `ProtectTarget.SelectTarget`, `SelectCard` de `ReturnCard*` | En el versus no se usa la IA; el rival elige en su teléfono. |
| `GameState` y `QuestData` (XP, monedas y cartas de premio), `QuestManager`, `ElFistoController`, `GachaManager`, `DailyGift*`, `CWQuestLoad` (misión multijugador) | Fuera de la batalla. |
| `iTween`, `Nc*` (partículas), `UVScroll`, `ULAnimController`, `KFFRandom` (sonidos y elección de la botella), `VOManager`/`VOController`, `AudioManager`, `TipManager`, `DebugButtonScript`, `DebugFloopActionCamera` | Cosméticos o de depuración. |
| `PlayerInfoScript`/`SwizzleCrypto` (cifrado del guardado), `SQWebFileServer` | No son de la batalla. |

## Prueba sin teléfonos (dos Macs en localhost)

El build de escritorio (`ar_mode` apagado) trae un modo de prueba: cada instancia juega sola por las mismas entradas que una persona.

```sh
Unity -batchmode -quit -projectPath . -buildTarget StandaloneOSX -executeMethod PlatformBuild.Build -buildPath Build/Mac/CardWars.app
APP="Build/Mac/CardWars.app/Contents/MacOS/Card Wars"
CW_VERSUS=host CW_VERSUS_GAMES=2 CW_TEST_SHOTS=/tmp/vs/shots CW_VERSUS_LOG=/tmp/vs/match.log \
  "$APP" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile /tmp/vs/host.log &
CW_VERSUS=join CW_VERSUS_ADDR=127.0.0.1 CW_VERSUS_GAMES=2 CW_TEST_SHOTS=/tmp/vs/shots CW_VERSUS_LOG=/tmp/vs/match.log \
  "$APP" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile /tmp/vs/join.log &
```

- **Variables:**
  - `CW_VERSUS` = `host` o `join`.
  - `CW_VERSUS_ADDR` (por defecto 127.0.0.1) y `CW_VERSUS_PORT`.
  - `CW_VERSUS_GAMES`: partidas, con revancha automática.
  - `CW_TEST_SHOTS` y `CW_TEST_INTERVAL`: capturas.
  - `CW_VERSUS_LOG`: registro de la partida.
  - `CW_VERSUS_BOTSEED`, `CW_VERSUS_TIMEOUT` (minutos) y `CW_TEST_CLICKS`.
- **El juego va en silencio.** Cada turno el registro dice `turn N in sync (hash)` o `DESYNC`, y al final `GAME n OVER: won/lost, desyncs 0`.
