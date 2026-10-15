using System.Text.Json;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// D1b: a scenario written as data (a file of the content pack, e.g. game/content/scenarios/living-village.json) turned
/// into the same calls a C# scenario makes on <see cref="ScenarioBuilder"/>, so both are checked by the same rules.
/// Times of day are "HH:MM"; squares are [x, y]. A value of the wrong shape is an InvalidDataException naming it.
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
        var r = new Reader($"scenario '{id}'");
        var b = new ScenarioBuilder(pack);
        if (r.OptInt(s, "seed") is { } seed)
            b.WithSeed((ulong)seed);
        foreach (var a in r.Objects(s, "areas"))
            b.AddArea(r.Text(a, "id"), r.Text(a, "name"));
        foreach (var l in r.Objects(s, "locations"))
            b.AddLocation(r.Text(l, "id"), r.Text(l, "name"), r.Text(l, "area"), r.OptBool(l, "lit") ?? false);
        foreach (var route in r.Objects(s, "routes"))
            b.AddRoute(r.Text(route, "from"), r.Text(route, "to"), Duration.FromMinutes(r.Int(route, "minutes")));
        foreach (var m in r.Objects(s, "maps"))
        {
            var area = r.Text(m, "area");
            var zones = r.Object(m, "zones").EnumerateObject()
                .ToDictionary(z => z.Name.Length == 1 ? z.Name[0] : throw r.Error($"zona '{z.Name}' non è un carattere"),
                    z => r.String(z.Value, $"zona '{z.Name}'"));
            b.AddMap(area, r.Values(m, "rows").Select(row => r.String(row, "riga della mappa")).ToArray(), zones);
            foreach (var e in r.Objects(m, "exits"))
                b.AddExit(area, r.Text(e, "location"), r.Square(e, "at"));
            foreach (var p in r.Objects(m, "posts"))
                b.AddPost(area, r.Text(p, "location"),
                    Enum.TryParse<PostKind>(r.Text(p, "kind"), out var kind) && Enum.IsDefined(kind) ? kind
                        : throw r.Error("tipo di posto sconosciuto"), r.Square(p, "at"));
            foreach (var l in r.Objects(m, "lights"))
                b.AddLight(area, r.Square(l, "at"), r.Int(l, "bright"), r.Int(l, "dim"));
            foreach (var indoor in r.Values(m, "interiors"))
                b.AddInterior(area, r.String(indoor, "zona interna"));
            foreach (var edge in r.Objects(m, "edges"))
            {
                var name = r.Text(edge, "kind");
                if (!Enum.TryParse<MapEdgeKind>(name, out var kind) || !Enum.IsDefined(kind))
                    throw r.Error($"tipo di confine sconosciuto '{name}'");
                b.AddEdge(area, r.Square(edge, "a"), r.Square(edge, "b"), kind,
                    edge.TryGetProperty("at", out _) ? r.Square(edge, "at") : null,
                    r.OptBool(edge, "open") ?? false, r.LockOf(edge));
            }
            foreach (var d in r.Objects(m, "doors"))
                b.AddDoor(area, r.Square(d, "at"), r.Bool(d, "open"), r.LockOf(d));
            foreach (var g in r.Objects(m, "ground")) // P4a
                b.AddGroundItem(area, r.Square(g, "at"), r.Text(g, "item"), r.OptInt(g, "count") ?? 1);
        }
        foreach (var f in r.Objects(s, "factions"))
        {
            var fid = r.Text(f, "id");
            var name = r.Text(f, "name");
            if (r.OptObject(f, "raids") is { } raids)
                b.AddRaidingFaction(fid, name, r.OptText(f, "homeStore") ?? throw r.Error("chi razzia ha un deposito"),
                    r.Int(f, "dailyUpkeep"), r.TimeOfDay(r.Text(f, "upkeepAt")),
                    r.Int(raids, "threshold"), r.Int(raids, "amount"), Duration.FromHours(r.Int(raids, "everyHours")));
            else
                b.AddFaction(fid, name, r.OptText(f, "homeStore"), r.OptInt(f, "dailyUpkeep") ?? 0,
                    r.OptText(f, "upkeepAt") is { } at ? r.TimeOfDay(at) : default, r.OptText(f, "authority"));
        }
        foreach (var pair in r.Values(s, "enmities"))
        {
            var two = r.Array(pair, "inimicizia").Select(x => r.String(x, "inimicizia")).ToArray();
            if (two.Length != 2)
                throw r.Error("un'inimicizia lega due fazioni");
            b.AddEnmity(two[0], two[1]);
        }
        foreach (var st in r.Objects(s, "stores"))
            b.AddStore(r.Text(st, "id"), r.Text(st, "name"), r.Text(st, "location"), r.Int(st, "food"), r.OptText(st, "owner"),
                st.TryGetProperty("at", out _) ? r.Square(st, "at") : null,
                st.TryGetProperty("access", out _) ? r.Values(st, "access").Select(x => r.Pair(x)).ToArray() : null);
        foreach (var a in r.Objects(s, "actors"))
        {
            var work = r.OptObject(a, "work");
            b.AddActor(r.Text(a, "id"), r.Text(a, "name"), r.Text(a, "location"),
                isPlayer: r.OptBool(a, "player") ?? false,
                food: r.OptInt(a, "food") ?? 0,
                factionId: r.OptText(a, "faction"),
                workLocationId: work is { } wk ? r.Text(wk, "location") : null,
                shiftStart: work is { } ws ? r.TimeOfDay(r.Text(ws, "from")) : default,
                shiftEnd: work is { } we ? r.TimeOfDay(r.Text(we, "to")) : default,
                at: a.TryGetProperty("at", out _) ? r.Square(a, "at") : null,
                torches: r.OptInt(a, "torches") ?? 0,
                build: r.OptText(a, "build"),
                items: a.TryGetProperty("items", out _)
                    ? r.Values(a, "items").Select(i => r.Array(i, "oggetto") is [var item, var count]
                        ? (r.String(item, "oggetto"), r.Number(count, "quantità"))
                        : throw r.Error("un oggetto è [id, quantità]")).ToArray()
                    : null,
                coins: r.OptInt(a, "coins"));
        }
        return b.Build();
    }

    /// <summary>Typed access to the JSON of one scenario: every wrong shape becomes a readable InvalidDataException.</summary>
    private sealed class Reader(string where)
    {
        public InvalidDataException Error(string what) => new($"{where}: {what}");

        public JsonElement Field(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v : throw Error($"manca '{name}'");

        public IEnumerable<JsonElement> Values(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? Array(v, name) : Enumerable.Empty<JsonElement>();

        public IEnumerable<JsonElement> Objects(JsonElement e, string name) =>
            Values(e, name).Select(v => v.ValueKind == JsonValueKind.Object ? v : throw Error($"'{name}' contiene qualcosa che non è un oggetto"));

        public JsonElement[] Array(JsonElement e, string what) =>
            e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : throw Error($"'{what}' non è un elenco");

        public JsonElement Object(JsonElement e, string name) =>
            Field(e, name) is { ValueKind: JsonValueKind.Object } o ? o : throw Error($"'{name}' non è un oggetto");

        public JsonElement? OptObject(JsonElement e, string name) =>
            e.TryGetProperty(name, out _) ? Object(e, name) : null;

        public string String(JsonElement e, string what) =>
            e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw Error($"'{what}' non è un testo");

        public string Text(JsonElement e, string name) => String(Field(e, name), name);

        /// <summary>
        /// P4a: { "lock": { "key": "farmhouse-key", "locked": true, "stuck": false, "forceDc": 15, "clue": "..." } } on a
        /// door or edge; null without one.
        /// </summary>
        public LockSpec? LockOf(JsonElement e) => e.TryGetProperty("lock", out var l)
            ? new LockSpec(OptText(l, "key"), OptBool(l, "locked") ?? false, OptBool(l, "stuck") ?? false, OptInt(l, "forceDc") ?? 15, OptText(l, "clue"))
            : null;

        public string? OptText(JsonElement e, string name) => e.TryGetProperty(name, out _) ? Text(e, name) : null;

        public int Number(JsonElement e, string what) =>
            e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : throw Error($"'{what}' non è un numero intero");

        public int Int(JsonElement e, string name) => Number(Field(e, name), name);

        public int? OptInt(JsonElement e, string name) => e.TryGetProperty(name, out _) ? Int(e, name) : null;

        public bool Bool(JsonElement e, string name) => Field(e, name).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Error($"'{name}' non è vero o falso"),
        };

        public bool? OptBool(JsonElement e, string name) => e.TryGetProperty(name, out _) ? Bool(e, name) : null;

        public (int X, int Y) Square(JsonElement e, string name) => Pair(Field(e, name));

        public (int X, int Y) Pair(JsonElement e) =>
            e.ValueKind == JsonValueKind.Array && e.EnumerateArray().ToArray() is [var x, var y]
                ? (Number(x, "x"), Number(y, "y"))
                : throw Error("una casella è [x, y]");

        public Duration TimeOfDay(string text) =>
            TimeSpan.TryParseExact(text, @"hh\:mm", null, out var t) ? Duration.FromMinutes((int)t.TotalMinutes)
                : throw Error($"ora non valida '{text}' (HH:MM)");
    }
}
