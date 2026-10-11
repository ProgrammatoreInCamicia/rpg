using System.Text.Json;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// D1b: a scenario written as data (a file of the content pack, e.g. game/content/scenarios/living-village.json) turned
/// into the same calls a C# scenario makes on <see cref="ScenarioBuilder"/>, so both are checked by the same rules.
/// Times of day are "HH:MM"; squares are [x, y].
/// </summary>
public static class ScenarioFile
{
    /// <summary>The ids of the scenarios a pack offers.</summary>
    public static IReadOnlyList<string> Ids(ContentPack? pack = null) => (pack ?? ContentPack.Default).Scenarios.Keys.Order().ToArray();

    public static Scenario Load(string id, ContentPack? pack = null)
    {
        pack ??= ContentPack.Default;
        if (!pack.Scenarios.TryGetValue(id, out var s))
            throw new InvalidDataException($"scenario sconosciuto '{id}'");
        var where = $"scenario '{id}'";
        var b = new ScenarioBuilder(pack);
        if (s.TryGetProperty("seed", out var seed))
            b.WithSeed(seed.GetUInt64());
        foreach (var a in List(s, "areas"))
            b.AddArea(Text(a, "id", where), Text(a, "name", where));
        foreach (var l in List(s, "locations"))
            b.AddLocation(Text(l, "id", where), Text(l, "name", where), Text(l, "area", where), l.TryGetProperty("lit", out var lit) && lit.GetBoolean());
        foreach (var r in List(s, "routes"))
            b.AddRoute(Text(r, "from", where), Text(r, "to", where), Duration.FromMinutes(Field(r, "minutes", where).GetInt32()));
        foreach (var m in List(s, "maps"))
        {
            var area = Text(m, "area", where);
            var zones = Field(m, "zones", where).EnumerateObject()
                .ToDictionary(z => z.Name.Length == 1 ? z.Name[0] : throw new InvalidDataException($"{where}: zona '{z.Name}' non è un carattere"),
                    z => z.Value.GetString() ?? "");
            b.AddMap(area, List(m, "rows").Select(r => r.GetString() ?? "").ToArray(), zones);
            foreach (var e in List(m, "exits"))
                b.AddExit(area, Text(e, "location", where), Square(e, "at", where));
            foreach (var p in List(m, "posts"))
                b.AddPost(area, Text(p, "location", where),
                    Enum.TryParse<PostKind>(Text(p, "kind", where), out var kind) && Enum.IsDefined(kind) ? kind
                        : throw new InvalidDataException($"{where}: tipo di posto sconosciuto"), Square(p, "at", where));
            foreach (var l in List(m, "lights"))
                b.AddLight(area, Square(l, "at", where), Field(l, "bright", where).GetInt32(), Field(l, "dim", where).GetInt32());
            foreach (var d in List(m, "doors"))
                b.AddDoor(area, Square(d, "at", where), Field(d, "open", where).GetBoolean());
        }
        foreach (var f in List(s, "factions"))
        {
            var fid = Text(f, "id", where);
            var name = Text(f, "name", where);
            string? Opt(string field) => f.TryGetProperty(field, out var v) ? v.GetString() : null;
            if (f.TryGetProperty("raids", out var raids))
                b.AddRaidingFaction(fid, name, Opt("homeStore") ?? throw new InvalidDataException($"{where}: chi razzia ha un deposito"),
                    Field(f, "dailyUpkeep", where).GetInt32(), TimeOfDay(Text(f, "upkeepAt", where), where),
                    Field(raids, "threshold", where).GetInt32(), Field(raids, "amount", where).GetInt32(),
                    Duration.FromHours(Field(raids, "everyHours", where).GetInt32()));
            else
                b.AddFaction(fid, name, Opt("homeStore"), f.TryGetProperty("dailyUpkeep", out var up) ? up.GetInt32() : 0,
                    f.TryGetProperty("upkeepAt", out var at) ? TimeOfDay(at.GetString() ?? "", where) : default, Opt("authority"));
        }
        foreach (var pair in List(s, "enmities"))
        {
            var two = pair.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (two.Length != 2)
                throw new InvalidDataException($"{where}: un'inimicizia lega due fazioni");
            b.AddEnmity(two[0], two[1]);
        }
        foreach (var st in List(s, "stores"))
            b.AddStore(Text(st, "id", where), Text(st, "name", where), Text(st, "location", where), Field(st, "food", where).GetInt32(),
                st.TryGetProperty("owner", out var owner) ? owner.GetString() : null,
                st.TryGetProperty("at", out _) ? Square(st, "at", where) : null,
                st.TryGetProperty("access", out var access) ? access.EnumerateArray().Select(x => Pair(x, where)).ToArray() : null);
        foreach (var a in List(s, "actors"))
        {
            JsonElement? work = a.TryGetProperty("work", out var w) ? w : null;
            b.AddActor(Text(a, "id", where), Text(a, "name", where), Text(a, "location", where),
                isPlayer: a.TryGetProperty("player", out var p) && p.GetBoolean(),
                food: a.TryGetProperty("food", out var food) ? food.GetInt32() : 0,
                factionId: a.TryGetProperty("faction", out var faction) ? faction.GetString() : null,
                workLocationId: work is { } wk ? Text(wk, "location", where) : null,
                shiftStart: work is { } ws ? TimeOfDay(Text(ws, "from", where), where) : default,
                shiftEnd: work is { } we ? TimeOfDay(Text(we, "to", where), where) : default,
                at: a.TryGetProperty("at", out _) ? Square(a, "at", where) : null,
                torches: a.TryGetProperty("torches", out var torches) ? torches.GetInt32() : 0,
                build: a.TryGetProperty("build", out var build) ? build.GetString() : null,
                items: a.TryGetProperty("items", out var items)
                    ? items.EnumerateArray().Select(i => (i[0].GetString() ?? "", i[1].GetInt32())).ToArray() : null,
                coins: a.TryGetProperty("coins", out var coins) ? coins.GetInt64() : null);
        }
        return b.Build();
    }

    private static IEnumerable<JsonElement> List(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static JsonElement Field(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) ? v : throw new InvalidDataException($"{where}: manca '{name}'");

    private static string Text(JsonElement e, string name, string where) =>
        Field(e, name, where).GetString() is { Length: > 0 } s ? s : throw new InvalidDataException($"{where}: '{name}' vuoto");

    private static (int X, int Y) Square(JsonElement e, string name, string where) => Pair(Field(e, name, where), where);

    private static (int X, int Y) Pair(JsonElement e, string where)
    {
        var xy = e.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        return xy.Length == 2 ? (xy[0], xy[1]) : throw new InvalidDataException($"{where}: una casella è [x, y]");
    }

    private static Duration TimeOfDay(string text, string where) =>
        TimeSpan.TryParseExact(text, @"hh\:mm", null, out var t) ? Duration.FromMinutes((int)t.TotalMinutes)
            : throw new InvalidDataException($"{where}: ora non valida '{text}' (HH:MM)");
}
