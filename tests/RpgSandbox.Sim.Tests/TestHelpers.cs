using System.Text;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Tests;

internal static class TestHelpers
{
    public static GameTime At(int day, int hour, int minute = 0) =>
        new(((day - 1) * 24L + hour) * 3600 + minute * 60L);

    /// <summary>Advances to an absolute instant.</summary>
    public static void AdvanceTo(this SimulationSession s, GameTime instant) =>
        s.Advance(instant.Since(s.Now));

    public static string SaveToString(this SimulationSession s)
    {
        using var stream = new MemoryStream();
        s.Save(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static LoadResult LoadFromString(string json) =>
        SimulationSession.TryLoad(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    public static SimulationSession SaveAndReload(this SimulationSession s)
    {
        var result = LoadFromString(s.SaveToString());
        Assert.True(result.Success, result.Error);
        return result.Session!;
    }

    public static ActorView Actor(this WorldView view, ActorId id) => view.Actors.Single(a => a.Id == id);
    public static StoreView Store(this WorldView view, StoreId id) => view.Stores.Single(s => s.Id == id);
    public static FactionView Faction(this WorldView view, FactionId id) => view.Factions.Single(f => f.Id == id);
    public static int TotalFood(this WorldView view) => view.Actors.Sum(a => a.Food) + view.Stores.Sum(s => s.Food);
}
