# Card Wars en AR: mapa de la batalla y plan

Plan para poner el tablero de batalla en realidad aumentada con el celular (AR Foundation + ARCore/ARKit). La cámara del teléfono hace de cámara del juego, el tablero se apoya en una mesa real a unos 50 cm, y la mano de cartas y los botones de NGUI siguen como capa 2D en la pantalla.

Las rutas son relativas a la raíz del proyecto y los scripts están en `Assets/Scripts/Assembly-CSharp/`. Los números de línea son de `feat/ar` @ 44c2af35.

## Tablero

- Vive en `BattleScene` (`Assets/Scene/_hirez/Scenes/`), en coordenadas de mundo alrededor del origen. P1 está en −X, P2 en +X, y los 4 carriles van a lo largo de Z.
  - `Environment/GameBoard` (rot Y 180): el borde mide unas **48 × 44 unidades** en X/Z.
  - `Battle Lanes`: los puntos de aparición `P{1,2}Lane{1-4}Creature` están en x=±6.1 y los `…Building` en x=±17.9, con y=−2.2 y z=±6/±17. Los colliders de carril `FLIPPED/P{1,2}FlippedLandscape{1-4}` están en x=±11.5.
- **Escala:** 48 unidades = 0.5 m, o sea un factor de ≈0.0104. Es mejor **escalar el XR Origin (≈96)** que el contenido, porque hay offsets en unidades de mundo repartidos por el código.
- **Lo que hay que ocultar se crea al cargar:**
  - Nivel y mesa: `Resources/environment/<Level|Table>Prefab`, instanciados en la raíz (CWLoadLevelForBattle.cs:29-31, 66-78).
  - Personajes y sillas: hijos de `Environment/P1CharacterPosition` (−67,−51,0) y `P2CharacterPosition` (67,−51,0), creados por GameDataScript.UpdateCharacters:128-164.
  - Desactivar los Renderers en vez de usar SetActive, porque los animadores siguen usando esos objetos.
- **Bug:** `tableObj` nunca se asigna, así que el interruptor de depuración `hideLevel` (DebugFlagsScript.cs:167-176) da NullReferenceException.

## Cámaras

- **3D:** `Battle GameCamera`, perspectiva con FOV 60, tag MainCamera.
  - Tiene **UICamera**: es la cámara 3D y además la que recibe los toques NGUI sobre los colliders del tablero.
  - Es la `newCamera`, `mainGameCam`, `BattleCamera` y `GameCamera` de muchos scripts.
  - **Plan: que esta misma cámara sea la cámara AR.** Se reparenta bajo `XROrigin/Camera Offset` y se le añaden ARCameraManager, ARCameraBackground y TrackedPoseDriver. clear = SolidColor con alfa 0.
- **Capas encima,** todas ortográficas con clear=Depth, así que se superponen bien:
  - UI NGUI: `Battle UI Camera`, profundidad 30.
  - Mano: `Battle FingerGesture Camera`, profundidad 40.
  - `CommandCard Camera` y `Particle Camera`, profundidad 100.
- **Pantalla partida:** `Battle BattleCamera P1/P2`. También llevan tag MainCamera: hay que quitárselo.
- **Qué mueve la cámara durante la batalla.** Una guarda `if (ARMode.On) return;` en cada una:
  - `CWiTweenCamTrigger.PlayCam` (:52-76; hay 79 instancias en la escena).
  - `CWiTweenBattleCam`: NotCutToBattleCamera :57-79, CutToBattleCamera :81-95, MoveBattleCamera :97-116.
  - `CWiTweenVantageCam.CreatureSpawnCamera` :69-103, llamada desde CWBranchSummonCam.
  - `CWBattleSequenceController.SplitCameraEnable` :181-197, que apaga la cámara principal. Dejar solo `camAlignFlag=false`. También `UpdateBattleCamera` :715.
  - `CWFloopActionManager.FloopCameraTrigger` :418-432 y `SetCameraPosition` :797-814.
  - `CWBattleEndCameraSwitch.OnClick` (una de sus instancias apaga la cámara principal).
  - `CWMenuCameraTarget` (3 instancias): hace LookAt en cada LateUpdate. Basta con `enabled=false`.
  - `CWBattleIntroController` :159-171: no reproducir las animaciones de cámara de la intro, pero esperar lo que duran para que los letreros no se descuadren.
  - Las cámaras de los efectos de hechizo (`Resources/props/FX_Battle_Spell_*`, registradas como SpellCamera en CWFloopActionManager :248-319) también hay que desactivarlas.

