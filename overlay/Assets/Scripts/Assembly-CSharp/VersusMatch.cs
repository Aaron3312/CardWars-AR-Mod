using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// "Versus en la mesa": two phones play the same battle in lockstep (docs/ar-versus.md). Both simulate it; only
// the choices travel: landscapes, cards played, floops, leader, targets, discard picks, end of turn and the
// attacker's battle ring. Each phone is PlayerType.User for itself, so anything both must agree on uses the
// canonical side instead: A = host, B = guest. The rival's moves enter where the AI used to decide
// (CWOpponentActionSequencer) and the rival's targets where the AI picked them (CardScript.ChoosesByHand).
// At the start of every turn both phones exchange a hash of the battle state and compare.
[DefaultExecutionOrder(-1000)]
public class VersusMatch : MonoBehaviour
{
	public enum Stage
	{
		Idle,
		Hosting,
		Joining,
		Lobby,
		Battle,
		Over
	}

	public class RemoteAction
	{
		public string Kind;

		public CardItem Card;

		public Lane Lane;
	}

	private class Msg
	{
		public string Kind;

		public int Turn;

		public string[] Args;

		public string Raw;
	}

	private class DeckSpec
	{
		public string LeaderId;

		public int LeaderRank;

		public string[] Landscapes;

		public string[] Cards;

		public string Name;
	}

	// A fixed board for every versus match: Candy Kingdom, plain rules.
	public const string QuestId = "43";

	private const int ProtocolVersion = 1;

	private const string BattleSceneName = "BattleScene";

	private const float PingInterval = 2f;

	// Long enough for a notification or a quick look at another app (the phone stops sending meanwhile).
	private const float PeerTimeout = 35f;

	private static VersusMatch instance;

	private static bool active;

	private VersusNet net;

	private Stage stage;

	private bool isHost;

	private string remoteName = string.Empty;

	private bool helloReceived;

	private DeckSpec remoteDeck;

	private Deck localDeck;

	private int seed;

	private int firstSide;

	private int game;

	private readonly LinkedList<Msg> gameQueue = new LinkedList<Msg>();

	private CardScript pendingTarget;

	private CardScript pendingPick;

	private bool remoteLandscapesApplied;

	private bool landscapesSent;

	private int lastHashedTurn;

	private readonly Dictionary<int, string> localStates = new Dictionary<int, string>();

	private readonly Dictionary<int, string> remoteStates = new Dictionary<int, string>();

	private readonly List<string> desyncs = new List<string>();

	private float desyncFlashUntil;

	private string protocolError;

	private float lastPing;

	private float lastHeard;

	private bool localRematch;

	private bool remoteRematch;

	private bool remoteLeft;

	private float gameOverAt = -1f;

	// False from a launch until its BattleScene has loaded: a rematch launches while the old battle, already
	// over, is still on screen.
	private bool battleLoaded;

	private bool localWon;

	private bool inputLocked;

	private float waitingSince = -1f;

	private string waitingText;

	// For the lobby and the test bot.
	public string StatusText { get; private set; }

	public event Action<string> OnLog;

	public static VersusMatch Instance
	{
		get
		{
			return instance;
		}
	}

	// True from the moment a versus battle is launched until the player leaves it.
	public static bool Active
	{
		get
		{
			return active;
		}
	}

	public Stage CurrentStage
	{
		get
		{
			return stage;
		}
	}

	public bool IsHost
	{
		get
		{
			return isHost;
		}
	}

	public string RemoteName
	{
		get
		{
			return remoteName;
		}
	}

	public string ConnectionError
	{
		get
		{
			return (net != null) ? net.Error : null;
		}
	}

	public bool Connected
	{
		get
		{
			return net != null && net.Connected;
		}
	}

	// The battle of the current game has loaded (not the previous one, during a rematch).
	public bool BattleLoaded
	{
		get
		{
			return battleLoaded && InBattleScene;
		}
	}

	public int GameNumber
	{
		get
		{
			return game;
		}
	}

	public int DesyncCount
	{
		get
		{
			return desyncs.Count;
		}
	}

	public bool GameOverShown
	{
		get
		{
			return stage == Stage.Over;
		}
	}

	public bool LocalWon
	{
		get
		{
			return localWon;
		}
	}

	public bool LocalRematchSent
	{
		get
		{
			return localRematch;
		}
	}

	public static VersusMatch GetOrCreate()
	{
		if (instance == null)
		{
			GameObject go = new GameObject("Versus Match");
			DontDestroyOnLoad(go);
			instance = go.AddComponent<VersusMatch>();
		}
		return instance;
	}

	// ---- Canonical sides -------------------------------------------------------------------------------

