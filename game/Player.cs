using Godot;

namespace TurboNinja;

// Kazdy peer steruje tylko swoim graczem (authority) i rozsyla jego pozycje; pozostali go tylko wyswietlaja.
public partial class Player : CharacterBody2D
{
	const float Speed = 320, JumpVelocity = -620, Gravity = 1600;

	public long PeerId { get; private set; }
	bool IsLocal => IsMultiplayerAuthority();

	public static Player Create(long peerId, Vector2 position)
	{
		var p = new Player
		{
			PeerId = peerId,
			Name = peerId.ToString(),
			Position = position,
			CollisionLayer = 2, // gracze nie blokuja sie nawzajem, tylko platformy (warstwa 1)
			CollisionMask = 1,
		};
		p.SetMultiplayerAuthority((int)peerId);
		p.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(32, 48) } });
		p.AddChild(new ColorRect { Size = new Vector2(32, 48), Position = new Vector2(-16, -24) });
		return p;
	}

	public override void _Ready()
	{
		// Lokalny gracz czarny z kamera, zdalny szary.
		GetChild<ColorRect>(1).Color = IsLocal ? Colors.Black : new Color(0.55f, 0.55f, 0.6f);
		if (IsLocal) AddChild(new Camera2D());
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsLocal) return;

		var v = Velocity;
		if (!IsOnFloor()) v.Y += Gravity * (float)delta;
		if (IsOnFloor() && (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Up)))
			v.Y = JumpVelocity;

		var dir = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0)
				- (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0);
		v.X = dir * Speed;

		Velocity = v;
		MoveAndSlide();

		if (Multiplayer.GetPeers().Length > 0) Rpc(nameof(SyncPosition), Position);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	void SyncPosition(Vector2 position) => Position = position;
}
