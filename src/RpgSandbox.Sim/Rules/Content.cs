using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RpgSandbox.Sim.Rules;

// D1a: the game's contents (items, weapons, armor, classes, creatures, pregenerated characters) as data files, loaded and
// validated here. The engine knows no content by name: it reads a pack. The files live in game/content/ (see its
// manifest); the default pack is compiled into this assembly from those same files, so neither Godot nor the tests open
// paths, and a save carries the pack it was played with (ARCHITECTURE_DECISIONS §14, contract D1).

public enum ArmorType { Light, Medium, Heavy, Shield }

/// <summary>SRD armor: base AC, how much Dexterity counts (null: all of it), the Strength it needs, Stealth Disadvantage.</summary>
public sealed record ArmorDefinition(ArmorType Type, int Base, int? DexMax, int Strength, bool StealthDisadvantage);

/// <summary>A creature as a stat block: everything about it in one place (C1 profile and character sheet derive from it).</summary>
public sealed record CreatureDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AbilityScores Abilities { get; init; }
    public required int ProficiencyBonus { get; init; }
    public required IReadOnlyList<Skill> Skills { get; init; }
    public required int ArmorClass { get; init; }
    public required int HitPoints { get; init; }
    public required int Speed { get; init; }
    public required int InitiativeBonus { get; init; }
    public bool Flees { get; init; }

    /// <summary>C5b: the Experience Points it is worth when defeated (SRD: by its Challenge Rating, as its stat block says).</summary>
    public required int Experience { get; init; }

    /// <summary>C5a: its kind (a <see cref="KindDefinition"/> id) and what a careful look (Insight) can tell of it.</summary>
    public required string Kind { get; init; }
    public IReadOnlyList<Tell> Tells { get; init; } = Array.Empty<Tell>();

    /// <summary>P5a: tools it is proficient with, by item id (thieves' tools: + Proficiency Bonus to pick a lock).</summary>
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();

    /// <summary>Items worn or wielded (one each) and other items carried (with how many).</summary>
    public required IReadOnlyList<string> Gear { get; init; }
    public required IReadOnlyList<(string Item, int Count)> Items { get; init; }
    public long Coins { get; init; }
    public required IReadOnlyList<AttackProfile> Attacks { get; init; }
}

/// <summary>SRD creature types (they decide which knowledge the Study action draws on).</summary>
public enum CreatureType
{
    Aberration, Beast, Celestial, Construct, Dragon, Elemental, Fey, Fiend, Giant, Humanoid, Monstrosity, Ooze, Plant, Undead,
}

/// <summary>C5a: something one can make out about a creature with a high enough check (DC), told in the world's words.</summary>
public sealed record Tell(int Dc, string Text);

/// <summary>
/// C5a: a kind of creature (human, wolf, ...): what it looks like by name, its SRD type, and what one may recall about
/// the kind with the Study action (<see cref="Tells"/>).
/// </summary>
public sealed record KindDefinition(string Id, string Name, CreatureType Type, IReadOnlyList<Tell> Tells)
{
    /// <summary>SRD "Areas of Knowledge": the skill that recalls lore about creatures of this type.</summary>
    public Skill StudySkill => Type switch
    {
        CreatureType.Aberration or CreatureType.Construct or CreatureType.Elemental or CreatureType.Fey or CreatureType.Monstrosity => Skill.Arcana,
        CreatureType.Giant or CreatureType.Humanoid => Skill.History,
        CreatureType.Celestial or CreatureType.Fiend or CreatureType.Undead => Skill.Religion,
        _ => Skill.Nature, // Beasts, Dragons, Oozes, Plants
    };
}

/// <summary>A player character as built by the rules: computed into a sheet and a profile (D1a).</summary>
public sealed record CharacterDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Class { get; init; }
    public required int Level { get; init; }
    public required AbilityScores Abilities { get; init; }
    public required IReadOnlyList<Skill> Skills { get; init; }
    public required Equipment Equipment { get; init; }
    public required IReadOnlyList<(string Item, int Count)> Items { get; init; }
    public long Coins { get; init; }

    /// <summary>C5a: its kind (a <see cref="KindDefinition"/> id).</summary>
    public required string Kind { get; init; }

    /// <summary>The weapons (by item id) whose mastery the character has unlocked.</summary>
    public required IReadOnlyList<string> Masteries { get; init; }
}