	// 0 = A (host), 1 = B (guest). Outside a match it is just the PlayerType.
	public static int Side(PlayerType player)
	{
		if (!active || instance == null)
		{
			return (int)player;
		}
		return (((int)player == 0) == instance.isHost) ? 0 : 1;
	}

	public static PlayerType PlayerOfSide(int side)
	{
		bool user = (side == 0) == (instance == null || instance.isHost);
		return user ? PlayerType.User : PlayerType.Opponent;
	}

	// The rival's choices come from the network, not from the AI.
	public static bool IsRemote(PlayerType player)
	{
		return active && (int)player == 1;
	}

	// Who starts, as seen on this phone (the ketchup bottle is made to point there).
	public static PlayerType FirstPlayer
	{
		get
		{
			return PlayerOfSide((instance != null) ? instance.firstSide : 0);
		}
	}

	private const string MarkerPref = "versus_use_marker";

	// This phone's choice for the AR board: snap it to the printed marker, or place it by tapping the table.
	// Each phone chooses for itself; the rival does not need to choose the same.
	public static bool UseMarker
	{
		get
		{
			return PlayerPrefs.GetInt(MarkerPref, 1) != 0;
		}
		set
		{
			PlayerPrefs.SetInt(MarkerPref, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}

	// BattleScene is loaded additively over LoadingScreen, so it is not the active scene.
	public static bool InBattleScene
	{
		get
		{
			return SceneManager.GetSceneByName(BattleSceneName).isLoaded;
		}
	}

	public static bool RemoteLandscapesReady
	{
		get
		{
			return !active || (instance != null && instance.remoteLandscapesApplied);
		}
	}

	// ---- Lobby -----------------------------------------------------------------------------------------

	public void Host()
	{
		ResetConnection();
		isHost = true;
		if (!PrepareLocalDeck())
		{
			return;
		}
		net = new VersusNet();
		net.Host(PortFromEnvironment());
		stage = Stage.Hosting;
		StatusText = "Esperando a que el rival se una…";
		Log("hosting on " + VersusNet.LocalIPv4() + ":" + net.Port);
	}

	public void Join(string code)
	{
		ResetConnection();
		isHost = false;
		if (!PrepareLocalDeck())
		{
			return;
		}
		string address = VersusNet.ResolveCode(code);
		if (address == null)
		{
			StatusText = "Escribe el número que muestra el otro teléfono (por ejemplo 45) o la IP completa.";
			return;
		}
		net = new VersusNet();
		net.Join(address, PortFromEnvironment());
		stage = Stage.Joining;
		StatusText = "Conectando con " + address + "…";
		Log("joining " + address);
	}

	// Back to the menu: closes the connection and forgets the match.
	public void Leave()
	{
		if (net != null && net.Connected)
		{
			net.Send("BYE");
		}
		ResetConnection();
		stage = Stage.Idle;
		StatusText = null;
	}

	public void Rematch()
	{
		if (stage != Stage.Over || localRematch || net == null || !net.Connected)
		{
			return;
		}
		localRematch = true;
		net.Send("REMATCH " + game);
		TryStartRematch();
	}

	private void ResetConnection()
	{
		if (net != null)
		{
			net.Close();
		}
		net = null;
		active = false;
		VersusRandom.Clear();
		helloReceived = false;
		remoteDeck = null;
		remoteName = string.Empty;
		remoteLeft = false;
		localRematch = false;
		remoteRematch = false;
		gameQueue.Clear();
		SetInputLock(false);
	}

	private bool PrepareLocalDeck()
	{
		PlayerInfoScript info = PlayerInfoScript.GetInstance();
		Deck deck = (info != null) ? info.GetSelectedDeckCopy() : null;
		if (deck == null)
		{
			StatusText = "No se encontró tu mazo.";
			return false;
		}
		if (deck.Leader == null)
		{
			deck.Leader = LeaderManager.Instance.CreateLeader("Leader_Finn", 1);
		}
		int min = ParametersManager.Instance.Min_Cards_In_Deck;
		if (deck.CardCount() < min)
		{
			StatusText = "Tu mazo tiene " + deck.CardCount() + " cartas; hacen falta al menos " + min + ".";
			return false;
		}
		if (Environment.GetEnvironmentVariable("CW_VERSUS_DECKVAR") == "1")
		{
			VaryDeckForTest(deck);
		}
		localDeck = deck;
		return true;
	}

	// Test runs on one Mac share a save, so both sides would bring the same deck and leader: this makes this
	// side's differ (cards in reverse order, another leader at a higher rank).
	private static void VaryDeckForTest(Deck deck)
	{
		List<CardItem> cards = new List<CardItem>(deck.GetCards());
		cards.Reverse();
		deck.Empty();
		foreach (CardItem card in cards)
		{
			deck.AddCard(card);
		}
		string current = deck.Leader.Form.ID;
		foreach (string id in new string[3] { "Leader_Finn", "Leader_Marceline", "Leader_PrincessBubblegum" })
		{
			if (id != current && LeaderManager.Instance.leaderForms.ContainsKey(id))
			{
				deck.Leader = LeaderManager.Instance.CreateLeader(id, deck.Leader.Rank + 3);
				break;
			}
		}
	}

	private static int PortFromEnvironment()
	{
		int port;
		if (int.TryParse(Environment.GetEnvironmentVariable("CW_VERSUS_PORT"), out port) && port > 0)
		{
			return port;
		}
		return VersusNet.DefaultPort;
	}

	// ---- Frame update and messages ---------------------------------------------------------------------

	private void Awake()
	{
		if (instance != null && instance != this)
		{
			Destroy(gameObject);
			return;
		}
		instance = this;
		useGUILayout = false;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	// Leaving the battle any other way (pause menu, results) ends the match: the menus must behave as usual.
	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (active && scene.name == BattleSceneName)
		{
			battleLoaded = true;
		}
		if (active && scene.name != BattleSceneName && scene.name != "LoadingScreen")
		{
			Log("left the battle for " + scene.name);
			Leave();
		}
	}

	private void Update()
	{
		if (net == null)
		{
			return;
		}
		if (net.Connected && (stage == Stage.Hosting || stage == Stage.Joining))
		{
			OnConnected();
		}
		string line;
		while (net.Receive(out line))
		{
			lastHeard = Time.realtimeSinceStartup;
			Handle(line);
		}
		if (net.Connected)
		{
			if (Time.realtimeSinceStartup - lastPing > PingInterval)
			{
				lastPing = Time.realtimeSinceStartup;
				net.Send("PING");
			}
			if (Time.realtimeSinceStartup - lastHeard > PeerTimeout)
			{
				net.Fail("El otro teléfono dejó de responder.");
			}
		}
		if (net.Closed && (stage == Stage.Hosting || stage == Stage.Joining))
		{
			StatusText = net.Error ?? "Se cerró la conexión.";
		}
		if (active && battleLoaded && InBattleScene)
		{
			BattleUpdate();
		}
		bool modal = active && (stage == Stage.Over || (net.Closed && stage == Stage.Battle));
		SetInputLock(modal);
	}

	private void OnConnected()
	{
		stage = Stage.Lobby;
		lastHeard = Time.realtimeSinceStartup;
		StatusText = "Conectado. Preparando la partida…";
		Log("connected");
		net.Send("HELLO " + ProtocolVersion + " " + Esc(SystemInfo.deviceName));
		net.Send("DECK " + SerializeDeck(localDeck));
	}

	private void Handle(string line)
	{
		string[] parts = line.Split(' ');
		switch (parts[0])
		{
		case "PING":
			break;
		case "HELLO":
			helloReceived = true;
			if (parts.Length > 2)
			{
				remoteName = Unesc(parts[2]);
			}
			if (parts.Length < 2 || parts[1] != ProtocolVersion.ToString())
			{
				net.Fail("El otro teléfono tiene otra versión del juego.");
			}
			break;
		case "DECK":
			remoteDeck = ParseDeck(parts);
			if (isHost && stage == Stage.Lobby && remoteDeck != null && helloReceived)
			{
				StartGame();
			}
			break;
		case "START":
			if (!isHost && parts.Length >= 4)
			{
				game = ParseInt(parts[1]);
				seed = ParseInt(parts[2]);
				firstSide = ParseInt(parts[3]);
				Launch();
			}
			break;
		case "G":
			EnqueueGameMessage(parts, line);
			break;
		case "HASH":
			if (parts.Length >= 4 && ParseInt(parts[1]) == game)
			{
				int turn = ParseInt(parts[2]);
				remoteStates[turn] = Unesc(parts[3]);
				CompareStates(turn);
			}
			break;
		case "REMATCH":
			if (parts.Length >= 2 && ParseInt(parts[1]) == game)
			{
				remoteRematch = true;
				TryStartRematch();
			}
			break;
		case "BYE":
			remoteLeft = true;
			net.Fail("El rival salió de la partida.");
			break;
		default:
			Log("unknown message: " + line);
			break;
		}
	}

	private void StartGame()
	{
		System.Random rng = new System.Random(Environment.TickCount ^ GetHashCode());
		game++;
		seed = rng.Next();
		// The loser of the last game starts the rematch; the first game is a coin toss.
		if (game == 1)
		{
			firstSide = rng.Next(2);
		}
		else
		{
			firstSide = localWon ? 1 : 0;
		}
		net.Send("START " + game + " " + seed + " " + firstSide);
		Launch();
	}

	private void TryStartRematch()
	{
		if (isHost && localRematch && remoteRematch && net != null && net.Connected)
		{
			StartGame();
		}
	}

	private void Launch()
	{
		Log("launch game " + game + " seed " + seed + " first side " + firstSide + (isHost ? " (host, A)" : " (guest, B)"));
		gameQueue.Clear();
		pendingTarget = null;
		pendingPick = null;
		remoteLandscapesApplied = false;
		landscapesSent = false;
		lastHashedTurn = 0;
		localStates.Clear();
		remoteStates.Clear();
		protocolError = null;
		localRematch = false;
		remoteRematch = false;
		gameOverAt = -1f;
		waitingSince = -1f;
		battleLoaded = false;
		VersusRandom.Seed(seed);
		active = true;
		stage = Stage.Battle;
		StatusText = null;
		QuestData quest = QuestManager.Instance.GetQuest(QuestId);
		Deck mine = localDeck.Clone();
		Deck rival = BuildDeck(remoteDeck);
		GameState gameState = GameState.Instance;
		gameState.ResetFromQuestData(quest, mine, rival);
		gameState.BattleResolver = null;
		Application.runInBackground = true;
		SLOTGameSingleton<SLOTSceneManager>.GetInstance().LoadLevel("LoadingScreen");
	}

	// ---- Decks -----------------------------------------------------------------------------------------

	private static string SerializeDeck(Deck deck)
	{
		StringBuilder lands = new StringBuilder();
		for (int i = 0; i < deck.GetLandscapeCount(); i++)
		{
			if (i > 0)
			{
				lands.Append(',');
			}
			lands.Append(deck.GetLandscape(i).ToString());
		}
		StringBuilder cards = new StringBuilder();
		List<CardItem> list = deck.GetCards();
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0)
			{
				cards.Append(',');
			}
			cards.Append(Esc(list[i].Form.ID)).Append(':').Append(list[i].Level);
		}
		return Esc(deck.Leader.Form.ID) + " " + deck.Leader.Rank + " " + lands + " " + cards + " " + Esc(deck.Name ?? "Mazo");
	}

