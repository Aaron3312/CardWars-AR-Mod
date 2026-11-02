using UnityEngine;

// OnGUI pieces for the versus screens. Sizes are in units of 1/360 of the screen's short side, the way NGUI
// scales the game's own UI, so they sit the same on a phone and on the desktop build. Call Begin() first.
public static class VersusGui
{
	private static Texture2D white;

	private static GUIStyle titleStyle;

	private static GUIStyle bodyStyle;

	private static GUIStyle buttonStyle;

	private static GUIStyle bigStyle;

	private static GUIStyle bannerStyle;

	public static float Ui { get; private set; }

	public static readonly Color Primary = new Color(0.15f, 0.6f, 0.3f, 0.95f);

	public static readonly Color Secondary = new Color(0.2f, 0.22f, 0.28f, 0.95f);

	public static void Begin()
	{
		Ui = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 360f);
		if (white == null)
		{
			white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			white.SetPixel(0, 0, Color.white);
			white.Apply();
		}
		if (titleStyle == null)
		{
			titleStyle = new GUIStyle(GUI.skin.label);
			titleStyle.alignment = TextAnchor.MiddleCenter;
			titleStyle.fontStyle = FontStyle.Bold;
			titleStyle.normal.textColor = Color.white;
			titleStyle.wordWrap = true;
			bodyStyle = new GUIStyle(titleStyle);
			bodyStyle.fontStyle = FontStyle.Normal;
			bodyStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
			bigStyle = new GUIStyle(titleStyle);
			buttonStyle = new GUIStyle(titleStyle);
			bannerStyle = new GUIStyle(bodyStyle);
		}
		titleStyle.fontSize = Mathf.RoundToInt(20f * Ui);
		bodyStyle.fontSize = Mathf.RoundToInt(14f * Ui);
		bigStyle.fontSize = Mathf.RoundToInt(44f * Ui);
		buttonStyle.fontSize = Mathf.RoundToInt(15f * Ui);
		bannerStyle.fontSize = Mathf.RoundToInt(14f * Ui);
	}

	public static void Fill(Rect rect, Color color)
	{
		Color old = GUI.color;
		GUI.color = color;
		GUI.DrawTexture(rect, white);
		GUI.color = old;
	}

	public static bool Button(Rect rect, string label, bool primary)
	{
		bool hover = rect.Contains(Event.current.mousePosition);
		Color color = primary ? Primary : Secondary;
		Fill(rect, hover ? Color.Lerp(color, Color.white, 0.12f) : color);
		GUI.Label(rect, label, buttonStyle);
		return GUI.Button(rect, GUIContent.none, GUIStyle.none);
	}

	public static void Label(Rect rect, string text, bool title)
	{
		GUI.Label(rect, text, title ? titleStyle : bodyStyle);
	}

	public static void Big(Rect rect, string text)
	{
		GUI.Label(rect, text, bigStyle);
	}

	public static float BodyHeight(string text, float width)
	{
		return bodyStyle.CalcHeight(new GUIContent(text), width);
	}

	// A centred box over a dimmed screen with a title, a text and up to two buttons. Returns 1 or 2 for the
	// button pressed this frame, 0 otherwise.
	public static int Panel(string title, string body, string button1, string button2)
	{
		float u = Ui;
		Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
		float width = Mathf.Min(Screen.width - 32f * u, 420f * u);
		float pad = 18f * u;
		float bodyHeight = string.IsNullOrEmpty(body) ? 0f : BodyHeight(body, width - 2f * pad);
		float height = pad + 32f * u + 10f * u + bodyHeight + 18f * u + 46f * u + pad;
		Rect box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
		Fill(box, new Color(0.08f, 0.09f, 0.12f, 0.96f));
		float y = box.y + pad;
		Label(new Rect(box.x + pad, y, width - 2f * pad, 32f * u), title, true);
		y += 32f * u + 10f * u;
		if (bodyHeight > 0f)
		{
			Label(new Rect(box.x + pad, y, width - 2f * pad, bodyHeight), body, false);
			y += bodyHeight;
		}
		y += 18f * u;
		int pressed = 0;
		float gap = 10f * u;
		if (button2 == null)
		{
			if (Button(new Rect(box.x + pad, y, width - 2f * pad, 46f * u), button1, true))
			{
				pressed = 1;
			}
		}
		else
		{
			float half = (width - 2f * pad - gap) * 0.5f;
			if (Button(new Rect(box.x + pad, y, half, 46f * u), button1, false))
			{
				pressed = 1;
			}
			if (Button(new Rect(box.x + pad + half + gap, y, half, 46f * u), button2, true))
			{
				pressed = 2;
			}
		}
		return pressed;
	}

	// A short line at the top of the screen, for waiting states and warnings.
	public static void Banner(string text, bool warning)
	{
		float u = Ui;
		float width = Mathf.Min(Screen.width * 0.6f, 520f * u);
		float pad = 8f * u;
		float textHeight = bannerStyle.CalcHeight(new GUIContent(text), width - 2f * pad);
		Rect box = new Rect((Screen.width - width) * 0.5f, 56f * u, width, textHeight + 2f * pad);
		Fill(box, warning ? new Color(0.65f, 0.12f, 0.1f, 0.85f) : new Color(0f, 0f, 0f, 0.6f));
		GUI.Label(new Rect(box.x + pad, box.y + pad, box.width - 2f * pad, textHeight), text, bannerStyle);
	}
}