## Entrada

- **Jugar carta:** se arrastra desde la mano (CWTBDragToMove). Al soltar, `checkReleasePoint` :508-529 proyecta los bounds de los carriles con `GameCamera.WorldToScreenPoint` y compara en pantalla. Eso supone mirar desde el lado de P1.
  - **En AR hay que reescribirlo:** lanzar un rayo desde la cámara contra el plano de la mesa (`tablePlane` y=0) y ver en qué bounds X/Z de carril cae.
- **Floop, info y objetivos de hechizo:** son OnClick NGUI sobre los BoxCollider de criaturas, edificios y carriles (CWCommandCardSet :38-74, FlippedLandscapeScript :67-76). Funcionan tal cual si la UICamera es la cámara AR.
- **Ojo con `NGUITools.FindCameraForLayer`** (CWPlayerHandsController :355-361, CWTutorialArrow): puede escoger una cámara que no es.

## Otros

- Siguen solos a la cámara: barras de vida (CWCreatureHPBarFaceCam), cartas volando (CWResultFlyCard, CWGachaFlyCard) y NcBillboard.
- CWHaloFaceCam :26-37 elige la cámara P1/P2 según la fase. Cambiarlo para que use siempre newCamera.
- `Time.timeScale` es 0 en el anillo de batalla y 5 en avance rápido: hay que probar que el tracking no se resienta. targetFrameRate es 60 o 300.
- Pipeline Built-in. Android con GLES3 (lo que pide ARCore), iOS con Metal. Solo landscape.
- `m_InitManagerOnStart: 1`: XR arranca siempre. Se puede pasar a manual cuando exista el modo no-AR en móvil.

## Componente del spike: `ARBattleRig` (solo con `ar_mode`)

1. Crear ARSession y un XROrigin (planos, raycast, anchors) a escala de unos 96.
2. Poner la Battle GameCamera bajo el origin con los componentes AR.
3. Tocar una mesa reconocida y mover el origin para que el pivote del tablero (0,−3.49,0) caiga ahí, orientado para que −X mire al jugador, con un anchor pegado a ese plano (ver "Colocación en la mesa y modo Ajustar").
4. Ocultar nivel, mesa y personajes cuando `CWBattleIntroController.FlagCharactersLoaded` esté listo.
5. Aplicar las guardas de cámara y el nuevo `checkReleasePoint`.

## Después: multijugador en la misma mesa

**Hecho en `feat/ar-versus`:** «Versus en la mesa», lockstep por la wifi, con o sin marcador impreso. Cómo se juega y cómo funciona: `docs/ar-versus.md`. Lo de abajo era el plan.

- **Tablero compartido:** los dos teléfonos reconocen un tapete impreso (AR Foundation image tracking), así tienen el mismo origen y cada jugador ve su lado.
- **Jugadas:** la del rival entra por `CWOpponentActionSequencer` (:62-87), que hoy pide `AIManager.MakeDecision()` y ejecuta líder, floop o `PlayCardToLane(carta, carril)`. Hay que cambiar esa fuente por las jugadas que llegan por la red.
- **Sincronizar al empezar:** mazos y semilla de `Random` (5 usos en GameState y 2 en PlayerInfoScript).

## Modo AR: lo implementado (spike)