	private DeckSpec ParseDeck(string[] parts)
	{
		if (parts.Length < 6)
		{
			net.Fail("El mazo del rival llegó incompleto.");
			return null;
		}
		return new DeckSpec
		{
			LeaderId = Unesc(parts[1]),
			LeaderRank = ParseInt(parts[2]),
			Landscapes = parts[3].Split(','),
			Cards = parts[4].Split(','),
			Name = Unesc(parts[5])
		};
	}

	private static Deck BuildDeck(DeckSpec spec)
	{
		Deck deck = new Deck();
		deck.Name = spec.Name;
		deck.Leader = LeaderManager.Instance.CreateLeader(spec.LeaderId, Mathf.Max(1, spec.LeaderRank));
		foreach (string entry in spec.Cards)
		{
			int colon = entry.LastIndexOf(':');
			string id = Unesc((colon < 0) ? entry : entry.Substring(0, colon));
			int level = (colon < 0) ? 1 : ParseInt(entry.Substring(colon + 1));
			CardForm form = CardDataManager.Instance.GetCard(id);
			if (form == null)
			{
				UnityEngine.Debug.LogError("Versus: unknown card " + id + " in the rival's deck");
				continue;
			}
			deck.AddCard(new CardItem(form, Mathf.Max(1, level), false));
		}
		foreach (string land in spec.Landscapes)
		{
			if (land.Length > 0 && deck.GetLandscapeCount() < 4)
			{
				deck.AddLandscape((LandscapeType)Enum.Parse(typeof(LandscapeType), land));
			}
		}
		return deck;
	}

