namespace TurboNinja.Core;

// Jedyne zrodlo prawdy o swiecie: ten sam obiekt idzie do pliku zapisu i do gracza dolaczajacego w trakcie gry.
// Nowy element gry = nowe pole tutaj (albo wpis w Flags/Counters), a zapis i sync dostaja go za darmo.
public sealed class WorldState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string LevelId { get; set; } = "level-1";
    public string? Checkpoint { get; set; }
    public double TimeSeconds { get; set; }

    public Dictionary<string, PlayerState> Players { get; set; } = new();
    // Bramy, przyciski, pokonani wrogowie, zebrane przedmioty: klucz = stabilne id obiektu w poziomie.
    public Dictionary<string, bool> Flags { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public BossState? Boss { get; set; }
}

public sealed class PlayerState
{
    public float X { get; set; }
    public float Y { get; set; }
    public int Hp { get; set; } = 100;
}

public sealed class BossState
{
    public int Hp { get; set; }
    public int Phase { get; set; }
}