- **`ar_mode`** (SQSettings.ARMode): si `server_settings.json` trae la clave, manda ella; si no la trae, está encendido en Android e iOS y apagado en escritorio y en el Editor. El JSON del repo no la trae, así que el APK sale con AR y el juego de Mac sigue igual. Para un APK sin AR basta `"ar_mode": false`.
- **`ARBattleRig`** (`Assets/Scripts/Assembly-CSharp/ARBattleRig.cs`) se crea solo al cargar `BattleScene`, y solo si `ar_mode` está encendido y hay un loader XR corriendo (ARCore/ARKit). Mientras existe, `ARBattleRig.Active` es true. Las guardas usan eso y no `ARMode`, porque CWMenuCameraTarget y CWiTweenCamTrigger también viven en los menús.
  - Crea ARSession + ARInputManager y un XROrigin con ARPlaneManager (solo planos horizontales), ARRaycastManager y ARAnchorManager.
  - La Battle GameCamera pasa a `XR Origin/Camera Offset` con TrackedPoseDriver (los mismos bindings que el XR Origin de AR Foundation), ARCameraManager y ARCameraBackground. clear SolidColor con alfa 0, near 1 y far 2000 en unidades del mundo (1 unidad ≈ 1 cm).
  - Le quita el tag MainCamera a las demás cámaras, apaga los CWMenuCameraTarget y la Animation de la cámara, y en cada LateUpdate apaga las cámaras de hechizo (y cualquier otra cámara en perspectiva con Skybox o SolidColor que no renderice a textura).
  - **Colocación:** el tablero no se mueve. Se mueve el origin, a escala 96 (o 96/tamaño), para que el pivote del tablero caiga en la mesa, con −X (el lado de P1) mirando al teléfono. Desde `feat/ar-placement` ya no flota delante del teléfono: ver la sección siguiente.
  - Oculta los Renderers del nivel y la mesa (`ARBattleRig.Hide`, llamado desde CWLoadLevelForBattle.TryLoadEnvironmentPrefab) y todo lo que cuelga de `P1/P2CharacterPosition`. Lo repasa cada 30 frames. Los Animator y Animation de lo oculto pasan a AlwaysAnimate para que no se paren los tiempos de las animaciones.
- **Guardas** (`if (ARBattleRig.Active) return;`): CWiTweenCamTrigger.PlayCam (cubre también FloopCameraTrigger y SetCamBackAfterBattle), CWiTweenBattleCam Not/CutToBattleCamera y MoveBattleCamera, CWiTweenVantageCam.CreatureSpawnCamera, CWBattleSequenceController.SplitCameraEnable (deja `camAlignFlag=false`) y UpdateBattleCamera, CWBattleEndCameraSwitch.OnClick y la línea que mueve la cámara en CWFloopActionManager.SetCameraPosition. CWHaloFaceCam ya no cambia a la cámara P1/P2. CWBattleIntroController no reproduce los clips de cámara, pero espera lo que duran para los letreros.
- **Soltar cartas:** en AR, `checkReleasePoint` (CWTBDragToMove) llama a `ARBattleRig.LaneContainsTouch`: lanza el rayo de la cámara por el toque contra el plano horizontal a la altura del collider del carril y mira si cae dentro de sus bounds en X/Z. Sin AR no cambia nada.
- **Verificado sin teléfono:** compila, el build de Mac con AR apagado sale bien y el APK de Android se construye (`PlatformBuild` con `-buildTarget Android`, unos 180 MB). El APK pide CAMERA, declara ARCore "required" y lleva las librerías de ARCore para arm64. No se ha probado nada en un teléfono.
- En el build de Android sale un `BuildDatabaseFailedException: arcoreimg failed` del preproceso de librerías de imágenes de ARCore. Es por la librería de prueba que trae el paquete de ARKit (`Packages/com.unity.xr.arkit/Tests/...`). El build sigue y termina bien.
  - Desde el versus sí usamos image tracking (el marcador). Ese paso de ARCore se detiene en la primera librería que falla, así que `Assets/Editor/ARMarkerSetup.cs` construye antes la base de datos de ARCore de nuestra librería.

## Colocación en la mesa y modo Ajustar (`feat/ar-placement`)