	// ---- Game messages (ordered: they replay the rival's turn) -----------------------------------------

	private void SendGame(string kind, params object[] args)
	{
		if (!active || net == null)
		{
			return;
		}
		StringBuilder sb = new StringBuilder();
		sb.Append("G ").Append(game).Append(' ').Append(kind).Append(' ').Append(CurrentTurn());
		foreach (object arg in args)
		{
			sb.Append(' ').Append(Convert.ToString(arg, CultureInfo.InvariantCulture));
		}
		string line = sb.ToString();
		Log("send " + line);
		net.Send(line);
	}

	private void EnqueueGameMessage(string[] parts, string line)
	{
		if (parts.Length < 4 || ParseInt(parts[1]) != game)
		{
			Log("dropped stale " + line);
			return;
		}
		string[] args = new string[parts.Length - 4];
		Array.Copy(parts, 4, args, 0, args.Length);
		Log("recv " + line);
		gameQueue.AddLast(new Msg
		{
			Kind = parts[2],
			Turn = ParseInt(parts[3]),
			Args = args,
			Raw = line
		});
	}

	private Msg Head(params string[] kinds)
	{
		if (gameQueue.Count == 0)
		{
			return null;
		}
		Msg head = gameQueue.First.Value;
		foreach (string kind in kinds)
		{
			if (head.Kind == kind)
			{
				return head;
			}
		}
		return null;
	}