/// <summary>SRD class core traits the rules use so far.</summary>
public sealed record ClassDefinition(string Id, string Name, int HitDie, IReadOnlyList<WeaponCategory> Weapons,
    IReadOnlyList<ArmorType> Armor, int Masteries);

/// <summary>The six ability scores.</summary>
public sealed record AbilityScores(int Strength, int Dexterity, int Constitution, int Intelligence, int Wisdom, int Charisma);

/// <summary>What one wears and wields (by item id): armor, shield, the weapon in hand. Each must be carried.</summary>
public sealed record Equipment(string? Armor = null, string? Shield = null, string? MainHand = null)
{
    public static readonly Equipment None = new();

    public IEnumerable<string> Items => new[] { Armor, Shield, MainHand }.OfType<string>();
}

/// <summary>A pack of contents: loaded from data files, validated, with its identity (id, version, hash) and its texts.</summary>
public sealed class ContentPack
{
    public required string Id { get; init; }
    public required string Version { get; init; }

    /// <summary>SHA-256 of the files in manifest order (name and text), for provenance and integrity.</summary>
    public required string Hash { get; init; }

    /// <summary>The files as loaded (manifest first), so a save can carry the whole pack.</summary>
    public required IReadOnlyList<(string Name, string Text)> Files { get; init; }

    public required IReadOnlyDictionary<string, ItemDefinition> Items { get; init; }
    public required IReadOnlyDictionary<string, ClassDefinition> Classes { get; init; }
    public required IReadOnlyDictionary<string, CreatureDefinition> Creatures { get; init; }
    public required IReadOnlyDictionary<string, CharacterDefinition> Characters { get; init; }

    /// <summary>C5a: kinds of creatures, by id.</summary>
    public IReadOnlyDictionary<string, KindDefinition> Kinds { get; init; } = new Dictionary<string, KindDefinition>();

    /// <summary>C5a: the kind an actor built from <paramref name="build"/> is.</summary>
    public KindDefinition KindOf(string build) =>
        Kinds[Creatures.TryGetValue(build, out var c) ? c.Kind : Characters[build].Kind];

    /// <summary>D1b: scenarios (worlds to play), by id, as read from their files; see Scenarios.ScenarioFile.</summary>
    internal IReadOnlyDictionary<string, JsonElement> Scenarios { get; init; } = new Dictionary<string, JsonElement>();

    public ItemDefinition? Item(string id) => Items.TryGetValue(id, out var item) ? item : null;

    public Weapon Weapon(string id) => Item(id)?.Weapon ?? throw new KeyNotFoundException($"nessuna arma '{id}'");

    /// <summary>A creature or a character: what an actor can be built from.</summary>
    public bool IsBuild(string id) => Creatures.ContainsKey(id) || Characters.ContainsKey(id);

    private static readonly Lazy<ContentPack> DefaultPack = new(LoadEmbedded);

    /// <summary>The game's own contents, compiled from game/content/ into this assembly.</summary>
    public static ContentPack Default => DefaultPack.Value;

    private static ContentPack LoadEmbedded()
    {
        var assembly = typeof(ContentPack).Assembly;
        var files = new Dictionary<string, string>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("content/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files[resource["content/".Length..].Replace('\\', '/')] = reader.ReadToEnd();
        }
        return Load(files);
    }

