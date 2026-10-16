using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// C5a: what the player learns of the creatures it meets, by looking at them and by fighting them.
internal sealed partial class Simulation
{
    /// <summary>
    /// After an attack: the player's own blow narrows the target's Armor Class (a natural 1 or 20 says nothing); a blow
    /// the player saw someone else strike teaches that it has that attack, and its kind too if the attack is natural.
    /// </summary>
    private void LearnFrom(Actor attacker, Actor target, AttackProfile attack, AttackResult result, int coverBonus)
    {
        var player = World.Actors[World.Player];
        if (attacker.IsPlayer)
        {
            if (result.Roll.Kept is not (1 or 20))
                player.Lore.Of(target.Id).LearnArmorClass(result.Roll.Total - coverBonus, result.Hit); // P3: its own AC, not the cover's
            return;
        }
        if (!SeesActor(player, attacker))
            return;
        player.Lore.Of(attacker.Id).Attacks.Add(attack.Name);
        if (attack.Requires is null && attack.Item is null && attack.Damage != Dice.None) // a bite, not a weapon nor a bare fist
            player.Lore.OfKind(World.Content.KindOf(attacker.Build).Id).Attacks.Add(attack.Name);
    }

    private CommandResult Observe(Encounter? fight, ObserveCommand command)
    {
        var actor = World.Actors[command.Actor];
        if (!actor.IsPlayer)
            return CommandResult.Rejected(RejectionReason.UnknownCommand, "Solo il protagonista tiene conto di ciò che osserva.");
        if (!World.Actors.TryGetValue(command.Target, out var target) || target.Id == actor.Id)
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Bersaglio sconosciuto: {command.Target}.");
        if (!SeesActor(actor, target))
            return CommandResult.Rejected(RejectionReason.NotVisible, $"Non vedi {target.Name}.");
        var kind = World.Content.KindOf(target.Build);
        var lore = actor.Lore.Of(target.Id);
        var kindLore = actor.Lore.OfKind(kind.Id);
        var insight = !lore.Observed;
        var study = !kindLore.Studied && actor.Sheet.IsProficient(kind.StudySkill); // as a GM: only what the character could know
        if (!insight && !study)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, $"Hai già osservato {target.Name} con attenzione.");
        if (fight is not null)
        {
            if (!IsTurnOf(fight, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            var me = CombatantOf(fight, actor.Id);
            if (me.ActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.");
            me.ActionUsed = true; // SRD: Search and Study are actions
        }

        var lines = new List<string>();
        if (insight)
        {
            lore.Observed = true;
            var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Insight) + CheckEdges(actor).Modifier, disadvantage: CheckEdges(actor).Disadvantage);
            lines.Add($"Intuizione: {roll.Describe()}: " + Reveal(World.Content.Creatures.TryGetValue(target.Build, out var c) ? c.Tells : Array.Empty<Tell>(),
                roll.Total, lore.Tells, "non noti nulla di utile"));
        }
        if (study)
        {
            kindLore.Studied = true;
            var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(kind.StudySkill) + CheckEdges(actor).Modifier, disadvantage: CheckEdges(actor).Disadvantage);
            lines.Add($"{SkillName(kind.StudySkill)}: {roll.Describe()}: " + Reveal(kind.Tells, roll.Total, kindLore.Tells, "non ricordi nulla di utile"));
        }
        var text = $"{actor.Name} osserva {target.Name}. {string.Join(" ", lines)}";
        World.RecordFact("Observe", text, actor.Id); // the player's own: no one else takes part
        return new CommandResult { Message = text };
    }

    /// <summary>Adds the tells the total reaches to what is known; says what was found, or <paramref name="nothing"/>.</summary>
    private static string Reveal(IReadOnlyList<Tell> tells, int total, List<string> known, string nothing)
    {
        var found = tells.Where(t => t.Dc <= total && !known.Contains(t.Text)).Select(t => t.Text).ToList();
        known.AddRange(found);
        return found.Count == 0 ? $"{nothing}." : string.Join(" ", found);
    }

    private static string SkillName(Skill skill) => skill switch
    {
        Skill.Arcana => "Arcano",
        Skill.History => "Storia",
        Skill.Nature => "Natura",
        Skill.Religion => "Religione",
        _ => skill.ToString(),
    };

    /// <summary>C5a: the card of a creature as the player knows it.</summary>
    internal CreatureCardView CardOf(Actor creature)
    {
        var player = World.Actors[World.Player];
        var kind = World.Content.KindOf(creature.Build);
        var lore = player.Lore.Creatures.GetValueOrDefault(creature.Id);
        return new CreatureCardView
        {
            Kind = kind.Name,
            Tells = lore?.Tells.ToArray() ?? Array.Empty<string>(),
            Attacks = lore?.Attacks.ToArray() ?? Array.Empty<string>(),
            ArmorClassAbove = lore?.ArmorClassAbove, ArmorClassAtMost = lore?.ArmorClassAtMost, ArmorClass = lore?.ArmorClass,
            Observed = lore?.Observed ?? false,
            CanObserve = !(lore?.Observed ?? false) || player.Sheet.IsProficient(kind.StudySkill)
                && !(player.Lore.Kinds.GetValueOrDefault(kind.Id)?.Studied ?? false),
            KindLore = KindCardOf(player, kind),
        };
    }

    /// <summary>C5a: what the player knows of a kind.</summary>
    internal KindCardView KindCardOf(Actor player, KindDefinition kind)
    {
        var lore = player.Lore.Kinds.GetValueOrDefault(kind.Id);
        return new KindCardView
        {
            Name = kind.Name, Tells = lore?.Tells.ToArray() ?? Array.Empty<string>(),
            Attacks = lore?.Attacks.ToArray() ?? Array.Empty<string>(), Studied = lore?.Studied ?? false,
        };
    }
}
