using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TurboNinja.Core;

namespace TurboNinja;

// Single player to host bez klientow. Kolega dolacza w dowolnej chwili: dostaje caly WorldState (Welcome) i liste graczy.
// Uruchomienie klienta: godot --path game -- --join=127.0.0.1
public partial class Game : Node2D
{
	const int DefaultPort = 7777;
	const string SavePath = "user://save.json";
	const string GateFlag = "gate-1";
	Vector2 _spawn;

	// Kolejnosc ma znaczenie: checkpoint mozna tylko przesunac do przodu. Kolejnosc bierze sie z kolejnosci wezlow w scenie poziomu.
	readonly List<(string Id, Vector2 Pos, ColorRect Flag)> Checkpoints = new();

	// Plyta naciskowa: otwiera brame, gdy stoi na niej gracz albo klon. Bramy i plyty ida przez Flags w WorldState.
	Rect2 PlateZone;

	WorldState _world = new();
	readonly Dictionary<long, Player> _players = new();
	readonly Dictionary<long, Node2D> _clones = new(); // shortcut: klony nie sa w Welcome, dolaczajacy ich nie zobaczy do nastepnego E
	// Poziomy = pliki Levels/*.tscn w kolejnosci nazw; pliki zaczynajace sie od "_" (szablon) sa pomijane.
	static readonly string[] Levels = DirAccess.GetFilesAt("res://Levels")
		.Select(f => f.Replace(".remap", "")).Where(f => f.EndsWith(".tscn") && !f.StartsWith("_"))
		.Select(f => f[..^5]).Order().ToArray();
	Node? _level;
	Vector2? _exit; // brak wezla Exit = ostatni poziom
	ColorRect? _plate, _gateRect; // plyta i brama sa opcjonalne w poziomie
	CollisionShape2D? _gateShape;

	public override void _Ready()
	{
		if (Arg(OS.GetCmdlineUserArgs(), "--level=") is { } startLevel) _world.LevelId = startLevel; // do testow poziomow
		LoadLevel(_world.LevelId);

		var peer = new ENetMultiplayerPeer();
		var args = OS.GetCmdlineUserArgs();
		var join = Arg(args, "--join=");
		var port = int.TryParse(Arg(args, "--port="), out var p) ? p : DefaultPort;

		if (join is null)
		{
			var err = peer.CreateServer(port, maxClients: 3);
			if (err == Error.Ok)
			{
				Multiplayer.MultiplayerPeer = peer;
				Multiplayer.PeerConnected += OnPeerConnected;
				Multiplayer.PeerDisconnected += RemovePlayer;
			}
			else GD.PushWarning($"Serwer nie wystartowal ({err}), gram bez sieci.");
			SpawnPlayer(1, _spawn);
		}
		else
		{
			var err = peer.CreateClient(join, port);
			if (err != Error.Ok) { GD.PushError($"Nie mozna polaczyc z {join}: {err}"); GetTree().Quit(1); return; }
			Multiplayer.MultiplayerPeer = peer;
			Multiplayer.ServerDisconnected += () => { GD.Print("Host sie rozlaczyl."); GetTree().Quit(); };
			// gracz powstanie po Welcome, gdy znamy stan swiata
		}
		RefreshWorldVisuals();
	}

	static string? Arg(string[] args, string key) => args.FirstOrDefault(a => a.StartsWith(key))?[key.Length..];

	void OnPeerConnected(long id)
	{
		GD.Print($"Dolaczyl gracz {id}");
		var ids = _players.Keys.ToArray();
		foreach (var other in ids) RpcId(other == 1 ? id : other, nameof(SpawnRemote), id); // host spawnuje lokalnie nizej
		SpawnPlayer(id, _spawn);
		RpcId(id, nameof(Welcome), SaveStore.Serialize(_world), ids);
	}

