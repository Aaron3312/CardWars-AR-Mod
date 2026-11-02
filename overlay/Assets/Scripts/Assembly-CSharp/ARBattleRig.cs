using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

// AR mode for the battle (docs/ar-plan.md). With SQSettings.ARMode on and an XR loader running, it is created
// when BattleScene loads: the Battle GameCamera becomes the AR camera under a scaled XR Origin. The board
// itself never moves: the origin is moved around it, because the game keeps world-space offsets everywhere.
// While Active, the scripts that move, animate or switch the battle camera return early.
//
// Flow: Placing (detected tables drawn as dots, the board hidden, an outline where the screen centre points,
// a tap on a table puts the board there) -> Placed (anchored to that table, planes hidden, game input on)
// <-> Adjusting (one finger drags, two fingers rotate and resize, buttons for height; game input off).
[DefaultExecutionOrder(-400)]
public class ARBattleRig : MonoBehaviour
{
	private enum Mode
	{
		Placing,
		Placed,
		Adjusting
	}

	private enum AdjustGesture
	{
		None,
		Drag,
		Twist
	}

	private enum ButtonId
	{
		None,
		Adjust,
		Fix,
		Replace,
		Cancel,
		Up,
		Down,
		Marker
	}

	private struct Pointer
	{
		public int id;

		public Vector2 pos;

		public UnityEngine.TouchPhase phase;
	}

	private struct HudButton
	{
		public ButtonId id;

		public Rect rect;

		public string label;

		public bool primary;
	}

	// Base size: the board is about 48 units across; 48 units = 0.5 m.
	private const float UnitsPerMeter = 96f;

	// The point of the board that sits on the table, in world units. Measured in BattleScene: the GameBoard
	// renderers span y -3.49 .. -2.49 (a 1-unit slab whose play surface is at -2.49), centred on x = z = 0.
	private static readonly Vector3 BoardPivot = new Vector3(0f, -3.49f, 0f);

	// Half the board footprint in world units (GameBoard bounds: x -24.1 .. 24.2, z -22.2 .. 22.3).
	private const float BoardHalfX = 24.2f;

	private const float BoardHalfZ = 22.3f;

	// Free layer (only the Battle GameCamera renders it) for the plane dots and the board outline.
	private const int OverlayLayer = 31;

	// A table must be a horizontal plane facing up of at least this size (metres) to put the board on it.
	private const float MinPlaneArea = 0.12f;

	private const float MinPlaneSide = 0.2f;

	private const float MinScale = 0.6f;

	private const float MaxScale = 1.6f;

	private const float HeightStep = 0.01f;

	private const float MaxHeight = 0.1f;

	private const string ScalePref = "ar_board_scale";

	private const string HeightPref = "ar_board_height";

	private const string BattleSceneName = "BattleScene";

	// Versus: the printed marker (Assets/ARBattle/VersusMarker.png, 15 cm) that puts both boards in one spot.
	private const string MarkerLibraryPath = "ARBattle/VersusMarkerLibrary";

	private const string MarkerName = "VersusMarker";

	// A placement tap: short and still, in seconds and dp.
	private const float TapMaxSeconds = 1.2f;

	private const float TapMaxDp = 32f;

	private const float RepeatDelay = 0.45f;

	private const float RepeatInterval = 0.12f;

	private static bool active;

	private static readonly List<GameObject> hiddenRoots = new List<GameObject>();

	private static readonly List<Renderer> rendererBuffer = new List<Renderer>();

	private static readonly List<Animator> animatorBuffer = new List<Animator>();

	private static readonly List<Animation> animationBuffer = new List<Animation>();

	private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

	private readonly List<Pointer> pointers = new List<Pointer>();

	private readonly List<HudButton> buttons = new List<HudButton>();

	// Fingers that went down on one of the buttons, and which button.
	private readonly Dictionary<int, ButtonId> buttonFingers = new Dictionary<int, ButtonId>();

	private Camera[] cameraBuffer = new Camera[32];

	private Camera gameCamera;

	private XROrigin origin;

	private ARPlaneManager planeManager;

	private ARRaycastManager raycastManager;

	private ARAnchorManager anchorManager;

	private ARTrackedImageManager imageManager;

	// The board follows the marker while it is in view (versus); the plane anchor holds it when it is not.
	private bool markerLocked;

	// Set by Ajustar: the player moved the board by hand, so the marker no longer pulls it back.
	private bool markerIgnored;

	private float nextMarkerAnchorTime;

	private Mode mode = Mode.Placing;

	// Board pose in session space (metres): the pivot sits at boardBase + up * heightOffset, turned by boardYaw
	// degrees, at boardScale times the base size.
	private Vector3 boardBase;

	private float boardYaw;

	private float boardScale = 1f;

	private float heightOffset;

	// The anchor holds the board on its table (attached to basePlane). Without one, the board follows the
	// height of basePlane.
	private ARAnchor anchor;

	private ARPlane basePlane;

	private float nextReanchorTime;

	private bool anchorWarned;

	// Pose to go back to when "Recolocar" is cancelled.
	private bool hasPlacement;

	private Vector3 savedBase;

	private float savedYaw;

	private ARPlane savedPlane;

	// Where the screen centre hits a table while placing.
	private bool aimValid;

	private Vector3 aimBase;

	private ARPlane aimPlane;

	private int validPlaneCount;

	private bool tracking;

	private AdjustGesture gesture;

	private int gestureFingerA = int.MinValue;

	private int gestureFingerB = int.MinValue;

	private Vector3 dragStartHit;

	private Vector3 dragStartBase;

	private float twistAngle0;

	private float twistDist0;

	private float twistYaw0;

	private float twistScale0;

	private int tapFinger = int.MinValue;

	private float tapStartTime;

	private Vector2 tapStartPos;

	private bool tapCancelled;

	private float repeatTime;

	// Game input (NGUI, FingerGestures, raw mouse taps) is off while placing, adjusting or pressing a button.
	private bool inputBlocked;

	private int blockUntilFrame;

	private bool savedSimulateMouse = true;

	private FingerGestures.GlobalTouchFilterDelegate previousTouchFilter;

	private FingerGestures touchFilterOwner;

	private int visibleMask;

	private bool boardShown = true;

	private GameObject ghost;

	private GameObject planeTemplateHolder;

	private Material planeMaterial;

	private Material lineMaterial;

	private Material ghostMaterial;

	private MaterialPropertyBlock planeBlock;

	private float ui = 1f;

	private string flashText;

	private float flashUntil;

	private GUIStyle hintStyle;

	private GUIStyle buttonStyle;

	private GUIStyle infoStyle;

	private Texture2D whiteTexture;

	private Texture2D upTexture;

	private Texture2D downTexture;

