using UnityEngine;
using UnityEngine.SceneManagement;

// "Versus en la mesa" in the main menu (AdventureTime): create a match and show its code, or join one by
// typing the code. OnGUI on top of NGUI, whose input is locked while the panel is open.
public class VersusMenu : MonoBehaviour
{
	private enum Screen2
	{
		Closed,
		Choose,
		Host,
		Join
	}

	private const string MenuSceneName = "AdventureTime";

	private Screen2 screen;

	private string code = string.Empty;

	private bool inputLocked;

	public static VersusMenu Instance { get; private set; }

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
		OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName || Instance != null)
		{
			return;
		}
		GameObject go = new GameObject("Versus Menu");
		SceneManager.MoveGameObjectToScene(go, scene);
		go.AddComponent<VersusMenu>();
	}

	private void Awake()
	{
		Instance = this;
		useGUILayout = false;
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
		SetInputLock(false);
	}

	public void OpenHost()
	{
		VersusMatch.GetOrCreate().Host();
		screen = Screen2.Host;
	}

	public void OpenJoin(string address)
	{
		code = address ?? string.Empty;
		screen = Screen2.Join;
		VersusMatch.GetOrCreate().Join(code);
	}

	private void Update()
	{
		SetInputLock(screen != Screen2.Closed);
	}

	private void SetInputLock(bool locked)
	{
		if (locked == inputLocked)
		{
			return;
		}
		inputLocked = locked;
		if (locked)
		{
			UICamera.LockInput();
		}
		else
		{
			UICamera.UnlockInput();
		}
	}

	private void Close()
	{
		VersusMatch match = VersusMatch.Instance;
		if (match != null && match.CurrentStage != VersusMatch.Stage.Battle)
		{
			match.Leave();
		}
		screen = Screen2.Closed;
	}

	private void OnGUI()
	{
		VersusGui.Begin();
		float u = VersusGui.Ui;
		switch (screen)
		{
		case Screen2.Closed:
			DrawEntryButton(u);
			break;
		case Screen2.Choose:
			DrawChoose(u);
			break;
		case Screen2.Host:
			DrawHost(u);
			break;
		case Screen2.Join:
			DrawJoin(u);
			break;
		}
	}

	// Left side, under the player's panel: the bottom row and the corners are the menu's own buttons.
	private void DrawEntryButton(float u)
	{
		Rect rect = new Rect(10f * u, 82f * u, 170f * u, 40f * u);
		if (VersusGui.Button(rect, "Versus en la mesa", true))
		{
			screen = Screen2.Choose;
		}
	}

	private static Rect Box(float u, float width, float height)
	{
		width = Mathf.Min(width * u, Screen.width - 24f * u);
		height = Mathf.Min(height * u, Screen.height - 24f * u);
		VersusGui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
		Rect box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
		VersusGui.Fill(box, new Color(0.08f, 0.09f, 0.12f, 0.97f));
		return box;
	}

	private void DrawChoose(float u)
	{
		Rect box = Box(u, 480f, 330f);
		float pad = 18f * u;
		float x = box.x + pad;
		float w = box.width - 2f * pad;
		float y = box.y + pad;
		float gap = 10f * u;
		VersusGui.Label(new Rect(x, y, w, 30f * u), "Versus en la mesa", true);
		y += 34f * u;
		string body = "Juega contra un amigo en la misma wifi, cada quien con su teléfono y su mazo actual. Uno crea la partida y el otro se une con el código.";
		float bodyHeight = VersusGui.BodyHeight(body, w);
		VersusGui.Label(new Rect(x, y, w, bodyHeight), body, false);
		y += bodyHeight + 10f * u;
		// How this phone puts the board on the table in AR; the rival may choose differently.
		bool marker = VersusMatch.UseMarker;
		VersusGui.Label(new Rect(x, y, w, 20f * u), "Tablero en tu teléfono:", false);
		y += 22f * u;
		float half = (w - gap) * 0.5f;
		if (VersusGui.Button(new Rect(x, y, half, 40f * u), "Con marcador", marker))
		{
			VersusMatch.UseMarker = true;
		}
		if (VersusGui.Button(new Rect(x + half + gap, y, half, 40f * u), "Sin marcador", !marker))
		{
			VersusMatch.UseMarker = false;
		}
		y += 44f * u;
		string hint = marker ? "Se pega a la hoja impresa (docs/marcador): los dos ven el mismo tablero." : "Cada quien toca la mesa para poner su tablero y lo afina con «Ajustar».";
		VersusGui.Label(new Rect(x, y, w, 20f * u), hint, false);
		float bottom = box.yMax - pad - 46f * u;
		float third = (w - 2f * gap) / 3f;
		if (VersusGui.Button(new Rect(x, bottom, third, 46f * u), "Cerrar", false))
		{
			Close();
		}
		if (VersusGui.Button(new Rect(x + third + gap, bottom, third, 46f * u), "Unirse", true))
		{
			code = string.Empty;
			screen = Screen2.Join;
		}
		if (VersusGui.Button(new Rect(x + 2f * (third + gap), bottom, third, 46f * u), "Crear partida", true))
		{
			OpenHost();
		}
	}

	private void DrawHost(float u)
	{
		VersusMatch match = VersusMatch.GetOrCreate();
		Rect box = Box(u, 460f, 300f);
		float pad = 18f * u;
		float x = box.x + pad;
		float w = box.width - 2f * pad;
		float y = box.y + pad;
		string ip = VersusNet.LocalIPv4();
		string last = ip.Substring(ip.LastIndexOf('.') + 1);
		VersusGui.Label(new Rect(x, y, w, 26f * u), "Tu código", true);
		y += 26f * u;
		VersusGui.Big(new Rect(x, y, w, 60f * u), last);
		y += 62f * u;
		string body = "Tu amigo toca «Unirse» y escribe " + last + ".\nSi no conecta, que escriba la IP completa: " + ip;
		float bodyHeight = VersusGui.BodyHeight(body, w);
		VersusGui.Label(new Rect(x, y, w, bodyHeight), body, false);
		y += bodyHeight + 8f * u;
		VersusGui.Label(new Rect(x, y, w, 24f * u), match.StatusText ?? string.Empty, false);
		float bottom = box.yMax - pad - 46f * u;
		if (VersusGui.Button(new Rect(x, bottom, w, 46f * u), "Cancelar", false))
		{
			Close();
		}
	}

	private void DrawJoin(float u)
	{
		VersusMatch match = VersusMatch.GetOrCreate();
		Rect box = Box(u, 560f, 300f);
		float pad = 16f * u;
		float gap = 8f * u;
		float keyW = 56f * u;
		float keyH = 46f * u;
		float padW = 3f * keyW + 2f * gap;
		float leftW = box.width - 3f * pad - padW;
		float x = box.x + pad;
		float y = box.y + pad;
		VersusGui.Label(new Rect(x, y, leftW, 26f * u), "Código del otro teléfono", true);
		y += 30f * u;
		VersusGui.Fill(new Rect(x, y, leftW, 56f * u), new Color(1f, 1f, 1f, 0.08f));
		VersusGui.Big(new Rect(x, y, leftW, 56f * u), code.Length > 0 ? code : " ");
		y += 64f * u;
		bool busy = match.CurrentStage == VersusMatch.Stage.Joining || match.CurrentStage == VersusMatch.Stage.Lobby;
		string status = match.StatusText ?? "El número grande que muestra al crear la partida (o su IP completa).";
		float statusHeight = Mathf.Min(VersusGui.BodyHeight(status, leftW), box.yMax - pad - keyH - gap - y);
		VersusGui.Label(new Rect(x, y, leftW, statusHeight), status, false);
		float bottom = box.yMax - pad - keyH;
		float half = (leftW - gap) * 0.5f;
		if (VersusGui.Button(new Rect(x, bottom, half, keyH), "Cancelar", false))
		{
			Close();
		}
		if (!busy && VersusGui.Button(new Rect(x + half + gap, bottom, half, keyH), "Conectar", true) && code.Length > 0)
		{
			match.Join(code);
		}
		string[] keys = new string[12] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "Borrar" };
		float px = box.xMax - pad - padW;
		float py = box.y + pad + (box.height - 2f * pad - (4f * keyH + 3f * gap)) * 0.5f;
		for (int i = 0; i < keys.Length; i++)
		{
			Rect key = new Rect(px + (i % 3) * (keyW + gap), py + (i / 3) * (keyH + gap), keyW, keyH);
			if (VersusGui.Button(key, keys[i], false) && !busy)
			{
				if (keys[i] == "Borrar")
				{
					code = (code.Length > 0) ? code.Substring(0, code.Length - 1) : code;
				}
				else if (code.Length < 15)
				{
					code += keys[i];
				}
			}
		}
	}
}