	// Klient: dostaje swiat i liste obecnych graczy, potem sam siebie spawnuje na ostatnim checkpoincie.
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	void Welcome(string worldJson, long[] existing)
	{
		_world = SaveStore.Deserialize(worldJson);
		if (Arg(OS.GetCmdlineUserArgs(), "--level=") is { } startLevel) _world.LevelId = startLevel; // do testow poziomow
		LoadLevel(_world.LevelId);
		foreach (var id in existing) SpawnPlayer(id, _spawn);
		SpawnPlayer(Multiplayer.GetUniqueId(), CheckpointPos(_world.Checkpoint));
		RefreshWorldVisuals();
		GD.Print($"Dolaczono jako {Multiplayer.GetUniqueId()}, graczy: {_players.Count}, checkpoint: {_world.Checkpoint}");
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	void SpawnRemote(long id) => SpawnPlayer(id, _spawn);

	void SpawnPlayer(long id, Vector2 position)
	{
		if (_players.ContainsKey(id)) return;
		var p = Player.Create(id, position);
		_players[id] = p;
		_world.Players[id.ToString()] = new PlayerState();
		AddChild(p);
	}

	void RemovePlayer(long id)
	{
		if (!_players.Remove(id, out var p)) return;
		_world.Players.Remove(id.ToString());
		p.QueueFree();
		if (_clones.Remove(id, out var clone)) clone.QueueFree();
		GD.Print($"Gracz {id} wyszedl");
	}

	// --- Checkpointy: kazdy peer wykrywa swojego gracza, decyduje host, wynik trafia do WorldState wszystkich.

	int CheckpointIndex(string? id) => Checkpoints.FindIndex(c => c.Id == id);

	Vector2 CheckpointPos(string? id) => CheckpointIndex(id) is var i and >= 0 ? Checkpoints[i].Pos : _spawn;

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	void ReachCheckpoint(string id)
	{
		if (!Multiplayer.IsServer() || CheckpointIndex(id) <= CheckpointIndex(_world.Checkpoint)) return;
		Rpc(nameof(ApplyCheckpoint), id);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	void ApplyCheckpoint(string id)
	{
		_world.Checkpoint = id;
		RefreshWorldVisuals();
	}

	// --- Wyjscie z poziomu: host przechodzi dalej dopiero, gdy WSZYSCY gracze stoja przy wyjsciu.

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	void ReachExit()
	{
		if (!Multiplayer.IsServer() || _exit is not { } exit) return;
		var next = Array.IndexOf(Levels, _world.LevelId) + 1;
		if (next >= Levels.Length || _players.Values.Any(p => p.Position.DistanceTo(exit) >= 60)) return;
		Rpc(nameof(ApplyLevel), Levels[next]);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	void ApplyLevel(string id)
	{
		_world.LevelId = id;
		_world.Checkpoint = null;
		_world.Flags.Clear();
		LoadLevel(id);
		if (_players.TryGetValue(Multiplayer.GetUniqueId(), out var me)) { me.Position = _spawn; me.Velocity = Vector2.Zero; }
		RefreshWorldVisuals();
	}

	// --- Flagi swiata (brama itd.): zmienia tylko host, reszta dostaje kopie.

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	void ApplyFlag(string key, bool value)
	{
		_world.Flags[key] = value;
		RefreshWorldVisuals();
	}

	// Klon cienia (E): zostawia w miejscu "ciebie", ktory trzyma plyte. Dzieki niemu plyte da sie zaliczyc solo.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	void PlaceClone(Vector2 position)
	{
		long owner = Multiplayer.GetRemoteSenderId();
		if (owner == 0) owner = Multiplayer.GetUniqueId();
		if (!_clones.TryGetValue(owner, out var clone))
		{
			clone = new Node2D();
			clone.AddChild(new ColorRect { Size = new Vector2(32, 48), Position = new Vector2(-16, -24), Color = new Color(0, 0, 0, 0.35f) });
			AddChild(clone);
			_clones[owner] = clone;
		}
		clone.Position = position;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E }
			&& _players.TryGetValue(Multiplayer.GetUniqueId(), out var me))
			Rpc(nameof(PlaceClone), me.Position);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_players.TryGetValue(Multiplayer.GetUniqueId(), out var me))
		{
			if (me.Position.Y > 1000) { me.Position = CheckpointPos(_world.Checkpoint); me.Velocity = Vector2.Zero; }

			var next = Checkpoints.FirstOrDefault(c => c.Pos.DistanceTo(me.Position) < 60);
			if (next.Id is not null && CheckpointIndex(next.Id) > CheckpointIndex(_world.Checkpoint))
				Rpc(nameof(ReachCheckpoint), next.Id);

			if (_exit is { } exit && exit.DistanceTo(me.Position) < 60 && Engine.GetPhysicsFrames() % 15 == 0)
				Rpc(nameof(ReachExit));
		}

		if (!Multiplayer.IsServer()) return; // zapis i swiat nalezy do hosta

		_world.TimeSeconds += delta;
		foreach (var (id, p) in _players)
		{
			var s = _world.Players[id.ToString()];
			s.X = p.Position.X;
			s.Y = p.Position.Y;
		}

		var pressed = _plate is not null && (_players.Values.Any(p => PlateZone.HasPoint(p.Position)) || _clones.Values.Any(c => PlateZone.HasPoint(c.Position)));
		if (pressed != _world.Flags.GetValueOrDefault(GateFlag)) Rpc(nameof(ApplyFlag), GateFlag, pressed);

		if (Input.IsKeyPressed(Key.F5))
			SaveStore.Save(_world, ProjectSettings.GlobalizePath(SavePath));
		if (Input.IsKeyPressed(Key.F9) && FileAccess.FileExists(SavePath))
		{
			var s = SaveStore.Load(ProjectSettings.GlobalizePath(SavePath));
			_world.TimeSeconds = s.TimeSeconds;
			if (s.LevelId != _world.LevelId) Rpc(nameof(ApplyLevel), s.LevelId);
			if (s.Checkpoint is not null) Rpc(nameof(ApplyCheckpoint), s.Checkpoint);
			if (_players.TryGetValue(1, out var hp))
			{
				hp.Position = s.Players.TryGetValue("1", out var host) ? new Vector2(host.X, host.Y) : CheckpointPos(s.Checkpoint);
				hp.Velocity = Vector2.Zero;
			}
		}
	}