	// True while the AR rig runs in the battle. Never true on desktop or outside BattleScene.
	public static bool Active
	{
		get
		{
			return active;
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != BattleSceneName || active || !SQSettings.ARMode)
		{
			return;
		}
		XRGeneralSettings xr = XRGeneralSettings.Instance;
		if (xr == null || xr.Manager == null || xr.Manager.activeLoader == null)
		{
			Debug.LogWarning("ARBattleRig: ar_mode is on but no XR loader is running; the battle stays as it was.");
			return;
		}
		GameObject go = new GameObject("AR Battle Rig");
		SceneManager.MoveGameObjectToScene(go, scene);
		go.AddComponent<ARBattleRig>();
	}

	// Hides the renderers of something the battle loads (level, table, characters, chairs) and keeps them
	// hidden. Animators keep running, because the game waits on character animations.
	public static void Hide(GameObject root)
	{
		if (!active || root == null)
		{
			return;
		}
		if (!hiddenRoots.Contains(root))
		{
			hiddenRoots.Add(root);
		}
		HideRenderers(root);
	}

	// AR replacement for CWTBDragToMove.checkReleasePoint, whose screen-space test assumes the camera looks
	// from P1's side: casts the touch onto the lane's height and checks the hit against its X/Z bounds.
	public static bool LaneContainsTouch(Camera camera, Vector2 screenPos, BoxCollider col)
	{
		bool wasEnabled = col.enabled;
		col.enabled = true;
		Bounds bounds = col.bounds;
		col.enabled = wasEnabled;
		Ray ray = camera.ScreenPointToRay(screenPos);
		Plane lanePlane = new Plane(Vector3.up, bounds.center);
		float enter;
		if (!lanePlane.Raycast(ray, out enter))
		{
			return false;
		}
		Vector3 point = ray.GetPoint(enter);
		return point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;
	}

	private void Awake()
	{
		active = true;
		useGUILayout = false;
	}

