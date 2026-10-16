namespace RpgSandbox.Sim.Rules;

/// <summary>
/// D1a: an actor's character sheet and combat profile, both derived from ONE definition in the content pack and from
/// what it wears and wields. A creature is its stat block; a character is computed by the SRD 5.2.1 rules (class,
/// level, abilities, equipment).
/// </summary>
public static class Builds
{
    /// <summary>SRD Character Advancement: +2 at levels 1–4, +3 at 5–8, and so on.</summary>
    public static int ProficiencyBonus(int level) => 2 + (level - 1) / 4;

    /// <summary>C5b, SRD Character Advancement: the Experience Points a character needs to reach each level (index 1–20).</summary>
    private static readonly int[] Thresholds =
        { 0, 0, 300, 900, 2_700, 6_500, 14_000, 23_000, 34_000, 48_000, 64_000, 85_000, 100_000, 120_000, 140_000, 165_000,
          195_000, 225_000, 265_000, 305_000, 355_000 };

    public const int MaxLevel = 20;

    /// <summary>The Experience Points needed to reach <paramref name="level"/> (1–20).</summary>
    public static int ExperienceFor(int level) => level is >= 1 and <= MaxLevel
        ? Thresholds[level] : throw new ArgumentOutOfRangeException(nameof(level));

    /// <summary>The level an Experience Point total reaches (SRD: when it equals or exceeds the threshold).</summary>
    public static int LevelFor(long experience) =>
        Enumerable.Range(1, MaxLevel).Last(l => experience >= Thresholds[l]);

    /// <summary>
    /// SRD Gaining a Level: the Hit Points a character of <paramref name="cls"/> with that Constitution modifier gains per
    /// level, by the Fixed Hit Points by Class table (half the Hit Die + 1, plus Constitution; at least 1).
    /// </summary>
    public static int HitPointsPerLevel(ClassDefinition cls, int constitutionModifier) =>
        Math.Max(1, cls.HitDie / 2 + 1 + constitutionModifier);

    /// <summary>What it starts wearing and wielding: a character's declared equipment; a creature's armor, shield and first weapon.</summary>
    public static Equipment InitialEquipment(ContentPack pack, string build)
    {
        if (pack.Characters.TryGetValue(build, out var character))
            return character.Equipment;
        var gear = Creature(pack, build).Gear.Select(g => pack.Items[g]).ToList();
        return new Equipment(
            gear.FirstOrDefault(i => i.Armor is { Type: not ArmorType.Shield })?.Id,
            gear.FirstOrDefault(i => i.Armor is { Type: ArmorType.Shield })?.Id,
            (gear.FirstOrDefault(i => i.Weapon is { Ranged: false }) ?? gear.FirstOrDefault(i => i.Weapon is not null))?.Id);
    }

    /// <summary>Everything it starts with: what it wears and wields (one each) and what else it carries.</summary>
    public static IReadOnlyList<(string Item, int Count)> Kit(ContentPack pack, string build)
    {
        var (worn, carried) = pack.Characters.TryGetValue(build, out var character)
            ? (character.Equipment.Items, character.Items)
            : (Creature(pack, build).Gear.AsEnumerable(), Creature(pack, build).Items);
        return worn.Select(i => (Item: i, Count: 1)).Concat(carried)
            .GroupBy(i => i.Item).Select(g => (g.Key, g.Sum(i => i.Count))).ToArray();
    }

    public static long Coins(ContentPack pack, string build) =>
        pack.Characters.TryGetValue(build, out var character) ? character.Coins : Creature(pack, build).Coins;

    /// <summary>A character's sheet at <paramref name="level"/> (C5b; its definition's level if null). A creature has no level.</summary>
    public static CharacterSheet Sheet(ContentPack pack, string build, Equipment equipment, int? level = null,
        CharacterChoices? choices = null)
    {
        if (!pack.Characters.TryGetValue(build, out var c))
        {
            var creature = Creature(pack, build);
            return Scores(creature.Abilities) with
            {
                Title = creature.Name, ProficiencyBonus = creature.ProficiencyBonus, SkillProficiencies = creature.Skills, ToolProficiencies = creature.Tools, SaveProficiencies = creature.Saves,
                ArmorClass = creature.ArmorClass, Speed = creature.Speed,
                StealthDisadvantage = creature.Gear.Any(g => pack.Items[g].Armor is { StealthDisadvantage: true }),
            };
        }
        var armor = equipment.Armor is { } a ? pack.Items[a].Armor : null;
        var shield = equipment.Shield is { } s ? pack.Items[s].Armor : null;
        var scores = Raised(c.Abilities, choices); // R3a: Ability Score Improvements
        var dex = Abilities.Modifier(scores.Dexterity);
        var ac = (armor is null ? 10 + dex : armor.Base + (armor.DexMax is { } max ? Math.Min(dex, max) : dex)) + (shield?.Base ?? 0)
                 + (armor is not null && choices?.FightingStyle == "Defense" ? 1 : 0); // R3a, SRD Defense: +1 AC in armor
        // SRD Heavy Armor: without the Strength it asks for, Speed drops by 10 feet.
        var speed = 30 - (armor is not null && scores.Strength < armor.Strength ? 10 : 0);
        return Scores(scores) with
        {
            Title = c.Title, ProficiencyBonus = ProficiencyBonus(level ?? c.Level), SkillProficiencies = c.Skills, SaveProficiencies = pack.Classes[c.Class].Saves,
            ArmorClass = ac, Speed = speed, StealthDisadvantage = armor?.StealthDisadvantage ?? false,
        };
    }