    /// <summary>
    /// Loads a pack from its files (name → JSON text; "manifest.json" lists the others, in order). Every rule broken is an
    /// InvalidDataException with a readable reason, as for saves.
    /// </summary>
    public static ContentPack Load(IReadOnlyDictionary<string, string> files)
    {
        var texts = new Dictionary<string, string>();
        foreach (var (name, text) in files)
            if (!texts.TryAdd(name.Replace('\\', '/'), text.Replace("\r\n", "\n")))
                throw new InvalidDataException($"contenuti: file ripetuto ({name})");
        if (!texts.TryGetValue("manifest.json", out var manifestText))
            throw new InvalidDataException("contenuti: manca manifest.json");
        var manifest = Parse(manifestText, "manifest.json");
        var id = Text(manifest, "id", "manifest.json");
        var version = Text(manifest, "version", "manifest.json");
        var listed = Array(manifest, "files", "manifest.json").Select(f => f.GetString() ?? "").ToList();
        if (listed.Distinct().Count() != listed.Count)
            throw new InvalidDataException("contenuti: file ripetuto nel manifest");
        foreach (var name in listed.Where(n => !texts.ContainsKey(n)))
            throw new InvalidDataException($"contenuti: manca il file {name}");
        foreach (var name in texts.Keys.Where(n => n != "manifest.json" && !listed.Contains(n)))
            throw new InvalidDataException($"contenuti: il file {name} non è nel manifest");

        var ordered = new List<(string Name, string Text)> { ("manifest.json", texts["manifest.json"]) };
        ordered.AddRange(listed.Select(n => (n, texts[n])));

        var items = new Dictionary<string, ItemDefinition>();
        var classes = new Dictionary<string, ClassDefinition>();
        var creatures = new Dictionary<string, CreatureDefinition>();
        var characters = new Dictionary<string, CharacterDefinition>();
        var kinds = new Dictionary<string, KindDefinition>();
        var documents = listed.Select(n => (Name: n, Root: Parse(texts[n], n))).ToList();
        // Items first: everything else refers to them.
        foreach (var (name, root) in documents)
            if (root.TryGetProperty("items", out var list))
                foreach (var e in list.EnumerateArray())
                    Add(items, ReadItem(e, name), name);
        foreach (var (name, root) in documents)
        {
            if (root.TryGetProperty("kinds", out var kn))
                foreach (var e in kn.EnumerateArray())
                    Add(kinds, ReadKind(e, name), name);
            if (root.TryGetProperty("classes", out var cl))
                foreach (var e in cl.EnumerateArray())
                    Add(classes, ReadClass(e, name), name);
            if (root.TryGetProperty("creatures", out var cr))
                foreach (var e in cr.EnumerateArray())
                    Add(creatures, ReadCreature(e, name, items), name);
        }
        foreach (var (name, root) in documents)
            if (root.TryGetProperty("characters", out var ch))
                foreach (var e in ch.EnumerateArray())
                    Add(characters, ReadCharacter(e, name, items, classes), name);
        var scenarios = new Dictionary<string, JsonElement>();
        foreach (var (name, root) in documents)
            if (root.TryGetProperty("scenario", out var sc) && !scenarios.TryAdd(Text(sc, "id", name), sc))
                throw new InvalidDataException($"contenuti: scenario ripetuto ({name})");
        foreach (var (build, kind) in creatures.Select(c => (c.Key, c.Value.Kind)).Concat(characters.Select(c => (c.Key, c.Value.Kind))))
            if (!kinds.ContainsKey(kind))
                throw new InvalidDataException($"contenuti: '{build}' è di un tipo sconosciuto '{kind}'");
        foreach (var dup in creatures.Keys.Intersect(characters.Keys))
            throw new InvalidDataException($"contenuti: '{dup}' è sia una creatura sia un personaggio");

        return new ContentPack
        {
            Id = id, Version = version, Hash = HashOf(ordered), Files = ordered,
            Items = items, Classes = classes, Creatures = creatures, Characters = characters, Kinds = kinds, Scenarios = scenarios,
        };
    }

