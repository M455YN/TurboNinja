using Xunit;
using TurboNinja.Core;

public class SaveStoreTests
{
    static WorldState Sample() => new()
    {
        Checkpoint = "cp-2",
        Players = { ["host"] = new PlayerState { X = 3.5f, Y = -1, Hp = 70 } },
        Flags = { ["door-1"] = true },
        Counters = { ["coins"] = 12 },
        Boss = new BossState { Hp = 900, Phase = 2 },
    };

    [Fact]
    public void RoundTripKeepsEverything()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        SaveStore.Save(Sample(), path);
        var s = SaveStore.Load(path);
        File.Delete(path);

        Assert.Equal("cp-2", s.Checkpoint);
        Assert.Equal(70, s.Players["host"].Hp);
        Assert.True(s.Flags["door-1"]);
        Assert.Equal(12, s.Counters["coins"]);
        Assert.Equal(2, s.Boss!.Phase);
    }

    [Fact]
    public void NewerVersionIsRejected() =>
        Assert.Throws<InvalidDataException>(() => SaveStore.Deserialize("{\"Version\":99}"));
}