    public static CombatProfile Profile(ContentPack pack, string build, Equipment equipment, int? level = null,
        CharacterChoices? choices = null)
    {
        var sheet = Sheet(pack, build, equipment, level, choices);
        if (!pack.Characters.TryGetValue(build, out var c))
        {
            var creature = Creature(pack, build);
            return new CombatProfile
            {
                Name = creature.Name, MaxHitPoints = creature.HitPoints, ArmorClass = creature.ArmorClass,
                InitiativeBonus = creature.InitiativeBonus, Speed = creature.Speed, Attacks = creature.Attacks,
                MakesDeathSaves = false, Flees = creature.Flees, Kit = Kit(pack, build), Coins = creature.Coins,
            };
        }
        var cls = pack.Classes[c.Class];
        var con = Abilities.Modifier(sheet.Constitution);
        var masteries = choices?.Masteries ?? c.Masteries; // R3a: changed after a Long Rest
        // SRD Hit Points: the full Hit Die at level 1, then the fixed average (half the die + 1) per level; Constitution each time.
        var hitPoints = cls.HitDie + con + ((level ?? c.Level) - 1) * HitPointsPerLevel(cls, con);
        var attacks = new List<AttackProfile>();
        if (equipment.MainHand is { } hand && pack.Items[hand].Weapon is { } weapon)
        {
            var twoHands = equipment.Shield is null && weapon.Has(WeaponProperty.Versatile);
            var attack = AttackProfile.With(sheet, weapon, cls.Weapons.Contains(weapon.Category), twoHands,
                masteryUnlocked: masteries.Contains(hand)) with { Requires = hand };
            attack = FightingStyle(attack, weapon, twoHands, choices?.FightingStyle);
            attacks.Add(weapon.Ranged && weapon.Ammunition is { } ammo ? attack with { Item = ammo, Fate = ItemFate.Spent } : attack);
        }
        // Thrown weapons carried besides the one in hand: drawn and thrown as part of the attack (SRD).
        foreach (var thrown in c.Items.Select(i => i.Item).Distinct().Where(i => pack.Items[i].Weapon?.Has(WeaponProperty.Thrown) == true))
            attacks.Add(AttackProfile.With(sheet, pack.Items[thrown].Weapon!, cls.Weapons.Contains(pack.Items[thrown].Weapon!.Category),
                thrown: true, masteryUnlocked: masteries.Contains(thrown)) with { Item = thrown, Fate = ItemFate.Thrown });
        attacks.Add(AttackProfile.Unarmed(sheet));
        return new CombatProfile
        {
            Name = cls.Name, MaxHitPoints = Math.Max(1, hitPoints), ArmorClass = sheet.ArmorClass,
            InitiativeBonus = Abilities.Modifier(sheet.Dexterity), Speed = sheet.Speed, Attacks = attacks,
            MakesDeathSaves = true, Kit = Kit(pack, build), Coins = c.Coins,
        };
    }

    private static CreatureDefinition Creature(ContentPack pack, string build) =>
        pack.Creatures.TryGetValue(build, out var creature) ? creature : throw new KeyNotFoundException($"nessuna creatura o personaggio '{build}'");


    /// <summary>R3a: the scores raised by the Ability Score Improvements chosen (SRD: never above 20).</summary>
    private static AbilityScores Raised(AbilityScores a, CharacterChoices? choices)
    {
        int Up(Ability ability, int score) =>
            Math.Min(20, score + (choices?.AbilityIncreases.Count(x => x == ability) ?? 0));
        return a with
        {
            Strength = Up(Ability.Strength, a.Strength), Dexterity = Up(Ability.Dexterity, a.Dexterity),
            Constitution = Up(Ability.Constitution, a.Constitution), Intelligence = Up(Ability.Intelligence, a.Intelligence),
            Wisdom = Up(Ability.Wisdom, a.Wisdom), Charisma = Up(Ability.Charisma, a.Charisma),
        };
    }

    /// <summary>
    /// R3a, SRD Fighting Style feats: Archery, +2 to attack rolls with Ranged weapons; Great Weapon Fighting, 1 and 2 on the
    /// damage dice count as 3 with a Melee weapon held in two hands that is Two-Handed or Versatile.
    /// </summary>
    private static AttackProfile FightingStyle(AttackProfile attack, Weapon weapon, bool twoHands, string? style) => style switch
    {
        "Archery" when weapon.Ranged => attack with { AttackBonus = attack.AttackBonus + 2 },
        "GreatWeaponFighting" when !weapon.Ranged && (weapon.Has(WeaponProperty.TwoHanded) || twoHands) => attack with { GreatWeaponFighting = true },
        _ => attack,
    };
    private static CharacterSheet Scores(AbilityScores a) => new()
    {
        Title = "", Strength = a.Strength, Dexterity = a.Dexterity, Constitution = a.Constitution,
        Intelligence = a.Intelligence, Wisdom = a.Wisdom, Charisma = a.Charisma, ProficiencyBonus = 2,
        SkillProficiencies = Array.Empty<Skill>(), ArmorClass = 10,
    };
}
