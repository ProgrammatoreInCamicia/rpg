using System.Text;
using System.Text.Json.Nodes;
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


    private static readonly HashSet<string> TimeFields = new()
    {
        "Now", "Due", "StartedAt", "CompletesAt", "At", "ArrivedAt", "TorchLitUntil", "SeenAt", "UpkeepTimeOfDay",
        "EvaluationInterval", "NextEvaluation", "Until", "Since", "AssignedAt", "ObservedAt", "LearnedAt", "RoundStart",
        "Seconds", "Start", "End", "From", "To",
    };

    /// <summary>
    /// P1c: rewrites a save as an older schema would have stored it: times in seconds (they must be whole), a free walk's
    /// steps in seconds and without its way. Then sets <paramref name="version"/>.
    /// </summary>
    public static void AsSchema(JsonNode save, int version)
    {
        if (version < 25)
            ToSeconds(save);
        if (version < 29) // P4a: no locks, no knowledge of them, no tools
            Strip(save, "Barriers", "KnownBarriers", "PendingRelock", "ToolProficiencies");
        save["Version"] = version;
    }

    /// <summary>Removes every property of those names, at any depth.</summary>
    private static void Strip(JsonNode node, params string[] names)
    {
        if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null)
                    Strip(item, names);
        if (node is not JsonObject obj)
            return;
        foreach (var name in names)
            obj.Remove(name);
        foreach (var (_, value) in obj.ToList())
            if (value is not null)
                Strip(value, names);
    }

    private static void ToSeconds(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null)
                    ToSeconds(item);
            return;
        }
        if (node is not JsonObject obj)
            return;
        if (obj["Ticks"] is JsonArray steps && obj.ContainsKey("Spots"))
        {
            obj.Remove("Ticks");
            obj.Remove("Vertices");
            obj["Seconds"] = new JsonArray(steps.Select(t => (JsonNode?)JsonValue.Create(Whole((long)t!))).ToArray());
        }
        foreach (var (name, value) in obj.ToList())
        {
            if (value is JsonValue v && TimeFields.Contains(name) && v.TryGetValue<long>(out var ticks))
                obj[name] = Whole(ticks);
            else if (value is not null)
                ToSeconds(value);
        }
    }

    private static long Whole(long ticks) =>
        ticks % GameTime.TicksPerSecond == 0 ? ticks / GameTime.TicksPerSecond
            : throw new InvalidOperationException($"{ticks} ticks are no whole second: an older save could not hold it.");
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