    /// <summary>SHA-256 over every file, in order: its name, a newline, its text, a NUL.</summary>
    public static string HashOf(IEnumerable<(string Name, string Text)> files)
    {
        var builder = new StringBuilder();
        foreach (var (name, text) in files)
            builder.Append(name).Append('\n').Append(text).Append('\0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- readers

    private static ItemDefinition ReadItem(JsonElement e, string file)
    {
        var id = Text(e, "id", file);
        var where = $"{file}, oggetto '{id}'";
        Weapon? weapon = null;
        if (e.TryGetProperty("weapon", out var w))
        {
            var properties = Array(w, "properties", where).Aggregate(WeaponProperty.None, (all, p) => all | EnumOf<WeaponProperty>(p, where));
            weapon = new Weapon
            {
                Id = id,
                Name = Text(e, "name", where),
                Category = EnumOf<WeaponCategory>(Field(w, "category", where), where),
                Ranged = w.TryGetProperty("ranged", out var r) && r.GetBoolean(),
                Damage = DiceOf(Text(w, "damage", where), where),
                Type = EnumOf<DamageType>(Field(w, "type", where), where),
                Properties = properties,
                Mastery = EnumOf<WeaponMastery>(Field(w, "mastery", where), where),
                TwoHandedDamage = w.TryGetProperty("twoHanded", out var two) ? DiceOf(two.GetString() ?? "", where) : null,
                Range = RangeOf(w, where),
                Ammunition = w.TryGetProperty("ammunition", out var a) ? a.GetString() : null,
            };
            if ((weapon.Has(WeaponProperty.Thrown) || weapon.Has(WeaponProperty.Ammunition)) != weapon.Range is not null)
                throw new InvalidDataException($"contenuti: {where}: la gittata va data se e solo se l'arma si lancia o usa munizioni");
        }
        ArmorDefinition? armor = null;
        if (e.TryGetProperty("armor", out var ar))
        {
            var type = EnumOf<ArmorType>(Field(ar, "type", where), where);
            armor = new ArmorDefinition(type, Int(ar, "base", where),
                ar.TryGetProperty("dexMax", out var dm) ? dm.GetInt32() : type == ArmorType.Heavy ? 0 : null,
                ar.TryGetProperty("strength", out var st) ? st.GetInt32() : 0,
                ar.TryGetProperty("stealthDisadvantage", out var sd) && sd.GetBoolean());
        }
        var pounds = Field(e, "pounds", where).GetDouble();
        var cost = Field(e, "cost", where).GetInt64();
        if (pounds < 0 || cost < 0)
            throw new InvalidDataException($"contenuti: {where}: peso e costo non possono essere negativi");
        return new ItemDefinition(id, Text(e, "name", where), pounds, cost) { Weapon = weapon, Armor = armor };
    }

    private static ClassDefinition ReadClass(JsonElement e, string file)
    {
        var id = Text(e, "id", file);
        var where = $"{file}, classe '{id}'";
        return new ClassDefinition(id, Text(e, "name", where), Int(e, "hitDie", where),
            Array(e, "weapons", where).Select(x => EnumOf<WeaponCategory>(x, where)).ToArray(),
            Array(e, "armor", where).Select(x => EnumOf<ArmorType>(x, where)).ToArray(), Int(e, "masteries", where));
    }

    private static CreatureDefinition ReadCreature(JsonElement e, string file, IReadOnlyDictionary<string, ItemDefinition> items)
    {
        var id = Text(e, "id", file);
        var where = $"{file}, creatura '{id}'";
        var gear = Array(e, "gear", where).Select(x => KnownItem(x.GetString(), items, where)).ToArray();
        var carried = Stacks(e, items, where);
        var attacks = Array(e, "attacks", where).Select(a => ReadAttack(a, items, where)).ToArray();
        var creature = new CreatureDefinition
        {
            Id = id, Name = Text(e, "name", where), Abilities = AbilitiesOf(Field(e, "abilities", where), where),
            ProficiencyBonus = Int(e, "proficiency", where), Skills = Array(e, "skills", where).Select(x => EnumOf<Skill>(x, where)).ToArray(),
            ArmorClass = Int(e, "armorClass", where), HitPoints = Int(e, "hitPoints", where), Speed = Int(e, "speed", where),
            InitiativeBonus = Int(e, "initiative", where), Flees = e.TryGetProperty("flees", out var f) && f.GetBoolean(),
            Experience = Int(e, "xp", where),
            Gear = gear, Items = carried, Coins = e.TryGetProperty("coins", out var c) ? c.GetInt64() : 0, Attacks = attacks,
            Kind = Text(e, "kind", where), Tells = TellsOf(e, where),
            Tools = e.TryGetProperty("tools", out var tools) ? tools.EnumerateArray().Select(t => KnownItem(t.GetString(), items, where)).ToArray() : System.Array.Empty<string>(),
        };
        if (creature.HitPoints < 1 || creature.ArmorClass < 1 || creature.Speed < 0 || attacks.Length == 0 || creature.Experience < 0)
            throw new InvalidDataException($"contenuti: {where}: punti ferita, CA, velocità, attacchi o punti esperienza non validi");
        foreach (var needed in attacks.Select(a => a.Requires).OfType<string>().Concat(attacks.Select(a => a.Item).OfType<string>()))
            if (!gear.Contains(needed) && carried.All(i => i.Item != needed))
                throw new InvalidDataException($"contenuti: {where}: un attacco usa '{needed}', che la creatura non porta");
        return creature;
    }

    private static AttackProfile ReadAttack(JsonElement a, IReadOnlyDictionary<string, ItemDefinition> items, string where)
    {
        var name = Text(a, "name", where);
        var bonus = Int(a, "damageBonus", where);
        var range = RangeOf(a, where);
        var fate = a.TryGetProperty("fate", out var f) ? EnumOf<ItemFate>(f, where) : ItemFate.None;
        var item = a.TryGetProperty("item", out var i) ? KnownItem(i.GetString(), items, where) : null;
        if ((item is null) != (fate == ItemFate.None))
            throw new InvalidDataException($"contenuti: {where}, attacco '{name}': un oggetto va dato con la sua sorte e viceversa");
        return new AttackProfile
        {
            Name = name, AttackBonus = Int(a, "bonus", where), Damage = DiceOf(Text(a, "damage", where), where), DamageBonus = bonus,
            Type = EnumOf<DamageType>(Field(a, "type", where), where), Melee = range is null, Range = range, AbilityModifier = bonus,
            Requires = a.TryGetProperty("requires", out var r) ? KnownItem(r.GetString(), items, where) : null,
            Item = item, Fate = fate,
        };
    }

    private static KindDefinition ReadKind(JsonElement e, string file)
    {
        var id = Text(e, "id", file);
        var where = $"{file}, tipo '{id}'";
        return new KindDefinition(id, Text(e, "name", where), EnumOf<CreatureType>(Field(e, "type", where), where), TellsOf(e, where));
    }

    /// <summary>C5a: optional "tells": [{ "dc": 12, "text": "..." }], each with a DC from 1 to 30 and a text.</summary>
    private static IReadOnlyList<Tell> TellsOf(JsonElement e, string where) =>
        e.TryGetProperty("tells", out _)
            ? Array(e, "tells", where).Select(t => Int(t, "dc", where) is var dc and >= 1 and <= 30
                ? new Tell(dc, Text(t, "text", where))
                : throw new InvalidDataException($"contenuti: {where}: un indizio ha una CD tra 1 e 30")).ToArray()
            : System.Array.Empty<Tell>();

    private static CharacterDefinition ReadCharacter(JsonElement e, string file, IReadOnlyDictionary<string, ItemDefinition> items,
        IReadOnlyDictionary<string, ClassDefinition> classes)
    {
        var id = Text(e, "id", file);
        var where = $"{file}, personaggio '{id}'";
        var cls = Text(e, "class", where);
        if (!classes.ContainsKey(cls))
            throw new InvalidDataException($"contenuti: {where}: classe sconosciuta '{cls}'");
        var eq = Field(e, "equipment", where);
        string? Slot(string slot) => eq.TryGetProperty(slot, out var s) ? KnownItem(s.GetString(), items, where) : null;
        var equipment = new Equipment(Slot("armor"), Slot("shield"), Slot("mainHand"));
        if (equipment.Armor is { } armor && items[armor].Armor is not { Type: not ArmorType.Shield })
            throw new InvalidDataException($"contenuti: {where}: '{armor}' non è un'armatura");
        if (equipment.Shield is { } shield && items[shield].Armor is not { Type: ArmorType.Shield })
            throw new InvalidDataException($"contenuti: {where}: '{shield}' non è uno scudo");
        if (equipment.MainHand is { } hand && items[hand].Weapon is null)
            throw new InvalidDataException($"contenuti: {where}: '{hand}' non è un'arma");
        var level = Int(e, "level", where);
        if (level is < 1 or > 20)
            throw new InvalidDataException($"contenuti: {where}: livello fuori da 1-20");
        return new CharacterDefinition
        {
            Id = id, Title = Text(e, "title", where), Class = cls, Level = level, Abilities = AbilitiesOf(Field(e, "abilities", where), where),
            Skills = Array(e, "skills", where).Select(x => EnumOf<Skill>(x, where)).ToArray(), Equipment = equipment,
            Items = Stacks(e, items, where), Coins = e.TryGetProperty("coins", out var c) ? c.GetInt64() : 0,
            Masteries = Array(e, "masteries", where).Select(x => KnownItem(x.GetString(), items, where)).ToArray(),
            Kind = Text(e, "kind", where),
        };
    }

    // ---------------------------------------------------------------- small helpers

    private static JsonElement Parse(string text, string file)
    {
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"contenuti: {file} non è JSON valido ({ex.Message})");
        }
    }

