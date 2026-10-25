using Godot;

namespace TurboNinja;

// Prostokatna przeszkoda (kolizja + wyglad) z jednym parametrem Size; [Tool] zeby rozmiar byl widoczny juz w edytorze.
[Tool]
public partial class Platform : StaticBody2D
{
	Vector2 _size = new(200, 20);
	bool _oneWay;
	Color _tint = new(0.25f, 0.3f, 0.4f);

	[Export] public Vector2 Size { get => _size; set { _size = value; Apply(); } }
	[Export] public bool OneWay { get => _oneWay; set { _oneWay = value; Apply(); } }
	[Export] public Color Tint { get => _tint; set { _tint = value; Apply(); } }

	public override void _Ready() => Apply();

	void Apply()
	{
		if (!IsInsideTree()) return; // setter dziala tez przy wczytywaniu sceny, zanim dzieci istnieja
		var shape = GetNodeOrNull<CollisionShape2D>("Shape");
		var visual = GetNodeOrNull<ColorRect>("Visual");
		if (shape is null || visual is null) return;
		shape.Shape = new RectangleShape2D { Size = _size };
		shape.Position = _size / 2;
		shape.OneWayCollision = _oneWay;
		visual.Size = _size;
		visual.Color = _tint;
	}
}