	private void Start()
	{
		if (gameCamera == null)
		{
			PanelManagerBattle panel = PanelManagerBattle.GetInstance();
			if (panel != null && panel.newCamera != null)
			{
				gameCamera = panel.newCamera.GetComponent<Camera>();
			}
		}
		if (gameCamera == null)
		{
			Debug.LogError("ARBattleRig: Battle GameCamera not found; AR is off for this battle.");
			Destroy(gameObject);
			return;
		}
		foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsInactive.Include))
		{
			if (cam != gameCamera && cam.CompareTag("MainCamera"))
			{
				cam.tag = "Untagged";
			}
		}
		foreach (CWMenuCameraTarget target in FindObjectsByType<CWMenuCameraTarget>(FindObjectsInactive.Include))
		{
			target.enabled = false;
		}
		Animation cameraAnimation = gameCamera.GetComponent<Animation>();
		if (cameraAnimation != null)
		{
			cameraAnimation.Stop();
			cameraAnimation.enabled = false;
		}
		iTween.Stop(gameCamera.gameObject);

		boardScale = Mathf.Clamp(PlayerPrefs.GetFloat(ScalePref, 1f), MinScale, MaxScale);
		heightOffset = Mathf.Clamp(PlayerPrefs.GetFloat(HeightPref, 0f), -MaxHeight, MaxHeight);
		CreateMaterials();

		// The origin stays inactive until it is wired, so its Awake already sees the camera and the offset.
		GameObject originGo = new GameObject("XR Origin");
		originGo.SetActive(false);
		originGo.transform.SetParent(transform, false);
		GameObject offsetGo = new GameObject("Camera Offset");
		offsetGo.transform.SetParent(originGo.transform, false);
		origin = originGo.AddComponent<XROrigin>();
		origin.Camera = gameCamera;
		origin.CameraFloorOffsetObject = offsetGo;
		origin.CameraYOffset = 0f;
		planeManager = originGo.AddComponent<ARPlaneManager>();
		planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
		planeManager.planePrefab = CreatePlaneTemplate();
		raycastManager = originGo.AddComponent<ARRaycastManager>();
		anchorManager = originGo.AddComponent<ARAnchorManager>();
		if (VersusMatch.Active)
		{
			// The library has to be in place before the manager is enabled, which happens with the origin.
			XRReferenceImageLibrary library = Resources.Load<XRReferenceImageLibrary>(MarkerLibraryPath);
			if (library != null)
			{
				imageManager = originGo.AddComponent<ARTrackedImageManager>();
				imageManager.referenceLibrary = library;
				imageManager.requestedMaxNumberOfMovingImages = 1;
				markerIgnored = !VersusMatch.UseMarker;
			}
			else
			{
				UnityEngine.Debug.LogWarning("ARBattleRig: Resources/" + MarkerLibraryPath + " missing; versus boards are placed by hand.");
			}
		}

		SetUpCamera(offsetGo.transform);
		originGo.SetActive(true);
		// The session goes after the origin: XR Simulation's session looks for an XR Origin when it starts.
		GameObject sessionGo = new GameObject("AR Session");
		sessionGo.transform.SetParent(transform, false);
		sessionGo.AddComponent<ARSession>();
		sessionGo.AddComponent<ARInputManager>();
		ghost = CreateGhost();
		visibleMask = gameCamera.cullingMask & ~(1 << OverlayLayer);
		ApplyPlacement();
		InstallTouchFilter();

		GameDataScript gameData = GameDataScript.GetInstance();
		if (gameData != null && gameData.characterPositions != null)
		{
			foreach (GameObject characterPosition in gameData.characterPositions)
			{
				Hide(characterPosition);
			}
		}
	}

	private void SetUpCamera(Transform offset)
	{
		Transform cameraTr = gameCamera.transform;
		cameraTr.SetParent(offset, false);
		cameraTr.localPosition = Vector3.zero;
		cameraTr.localRotation = Quaternion.identity;
		cameraTr.localScale = Vector3.one;
		// Camera clip planes are in world units: 1 unit is about 1 cm here.
		gameCamera.nearClipPlane = 1f;
		gameCamera.farClipPlane = 2000f;
		gameCamera.clearFlags = CameraClearFlags.SolidColor;
		gameCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);

		// Same bindings as AR Foundation's "XR Origin (Mobile AR)".
		TrackedPoseDriver poseDriver = gameCamera.gameObject.AddComponent<TrackedPoseDriver>();
		InputAction position = new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition", null, null, "Vector3");
		position.AddBinding("<HandheldARInputDevice>/devicePosition");
		InputAction rotation = new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", null, null, "Quaternion");
		rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
		poseDriver.positionInput = new InputActionProperty(position);
		poseDriver.rotationInput = new InputActionProperty(rotation);

		gameCamera.gameObject.AddComponent<ARCameraManager>();
		gameCamera.gameObject.AddComponent<ARCameraBackground>();
	}

	// ---- Placement maths -------------------------------------------------------------------------------

	// Moves the origin so the board pivot appears at boardBase + up * heightOffset (session space), turned by
	// boardYaw and scaled by boardScale. Planes, anchors and the camera hang from the origin, so they follow.
	private void ApplyPlacement()
	{
		Transform originTr = origin.transform;
		Quaternion rotation = Quaternion.Inverse(Quaternion.Euler(0f, boardYaw, 0f));
		float unitsPerMeter = UnitsPerMeter / boardScale;
		Vector3 pivot = boardBase + Vector3.up * heightOffset;
		originTr.localScale = Vector3.one * unitsPerMeter;
		originTr.rotation = rotation;
		originTr.position = BoardPivot - rotation * (pivot * unitsPerMeter);
	}

	private Vector3 CameraSessionPosition()
	{
		return origin.transform.InverseTransformPoint(gameCamera.transform.position);
	}

	// Yaw that turns the board's P1 side (-X) toward the player.
	private static float YawToward(Vector3 towardPlayer)
	{
		towardPlayer.y = 0f;
		if (towardPlayer.sqrMagnitude < 1E-06f)
		{
			towardPlayer = Vector3.back;
		}
		towardPlayer.Normalize();
		return Quaternion.LookRotation(Vector3.Cross(-towardPlayer, Vector3.up), Vector3.up).eulerAngles.y;
	}

	private static float YawOf(Quaternion rotation)
	{
		Vector3 forward = rotation * Vector3.forward;
		return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
	}

	private static bool IsTable(ARPlane plane)
	{
		if (plane == null || plane.subsumedBy != null || plane.alignment != PlaneAlignment.HorizontalUp || plane.trackingState != TrackingState.Tracking)
		{
			return false;
		}
		Vector2 size = plane.size;
		return size.x * size.y >= MinPlaneArea && Mathf.Min(size.x, size.y) >= MinPlaneSide;
	}

	// ARCore merges planes as it learns the table; follow the merge to the plane that is still alive.
	private static ARPlane LivePlane(ARPlane plane)
	{
		int guard = 0;
		while (plane != null && plane.subsumedBy != null && guard++ < 16)
		{
			plane = plane.subsumedBy;
		}
		return plane;
	}

	private float SessionHeight(ARPlane plane)
	{
		return origin.transform.InverseTransformPoint(plane.transform.position).y;
	}

	// First table under a screen point, in session space.
	private bool RaycastTable(Vector2 screenPos, out Vector3 sessionPos, out ARPlane plane)
	{
		sessionPos = Vector3.zero;
		plane = null;
		if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
		{
			return false;
		}
		foreach (ARRaycastHit hit in hits)
		{
			ARPlane hitPlane = hit.trackable as ARPlane;
			if (IsTable(hitPlane))
			{
				sessionPos = hit.sessionRelativePose.position;
				plane = hitPlane;
				return true;
			}
		}
		return false;
	}

	// Screen ray against the horizontal plane at the board's height, in session space.
	private bool RayToBoardHeight(Vector2 screenPos, out Vector3 sessionPos)
	{
		sessionPos = Vector3.zero;
		Ray ray = gameCamera.ScreenPointToRay(screenPos);
		Transform originTr = origin.transform;
		Vector3 from = originTr.InverseTransformPoint(ray.origin);
		Vector3 dir = originTr.InverseTransformDirection(ray.direction);
		float height = boardBase.y + heightOffset;
		if (Mathf.Abs(dir.y) < 1E-04f)
		{
			return false;
		}
		float t = (height - from.y) / dir.y;
		if (t <= 0f || t > 20f)
		{
			return false;
		}
		sessionPos = from + dir * t;
		return true;
	}

	private void PlaceAt(Vector3 sessionPos, ARPlane plane)
	{
		boardBase = sessionPos;
		boardYaw = YawToward(CameraSessionPosition() - sessionPos);
		basePlane = plane;
		ApplyPlacement();
		Anchor();
		hasPlacement = true;
		mode = Mode.Placed;
	}

	// Attaches a fresh anchor to the table under the board, at table height (the extra height stays apart so
	// the saved value means the same next time).
	private void Anchor()
	{
		RemoveAnchor();
		ARPlane plane = LivePlane(basePlane);
		if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
		{
			plane = FindTableUnderBoard();
		}
		if (plane == null)
		{
			WarnAnchor("no table under the board");
			return;
		}
		basePlane = plane;
		Transform originTr = origin.transform;
		Pose pose = new Pose(originTr.TransformPoint(boardBase), originTr.rotation * Quaternion.Euler(0f, boardYaw, 0f));
		try
		{
			anchor = anchorManager.AttachAnchor(plane, pose);
		}
		catch (InvalidOperationException e)
		{
			anchor = null;
			WarnAnchor(e.Message);
			return;
		}
		if (anchor == null)
		{
			WarnAnchor("AttachAnchor returned null");
		}
	}

	private void WarnAnchor(string why)
	{
		if (!anchorWarned)
		{
			anchorWarned = true;
			Debug.LogWarning("ARBattleRig: no anchor (" + why + "); the board keeps to the table height instead.");
		}
	}

	private ARPlane FindTableUnderBoard()
	{
		Transform originTr = origin.transform;
		Vector3 above = originTr.TransformPoint(boardBase + Vector3.up * 0.5f);
		Vector3 down = originTr.TransformDirection(Vector3.down);
		ARPlane best = null;
		if (raycastManager.Raycast(new Ray(above, down), hits, TrackableType.PlaneWithinPolygon))
		{
			foreach (ARRaycastHit hit in hits)
			{
				ARPlane plane = hit.trackable as ARPlane;
				if (plane != null && plane.subsumedBy == null && plane.alignment == PlaneAlignment.HorizontalUp && Mathf.Abs(hit.sessionRelativePose.position.y - boardBase.y) < 0.06f)
				{
					return plane;
				}
			}
		}
		float bestDistance = 0.06f;
		foreach (ARPlane plane in planeManager.trackables)
		{
			if (plane.subsumedBy == null && plane.alignment == PlaneAlignment.HorizontalUp)
			{
				float distance = Mathf.Abs(SessionHeight(plane) - boardBase.y);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = plane;
				}
			}
		}
		return best;
	}

	private void RemoveAnchor()
	{
		if (anchor != null && anchorManager != null && anchorManager.enabled)
		{
			anchorManager.TryRemoveAnchor(anchor);
		}
		anchor = null;
	}

	// While placed, the board takes its pose from the anchor as ARCore/ARKit refine the table. If tracking is
	// lost it keeps the last pose.
	private void FollowTable()
	{
		if (!tracking)
		{
			return;
		}
		if (anchor != null)
		{
			if (anchor.trackingState == TrackingState.Tracking)
			{
				Transform originTr = origin.transform;
				boardBase = originTr.InverseTransformPoint(anchor.transform.position);
				boardYaw = YawOf(Quaternion.Inverse(originTr.rotation) * anchor.transform.rotation);
			}
			return;
		}
		basePlane = LivePlane(basePlane);
		if (basePlane != null && basePlane.trackingState == TrackingState.Tracking)
		{
			boardBase.y = SessionHeight(basePlane);
		}
		if (Time.unscaledTime >= nextReanchorTime)
		{
			// The system dropped the anchor, or there never was one: try again now and then.
			nextReanchorTime = Time.unscaledTime + 2f;
			Anchor();
		}
	}

	// ---- Versus marker ---------------------------------------------------------------------------------

	// Where the marker is, in session space, and the yaw that turns this phone's side (P1, -X) toward its
	// player. The image's bottom edge (-Z of the tracked image, the edge with the notch) is the host's seat;
	// the guest sits across, so its board is turned 180 degrees and both see one physical board.
	private bool MarkerPose(out Vector3 sessionPos, out float yaw)
	{
		sessionPos = Vector3.zero;
		yaw = 0f;
		if (imageManager == null || !tracking)
		{
			return false;
		}
		foreach (ARTrackedImage image in imageManager.trackables)
		{
			if (image.trackingState != TrackingState.Tracking || image.referenceImage.name != MarkerName)
			{
				continue;
			}
			Transform originTr = origin.transform;
			sessionPos = originTr.InverseTransformPoint(image.transform.position);
			Vector3 towardHost = -originTr.InverseTransformDirection(image.transform.forward);
			VersusMatch match = VersusMatch.Instance;
			bool host = match == null || match.IsHost;
			yaw = YawToward(host ? towardHost : -towardHost);
			return true;
		}
		return false;
	}

	private bool TryPlaceAtMarker()
	{
		Vector3 sessionPos;
		float yaw;
		if (markerIgnored || !MarkerPose(out sessionPos, out yaw))
		{
			return false;
		}
		// The printed sheet lies on the table: the board sits on it at its standard size.
		boardScale = 1f;
		heightOffset = 0f;
		boardBase = sessionPos;
		boardYaw = yaw;
		basePlane = FindTableUnderBoard();
		ApplyPlacement();
		Anchor();
		nextMarkerAnchorTime = Time.unscaledTime + 3f;
		hasPlacement = true;
		markerLocked = true;
		mode = Mode.Placed;
		Flash("Tablero sobre el marcador: los dos lo ven en el mismo sitio");
		return true;
	}

	// While the marker is in view the board eases onto it, and the plane anchor is refreshed now and then so
	// it stays put when the marker goes out of view. Returns false when the table anchor should take over.
	private bool FollowMarker()
	{
		Vector3 sessionPos;
		float yaw;
		if (!markerLocked || !MarkerPose(out sessionPos, out yaw))
		{
			return false;
		}
		float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
		boardBase = Vector3.Lerp(boardBase, sessionPos, k);
		boardYaw = Mathf.LerpAngle(boardYaw, yaw, k);
		if (Time.unscaledTime >= nextMarkerAnchorTime)
		{
			nextMarkerAnchorTime = Time.unscaledTime + 3f;
			Anchor();
		}
		return true;
	}

	// ---- Frame update ----------------------------------------------------------------------------------

	private void Update()
	{
		if (origin == null)
		{
			return;
		}
		tracking = ARSession.state == ARSessionState.SessionTracking;
		ui = UiScale();
		LayoutButtons();
		ReadPointers();
		UpdatePlanes();
		HandleButtons();
		if (mode == Mode.Placing)
		{
			if (!TryPlaceAtMarker())
			{
				UpdateAim();
				HandlePlacementTap();
			}
		}
		else if (mode == Mode.Adjusting)
		{
			HandleAdjustGestures();
		}
		if (touchFilterOwner == null && FingerGestures.Instance != null)
		{
			InstallTouchFilter();
		}
		bool block = mode != Mode.Placed || buttonFingers.Count > 0;
		if (block)
		{
			// Keep it one more frame after the last finger lifts, so NGUI does not see a lone release.
			blockUntilFrame = Time.frameCount + 1;
		}
		SetGameInputBlocked(block || Time.frameCount <= blockUntilFrame);
		if (Time.frameCount % 30 == 0)
		{
			for (int i = hiddenRoots.Count - 1; i >= 0; i--)
			{
				if (hiddenRoots[i] == null)
				{
					hiddenRoots.RemoveAt(i);
				}
				else
				{
					HideRenderers(hiddenRoots[i]);
				}
			}
			if (!boardShown)
			{
				KeepAnimating();
			}
		}
	}

	private void LateUpdate()
	{
		if (origin == null)
		{
			return;
		}
		if (mode == Mode.Placed)
		{
			if (!FollowMarker())
			{
				FollowTable();
			}
		}
		if (mode != Mode.Placing || aimValid)
		{
			ApplyPlacement();
		}
		ApplyVisibility(mode != Mode.Placing, mode != Mode.Placed);
		if (ghost != null)
		{
			ghost.SetActive(mode == Mode.Placing && aimValid);
		}
		// Spell effects bring their own full-screen cameras; the phone camera is the only view in AR.
		CWFloopActionManager floopManager = CWFloopActionManager.GetInstance();
		if (floopManager != null && floopManager.SpellCamera != null)
		{
			floopManager.SpellCamera.enabled = false;
			floopManager.SpellCamera = null;
			floopManager.usingSpellCamera = false;
		}
		if (Camera.allCamerasCount > cameraBuffer.Length)
		{
			cameraBuffer = new Camera[Camera.allCamerasCount];
		}
		int count = Camera.GetAllCameras(cameraBuffer);
		for (int i = 0; i < count; i++)
		{
			Camera cam = cameraBuffer[i];
			if (cam != gameCamera && !cam.orthographic && cam.targetTexture == null && (cam.clearFlags == CameraClearFlags.Skybox || cam.clearFlags == CameraClearFlags.SolidColor))
			{
				cam.enabled = false;
			}
			cameraBuffer[i] = null;
		}
	}

	// The board (everything the game camera normally draws) is hidden until it is placed; the overlay layer
	// (planes and outline) is drawn only while placing or adjusting. Planes keep being tracked either way.
	private void ApplyVisibility(bool showBoard, bool showOverlay)
	{
		int overlay = 1 << OverlayLayer;
		if (showBoard)
		{
			if (!boardShown)
			{
				gameCamera.cullingMask = visibleMask;
				boardShown = true;
			}
			visibleMask = gameCamera.cullingMask & ~overlay;
			gameCamera.cullingMask = visibleMask | (showOverlay ? overlay : 0);
		}
		else
		{
			if (boardShown)
			{
				visibleMask = gameCamera.cullingMask & ~overlay;
				boardShown = false;
			}
			gameCamera.cullingMask = overlay;
		}
	}

	// While the board is not drawn, animations the game waits on must not be culled.
	private static void KeepAnimating()
	{
		foreach (Animator a in FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
		{
			a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
		}
		foreach (Animation a in FindObjectsByType<Animation>(FindObjectsInactive.Exclude))
		{
			a.cullingType = AnimationCullingType.AlwaysAnimate;
		}
	}

	private void UpdatePlanes()
	{
		validPlaneCount = 0;
		if (planeBlock == null)
		{
			planeBlock = new MaterialPropertyBlock();
		}
		foreach (ARPlane plane in planeManager.trackables)
		{
			bool table = IsTable(plane);
			if (table)
			{
				validPlaneCount++;
			}
			MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
			if (meshRenderer != null)
			{
				// Tables big enough for the board are brighter than the bits still being recognised.
				planeBlock.SetColor("_DotColor", table ? new Color(0.55f, 0.95f, 1f, 0.75f) : new Color(1f, 1f, 1f, 0.3f));
				planeBlock.SetColor("_Color", table ? new Color(0.55f, 0.95f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.03f));
				meshRenderer.SetPropertyBlock(planeBlock);
			}
			LineRenderer line = plane.GetComponent<LineRenderer>();
			if (line != null)
			{
				Color lineColor = table ? new Color(0.55f, 0.95f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.25f);
				line.startColor = lineColor;
				line.endColor = lineColor;
			}
		}
	}

	private void UpdateAim()
	{
		aimValid = tracking && RaycastTable(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out aimBase, out aimPlane);
		if (aimValid)
		{
			// The outline sits where the board would go if placed now.
			boardBase = aimBase;
			boardYaw = YawToward(CameraSessionPosition() - aimBase);
		}
	}

	private void HandlePlacementTap()
	{
		foreach (Pointer p in pointers)
		{
			if (buttonFingers.ContainsKey(p.id))
			{
				continue;
			}
			if (p.phase == UnityEngine.TouchPhase.Began)
			{
				if (tapFinger == int.MinValue)
				{
					tapFinger = p.id;
					tapStartTime = Time.unscaledTime;
					tapStartPos = p.pos;
					tapCancelled = pointers.Count > 1;
				}
				else
				{
					tapCancelled = true;
				}
				continue;
			}
			if (p.id != tapFinger)
			{
				continue;
			}
			if ((p.pos - tapStartPos).magnitude > TapMaxDp * ui)
			{
				tapCancelled = true;
			}
			if (p.phase == UnityEngine.TouchPhase.Ended || p.phase == UnityEngine.TouchPhase.Canceled)
			{
				bool isTap = p.phase == UnityEngine.TouchPhase.Ended && !tapCancelled && Time.unscaledTime - tapStartTime <= TapMaxSeconds;
				tapFinger = int.MinValue;
				if (isTap)
				{
					TryPlaceFromTap(p.pos);
				}
			}
		}
	}

	private void TryPlaceFromTap(Vector2 screenPos)
	{
		if (!tracking)
		{
			Flash("Espera a que la cámara AR reconozca la mesa");
			return;
		}
		Vector3 sessionPos;
		ARPlane plane;
		if (RaycastTable(screenPos, out sessionPos, out plane))
		{
			PlaceAt(sessionPos, plane);
		}
		else if (aimValid)
		{
			PlaceAt(aimBase, aimPlane);
		}
		else
		{
			Flash(validPlaneCount == 0 ? "Aún no hay mesa reconocida: mueve el teléfono despacio sobre ella" : "Toca sobre los puntos: es la mesa reconocida");
		}
	}

	private void HandleAdjustGestures()
	{
		Pointer a = default(Pointer);
		Pointer b = default(Pointer);
		int count = 0;
		foreach (Pointer p in pointers)
		{
			if (buttonFingers.ContainsKey(p.id) || p.phase == UnityEngine.TouchPhase.Ended || p.phase == UnityEngine.TouchPhase.Canceled)
			{
				continue;
			}
			if (count == 0)
			{
				a = p;
			}
			else if (count == 1)
			{
				b = p;
			}
			count++;
		}
		if (count == 0)
		{
			gesture = AdjustGesture.None;
			return;
		}
		if (count == 1)
		{
			// One finger drags the board over the table: the board follows the finger's point on it.
			Vector3 hit;
			if (!RayToBoardHeight(a.pos, out hit))
			{
				return;
			}
			if (gesture != AdjustGesture.Drag || gestureFingerA != a.id)
			{
				gesture = AdjustGesture.Drag;
				gestureFingerA = a.id;
				dragStartHit = hit;
				dragStartBase = boardBase;
				return;
			}
			Vector3 delta = hit - dragStartHit;
			boardBase = dragStartBase + new Vector3(delta.x, 0f, delta.z);
			return;
		}
		// Two fingers: turning them rotates the board around its centre, pinching resizes it.
		Vector2 span = b.pos - a.pos;
		float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;
		float distance = span.magnitude;
		if (gesture != AdjustGesture.Twist || gestureFingerA != a.id || gestureFingerB != b.id)
		{
			gesture = AdjustGesture.Twist;
			gestureFingerA = a.id;
			gestureFingerB = b.id;
			twistAngle0 = angle;
			twistDist0 = Mathf.Max(distance, 1f);
			twistYaw0 = boardYaw;
			twistScale0 = boardScale;
			return;
		}
		// Unity's +Y turn is clockwise seen from above; a counter-clockwise twist on screen turns it the other way.
		boardYaw = twistYaw0 - Mathf.DeltaAngle(twistAngle0, angle);
		boardScale = Mathf.Clamp(twistScale0 * distance / twistDist0, MinScale, MaxScale);
	}

	// ---- Buttons ---------------------------------------------------------------------------------------

	private static float UiScale()
	{
		// dp from Screen.dpi, but never more than a 360 dp tall screen allows (Android may render below the
		// panel's physical resolution); Screen.dpi can be 0.
		float byHeight = Mathf.Min(Screen.width, Screen.height) / 360f;
		float scale = Screen.dpi > 0f ? Mathf.Min(Screen.dpi / 160f, byHeight) : byHeight;
		return Mathf.Max(1f, scale);
	}

	// Bottom-left corner: the hand of cards is centred at the bottom, the player and rival panels take the
	// top corners and the menu and quest buttons the middle of the sides.
	private void LayoutButtons()
	{
		buttons.Clear();
		float margin = 12f * ui;
		float gap = 8f * ui;
		float height = 44f * ui;
		float bottom = Screen.height - margin - height;
		switch (mode)
		{
		case Mode.Placed:
			AddButton(ButtonId.Adjust, new Rect(margin, bottom, 96f * ui, height), "Ajustar", false);
			break;
		case Mode.Adjusting:
		{
			float fixWidth = 96f * ui;
			AddButton(ButtonId.Fix, new Rect(margin, bottom, fixWidth, height), "Fijar", true);
			AddButton(ButtonId.Replace, new Rect(margin + fixWidth + gap, bottom, 116f * ui, height), "Recolocar", false);
			float row = bottom - gap - height;
			AddButton(ButtonId.Up, new Rect(margin, row, 48f * ui, height), null, false);
			AddButton(ButtonId.Down, new Rect(margin + 48f * ui + gap, row, 48f * ui, height), null, false);
			break;
		}
		case Mode.Placing:
		{
			float x = margin;
			if (hasPlacement)
			{
				AddButton(ButtonId.Cancel, new Rect(x, bottom, 116f * ui, height), "Cancelar", false);
				x += 116f * ui + gap;
			}
			if (imageManager != null)
			{
				// Versus: with or without the printed marker, switchable while placing.
				AddButton(ButtonId.Marker, new Rect(x, bottom, 150f * ui, height), markerIgnored ? "Usar marcador" : "Sin marcador", false);
			}
			break;
		}
		}
	}

	private void AddButton(ButtonId id, Rect rect, string label, bool primary)
	{
		buttons.Add(new HudButton
		{
			id = id,
			rect = rect,
			label = label,
			primary = primary
		});
	}

	private ButtonId ButtonAt(Vector2 screenPos)
	{
		Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
		float pad = 6f * ui;
		foreach (HudButton button in buttons)
		{
			Rect r = button.rect;
			if (guiPos.x >= r.xMin - pad && guiPos.x <= r.xMax + pad && guiPos.y >= r.yMin - pad && guiPos.y <= r.yMax + pad)
			{
				return button.id;
			}
		}
		return ButtonId.None;
	}

	private bool HasButton(ButtonId id)
	{
		foreach (HudButton button in buttons)
		{
			if (button.id == id)
			{
				return true;
			}
		}
		return false;
	}

	private void HandleButtons()
	{
		foreach (Pointer p in pointers)
		{
			if (p.phase == UnityEngine.TouchPhase.Began)
			{
				ButtonId id = ButtonAt(p.pos);
				if (id != ButtonId.None)
				{
					buttonFingers[p.id] = id;
					if (id == ButtonId.Up || id == ButtonId.Down)
					{
						// Height steps on press, and again while held.
						StepHeight(id);
						repeatTime = Time.unscaledTime + RepeatDelay;
					}
				}
				continue;
			}
			ButtonId pressed;
			if (!buttonFingers.TryGetValue(p.id, out pressed))
			{
				continue;
			}
			if (p.phase == UnityEngine.TouchPhase.Ended || p.phase == UnityEngine.TouchPhase.Canceled)
			{
				buttonFingers.Remove(p.id);
				if (p.phase == UnityEngine.TouchPhase.Ended && pressed != ButtonId.Up && pressed != ButtonId.Down && ButtonAt(p.pos) == pressed && HasButton(pressed))
				{
					Press(pressed);
				}
			}
			else if ((pressed == ButtonId.Up || pressed == ButtonId.Down) && Time.unscaledTime >= repeatTime && ButtonAt(p.pos) == pressed)
			{
				StepHeight(pressed);
				repeatTime = Time.unscaledTime + RepeatInterval;
			}
		}
		// Fingers whose touch vanished without an Ended phase.
		if (buttonFingers.Count > 0 && pointers.Count == 0)
		{
			buttonFingers.Clear();
		}
	}

	private void StepHeight(ButtonId id)
	{
		if (mode != Mode.Adjusting)
		{
			return;
		}
		heightOffset = Mathf.Clamp(heightOffset + (id == ButtonId.Up ? HeightStep : -HeightStep), -MaxHeight, MaxHeight);
		heightOffset = Mathf.Round(heightOffset * 100f) / 100f;
	}

	private void Press(ButtonId id)
	{
		switch (id)
		{
		case ButtonId.Adjust:
			if (mode == Mode.Placed)
			{
				mode = Mode.Adjusting;
				gesture = AdjustGesture.None;
				markerLocked = false;
				markerIgnored = true;
			}
			break;
		case ButtonId.Fix:
			if (mode == Mode.Adjusting)
			{
				Anchor();
				SavePrefs();
				mode = Mode.Placed;
			}
			break;
		case ButtonId.Replace:
			if (mode == Mode.Adjusting)
			{
				savedBase = boardBase;
				savedYaw = boardYaw;
				savedPlane = basePlane;
				SavePrefs();
				aimValid = false;
				tapFinger = int.MinValue;
				markerIgnored = !VersusMatch.UseMarker;
				mode = Mode.Placing;
			}
			break;
		case ButtonId.Marker:
			if (mode == Mode.Placing && imageManager != null)
			{
				markerIgnored = !markerIgnored;
				VersusMatch.UseMarker = !markerIgnored;
				Flash(markerIgnored ? "Sin marcador: toca la mesa, sobre los puntos, para poner el tablero" : "Con marcador: apunta a la hoja impresa");
			}
			break;
		case ButtonId.Cancel:
			if (mode == Mode.Placing && hasPlacement)
			{
				boardBase = savedBase;
				boardYaw = savedYaw;
				basePlane = savedPlane;
				aimValid = false;
				gesture = AdjustGesture.None;
				mode = Mode.Adjusting;
			}
			break;
		}
	}

	private void SavePrefs()
	{
		PlayerPrefs.SetFloat(ScalePref, boardScale);
		PlayerPrefs.SetFloat(HeightPref, heightOffset);
		PlayerPrefs.Save();
	}

	// ---- Input -----------------------------------------------------------------------------------------

	private void ReadPointers()
	{
		pointers.Clear();
		for (int i = 0; i < Input.touchCount; i++)
		{
			Touch touch = Input.GetTouch(i);
			pointers.Add(new Pointer
			{
				id = touch.fingerId,
				pos = touch.position,
				phase = touch.phase
			});
		}
#if UNITY_EDITOR
		// The mouse stands in for one finger in the editor (XR Simulation).
		if (pointers.Count == 0 && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
		{
			pointers.Add(new Pointer
			{
				id = -1,
				pos = Input.mousePosition,
				phase = Input.GetMouseButtonDown(0) ? UnityEngine.TouchPhase.Began : (Input.GetMouseButtonUp(0) ? UnityEngine.TouchPhase.Ended : UnityEngine.TouchPhase.Moved)
			});
		}
#endif
	}

	// Cards are dragged through FingerGestures: a finger that goes down while the game input is off, or on one
	// of the AR buttons, is ignored until it lifts.
	private void InstallTouchFilter()
	{
		touchFilterOwner = FingerGestures.Instance;
		if (touchFilterOwner == null)
		{
			return;
		}
		previousTouchFilter = FingerGestures.GlobalTouchFilter;
		FingerGestures.GlobalTouchFilter = FilterTouch;
	}

	private bool FilterTouch(int fingerIndex, Vector2 position)
	{
		if (inputBlocked || ButtonAt(position) != ButtonId.None)
		{
			return false;
		}
		return previousTouchFilter == null || previousTouchFilter(fingerIndex, position);
	}

	// NGUI taps (floop, info, spell targets, UI) and the raw mouse taps some screens read (battle ring, results).
	private void SetGameInputBlocked(bool block)
	{
		if (block == inputBlocked)
		{
			return;
		}
		inputBlocked = block;
		if (block)
		{
			UICamera.LockInput();
			savedSimulateMouse = Input.simulateMouseWithTouches;
			Input.simulateMouseWithTouches = false;
		}
		else
		{
			UICamera.UnlockInput();
			Input.simulateMouseWithTouches = savedSimulateMouse;
		}
	}

	private void Flash(string text)
	{
		flashText = text;
		flashUntil = Time.unscaledTime + 2.5f;
	}

	// ---- Visuals ---------------------------------------------------------------------------------------

	private void CreateMaterials()
	{
		Shader shader = Resources.Load<Shader>("ARBattle/AROverlay");
		if (shader == null)
		{
			Debug.LogError("ARBattleRig: Resources/ARBattle/AROverlay shader missing; planes and outline are not drawn.");
			return;
		}
		planeMaterial = new Material(shader);
		planeMaterial.SetFloat("_DotSpacing", 0.05f);
		planeMaterial.SetFloat("_DotRadius", 0.14f);
		planeMaterial.renderQueue = 3000;
		lineMaterial = new Material(shader);
		lineMaterial.SetFloat("_VertexColor", 1f);
		lineMaterial.renderQueue = 3000;
		ghostMaterial = new Material(shader);
		ghostMaterial.SetFloat("_VertexColor", 1f);
		ghostMaterial.renderQueue = 3001;
	}

	// The plane "prefab", made in code. The template is active itself but hangs from an inactive holder, so it
	// never shows, while AR Foundation's copies (which take the template's activeSelf) come out active.
	private GameObject CreatePlaneTemplate()
	{
		planeTemplateHolder = new GameObject("AR Plane Template Holder");
		planeTemplateHolder.SetActive(false);
		planeTemplateHolder.transform.SetParent(transform, false);
		GameObject template = new GameObject("AR Plane");
		template.layer = OverlayLayer;
		template.transform.SetParent(planeTemplateHolder.transform, false);
		template.AddComponent<MeshFilter>();
		MeshRenderer meshRenderer = template.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = planeMaterial;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		LineRenderer line = template.AddComponent<LineRenderer>();
		line.sharedMaterial = lineMaterial;
		line.useWorldSpace = false;
		line.loop = true;
		// In metres; ARPlaneMeshVisualizer multiplies it by the origin's scale.
		line.widthMultiplier = 0.004f;
		line.positionCount = 0;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;
		template.AddComponent<ARPlaneMeshVisualizer>();
		return template;
	}

	// Outline of the board footprint at the board's own position (world units): blue half for P1, red for
	// P2, a white frame and an arrow that points from P1's edge to the player.
	private GameObject CreateGhost()
	{
		GameObject go = new GameObject("AR Board Outline");
		go.layer = OverlayLayer;
		go.transform.SetParent(transform, false);
		go.transform.position = BoardPivot + new Vector3(0f, 0.15f, 0f);
		List<Vector3> vertices = new List<Vector3>();
		List<Color> colors = new List<Color>();
		List<int> triangles = new List<int>();
		Color blue = new Color(0.25f, 0.55f, 1f, 0.28f);
		Color red = new Color(1f, 0.35f, 0.3f, 0.28f);
		Color white = new Color(1f, 1f, 1f, 0.9f);
		AddQuad(vertices, colors, triangles, -BoardHalfX, -BoardHalfZ, 0f, BoardHalfZ, blue);
		AddQuad(vertices, colors, triangles, 0f, -BoardHalfZ, BoardHalfX, BoardHalfZ, red);
		float w = 0.9f;
		AddQuad(vertices, colors, triangles, -BoardHalfX, -BoardHalfZ, BoardHalfX, -BoardHalfZ + w, white);
		AddQuad(vertices, colors, triangles, -BoardHalfX, BoardHalfZ - w, BoardHalfX, BoardHalfZ, white);
		AddQuad(vertices, colors, triangles, -BoardHalfX, -BoardHalfZ, -BoardHalfX + w, BoardHalfZ, white);
		AddQuad(vertices, colors, triangles, BoardHalfX - w, -BoardHalfZ, BoardHalfX, BoardHalfZ, white);
		AddQuad(vertices, colors, triangles, -w * 0.5f, -BoardHalfZ, w * 0.5f, BoardHalfZ, new Color(1f, 1f, 1f, 0.45f));
		int start = vertices.Count;
		vertices.Add(new Vector3(-BoardHalfX - 7f, 0f, 0f));
		vertices.Add(new Vector3(-BoardHalfX - 2f, 0f, -4f));
		vertices.Add(new Vector3(-BoardHalfX - 2f, 0f, 4f));
		colors.Add(white);
		colors.Add(white);
		colors.Add(white);
		triangles.Add(start);
		triangles.Add(start + 1);
		triangles.Add(start + 2);
		Mesh mesh = new Mesh();
		mesh.name = "AR Board Outline";
		mesh.SetVertices(vertices);
		mesh.SetColors(colors);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		go.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = ghostMaterial;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		go.SetActive(false);
		return go;
	}

	private static void AddQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles, float x0, float z0, float x1, float z1, Color color)
	{
		int start = vertices.Count;
		vertices.Add(new Vector3(x0, 0f, z0));
		vertices.Add(new Vector3(x0, 0f, z1));
		vertices.Add(new Vector3(x1, 0f, z1));
		vertices.Add(new Vector3(x1, 0f, z0));
		for (int i = 0; i < 4; i++)
		{
			colors.Add(color);
		}
		triangles.Add(start);
		triangles.Add(start + 1);
		triangles.Add(start + 2);
		triangles.Add(start);
		triangles.Add(start + 2);
		triangles.Add(start + 3);
	}

	private static void HideRenderers(GameObject root)
	{
		root.GetComponentsInChildren(true, rendererBuffer);
		foreach (Renderer r in rendererBuffer)
		{
			r.enabled = false;
		}
		root.GetComponentsInChildren(true, animatorBuffer);
		foreach (Animator a in animatorBuffer)
		{
			a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
		}
		root.GetComponentsInChildren(true, animationBuffer);
		foreach (Animation a in animationBuffer)
		{
			a.cullingType = AnimationCullingType.AlwaysAnimate;
		}
		rendererBuffer.Clear();
		animatorBuffer.Clear();
		animationBuffer.Clear();
	}

	// ---- On-screen text and buttons --------------------------------------------------------------------

	private string CurrentHint()
	{
		if (Time.unscaledTime < flashUntil)
		{
			return flashText;
		}
		ARSessionState state = ARSession.state;
		if (mode == Mode.Placing)
		{
			if (state == ARSessionState.Unsupported)
			{
				return "Este teléfono no admite AR";
			}
			if (state == ARSessionState.NeedsInstall || state == ARSessionState.Installing)
			{
				return "Instalando los servicios de AR de Google…";
			}
			if (state < ARSessionState.SessionInitializing)
			{
				return "Iniciando la cámara AR…";
			}
			if (imageManager != null && !markerIgnored)
			{
				return aimValid ? "Apunta al marcador impreso, o toca la mesa para poner el tablero donde está el contorno" : "Apunta al marcador impreso para poner el tablero (o toca «Sin marcador» y ponlo tocando la mesa)";
			}
			if (!tracking || validPlaneCount == 0)
			{
				return "Mueve el teléfono despacio sobre la mesa hasta que aparezcan puntos";
			}
			return aimValid ? "Toca la mesa para poner el tablero (el contorno muestra dónde quedará)" : "Toca la mesa, sobre los puntos, para poner el tablero";
		}
		if (!tracking)
		{
			return "Se perdió el seguimiento (" + NotTrackingText() + "). El tablero se queda donde estaba: apunta otra vez a la mesa.";
		}
		if (mode == Mode.Adjusting)
		{
			return "Un dedo: mover · Dos dedos: girar y cambiar el tamaño · Fijar cuando esté listo";
		}
		return null;
	}

	private static string NotTrackingText()
	{
		switch (ARSession.notTrackingReason)
		{
		case NotTrackingReason.InsufficientLight:
			return "poca luz";
		case NotTrackingReason.InsufficientFeatures:
			return "superficie sin textura";
		case NotTrackingReason.ExcessiveMotion:
			return "movimiento rápido";
		case NotTrackingReason.Relocalizing:
			return "recuperando la posición";
		case NotTrackingReason.CameraUnavailable:
			return "cámara no disponible";
		default:
			return "cámara";
		}
	}

	private void EnsureStyles()
	{
		if (whiteTexture == null)
		{
			whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			whiteTexture.SetPixel(0, 0, Color.white);
			whiteTexture.Apply();
			upTexture = TriangleTexture(true);
			downTexture = TriangleTexture(false);
		}
		if (hintStyle == null)
		{
			hintStyle = new GUIStyle(GUI.skin.label);
			hintStyle.alignment = TextAnchor.MiddleCenter;
			hintStyle.wordWrap = true;
			hintStyle.normal.textColor = Color.white;
			buttonStyle = new GUIStyle(GUI.skin.label);
			buttonStyle.alignment = TextAnchor.MiddleCenter;
			buttonStyle.fontStyle = FontStyle.Bold;
			buttonStyle.normal.textColor = Color.white;
			infoStyle = new GUIStyle(GUI.skin.label);
			infoStyle.alignment = TextAnchor.MiddleLeft;
			infoStyle.normal.textColor = Color.white;
		}
		hintStyle.fontSize = Mathf.RoundToInt(15f * ui);
		buttonStyle.fontSize = Mathf.RoundToInt(15f * ui);
		infoStyle.fontSize = Mathf.RoundToInt(13f * ui);
	}

	private static Texture2D TriangleTexture(bool up)
	{
		const int size = 32;
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		for (int y = 0; y < size; y++)
		{
			// Texture rows go bottom to top.
			float t = (y + 0.5f) / size;
			float halfWidth = (up ? 1f - t : t) * 0.5f;
			for (int x = 0; x < size; x++)
			{
				float dx = Mathf.Abs((x + 0.5f) / size - 0.5f);
				float alpha = Mathf.Clamp01((halfWidth - dx) * size);
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
			}
		}
		tex.Apply();
		return tex;
	}

	private void OnGUI()
	{
		if (origin == null)
		{
			return;
		}
		EnsureStyles();
		Color oldColor = GUI.color;
		string hint = CurrentHint();
		if (hint != null)
		{
			float width = Mathf.Min(Screen.width * 0.62f, 560f * ui);
			float pad = 10f * ui;
			float textHeight = hintStyle.CalcHeight(new GUIContent(hint), width - 2f * pad);
			Rect box = new Rect((Screen.width - width) * 0.5f, 12f * ui, width, textHeight + 2f * pad);
			GUI.color = new Color(0f, 0f, 0f, 0.6f);
			GUI.DrawTexture(box, whiteTexture);
			GUI.color = oldColor;
			GUI.Label(new Rect(box.x + pad, box.y + pad, box.width - 2f * pad, textHeight), hint, hintStyle);
		}
		foreach (HudButton button in buttons)
		{
			bool held = buttonFingers.ContainsValue(button.id);
			Color background = button.primary ? new Color(0.15f, 0.6f, 0.3f, 0.85f) : new Color(0f, 0f, 0f, 0.6f);
			if (held)
			{
				background = Color.Lerp(background, Color.white, 0.35f);
			}
			GUI.color = background;
			GUI.DrawTexture(button.rect, whiteTexture);
			GUI.color = oldColor;
			if (button.id == ButtonId.Up || button.id == ButtonId.Down)
			{
				float icon = button.rect.height * 0.45f;
				Rect iconRect = new Rect(button.rect.center.x - icon * 0.5f, button.rect.center.y - icon * 0.5f, icon, icon);
				GUI.DrawTexture(iconRect, button.id == ButtonId.Up ? upTexture : downTexture);
			}
			else
			{
				GUI.Label(button.rect, button.label, buttonStyle);
			}
		}
		if (mode == Mode.Adjusting && buttons.Count > 0)
		{
			// Height and size next to the height buttons.
			Rect down = buttons[buttons.Count - 1].rect;
			string info = "Altura " + (heightOffset >= 0f ? "+" : "") + Mathf.RoundToInt(heightOffset * 100f) + " cm · Tamaño " + boardScale.ToString("0.0") + "x";
			Rect infoRect = new Rect(down.xMax + 8f * ui, down.y, 220f * ui, down.height);
			GUI.color = new Color(0f, 0f, 0f, 0.6f);
			GUI.DrawTexture(new Rect(infoRect.x, infoRect.y, infoStyle.CalcSize(new GUIContent(info)).x + 16f * ui, infoRect.height), whiteTexture);
			GUI.color = oldColor;
			GUI.Label(new Rect(infoRect.x + 8f * ui, infoRect.y, infoRect.width, infoRect.height), info, infoStyle);
		}
		GUI.color = oldColor;
	}

	private void OnDestroy()
	{
		active = false;
		hiddenRoots.Clear();
		// Never leave the game without its input.
		SetGameInputBlocked(false);
		if (touchFilterOwner != null && FingerGestures.Instance == touchFilterOwner)
		{
			FingerGestures.GlobalTouchFilter = previousTouchFilter;
		}
		if (whiteTexture != null)
		{
			Destroy(whiteTexture);
			Destroy(upTexture);
			Destroy(downTexture);
		}
		if (planeMaterial != null)
		{
			Destroy(planeMaterial);
			Destroy(lineMaterial);
			Destroy(ghostMaterial);
		}
	}
}