	private Msg Take(params string[] kinds)
	{
		Msg head = Head(kinds);
		if (head != null)
		{
			gameQueue.RemoveFirst();
		}
		return head;
	}

	// A message that does not fit where the battle is: logged and dropped so the battle does not hang.
	private void DropUnexpected(string expected)
	{
		Msg head = gameQueue.First.Value;
		gameQueue.RemoveFirst();
		protocolError = "Se esperaba " + expected + " y llegó " + head.Kind;
		desyncFlashUntil = Time.realtimeSinceStartup + 10f;
		UnityEngine.Debug.LogError("VERSUS PROTOCOL: expected " + expected + ", got " + head.Raw);
	}

	private static int CurrentTurn()
	{
		GameDataScript data = GameDataScript.GetInstance();
		return (data != null) ? data.Turn : 0;
	}

	// The rival's next move for CWOpponentActionSequencer, or null while it has not arrived.
	public static RemoteAction NextRemoteAction()
	{
		if (instance == null || instance.pendingTarget != null || instance.pendingPick != null || instance.gameQueue.Count == 0)
		{
			return null;
		}
		Msg msg = instance.Take("PLAY", "FLOOP", "LEADER", "END");
		if (msg == null)
		{
			instance.DropUnexpected("una jugada");
			return null;
		}
		GameState gameState = GameState.Instance;
		RemoteAction action = new RemoteAction
		{
			Kind = msg.Kind
		};
		switch (msg.Kind)
		{
		case "PLAY":
		{
			int index = ParseInt(msg.Args[0]);
			string id = Unesc(msg.Args[1]);
			int lane = ParseInt(msg.Args[2]);
			List<CardItem> hand = gameState.GetHand(PlayerType.Opponent);
			CardItem card = (index >= 0 && index < hand.Count && hand[index].Form.ID == id) ? hand[index] : hand.Find((CardItem c) => c.Form.ID == id);
			if (card == null)
			{
				instance.ReportProblem("the rival played " + id + " (hand index " + index + ") but it is not in its hand here");
				return NextRemoteAction();
			}
			action.Card = card;
			action.Lane = (lane >= 0 && card.Form.Type != CardType.Spell) ? gameState.GetLane(PlayerType.Opponent, lane) : null;
			break;
		}
		case "FLOOP":
		{
			int lane = ParseInt(msg.Args[0]);
			if (!gameState.LaneHasCreature(PlayerType.Opponent, lane))
			{
				instance.ReportProblem("the rival flooped lane " + lane + " but there is no creature there here");
				return NextRemoteAction();
			}
			action.Lane = gameState.GetLane(PlayerType.Opponent, lane);
			action.Card = gameState.GetCreature(PlayerType.Opponent, lane).Data;
			break;
		}
		}
		instance.waitingSince = -1f;
		return action;
	}

	public static void AwaitRemoteTarget(CardScript script)
	{
		if (instance != null)
		{
			instance.pendingTarget = script;
		}
	}

	public static void AwaitRemotePick(CardScript script)
	{
		if (instance != null)
		{
			instance.pendingPick = script;
		}
	}

	// The attacker's battle ring result for this lane, once it arrives.
	public static bool TryTakeRing(int lane, out string result)
	{
		result = null;
		if (instance == null || instance.gameQueue.Count == 0 || instance.pendingTarget != null || instance.pendingPick != null)
		{
			return false;
		}
		Msg msg = instance.Take("RING");
		if (msg == null)
		{
			instance.DropUnexpected("la tirada del anillo");
			return false;
		}
		if (ParseInt(msg.Args[0]) != lane)
		{
			instance.ReportProblem("ring for lane " + msg.Args[0] + " arrived while lane " + lane + " attacks");
		}
		result = msg.Args[1];
		return true;
	}

	private void ProcessPendingChoices()
	{
		if (pendingTarget != null && Head("TARGET") != null)
		{
			Msg msg = Take("TARGET");
			CardScript script = pendingTarget;
			pendingTarget = null;
			int index = ParseInt(msg.Args[0]);
			GameState gameState = GameState.Instance;
			if (!script.SelectionFilter(gameState.GetLane(gameState.SelectionSideForVersus, index)))
			{
				ReportProblem("the rival targeted lane " + index + " which is not a valid target here");
			}
			gameState.SelectTarget(index);
		}
		else if (pendingPick != null && Head("PICK") != null)
		{
			Msg msg = Take("PICK");
			CardScript script = pendingPick;
			pendingPick = null;
			int index = ParseInt(msg.Args[0]);
			string id = Unesc(msg.Args[1]);
			List<CardItem> pile = GameState.Instance.GetDiscardPile(script.Owner);
			CardItem card = (index >= 0 && index < pile.Count && pile[index].Form.ID == id) ? pile[index] : pile.Find((CardItem c) => c.Form.ID == id);
			if (card == null)
			{
				ReportProblem("the rival picked " + id + " from its discard pile but it is not there here");
				script.CloseDiscardPile();
				return;
			}
			script.CardSelection(card);
		}
		else if ((pendingTarget != null || pendingPick != null) && gameQueue.Count > 0 && Head("TARGET", "PICK") == null)
		{
			DropUnexpected(pendingTarget != null ? "un objetivo" : "una carta del descarte");
		}
	}

