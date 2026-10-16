using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R3a: the Paladin's features up to level 5 without magic (SRD 5.2.1 p. 53–56): Lay On Hands, Fighting Style, Ability
// Score Improvement, Extra Attack, changing the weapon masteries after a Long Rest; the Bonus Action of the turn.
internal sealed partial class Simulation
{
    /// <summary>The character's class (null for a creature).</summary>
    private string? ClassOf(Actor actor) => World.Content.Characters.TryGetValue(actor.Build, out var c) ? c.Class : null;

    private bool HasFeature(Actor actor, string feature) => ClassFeatures.Has(ClassOf(actor), World.LevelOf(actor), feature);

    /// <summary>Whether it has anything to spend a Bonus Action on (SRD: only if a rule gives it one).</summary>
    private bool HasBonusAction(Actor actor) => HasFeature(actor, ClassFeatures.LayOnHands);

    /// <summary>R3a, SRD Extra Attack: the first attack takes the action and leaves the others of the Attack action.</summary>
    private void TakeAttack(Encounter e, Actor actor)
    {
        var c = CombatantOf(e, actor.Id);
        if (!c.ActionUsed)
        {
            c.ActionUsed = true;
            c.AttacksLeft = ClassFeatures.AttacksPerAction(ClassOf(actor), World.LevelOf(actor)) - 1;
        }
        else if (c.AttacksLeft > 0)
            c.AttacksLeft--;
    }

    // ---------------------------------------------------------------- Lay On Hands

    /// <summary>The Lay On Hands pool: 5 × the Paladin level; null without the feature.</summary>
    internal (int Remaining, int Max)? LayOnHandsPool(Actor actor) =>
        HasFeature(actor, ClassFeatures.LayOnHands) && World.LevelOf(actor) is { } level
            ? (Math.Max(0, 5 * level - actor.LayOnHandsSpent), 5 * level)
            : null;