    private static JsonElement Field(JsonElement e, string name, string where) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v
            : throw new InvalidDataException($"contenuti: {where}: manca '{name}'");

    private static string Text(JsonElement e, string name, string where) =>
        Field(e, name, where).GetString() is { Length: > 0 } s ? s : throw new InvalidDataException($"contenuti: {where}: '{name}' vuoto");

    private static int Int(JsonElement e, string name, string where) => Field(e, name, where).GetInt32();

    private static IEnumerable<JsonElement> Array(JsonElement e, string name, string where) =>
        Field(e, name, where) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToArray()
            : throw new InvalidDataException($"contenuti: {where}: '{name}' deve essere un elenco");

    private static T EnumOf<T>(JsonElement e, string where) where T : struct, Enum =>
        Enum.TryParse<T>(e.GetString(), out var v) && Enum.IsDefined(v) ? v
            : throw new InvalidDataException($"contenuti: {where}: valore sconosciuto '{e.GetString()}'");

    private static Dice DiceOf(string text, string where)
    {
        var parts = text.Split('d');
        return parts.Length == 2 && int.TryParse(parts[0], out var n) && int.TryParse(parts[1], out var s) && n > 0 && s > 1
            ? new Dice(n, s)
            : throw new InvalidDataException($"contenuti: {where}: dadi non validi '{text}'");
    }