	// ---- What this phone sends --------------------------------------------------------------------------

	public static void SendPlay(CardItem card, int lane)
	{
		if (active && instance != null)
		{
			int index = GameState.Instance.GetHand(PlayerType.User).IndexOf(card);
			instance.SendGame("PLAY", index, Esc(card.Form.ID), (card.Form.Type == CardType.Spell) ? -1 : lane);
		}
	}

	public static void SendFloop(int lane)
	{
		if (active && instance != null)
		{
			instance.SendGame("FLOOP", lane);
		}
	}

	public static void SendLeader()
	{
		if (active && instance != null)
		{
			instance.SendGame("LEADER");
		}
	}

	public static void SendTarget(int lane)
	{
		if (active && instance != null)
		{
			instance.SendGame("TARGET", lane);
		}
	}

	public static void SendPick(CardScript script, CardItem card)
	{
		if (active && instance != null)
		{
			int index = GameState.Instance.GetDiscardPile(script.Owner).IndexOf(card);
			instance.SendGame("PICK", index, Esc(card.Form.ID));
		}
	}

	public static void SendEnd()
	{
		if (active && instance != null)
		{
			instance.SendGame("END");
		}
	}

	public static void SendRing(int lane, string result)
	{
		if (active && instance != null)
		{
			instance.SendGame("RING", lane, result);
		}
	}

	// The local landscapes, once placed (LandscapeReadyButtonScript).
	public static void SendLandscapes()
	{
		if (!active || instance == null || instance.landscapesSent)
		{
			return;
		}
		instance.landscapesSent = true;
		GameState gameState = GameState.Instance;
		instance.SendGame("LAND", gameState.GetLandscapeType(PlayerType.User, 0), gameState.GetLandscapeType(PlayerType.User, 1), gameState.GetLandscapeType(PlayerType.User, 2), gameState.GetLandscapeType(PlayerType.User, 3));
	}

	// ---- Battle ----------------------------------------------------------------------------------------

	private void BattleUpdate()
	{
		BattlePhaseManager phaseMgr = BattlePhaseManager.GetInstance();
		GameDataScript data = GameDataScript.GetInstance();
		if (phaseMgr == null || data == null || !GameState.Instance.IsSetUp)
		{
			return;
		}
		if (!remoteLandscapesApplied && Head("LAND") != null && LandscapeManagerScript.GetInstance() != null)
		{
			ApplyRemoteLandscapes(Take("LAND"));
		}
		ProcessPendingChoices();
		BattlePhase phase = phaseMgr.Phase;
		if ((phase == BattlePhase.P1Setup || phase == BattlePhase.P2Setup) && data.Turn != lastHashedTurn)
		{
			lastHashedTurn = data.Turn;
			string state = BattleState(data.Turn);
			localStates[data.Turn] = state;
			net.Send("HASH " + game + " " + data.Turn + " " + Esc(state));
			Log("turn " + data.Turn + " hash " + Hash(state).ToString("x8"));
			CompareStates(data.Turn);
		}
		if (stage == Stage.Battle && IsGameOverPhase(phase))
		{
			if (gameOverAt < 0f)
			{
				gameOverAt = Time.realtimeSinceStartup;
				localWon = GameState.Instance.GetHealth(PlayerType.User) > 0;
				Log("game over: " + (localWon ? "won" : "lost") + ", desyncs " + desyncs.Count);
			}
			else if (Time.realtimeSinceStartup - gameOverAt > 3.5f)
			{
				stage = Stage.Over;
			}
		}
		UpdateWaitingText(phase);
	}

	private static bool IsGameOverPhase(BattlePhase phase)
	{
		switch (phase)
		{
		case BattlePhase.Result_P1Defeated:
		case BattlePhase.Result_P2Defeated:
		case BattlePhase.Result_P1Win:
		case BattlePhase.Result_P1WinPanel:
		case BattlePhase.Result_P1Lose:
		case BattlePhase.Result_P1LosePanel:
		case BattlePhase.Result_P1MPWinPanel:
		case BattlePhase.Result_P1MPLosePanel:
		case BattlePhase.Result_P1Dweeb:
		case BattlePhase.P2Win:
		case BattlePhase.P2Dweeb:
		case BattlePhase.GameOver:
		case BattlePhase.PostgameWin:
		case BattlePhase.PostgameLose:
			return true;
		default:
			return false;
		}
	}