    /// <summary>
    /// SRD Lay On Hands: a Bonus Action touching a creature (oneself included): Hit Points from the pool, or 5 points to
    /// end the Poisoned condition (those restore nothing). Outside a fight it is done at once.
    /// </summary>
    private CommandResult LayOnHands(LayOnHandsCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor) || !World.Actors.TryGetValue(command.Target, out var target))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, "Attore sconosciuto.");
        if (LayOnHandsPool(actor) is not { } pool)
            return CommandResult.Rejected(RejectionReason.UnknownCommand, $"{actor.Name} non sa imporre le mani.");
        if (target.Vitality.State == LifeState.Dead)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, $"{target.Name} è morto.");
        if (target.Id != actor.Id && (actor.MapArea != target.MapArea || CurrentSpot(actor) is not { } a || CurrentSpot(target) is not { } b
                || !WithinFiveFeet(World.Maps[actor.MapArea!.Value], a, b)))
            return CommandResult.Rejected(RejectionReason.OutOfReach, $"Devi toccare {target.Name}: avvicinati.");
        var cost = command.CurePoison ? 5 : command.Amount;
        if (cost <= 0 || cost > pool.Remaining)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, $"Nella riserva restano {pool.Remaining} punti.");
        if (command.CurePoison && !Has(target, ConditionKind.Poisoned))
            return CommandResult.Rejected(RejectionReason.NotHeld, $"{target.Name} non è avvelenato.");
        if (!command.CurePoison && target.Vitality.Current >= target.Vitality.Maximum)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, $"{target.Name} non è ferito.");
        var e = EncounterOf(actor);
        if (e is not null)
        {
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            if (CombatantOf(e, actor.Id).BonusActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione bonus in questo turno.");
            CombatantOf(e, actor.Id).BonusActionUsed = true;
        }
        else if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");

        actor.LayOnHandsSpent += cost;
        string text;
        if (command.CurePoison)
        {
            RemoveCondition(e, target, ConditionKind.Poisoned, "non è più avvelenato");
            text = $"{actor.Name} impone le mani su {target.Name}: il veleno svanisce.";
        }
        else
        {
            var healed = target.Vitality.Heal(cost);
            actor.LayOnHandsSpent -= cost - healed; // what could not be restored stays in the pool
            text = $"{actor.Name} impone le mani su {target.Name}: {healed} punti ferita.";
        }
        if (e is not null)
            Record(e, CombatLogKind.Condition, actor, target, (_, _) => text);
        World.RecordFact("LayOnHands", text, actor.Id, target.Id);
        return new CommandResult { Message = text };
    }

    // ---------------------------------------------------------------- choices

    /// <summary>The choices the character has to make, or may make now.</summary>
    internal IReadOnlyList<ChoiceView> PendingChoicesOf(Actor actor)
    {
        var choices = new List<ChoiceView>();
        if (HasFeature(actor, ClassFeatures.FightingStyle) && actor.Choices.FightingStyle is null)
            choices.Add(new ChoiceView
            {
                Id = "fighting-style", Kind = "FightingStyle", Title = "Stile di combattimento", Options = ClassFeatures.FightingStyles, Picks = 1,
            });
        var improvements = ClassFeatures.AbilityScoreImprovements(ClassOf(actor), World.LevelOf(actor));
        if (actor.Choices.AbilityIncreases.Count < 2 * improvements)
            choices.Add(new ChoiceView
            {
                Id = $"ability-scores-{actor.Choices.AbilityIncreases.Count / 2 + 1}", Kind = "AbilityScores", Title = "Aumento dei punteggi",
                Options = Enum.GetNames<Ability>(), Picks = 2,
            });
        if (actor.MasteryChangeOpen && ClassOf(actor) is { } cls && World.Content.Classes[cls].Masteries > 0)
            choices.Add(new ChoiceView
            {
                Id = "weapon-mastery", Kind = "WeaponMastery", Title = "Padronanza delle armi", Optional = true,
                Options = MasteryOptions(actor), Picks = World.Content.Classes[cls].Masteries,
            });
        return choices;
    }

    /// <summary>The weapons (item ids) with a mastery the character is proficient with.</summary>
    private IReadOnlyList<string> MasteryOptions(Actor actor)
    {
        var cls = World.Content.Classes[ClassOf(actor)!];
        return World.Content.Items.Values
            .Where(i => i.Weapon is { } w && cls.Weapons.Contains(w.Category))
            .Select(i => i.Id).Order(StringComparer.Ordinal).ToArray();
    }

    private CommandResult Choose(ChooseCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (EncounterOf(actor) is not null)
            return CommandResult.Rejected(RejectionReason.InCombat, "Non durante uno scontro.");
        if (PendingChoicesOf(actor).FirstOrDefault(c => c.Id == command.Choice) is not { } choice)
            return CommandResult.Rejected(RejectionReason.UnknownCommand, "Non c'è questa scelta da fare.");
        var picked = command.Options;
        if (picked.Count != choice.Picks || picked.Any(o => !choice.Options.Contains(o))
            || choice.Kind != "AbilityScores" && picked.Distinct().Count() != picked.Count)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, $"Scegli {choice.Picks} fra: {string.Join(", ", choice.Options)}.");
        switch (choice.Kind)
        {
            case "FightingStyle":
                actor.Choices = actor.Choices with { FightingStyle = picked[0] };
                break;
            case "AbilityScores":
                var raised = picked.Select(Enum.Parse<Ability>).ToArray();
                if (raised.Any(a => actor.Sheet.Score(a) + raised.Count(x => x == a) > 20))
                    return CommandResult.Rejected(RejectionReason.InvalidAmount, "Un punteggio non può superare 20.");
                actor.Choices = actor.Choices with { AbilityIncreases = actor.Choices.AbilityIncreases.Concat(raised).ToArray() };
                break;
            case "WeaponMastery":
                actor.Choices = actor.Choices with { Masteries = picked.ToArray() };
                actor.MasteryChangeOpen = false;
                break;
        }
        var oldMaximum = actor.Profile.MaxHitPoints;
        World.Refresh(actor);
        actor.Vitality.RaiseMaximum(actor.Profile.MaxHitPoints - oldMaximum); // SRD: a higher Constitution raises it at once
        World.RecordFact("ChoiceMade", $"{actor.Name}: {choice.Title}, {string.Join(", ", picked)}.", actor.Id);
        return new CommandResult { Message = $"{choice.Title}: {string.Join(", ", picked)}." };
    }

    /// <summary>R3a, at the end of a Long Rest: the Lay On Hands pool is full again, and the masteries may be changed.</summary>
    private void FeaturesAfterLongRest(Actor actor)
    {
        actor.LayOnHandsSpent = 0;
        actor.MasteryChangeOpen = ClassOf(actor) is { } cls && World.Content.Classes[cls].Masteries > 0;
    }
}