    private static (int Normal, int Long)? RangeOf(JsonElement e, string where)
    {
        if (!e.TryGetProperty("range", out var r))
            return null;
        var values = r.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        return values.Length == 2 && values[0] > 0 && values[1] >= values[0] ? (values[0], values[1])
            : throw new InvalidDataException($"contenuti: {where}: gittata non valida");
    }

    private static AbilityScores AbilitiesOf(JsonElement a, string where) => new(Int(a, "str", where), Int(a, "dex", where),
        Int(a, "con", where), Int(a, "int", where), Int(a, "wis", where), Int(a, "cha", where));

    private static string KnownItem(string? id, IReadOnlyDictionary<string, ItemDefinition> items, string where) =>
        id is not null && items.ContainsKey(id) ? id : throw new InvalidDataException($"contenuti: {where}: oggetto sconosciuto '{id}'");

    private static (string Item, int Count)[] Stacks(JsonElement e, IReadOnlyDictionary<string, ItemDefinition> items, string where) =>
        Array(e, "items", where).Select(s =>
        {
            var pair = s.EnumerateArray().ToArray();
            var count = pair.Length == 2 ? pair[1].GetInt32() : 0;
            return count > 0 ? (KnownItem(pair[0].GetString(), items, where), count)
                : throw new InvalidDataException($"contenuti: {where}: quantità non valida");
        }).ToArray();

    private static void Add<T>(Dictionary<string, T> into, T value, string file) where T : notnull
    {
        var id = value switch
        {
            ItemDefinition i => i.Id, ClassDefinition c => c.Id, CreatureDefinition r => r.Id, CharacterDefinition h => h.Id,
            KindDefinition k => k.Id, _ => "",
        };
        if (!into.TryAdd(id, value))
            throw new InvalidDataException($"contenuti: '{id}' ripetuto ({file})");
    }
}