	// In a versus match the scene's own result screens (rewards, quest progress, "continue for a gem") never
	// open: BattlePhaseManager asks this before playing a phase's tween.
	public static bool SuppressPhaseTween(BattlePhase phase)
	{
		return active && IsGameOverPhase(phase) && phase != BattlePhase.Result_P1Defeated && phase != BattlePhase.Result_P2Defeated;
	}

	private void ApplyRemoteLandscapes(Msg msg)
	{
		GameState gameState = GameState.Instance;
		for (int i = 0; i < 4 && i < msg.Args.Length; i++)
		{
			LandscapeType type = (LandscapeType)Enum.Parse(typeof(LandscapeType), msg.Args[i]);
			gameState.SetLandscape(PlayerType.Opponent, i, type);
			gameState.GetDeck(PlayerType.Opponent).SetLandscape(i, type);
		}
		LandscapeManagerScript.GetInstance().UpdateLandscapes();
		remoteLandscapesApplied = true;
		Log("rival landscapes applied");
	}

	private void UpdateWaitingText(BattlePhase phase)
	{
		string text = null;
		if (stage != Stage.Battle)
		{
			text = null;
		}
		else if (landscapesSent && !remoteLandscapesApplied)
		{
			text = "Esperando a que el rival coloque sus paisajes…";
		}
		else if (pendingTarget != null)
		{
			text = "El rival está eligiendo un objetivo…";
		}
		else if (pendingPick != null)
		{
			text = "El rival está eligiendo una carta…";
		}
		else if (phase == BattlePhase.P2Setup && gameQueue.Count == 0)
		{
			text = "Turno del rival…";
		}
		else if (phase == BattlePhase.P2Battle && gameQueue.Count == 0)
		{
			text = "El rival está atacando…";
		}
		if (text != waitingText)
		{
			waitingText = text;
			waitingSince = (text != null) ? Time.realtimeSinceStartup : -1f;
		}
	}

	// ---- State hash ------------------------------------------------------------------------------------

	// Everything the two simulations must agree on, written in canonical order (A, then B).
	public static string BattleState(int turn)
	{
		GameState gameState = GameState.Instance;
		StringBuilder sb = new StringBuilder();
		sb.Append("t").Append(turn).Append(";mp").Append(gameState.CurrentMagicPoints);
		sb.Append(";rng").Append(VersusRandom.Draws(0)).Append(',').Append(VersusRandom.Draws(1));
		for (int side = 0; side < 2; side++)
		{
			PlayerType player = PlayerOfSide(side);
			sb.Append(side == 0 ? ";A:" : ";B:");
			sb.Append("hp").Append(gameState.GetHealth(player));
			sb.Append(",m").Append(gameState.GetMagicPoints(player));
			sb.Append(",cd").Append(gameState.GetLeaderCooldown(player));
			AppendCards(sb, ",hand", gameState.GetHand(player));
			AppendCards(sb, ",deck", gameState.GetDeck(player).GetCards());
			AppendCards(sb, ",disc", gameState.GetDiscardPile(player));
			for (int lane = 0; lane < 4; lane++)
			{
				sb.Append(",L").Append(lane).Append('=').Append(gameState.GetLandscapeType(player, lane));
				if (gameState.LaneHasCreature(player, lane))
				{
					CreatureScript creature = gameState.GetCreature(player, lane);
					sb.Append("/c:").Append(creature.Data.Form.ID).Append(':').Append(creature.ATK).Append(':').Append(creature.DEF).Append(':').Append(creature.Damage);
				}
				if (gameState.LaneHasBuilding(player, lane))
				{
					sb.Append("/b:").Append(gameState.GetBuilding(player, lane).Data.Form.ID);
				}
			}
		}
		return sb.ToString();
	}

