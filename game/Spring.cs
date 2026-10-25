using Godot;

namespace TurboNinja;

// Sprezyna: podrzuca gracza, ktory na nia wskoczy. Dziala tylko u wlasciciela postaci (kazdy peer steruje swoim graczem).
public partial class Spring : Area2D
{
	[Export] public float Force { get; set; } = 850;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 2; // gracze
		BodyEntered += b =>
		{
			if (b is Player p && p.IsMultiplayerAuthority()) p.Velocity = new Vector2(p.Velocity.X, -Force);
		};
	}
}
