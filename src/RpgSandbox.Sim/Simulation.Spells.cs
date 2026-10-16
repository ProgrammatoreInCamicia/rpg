using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R3b: spellcasting (SRD 5.2.1 "Spells" p. 105–106 and the Paladin's Spellcasting, p. 53–54): slots, prepared spells,
// Concentration, and the effects of the spells the game has (srd/spells.json).
internal sealed partial class Simulation
{
    private SpellcastingDefinition? CastingOf(Actor actor) =>
        ClassOf(actor) is { } cls ? World.Content.Classes[cls].Spellcasting : null;

    /// <summary>The spell slots the character has at its level, by spell level (index 0: level 1).</summary>
    internal IReadOnlyList<int> SlotsMax(Actor actor) =>
        CastingOf(actor) is { } sc && World.LevelOf(actor) is { } level ? sc.Slots[Math.Min(level, sc.Slots.Count) - 1] : Array.Empty<int>();

    private int SlotsLeft(Actor actor, int level)
    {
        var max = SlotsMax(actor);
        if (level < 1 || level > max.Count)
            return 0;
        return max[level - 1] - (actor.SlotsSpent.Count >= level ? actor.SlotsSpent[level - 1] : 0);
    }

    private void SpendSlot(Actor actor, int level)
    {
        while (actor.SlotsSpent.Count < level)
            actor.SlotsSpent.Add(0);
        actor.SlotsSpent[level - 1]++;
    }

    private int PreparedMax(Actor actor) =>
        CastingOf(actor) is { } sc && World.LevelOf(actor) is { } level ? sc.Prepared[Math.Min(level, sc.Prepared.Count) - 1] : 0;

    /// <summary>Spells always prepared by a feature: Divine Smite from Paladin's Smite (level 2).</summary>
    private IEnumerable<string> AlwaysPrepared(Actor actor)
    {
        if (HasFeature(actor, ClassFeatures.Smite))
            yield return "divine-smite";
        foreach (var spell in OathSpells(actor)) // R3c
            yield return spell;
    }

    private bool IsPrepared(Actor actor, string spell) => actor.PreparedSpells.Contains(spell) || AlwaysPrepared(actor).Contains(spell);

    private int SpellModifier(Actor actor) => CastingOf(actor) is { } sc ? Abilities.Modifier(actor.Sheet.Score(sc.Ability)) : 0;

    /// <summary>SRD: Spell save DC = 8 + spellcasting ability modifier + Proficiency Bonus.</summary>
    internal int SpellSaveDc(Actor actor) => 8 + SpellModifier(actor) + actor.Sheet.ProficiencyBonus;

    internal SpellbookView? SpellbookOf(Actor actor)
    {
        if (CastingOf(actor) is null || World.LevelOf(actor) is null)
            return null;
        var always = AlwaysPrepared(actor).ToHashSet();
        return new SpellbookView
        {
            Prepared = actor.PreparedSpells.Concat(always).Distinct().Where(World.Content.Spells.ContainsKey)
                .Select(id => World.Content.Spells[id]).OrderBy(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => new SpellView
                {
                    Id = s.Id, Name = s.Name, Level = s.Level, Casting = s.Casting.ToString(), RangeFeet = s.RangeFeet,
                    Targets = s.Targets, Concentration = s.Concentration, AlwaysPrepared = always.Contains(s.Id),
                    Description = s.Description,
                }).ToArray(),
            Slots = SlotsMax(actor).Select((max, i) => new SlotView { Level = i + 1, Remaining = SlotsLeft(actor, i + 1), Max = max }).ToArray(),
            SaveDc = SpellSaveDc(actor), AttackBonus = SpellModifier(actor) + actor.Sheet.ProficiencyBonus,
            Concentrating = actor.Concentrating,
            FreeSmite = HasFeature(actor, ClassFeatures.Smite) && !actor.FreeSmiteUsed,
            ClassSpells = World.Content.Spells.Values.Where(s => s.Classes.Contains(ClassOf(actor) ?? ""))
                .OrderBy(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => new SpellView
                {
                    Id = s.Id, Name = s.Name, Level = s.Level, Casting = s.Casting.ToString(), RangeFeet = s.RangeFeet,
                    Targets = s.Targets, Concentration = s.Concentration, AlwaysPrepared = always.Contains(s.Id),
                    Description = s.Description, Prepared = IsPrepared(actor, s.Id), WithinReach = s.Level <= SlotsMax(actor).Count,
                }).ToArray(),
        };
    }

    internal IEnumerable<EffectView> EffectsOn(Actor actor) =>
        actor.Effects.Where(f => f.Until is not { } until || until > World.Now).Select(f => new EffectView
        {
            Spell = f.Spell, Name = World.Content.Spells.TryGetValue(f.Spell, out var s) ? s.Name : f.Spell, Caster = f.Caster, Until = f.Until,
        });

    internal bool HasEffect(Actor actor, string spell) =>
        actor.Effects.Any(f => f.Spell == spell && (f.Until is not { } until || until > World.Now));

    // ---------------------------------------------------------------- casting

    private CommandResult Cast(CastCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var caster))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!World.Content.Spells.TryGetValue(command.Spell, out var spell) || !IsPrepared(caster, spell.Id))
            return CommandResult.Rejected(RejectionReason.UnknownCommand, "Non hai preparato questo incantesimo.");
        if (spell.Casting == CastingTime.AfterMeleeHit)
            return CommandResult.Rejected(RejectionReason.UnknownCommand, $"{spell.Name} si lancia colpendo in mischia.");
        if (command.SlotLevel < spell.Level || SlotsLeft(caster, command.SlotLevel) <= 0)
            return CommandResult.Rejected(RejectionReason.NoSlot, $"Non hai slot di livello {command.SlotLevel}.");

        // Targets: none means oneself (a spell on Self); up to the spell's number, one more a slot level for Bless.
        var most = spell.Targets + (spell.Id == "bless" ? command.SlotLevel - spell.Level : 0);
        var ids = command.Targets.Count == 0 ? new[] { caster.Id } : command.Targets.Distinct().ToArray();
        if (spell.Targets == 0 && (ids.Length != 1 || ids[0] != caster.Id) || spell.Targets > 0 && ids.Length > most)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, spell.Targets == 0 ? $"{spell.Name} è su di te." : $"Al massimo {most} bersagli.");
        var targets = new List<Actor>();
        foreach (var id in ids)
        {
            if (!World.Actors.TryGetValue(id, out var target) || target.Vitality.State == LifeState.Dead)
                return CommandResult.Rejected(RejectionReason.ActorNotFound, "Bersaglio non valido.");
            if (OutOfSpellRange(caster, target, spell) is { } why)
                return CommandResult.Rejected(RejectionReason.OutOfReach, why);
            targets.Add(target);
        }
        if (spell.Id == "lesser-restoration" && RestorableCondition(targets[0], command.Condition) is null)
            return CommandResult.Rejected(RejectionReason.NotHeld, $"{targets[0].Name} non ha condizioni da togliere.");

        var e = EncounterOf(caster);
        if (e is not null)
        {
            var me = CombatantOf(e, caster.Id);
            if (!IsTurnOf(e, caster))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            if (Incapacitated(caster))
                return CommandResult.Rejected(RejectionReason.NoActionLeft, $"{caster.Name} non può agire.");
            if (me.SpellSlotSpent)
                return CommandResult.Rejected(RejectionReason.NoSlot, "Hai già speso uno slot in questo turno.");
            if (spell.Casting == CastingTime.Action ? me.ActionUsed : me.BonusActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft,
                    spell.Casting == CastingTime.Action ? "Hai già usato l'azione in questo turno." : "Hai già usato l'azione bonus in questo turno.");
            if (spell.Casting == CastingTime.Action)
                me.ActionUsed = true;
            else
                me.BonusActionUsed = true;
            me.SpellSlotSpent = true;
        }
        else if (caster.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{caster.Name} è già impegnato.");

        SpendSlot(caster, command.SlotLevel);
        caster.Sneak = null; // SRD Hide: a spell with a Verbal component gives one away
        if (spell.Concentration)
        {
            EndConcentration(caster, "lancia un altro incantesimo");
            caster.Concentrating = spell.Id;
        }
        var text = ApplySpell(e, caster, spell, command.SlotLevel, targets, command.Condition);
        if (e is not null)
            Record(e, CombatLogKind.Spell, caster, targets.Count == 1 && targets[0] != caster ? targets[0] : null, (_, _) => text);
        World.RecordFact("SpellCast", text, targets.Select(t => t.Id).Prepend(caster.Id).Distinct().ToArray());
        return new CommandResult { Message = text };
    }

    /// <summary>Why a target is out of the spell's reach: beyond its range, out of sight, or behind Total Cover (SRD); null if fine.</summary>
    private string? OutOfSpellRange(Actor caster, Actor target, SpellDefinition spell)
    {
        if (target.Id == caster.Id)
            return spell.RangeFeet == 0 || spell.Targets > 0 ? null : $"{spell.Name} non si lancia su di te.";
        if (spell.RangeFeet == 0)
            return $"{spell.Name} è su di te.";
        if (caster.MapArea is not { } area || target.MapArea != area || CurrentSpot(caster) is not { } from || CurrentSpot(target) is not { } to)
            return $"{target.Name} non è qui.";
        var map = World.Maps[area];
        if (spell.RangeFeet == 5)
            return WithinFiveFeet(map, from, to) ? null : $"Devi toccare {target.Name}: avvicinati.";
        if (!SeesActor(caster, target, ignoreFacing: EncounterOf(caster) is null))
            return $"Non vedi {target.Name}.";
        if (from.DistanceTo(to) > spell.RangeFeet * Spot.PerFoot)
            return $"{target.Name} è oltre la gittata ({spell.RangeFeet} piedi).";
        return AimAt(map, from, to).Aim is null ? $"Non hai una via libera verso {target.Name}." : null;
    }

    /// <summary>The effect of a spell (srd/spells.json), and the line that tells it.</summary>
    private string ApplySpell(Encounter? e, Actor caster, SpellDefinition spell, int slot, IReadOnlyList<Actor> targets, ConditionKind? condition)
    {
        string Names() => string.Join(", ", targets.Select(t => t == caster ? "sé" : t.Name));
        switch (spell.Id)
        {
            case "cure-wounds": // SRD: 2d8 + the spellcasting ability modifier; 2d8 more a slot level above 1
            {
                var target = targets[0];
                var rolls = Enumerable.Range(0, 2 * slot).Select(_ => World.Rng.Roll(8)).ToArray();
                var healed = target.Vitality.Heal(Math.Max(0, rolls.Sum() + SpellModifier(caster)));
                return $"{caster.Name} lancia {spell.Name} su {Names()}: [{string.Join(", ", rolls)}] +{SpellModifier(caster)}, {healed} punti ferita.";
            }
            case "lesser-restoration":
            {
                var target = targets[0];
                var kind = RestorableCondition(target, condition)!.Value;
                target.Conditions.RemoveAll(c => c.Kind == kind);
                return $"{caster.Name} lancia {spell.Name} su {Names()}: non è più {ConditionName(kind)}.";
            }
            case "aid": // SRD: Hit Point maximum and current up by 5, 5 more a slot level above 2, for 8 hours
            {
                var amount = 5 * (slot - 1);
                foreach (var target in targets)
                {
                    RemoveEffect(target, "aid");
                    target.Vitality.RaiseMaximum(amount);
                    if (target.Vitality.Current == 0)
                        target.Vitality.Heal(amount);
                    AddEffect(target, spell, caster, amount);
                }
                return $"{caster.Name} lancia {spell.Name} su {Names()}: {amount} punti ferita in più per 8 ore.";
            }
            default: // Bless, Shield of Faith, Divine Favor: in effect for their duration
                foreach (var target in targets)
                {
                    RemoveEffect(target, spell.Id); // the same spell does not add up: the newer one stays
                    AddEffect(target, spell, caster, 0);
                }
                return $"{caster.Name} lancia {spell.Name} su {Names()}.";
        }
    }

    /// <summary>Lesser Restoration ends Blinded, Deafened, Paralyzed or Poisoned: the one asked, or the first it has.</summary>
    private ConditionKind? RestorableCondition(Actor target, ConditionKind? asked)
    {
        var ends = new[] { ConditionKind.Blinded, ConditionKind.Deafened, ConditionKind.Paralyzed, ConditionKind.Poisoned };
        return asked is { } kind ? ends.Contains(kind) && Has(target, kind) ? kind : null
            : ends.Cast<ConditionKind?>().FirstOrDefault(k => Has(target, k!.Value));
    }

    private void AddEffect(Actor target, SpellDefinition spell, Actor caster, int amount)
    {
        var until = spell.DurationMinutes > 0 ? World.Now.Plus(Duration.FromMinutes(spell.DurationMinutes)) : (GameTime?)null;
        target.Effects.Add(new SpellEffect { Spell = spell.Id, Caster = caster.Id, Until = until, Concentration = spell.Concentration, Amount = amount });
        if (until is { } at)
            World.Scheduler.Schedule(at, new ExpireEffects());
    }

    /// <summary>An effect ends: Aid takes its Hit Points back.</summary>
    private void RemoveEffect(Actor target, string spell)
    {
        foreach (var effect in target.Effects.Where(f => f.Spell == spell).ToList())
        {
            target.Effects.Remove(effect);
            if (effect.Spell == "aid")
                target.Vitality.LowerMaximum(effect.Amount);
        }
    }

    /// <summary>The spells whose time ran out end; the caster no longer concentrates on one that ended.</summary>
    private void ExpireSpellEffects()
    {
        foreach (var actor in World.Actors.Values.OrderBy(a => a.Id.Value, StringComparer.Ordinal))
            foreach (var effect in actor.Effects.Where(f => f.Until is { } until && until <= World.Now).ToList())
            {
                RemoveEffect(actor, effect.Spell);
                if (World.Actors.TryGetValue(effect.Caster, out var caster) && caster.Concentrating == effect.Spell
                    && !World.Actors.Values.Any(a => a.Effects.Any(f => f.Caster == caster.Id && f.Concentration)))
                    caster.Concentrating = null;
            }
    }

    /// <summary>SRD Concentration: it ends, and with it the effects that hang on it.</summary>
    internal void EndConcentration(Actor caster, string why)
    {
        if (caster.Concentrating is not { } spell)
            return;
        caster.Concentrating = null;
        foreach (var actor in World.Actors.Values)
            foreach (var effect in actor.Effects.Where(f => f.Caster == caster.Id && f.Concentration).ToList())
                RemoveEffect(actor, effect.Spell);
        var name = World.Content.Spells.TryGetValue(spell, out var s) ? s.Name : spell;
        if (EncounterOf(caster) is { } e)
            Record(e, CombatLogKind.Spell, caster, null, (a, _) => $"{a} perde la concentrazione su {name}: {why}.");
        World.RecordFact("ConcentrationLost", $"{caster.Name} perde la concentrazione su {name}: {why}.", caster.Id);
    }

    /// <summary>
    /// SRD Concentration: damage calls for a Constitution saving throw, DC 10 or half the damage (the higher), at most 30;
    /// falling unconscious or Incapacitated ends it.
    /// </summary>
    private void ConcentrationAfterHarm(Actor actor, int damage)
    {
        if (actor.Concentrating is null)
            return;
        if (!actor.Conscious || Incapacitated(actor))
        {
            EndConcentration(actor, actor.Conscious ? "è incapacitato" : "perde i sensi");
            return;
        }
        if (damage > 0 && !SavingThrow(EncounterOf(actor), actor, Ability.Constitution, Math.Min(30, Math.Max(10, damage / 2)), "la concentrazione"))
            EndConcentration(actor, "il colpo lo distrae");
    }

    /// <summary>Bless: 1d4 more on attack rolls and saving throws (0 without it).</summary>
    private int BlessRoll(Actor actor) => HasEffect(actor, "bless") ? World.Rng.Roll(4) : 0;

    /// <summary>Shield of Faith: +2 to the Armor Class.</summary>
    private int SpellArmorBonus(Actor actor) => HasEffect(actor, "shield-of-faith") ? 2 : 0;

    /// <summary>
    /// After a hit: Divine Favor's 1d4 Radiant with a weapon; Divine Smite's 2d8 Radiant (1d8 more a slot level above 1, and
    /// against a Fiend or an Undead). On a Critical Hit their dice are rolled twice, like the attack's. The extra damage, and
    /// how the log tells it.
    /// </summary>
    private (int Damage, string Text) ExtraOnHit(Actor attacker, Actor target, AttackProfile attack, bool critical, int? smiteSlot)
    {
        var parts = new List<string>();
        var total = 0;
        void Roll(int count, int sides, string what)
        {
            var dice = Enumerable.Range(0, count * (critical ? 2 : 1)).Select(_ => World.Rng.Roll(sides)).ToArray();
            total += dice.Sum();
            parts.Add($"{what} [{string.Join(", ", dice)}] radiosi");
        }
        if (HasEffect(attacker, "divine-favor") && (attack.Requires is not null || attack.Item is not null))
            Roll(1, 4, "Favore divino");
        if (smiteSlot is { } slot)
            Roll(2 + (slot - 1) + (World.Content.KindOf(target.Build).Type is CreatureType.Fiend or CreatureType.Undead ? 1 : 0), 8, "Punizione divina");
        return (total, parts.Count == 0 ? "" : " + " + string.Join(" + ", parts));
    }

    /// <summary>
    /// R3b: whether an attack can carry a Divine Smite: a melee attack in the attacker's turn, Divine Smite prepared, the
    /// Bonus Action free, and a slot (only one a turn) or the free use. Null if it can; otherwise why not.
    /// </summary>
    private string? SmiteRefused(Encounter? e, Actor attacker, AttackProfile attack, int? slot, bool free)
    {
        if (slot is null && !free)
            return null;
        if (e is null)
            return "Punizione divina solo durante uno scontro (non sul colpo d'apertura).";
        if (!attack.Melee || !IsPrepared(attacker, "divine-smite"))
            return "Punizione divina si lancia colpendo in mischia.";
        var me = CombatantOf(e, attacker.Id);
        if (me.BonusActionUsed)
            return "Hai già usato l'azione bonus in questo turno.";
        if (free)
            return HasFeature(attacker, ClassFeatures.Smite) && !attacker.FreeSmiteUsed ? null : "Non hai più la Punizione gratuita fino al prossimo riposo lungo.";
        if (me.SpellSlotSpent)
            return "Hai già speso uno slot in questo turno.";
        return SlotsLeft(attacker, slot!.Value) > 0 ? null : $"Non hai slot di livello {slot}.";
    }

    /// <summary>R3b, at the end of a Long Rest: every slot and the free Divine Smite are back; one prepared spell may be swapped.</summary>
    private void SpellsAfterLongRest(Actor actor)
    {
        actor.SlotsSpent.Clear();
        actor.FreeSmiteUsed = false;
        actor.SpellSwapOpen = CastingOf(actor) is not null;
    }

    /// <summary>The spells of the character's class it may prepare: of a level it has slots for, not always prepared.</summary>
    private IReadOnlyList<string> PreparableSpells(Actor actor)
    {
        var cls = ClassOf(actor);
        var top = SlotsMax(actor).Count;
        var always = AlwaysPrepared(actor).ToHashSet();
        return World.Content.Spells.Values
            .Where(s => s.Classes.Contains(cls ?? "") && s.Level >= 1 && s.Level <= top && !always.Contains(s.Id))
            .OrderBy(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id).ToArray();
    }
}