Tras la primera prueba en el teléfono (funcionaba, pero "a veces flota" y no había forma de mover el mapa), `ARBattleRig` pasó a tener tres modos: **Colocar → Fijado ⇄ Ajustar**.

### Colocar

- **Superficies visibles.** Los planos horizontales que detecta ARCore/ARKit se pintan con una rejilla de puntos semitransparente y su borde. El "prefab" del plano se hace por código (MeshFilter + MeshRenderer + LineRenderer + `ARPlaneMeshVisualizer`); el shader `Hidden/CardWars/AROverlay` está en `Assets/Resources/ARBattle/` para que el build siempre lo lleve. Las mesas que ya sirven (horizontales hacia arriba, ≥ 0.12 m² y ≥ 20 cm de lado) se ven en celeste; los trozos aún pequeños, en blanco tenue.
- **El tablero no se ve hasta colocarlo.** La Battle GameCamera solo dibuja la capa 31 (planos y contorno) mientras se coloca. Mientras el tablero no se dibuja, todos los Animator y Animation pasan a AlwaysAnimate cada 30 frames, para que nada que el juego espere se quede parado.
- **Letrero en tres pasos:** "Mueve el teléfono despacio sobre la mesa hasta que aparezcan puntos" → "Toca la mesa, sobre los puntos, para poner el tablero" → con el centro de la pantalla sobre una mesa válida aparece el **contorno del tablero** (mitad azul P1, mitad roja P2, flecha hacia tu lado) justo donde y como quedará, y el letrero dice "Toca la mesa para poner el tablero (el contorno muestra dónde quedará)".
- **Toque:** raycast `PlaneWithinPolygon` en el punto tocado; solo vale un plano que cumpla lo de arriba (el primero válido en el rayo, así un libro pequeño encima de la mesa no gana). Si el toque no da en una mesa pero el contorno está visible, se pone en el contorno. Si no, un aviso dice por qué. El toque puede durar hasta 1.2 s y moverse hasta 32 dp (antes eran 0.35 s y 40 px, unos 2 mm en un S25 Ultra).
- **Mientras se coloca, el juego no recibe toques** (ni NGUI, ni cartas, ni toques crudos como el del anillo de batalla).

### Fijado

- **Ancla pegada al plano:** `ARAnchorManager.AttachAnchor(plane, pose)` en el centro del tablero, a la altura del plano. Cada frame el tablero toma la posición y el giro (solo yaw) del ancla mientras está en Tracking, así sigue a la mesa cuando ARCore la corrige. La altura extra elegida se suma aparte (`ancla + up × altura`), de modo que el valor guardado significa lo mismo en la siguiente partida.
- Si ARCore funde el plano en otro (`subsumedBy`), se sigue la cadena hasta el plano vivo. Si el ancla no se pudo crear o el sistema la quita, el tablero se queda donde estaba, sigue la altura de su plano y cada 2 s intenta anclarse de nuevo al plano que tenga debajo (raycast hacia abajo, ±6 cm).
- **Pivote medido:** en BattleScene los renderers de `Environment/GameBoard` van de y −3.49 a −2.49 (una losa de 1 unidad con la superficie de juego en −2.49), centrados en x = z = 0. El pivote pasó de (0,−3,0) a **(0,−3.49,0)**: la base del tablero apoya en la mesa. Con −3 quedaba medio centímetro hundido.
- Los planos se esconden (la capa 31 sale de la máscara de la cámara) pero se siguen rastreando.
- **Si se pierde el tracking,** el tablero no se toca (no se sigue el ancla ni se recalcula nada) y el letrero dice "Se perdió el seguimiento (poca luz / movimiento rápido / …). El tablero se queda donde estaba: apunta otra vez a la mesa."
- Botón **Ajustar** abajo a la izquierda (la mano va al centro abajo, los paneles de jugador y rival arriba en las esquinas, el menú y la info de misión a media altura en los lados).

### Ajustar