	// Wyglad swiata wynika wylacznie z WorldState, wiec dziala tak samo po zapisie, sync i dolaczeniu w trakcie gry.
	void RefreshWorldVisuals()
	{
		var open = _world.Flags.GetValueOrDefault(GateFlag);
		if (_plate is not null) _plate.Color = open ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.8f, 0.3f, 0.3f);
		if (_gateRect is not null) _gateRect.Visible = !open;
		_gateShape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, open);

		var reached = CheckpointIndex(_world.Checkpoint);
		for (var i = 0; i < Checkpoints.Count; i++)
			Checkpoints[i].Flag.Color = i <= reached ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
	}

	// Poziom to scena Levels/<id>.tscn (edytowalna w edytorze); kod tylko znajduje w niej potrzebne wezly.
	void LoadLevel(string id)
	{
		_level?.QueueFree();
		Checkpoints.Clear();
		foreach (var clone in _clones.Values) clone.QueueFree();
		_clones.Clear();

		var level = _level = GD.Load<PackedScene>($"res://Levels/{id}.tscn").Instantiate();
		AddChild(level);

		_exit = level.GetNodeOrNull<Marker2D>("Exit")?.Position;
		_spawn = level.GetNode<Marker2D>("Spawn").Position;
		_plate = level.GetNodeOrNull<ColorRect>("Plate");
		if (_plate is not null) PlateZone = new Rect2(_plate.Position.X, _plate.Position.Y - 72, _plate.Size.X, 80);
		_gateShape = level.GetNodeOrNull<CollisionShape2D>("Gate/Shape");
		_gateRect = level.GetNodeOrNull<ColorRect>("Gate/Visual");
		foreach (var cp in level.GetNodeOrNull("Checkpoints")?.GetChildren().OfType<Marker2D>() ?? Enumerable.Empty<Marker2D>())
			Checkpoints.Add((cp.Name, cp.Position, cp.GetNode<ColorRect>("Flag")));
	}
}