	private static void AppendCards(StringBuilder sb, string label, List<CardItem> cards)
	{
		sb.Append(label).Append('[');
		for (int i = 0; i < cards.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(' ');
			}
			sb.Append(cards[i].Form.ID);
		}
		sb.Append(']');
	}

	public static uint Hash(string text)
	{
		uint hash = 2166136261u;
		foreach (char c in text)
		{
			hash = (hash ^ c) * 16777619u;
		}
		return hash;
	}

	private void CompareStates(int turn)
	{
		string mine;
		string theirs;
		if (!localStates.TryGetValue(turn, out mine) || !remoteStates.TryGetValue(turn, out theirs))
		{
			return;
		}
		if (mine == theirs)
		{
			Log("turn " + turn + " in sync (" + Hash(mine).ToString("x8") + ")");
			return;
		}
		desyncs.Add("turno " + turn);
		desyncFlashUntil = Time.realtimeSinceStartup + 10f;
		UnityEngine.Debug.LogError("VERSUS DESYNC game " + game + " turn " + turn + "\n local: " + mine + "\nremote: " + theirs);
		Log("DESYNC turn " + turn);
	}

	private void ReportProblem(string what)
	{
		protocolError = what;
		desyncFlashUntil = Time.realtimeSinceStartup + 10f;
		UnityEngine.Debug.LogError("VERSUS: " + what);
	}

	// ---- Out of cards ----------------------------------------------------------------------------------

	// Both players follow the same rule when their deck runs out at the start of a turn (the AI's rule, made
	// symmetric): the hand and the discard pile go back into the deck, it is shuffled and 5 cards are drawn.
	public static void ReshuffleOutOfCards(PlayerType player)
	{
		GameState gameState = GameState.Instance;
		List<CardItem> hand = gameState.GetHand(player);
		while (hand.Count > 0)
		{
			CardItem card = hand[0];
			gameState.RemoveCardFromHand(player, card);
			gameState.DiscardCard(player, card);
		}
		List<CardItem> pile = gameState.GetDiscardPile(player);
		Deck deck = gameState.GetDeck(player);
		while (pile.Count > 0)
		{
			CardItem item = pile[0];
			pile.RemoveAt(0);
			deck.AddCard(item);
		}
		gameState.Reshuffle(player);
		for (int i = 0; i < 5; i++)
		{
			gameState.DrawCard(player);
		}
	}

	// ---- Screens ---------------------------------------------------------------------------------------

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

	public void BackToMenu()
	{
		Leave();
		SLOTGameSingleton<SLOTSceneManager>.GetInstance().LoadLevel("AdventureTime");
	}

	private void OnGUI()
	{
		if (!active)
		{
			return;
		}
		VersusGui.Begin();
		if (stage == Stage.Battle && net != null && net.Closed)
		{
			string title = remoteLeft ? "El rival salió de la partida" : "Se perdió la conexión con el rival";
			if (VersusGui.Panel(title, net.Error ?? string.Empty, "Volver al menú", null) == 1)
			{
				BackToMenu();
			}
			return;
		}
		if (stage == Stage.Over)
		{
			string title = localWon ? "¡Ganaste!" : "Perdiste";
			string body;
			string second = "Revancha";
			if (net == null || !net.Connected)
			{
				body = remoteLeft ? "El rival salió." : "Ya no hay conexión con el rival.";
				second = null;
			}
			else if (localRematch)
			{
				body = remoteRematch ? "Empezando la revancha…" : "Esperando a que el rival acepte la revancha…";
				second = null;
			}
			else
			{
				body = remoteRematch ? "El rival quiere la revancha." : "Contra " + remoteName + ".";
			}
			if (desyncs.Count > 0)
			{
				body += "\nAviso: hubo " + desyncs.Count + " desincronización(es); revisa el registro.";
			}
			int pressed = VersusGui.Panel(title, body, "Volver al menú", second);
			if (pressed == 1)
			{
				BackToMenu();
			}
			else if (pressed == 2)
			{
				Rematch();
			}
			return;
		}
		if (Time.realtimeSinceStartup < desyncFlashUntil)
		{
			string text = (desyncs.Count > 0) ? ("Aviso: los dos teléfonos ya no ven la misma partida (" + desyncs[desyncs.Count - 1] + ").") : ("Aviso: " + protocolError);
			VersusGui.Banner(text, true);
		}
		else if (waitingText != null && Time.realtimeSinceStartup - waitingSince > 0.8f)
		{
			VersusGui.Banner(waitingText, false);
		}
	}

	// Coming back from the background: the silence was ours, not the rival's.
	private void OnApplicationPause(bool paused)
	{
		if (!paused)
		{
			lastHeard = Time.realtimeSinceStartup;
			lastPing = 0f;
		}
	}

	private void OnApplicationQuit()
	{
		if (net != null)
		{
			net.Send("BYE");
			net.Close();
		}
	}

	// ---- Helpers ---------------------------------------------------------------------------------------

	private void Log(string text)
	{
		UnityEngine.Debug.Log("Versus: " + text);
		if (OnLog != null)
		{
			OnLog(text);
		}
	}

	public static string Esc(string text)
	{
		return Uri.EscapeDataString(text ?? string.Empty);
	}

	public static string Unesc(string text)
	{
		return Uri.UnescapeDataString(text ?? string.Empty);
	}

	private static int ParseInt(string text)
	{
		int value;
		int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		return value;
	}
}