- Los toques no llegan al juego: `UICamera.LockInput` (floop, info, UI), `FingerGestures.GlobalTouchFilter` (arrastrar cartas) e `Input.simulateMouseWithTouches = false` (los `GetMouseButtonDown` crudos del anillo de batalla, resultados y tutoriales). Al fijar se devuelve todo un frame después de soltar el dedo, y `OnDestroy` lo devuelve siempre.
- **Un dedo** arrastra el tablero sobre el plano de la mesa (el punto bajo el dedo se queda bajo el dedo).
- **Dos dedos girando** lo rotan alrededor de su centro; **pellizcando** cambia el tamaño entre 0.6× y 1.6× (30 a 80 cm). Al levantar o poner un dedo el gesto vuelve a empezar desde donde está, sin saltos.
- **▲ / ▼** suben o bajan la altura en pasos de 1 cm (±10 cm; al mantener pulsado se repite). Al lado se ve "Altura +N cm · Tamaño N.Nx".
- **Fijar** vuelve a anclar al plano en la nueva posición y devuelve los toques al juego. **Recolocar** vuelve a Colocar (con **Cancelar** para volver a donde estaba).
- Se guardan en PlayerPrefs `ar_board_scale` y `ar_board_height` (metros) al fijar y al recolocar, y se aplican en la siguiente batalla.
- Los botones son OnGUI pero los toques se leen a mano (no GUI.Button), en dp: `min(Screen.dpi/160, alto/360)`, con 160 dpi si `Screen.dpi` es 0. Los toques que empiezan en un botón no llegan ni al juego ni a los gestos. El toque de dos dedos del spike ya no existe.

### Por qué flotaba

1. **A propósito, antes de tocar:** el spike ponía el tablero 50 cm delante y 30 cm debajo del teléfono hasta que se tocaba una mesa. Los planos no se veían, así que un toque fuera del polígono detectado (o que durara más de 0.35 s o se moviera más de 40 px, unos 2 mm en un S25 Ultra) no hacía nada y el tablero seguía flotando sin ningún aviso.
2. **El toque de dos dedos** soltaba el ancla en silencio. El tablero se quedaba en el aire de la sesión, sin seguir la mesa, y el siguiente toque de un dedo sobre cualquier plano (jugando) lo movía.
3. **Sin filtro de plano:** se usaba `hits[0]` de cualquier plano, incluido uno recién nacido o pequeño (encima de un objeto, o el suelo junto al borde de la mesa), y si `AttachAnchor` devolvía null no había ancla y el tablero no seguía la corrección de altura del plano.
4. **Pivote:** −3 está en medio de la losa; el tablero quedaba 0.49 unidades (unos 5 mm) hundido.

El seguimiento por frame no era la causa: el ancla cuelga de `Trackables`, hijo del origin con pose local identidad (`XROrigin.OnBeforeRender` le copia la pose del Camera Offset, que también es identidad con `CameraYOffset = 0`), así que leer su posición con `origin.InverseTransformPoint` da su pose de sesión aunque el origin se mueva y escale cada frame. No había otro código que moviera el origin ni que recreara el ancla.

### Verificado sin teléfono

- Compila sin errores (import en batchmode) y el APK de Android se construye (unos 180 MB). El shader va en el build porque está en `Resources`.
- Las medidas del tablero salen de una sonda en el editor sobre BattleScene (bounds de los renderers y de los colliders de carril).
- **Prueba con XR Simulation en el editor, en batchmode:** un PlayMode test temporal (no está en el repo) con la sala por defecto de AR Foundation y un tablero de mentira donde está GameBoard. Esa sala solo trae planos simulados del suelo y la pared (su mesa no tiene), así que el tablero se colocó en el suelo. Resultado:
  - Al colocar, la cámara solo dibuja la capa 31 y aparece el contorno.
  - El toque coloca el tablero. El ancla queda en Tracking, pegada al plano, y la base del tablero cae justo en el plano (y = −3.490 en el mundo, 0.006 m en la sesión).
  - Los planos se esconden al fijar.
  - En Ajustar (escala 1.3, +2 cm, giro de 30° y arrastre de 10 cm), Fijar crea un ancla nueva y quita la vieja. El pivote queda a plano + 0.02 m, el origin a escala 73.8 y las PlayerPrefs se guardan.
  - Recolocar y Cancelar vuelven al sitio.
  - Hay capturas de la cámara en cada paso.
