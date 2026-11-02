using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Unattended versus test, off unless CW_VERSUS is "host", "join" or "idle" (only screenshots and the widget
// log, to look at the menus). With host or join the game plays muted and on its own: it
// creates or joins a match from the main menu (CW_VERSUS_ADDR, default 127.0.0.1), places its landscapes,
// spins the bottle and plays its turns through the same entry points as a person (cards, floops, targets,
// discard picks, the battle ring, end of turn). It plays CW_VERSUS_GAMES games (default 1) with rematches,
// then quits. Screenshots go to CW_TEST_SHOTS every CW_TEST_INTERVAL seconds (default 10), the match log to
// CW_VERSUS_LOG. CW_TEST_CLICKS ("text|text") clicks NGUI widgets outside the battle, to get past prompts.
public class VersusTestBot : MonoBehaviour
{
	private static VersusTestBot bot;

	private string role;

	private string address;

	private int games = 1;

	private int gamesDone;

	private string shotDir;

	private float interval = 10f;

	private float nextShot;

	private int shotCount;

	private string logPath;

	private string[] clicks;

	private string lastWidgets;

	private System.Random rng;

	private float nextAct;

	private float menuSince = -1f;

	private float joinRetryAt;

	private bool landscapesPlaced;

	private float landscapesSeenAt = -1f;

	private bool bottleSpun;

	private bool readyClicked;

	private int actionTurn = -1;

	private int actionsThisTurn;

	private int handledGame;

	private float overSince = -1f;

	private float deadline;

	private float ringCallAt = -1f;

	private float ringStart;

	private float ringDelay = 0.8f;

	public static bool Running
	{
		get
		{
			return bot != null;
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		string mode = Environment.GetEnvironmentVariable("CW_VERSUS");
		if (mode != "host" && mode != "join" && mode != "idle")
		{
			return;
		}
		GameObject go = new GameObject("VersusTestBot");
		DontDestroyOnLoad(go);
		bot = go.AddComponent<VersusTestBot>();
		bot.role = mode;
		bot.address = Environment.GetEnvironmentVariable("CW_VERSUS_ADDR") ?? "127.0.0.1";
		int value;
		if (int.TryParse(Environment.GetEnvironmentVariable("CW_VERSUS_GAMES"), out value) && value > 0)
		{
			bot.games = value;
		}
		float seconds;
		if (float.TryParse(Environment.GetEnvironmentVariable("CW_TEST_INTERVAL"), out seconds) && seconds > 0f)
		{
			bot.interval = seconds;
		}
		bot.shotDir = Environment.GetEnvironmentVariable("CW_TEST_SHOTS");
		if (!string.IsNullOrEmpty(bot.shotDir))
		{
			Directory.CreateDirectory(bot.shotDir);
		}
		bot.logPath = Environment.GetEnvironmentVariable("CW_VERSUS_LOG");
		string clicks = Environment.GetEnvironmentVariable("CW_TEST_CLICKS");
		if (!string.IsNullOrEmpty(clicks))
		{
			bot.clicks = clicks.Split('|').Where((string c) => c.Length > 0).ToArray();
		}
		int botSeed;
		if (!int.TryParse(Environment.GetEnvironmentVariable("CW_VERSUS_BOTSEED"), out botSeed))
		{
			botSeed = (mode == "host") ? 1 : 2;
		}
		bot.rng = new System.Random(botSeed);
		float minutes;
		if (!float.TryParse(Environment.GetEnvironmentVariable("CW_VERSUS_TIMEOUT"), out minutes) || minutes <= 0f)
		{
			minutes = 45f;
		}
		bot.deadline = minutes * 60f;
		Application.runInBackground = true;
		AudioListener.volume = 0f;
		Application.logMessageReceived += bot.OnUnityLog;
		bot.Write("bot " + mode + " started, games " + bot.games);
	}

	// The battle ring asks every frame while it spins. Each ring aims at an outcome (hit 40 %, critical
	// 35 %, miss 25 %) and taps when the needle is in that zone, after a short wait like a person would.
	public static bool TapRing()
	{
		if (bot == null)
		{
			return false;
		}
		float now = Time.realtimeSinceStartup;
		if (now - bot.ringCallAt > 0.5f)
		{
			bot.ringStart = now;
			bot.ringDelay = 0.3f + (float)bot.rng.NextDouble() * 0.6f;
			double roll = bot.rng.NextDouble();
			bot.ringAim = (roll < 0.4) ? "Hit" : ((roll < 0.75) ? "Crit" : "Miss");
		}
		bot.ringCallAt = now;
		if (now - bot.ringStart < bot.ringDelay)
		{
			return false;
		}
		if (now - bot.ringStart > 8f)
		{
			return true;
		}
		CWBattleSequenceController ring = CWBattleSequenceController.GetInstance();
		float angle = ring.currentAngle;
		bool hit = angle >= ring.hitAreaStart && angle <= ring.hitAreaEnd;
		bool crit = angle >= ring.critAreaStart && angle <= ring.critAreaEnd;
		switch (bot.ringAim)
		{
		case "Hit":
			return hit;
		case "Crit":
			return crit;
		default:
			return !hit && !crit;
		}
	}

	private string ringAim = "Hit";

	private void OnUnityLog(string text, string stack, LogType type)
	{
		if (text.StartsWith("VERSUS") || (type == LogType.Exception && text.Length > 0))
		{
			Write("[" + type + "] " + text);
		}
	}

	private void Write(string text)
	{
		string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + role + " " + text;
		UnityEngine.Debug.Log("VersusTestBot: " + text);
		if (!string.IsNullOrEmpty(logPath))
		{
			try
			{
				File.AppendAllText(logPath, line + "\n");
			}
			catch (Exception)
			{
			}
		}
	}

	private void Shot(string tag)
	{
		if (string.IsNullOrEmpty(shotDir))
		{
			return;
		}
		string file = Path.Combine(shotDir, string.Format("{0}-{1:D3}{2}.png", role, shotCount++, (tag == null) ? string.Empty : ("-" + tag)));
		ScreenCapture.CaptureScreenshot(file);
	}

	private void Update()
	{
		AudioListener.volume = 0f;
		float now = Time.realtimeSinceStartup;
		if (now > deadline)
		{
			Write("timeout, quitting");
			Quit();
			return;
		}
		if (now >= nextShot)
		{
			nextShot = now + interval;
			Shot(null);
		}
		VersusMatch match = VersusMatch.Instance;
		if (match != null && match.CurrentStage != VersusMatch.Stage.Idle && !hooked)
		{
			hooked = true;
			match.OnLog += (string text) => Write(text);
		}
		if (VersusMatch.Active && match != null && match.BattleLoaded)
		{
			menuSince = -1f;
			BattleBot(match);
			return;
		}
		if (SceneManager.GetSceneByName("AdventureTime").isLoaded)
		{
			MenuBot(match);
		}
		else
		{
			menuSince = -1f;
		}
		if (!VersusMatch.InBattleScene && now >= nextAct)
		{
			nextAct = now + 3f;
			LogAndClickWidgets();
		}
	}

	private bool hooked;

	private void MenuBot(VersusMatch match)
	{
		float now = Time.realtimeSinceStartup;
		if (menuSince < 0f)
		{
			menuSince = now;
		}
		if (now - menuSince < 4f || VersusMenu.Instance == null || role == "idle")
		{
			return;
		}
		if (match == null || match.CurrentStage == VersusMatch.Stage.Idle)
		{
			if (gamesDone >= games)
			{
				return;
			}
			if (role == "host")
			{
				Write("creating the match");
				VersusMenu.Instance.OpenHost();
			}
			else if (now >= joinRetryAt)
			{
				Write("joining " + address);
				VersusMenu.Instance.OpenJoin(address);
				joinRetryAt = now + 4f;
			}
			Shot("menu");
			return;
		}
		if (role == "join" && match.CurrentStage == VersusMatch.Stage.Joining && match.ConnectionError != null && now >= joinRetryAt)
		{
			Write("join failed (" + match.ConnectionError + "), retrying");
			VersusMenu.Instance.OpenJoin(address);
			joinRetryAt = now + 4f;
		}
	}

	private void BattleBot(VersusMatch match)
	{
		float now = Time.realtimeSinceStartup;
		if (match.GameNumber != handledGame)
		{
			handledGame = match.GameNumber;
			landscapesPlaced = false;
			landscapesSeenAt = -1f;
			bottleSpun = false;
			readyClicked = false;
			actionTurn = -1;
			overSince = -1f;
			Write("battle " + handledGame + " loading");
		}
		if (match.ConnectionError != null)
		{
			if (overSince < 0f)
			{
				overSince = now;
				Write("connection lost: " + match.ConnectionError);
				Shot("lost");
			}
			else if (now - overSince > 5f)
			{
				Quit();
			}
			return;
		}
		if (match.GameOverShown)
		{
			if (overSince < 0f)
			{
				overSince = now;
				gamesDone++;
				Write("GAME " + match.GameNumber + " OVER: " + (match.LocalWon ? "won" : "lost") + ", desyncs " + match.DesyncCount + ", turn " + GameDataScript.GetInstance().Turn);
				Shot("over");
			}
			else if (now - overSince > 3f)
			{
				if (gamesDone < games)
				{
					if (!match.LocalRematchSent)
					{
						Write("asking for a rematch");
						match.Rematch();
					}
				}
				else if (now - overSince > 6f)
				{
					Quit();
				}
			}
			return;
		}
		if (now < nextAct)
		{
			return;
		}
		BattlePhaseManager phaseMgr = BattlePhaseManager.GetInstance();
		GameState gameState = GameState.Instance;
		if (phaseMgr == null || gameState == null || !gameState.IsSetUp)
		{
			return;
		}
		if (!landscapesPlaced)
		{
			CWLandscapeFillCards fill = FindAnyObjectByType<CWLandscapeFillCards>();
			if (fill != null && fill.isActiveAndEnabled)
			{
				if (landscapesSeenAt < 0f)
				{
					landscapesSeenAt = now;
				}
				else if (now - landscapesSeenAt > 2f)
				{
					PlaceLandscapes();
				}
			}
			return;
		}
		if (!readyClicked)
		{
			BottleBot();
			return;
		}
		BattlePhase phase = phaseMgr.Phase;
		if (phase == BattlePhase.P1SetupLanePlyr || phase == BattlePhase.P1SetupLaneOpp)
		{
			PickTarget();
			return;
		}
		if (PickDiscard())
		{
			return;
		}
		if (phase == BattlePhase.P1Setup)
		{
			PlayTurn(gameState);
		}
	}

	private void PlaceLandscapes()
	{
		GameState gameState = GameState.Instance;
		LandscapeManagerScript landscapes = LandscapeManagerScript.GetInstance();
		for (int lane = 0; lane < 4; lane++)
		{
			gameState.SetLandscape(PlayerType.User, lane, gameState.GetLandscapeInDeck(PlayerType.User, lane));
		}
		landscapes.UpdateLandscapes();
		landscapes.populatedLandscapeCount = 4;
		foreach (CWLandscapeCardDragOld card in landscapes.CardScripts)
		{
			if (card != null)
			{
				card.gameObject.SetActive(false);
			}
		}
		landscapes.ReadyButton.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
		TutorialMonitor.Instance.TriggerTutorial(TutorialTrigger.StartBottleSpin);
		landscapesPlaced = true;
		nextAct = Time.realtimeSinceStartup + 2f;
		Write("landscapes placed: " + string.Join(",", Enumerable.Range(0, 4).Select((int i) => gameState.GetLandscapeType(PlayerType.User, i).ToString()).ToArray()));
		Shot("landscapes");
	}

	private void BottleBot()
	{
		CWKetchupBottleScript bottle = FindAnyObjectByType<CWKetchupBottleScript>();
		if (bottle == null)
		{
			return;
		}
		bool done = (bool)typeof(CWKetchupBottleScript).GetField("done", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bottle);
		if (!bottleSpun)
		{
			if (VersusMatch.RemoteLandscapesReady && !bottle.Spin && !done)
			{
				Write("spinning the bottle");
				bottle.SetSpinFlag();
				bottleSpun = bottle.Spin;
				nextAct = Time.realtimeSinceStartup + 1f;
			}
			return;
		}
		if (done && bottle.battleReadyButton != null && bottle.battleReadyButton.activeInHierarchy)
		{
			Write("bottle says " + (bottle.whoGoesFirstLabel != null ? bottle.whoGoesFirstLabel.text : "?") + "; ready");
			Shot("bottle");
			bottle.battleReadyButton.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
			readyClicked = true;
			nextAct = Time.realtimeSinceStartup + 3f;
		}
	}

	private void PickTarget()
	{
		List<FlippedLandscapeScript> lanes = FindObjectsByType<FlippedLandscapeScript>(FindObjectsInactive.Exclude).Where((FlippedLandscapeScript f) => f.Flash).ToList();
		if (lanes.Count == 0)
		{
			return;
		}
		FlippedLandscapeScript pick = lanes[rng.Next(lanes.Count)];
		Write("target lane " + pick.Index + " (" + pick.name + ")");
		pick.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
		nextAct = Time.realtimeSinceStartup + 2f;
	}

	private bool PickDiscard()
	{
		foreach (CWDiscardCard card in FindObjectsByType<CWDiscardCard>(FindObjectsInactive.Exclude))
		{
			if (card.filterScript != null && !card.filterScript.CardSelected && card.card != null && card.filterScript.CardFilter(card.card))
			{
				Write("discard pick " + card.card.Form.ID);
				card.OnClick();
				nextAct = Time.realtimeSinceStartup + 2f;
				return true;
			}
		}
		return false;
	}

	private void PlayTurn(GameState gameState)
	{
		CWPlayerHandsController hands = CWPlayerHandsController.GetInstance();
		if (hands == null || hands.spinStart || gameState.IsSummoning(PlayerType.User))
		{
			return;
		}
		int turn = GameDataScript.GetInstance().Turn;
		if (turn != actionTurn)
		{
			actionTurn = turn;
			actionsThisTurn = 0;
			nextAct = Time.realtimeSinceStartup + 1.5f;
			return;
		}
		actionsThisTurn++;
		if (actionsThisTurn <= 8)
		{
			if (rng.NextDouble() < 0.5 && TryLeader(gameState))
			{
				return;
			}
			if (rng.NextDouble() < 0.35 && TryFloop(gameState))
			{
				return;
			}
			if (TryPlayCard(gameState, hands))
			{
				return;
			}
			if (TryFloop(gameState))
			{
				return;
			}
		}
		CWP1SetupBranch branch = FindObjectsByType<CWP1SetupBranch>(FindObjectsInactive.Include).FirstOrDefault();
		if (branch != null)
		{
			Write("end turn " + turn);
			branch.Advance();
			nextAct = Time.realtimeSinceStartup + 2f;
		}
	}

	private bool TryPlayCard(GameState gameState, CWPlayerHandsController hands)
	{
		List<KeyValuePair<CardItem, int>> options = new List<KeyValuePair<CardItem, int>>();
		foreach (CardItem card in gameState.GetHand(PlayerType.User))
		{
			if (card.Form.Type == CardType.Spell)
			{
				if (card.Form.CanPlay(PlayerType.User, 0))
				{
					options.Add(new KeyValuePair<CardItem, int>(card, 0));
				}
				continue;
			}
			for (int lane = 0; lane < 4; lane++)
			{
				if (!gameState.LaneHasCard(PlayerType.User, lane, card.Form.Type) && card.Form.CanPlay(PlayerType.User, lane))
				{
					options.Add(new KeyValuePair<CardItem, int>(card, lane));
				}
			}
		}
		if (options.Count == 0 || !hands.CanPlay())
		{
			return false;
		}
		KeyValuePair<CardItem, int> pick = options[rng.Next(options.Count)];
		// What CWTBDragToMove.BeginDrag sets up: the spell spin animates the card being dragged.
		CWTBTapToBringForward handCard = FindObjectsByType<CWTBTapToBringForward>(FindObjectsInactive.Exclude).FirstOrDefault((CWTBTapToBringForward c) => c.card == pick.Key);
		if (handCard == null)
		{
			return false;
		}
		PanelManagerBattle.GetInstance().currentCardObj = handCard.gameObject;
		hands.card = pick.Key;
		hands.cardName = pick.Key.Form.Name;
		Write("play " + pick.Key.Form.ID + " (" + pick.Key.Form.Type + ") lane " + pick.Value);
		hands.lane = pick.Value;
		hands.PlayCard(pick.Value, pick.Key);
		nextAct = Time.realtimeSinceStartup + 3f;
		return true;
	}

	private bool TryLeader(GameState gameState)
	{
		if (!gameState.IsLeaderAbilityReady(PlayerType.User))
		{
			return false;
		}
		// What CWTriggerLeaderAbility.OnClick does (its button sits in a panel that opens from the portrait).
		Write("leader ability " + gameState.GetLeader(PlayerType.User).Form.ID);
		BattlePhaseManager.GetInstance().Phase = BattlePhase.P1LeaderAbility;
		CWFloopActionManager.GetInstance().TriggerLeader(PlayerType.User);
		nextAct = Time.realtimeSinceStartup + 6f;
		return true;
	}

	private bool TryFloop(GameState gameState)
	{
		List<int> lanes = new List<int>();
		for (int lane = 0; lane < 4; lane++)
		{
			if (gameState.LaneHasCreature(PlayerType.User, lane) && gameState.CanFloopCard(PlayerType.User, gameState.GetCreature(PlayerType.User, lane)))
			{
				lanes.Add(lane);
			}
		}
		if (lanes.Count == 0)
		{
			return false;
		}
		int pick = lanes[rng.Next(lanes.Count)];
		CreatureScript creature = gameState.GetCreature(PlayerType.User, pick);
		CWFloopActionManager floop = CWFloopActionManager.GetInstance();
		floop.player = PlayerType.User;
		floop.lane = pick;
		floop.card = creature.Data;
		floop.anim = CreatureManagerScript.GetInstance().Instances[0, pick, 0].GetComponent<Animation>();
		Write("floop lane " + pick + " " + creature.Data.Form.ID);
		floop.SendMessage("PlayFloopAction", SendMessageOptions.DontRequireReceiver);
		nextAct = Time.realtimeSinceStartup + 4f;
		return true;
	}

	// NGUI widgets take clicks through their collider: UICamera sends "OnClick" to its GameObject.
	private void LogAndClickWidgets()
	{
		List<KeyValuePair<GameObject, string>> widgets = new List<KeyValuePair<GameObject, string>>();
		foreach (Collider collider in FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
		{
			if (!collider.enabled || (collider.GetComponent<UIWidget>() == null && collider.GetComponent<UIButtonMessage>() == null && collider.GetComponent<UIButtonTween>() == null))
			{
				continue;
			}
			string text = string.Join(" ", collider.GetComponentsInChildren<UILabel>().Select((UILabel l) => l.text).Where((string t) => !string.IsNullOrEmpty(t)).ToArray());
			widgets.Add(new KeyValuePair<GameObject, string>(collider.gameObject, text.Replace("\n", " ")));
		}
		string joined = string.Join(", ", widgets.Select((KeyValuePair<GameObject, string> w) => w.Key.name + "[" + w.Value + "]").ToArray());
		if (joined != lastWidgets)
		{
			lastWidgets = joined;
			Write("widgets in " + SceneManager.GetActiveScene().name + ": " + joined);
		}
		if (clicks == null)
		{
			return;
		}
		foreach (string click in clicks)
		{
			foreach (KeyValuePair<GameObject, string> widget in widgets)
			{
				if (widget.Value.IndexOf(click, StringComparison.OrdinalIgnoreCase) >= 0 || widget.Key.name.IndexOf(click, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					Write("click " + widget.Key.name + "[" + widget.Value + "]");
					widget.Key.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
					return;
				}
			}
		}
	}

	private void Quit()
	{
		Write("quit");
		Application.Quit();
	}
}
