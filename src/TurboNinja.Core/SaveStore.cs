using System.Text.Json;

namespace TurboNinja.Core;

public static class SaveStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(WorldState s) => JsonSerializer.Serialize(s, Options);

    public static WorldState Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize<WorldState>(json) ?? throw new InvalidDataException("Pusty zapis.");
        if (s.Version > WorldState.CurrentVersion)
            throw new InvalidDataException($"Zapis z nowszej wersji gry ({s.Version}).");
        // shortcut: brak migracji, dodaj tu kroki "if (s.Version < 2) ..." gdy zmienisz format
        return s;
    }

    // Zapis do pliku tymczasowego i podmiana, zeby przerwany zapis nie zniszczyl poprzedniego.
    public static void Save(WorldState s, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(s));
        File.Move(tmp, path, overwrite: true);
    }

    public static WorldState Load(string path) => Deserialize(File.ReadAllText(path));
}