- **Para que XR Simulation corra hizo falta:**
  - Crear la sesión después del XR Origin. Está en el código: la sesión simulada busca el origin al arrancar.
  - En la prueba, crear el loader a mano, registrar el layout de dispositivo de la simulación y arrancar su subsistema de input, porque el proyecto no le da loader a Standalone a propósito.
- **No probado:** los gestos táctiles reales (la prueba llama a las funciones de los botones y cambia escala, giro y posición a mano), el bloqueo de NGUI y FingerGestures con una batalla real, el OnGUI (las capturas son de la cámara, no de la pantalla) y nada en un teléfono.

### Límites conocidos

- **Mientras se coloca por primera vez, todo el juego está bloqueado,** también el botón de menú. Si ARCore nunca reconoce un plano (poca luz, mesa lisa), solo queda ponerlo en el suelo o cerrar la app. "Cancelar" solo aparece al recolocar.
- **Pulsar "Ajustar" justo durante el anillo de batalla también lo para.** Ese `GetMouseButtonDown(0)` ya está calculado en ese frame cuando se apaga `simulateMouseWithTouches`.
- **El bloqueo de NGUI es el contador de `UICamera.LockInput`.** UITweener y SearchOpponent también lo usan. Un `UnlockInput` de más por su parte soltaría el bloqueo antes de tiempo, aunque en la batalla no se ha visto que pase.

### Qué falta probar en el teléfono

- Permiso de cámara al entrar a la batalla (lo pide ARCore) y que se vea la cámara detrás del tablero, no un fondo negro.
- Al entrar: no se ve el tablero, el letrero dice "Mueve el teléfono despacio…". Al mover el teléfono sobre la mesa aparecen puntos (primero blancos tenues, luego celestes cuando el plano ya vale).
- Con el centro de la pantalla en la mesa aparece el contorno azul/rojo con la flecha hacia ti. Tocar la mesa lo pone ahí: tamaño (unos 50 cm), que **apoye en la mesa** (ni hundido ni flotando), tu lado (azul) más cerca, y que los puntos desaparezcan.
- Caminar alrededor, alejarse y acercarse: que se quede quieto en la mesa. Tapar la cámara un momento: el letrero de "Se perdió el seguimiento" y que el tablero vuelva a su sitio al recuperarlo.
- Ajustar: arrastrar con un dedo, girar y pellizcar con dos, ▲/▼, Fijar. Que mientras ajustas no se juegue ninguna carta ni se haga floop, y que al fijar el juego responda otra vez. Recolocar y Cancelar.
- Salir y entrar a otra batalla: que conserve tamaño y altura.
- Que el botón Ajustar no tape nada del juego en la esquina inferior izquierda, y que se lea bien.
- Los dos límites de arriba: tocar un botón de NGUI mientras colocas (no debe hacer nada) y pulsar Ajustar en mitad del anillo de batalla.
- Arrastrar cartas a cada carril desde distintos ángulos, y los hechizos.
- Tocar criaturas y edificios (floop, info, objetivos de hechizo), que van por NGUI sobre la cámara AR.
- Que las barras de vida, halos y cartas que vuelan miren a la cámara. Que los hechizos con cámara propia (Volcano, CerebralBloodstorm, ClairvoyantDaggerStorm, BoneWand, PumaClaw) no tapen la vista.
- El anillo de batalla con `Time.timeScale` 0 y el avance rápido: que el tracking no se trabe.
- Legibilidad de la UI 2D encima del video, y el final de la batalla (cofres, resultados) sin los movimientos de cámara.
- `NGUITools.FindCameraForLayer` en el efecto del final del giro de la carta (CWPlayerHandsController).
