using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R1: Short and Long Rests, Hit Point Dice (SRD 5.2.1 "Short Rest", "Long Rest", "Hit Point Dice").
internal sealed partial class Simulation
{
    private static readonly Duration ShortRestTime = Duration.FromHours(1);
    private static readonly Duration LongRestTime = Duration.FromHours(8);

    /// <summary>SRD Long Rest: after finishing one, at least 16 hours before starting another.</summary>
    private static readonly Duration LongRestGap = Duration.FromHours(16);

    /// <summary>ADAPTATION: an NPC heals fully after this long without damage or fights (it keeps no Hit Point Dice).</summary>
    private static readonly Duration NpcRecovery = Duration.FromHours(8);

    /// <summary>A character's Hit Point Dice: as many as its level, of its class's die; null for a creature.</summary>
    internal (int Max, int Die)? HitDiceOf(Actor actor) =>
        World.LevelOf(actor) is { } level && World.Content.Characters.TryGetValue(actor.Build, out var character)
            ? (level, World.Content.Classes[character.Class].HitDie)
            : null;

    /// <summary>From when the next Long Rest can be started; null if it can be now.</summary>
    internal GameTime? LongRestFrom(Actor actor) =>
        actor.LastLongRestEnd is { } end && end.Plus(LongRestGap) > World.Now ? end.Plus(LongRestGap) : null;

    /// <summary>Asleep in a Long Rest (ADAPTATION: the whole of it): it sees nothing.</summary>
    internal static bool Asleep(Actor actor) => actor.CurrentAction is RestAction { Long: true };

    private CommandResult StartRest(RestCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (EncounterOf(actor) is not null)
            return CommandResult.Rejected(RejectionReason.InCombat, "Non puoi riposare durante uno scontro.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (command.Long && LongRestFrom(actor) is { } from)
            return CommandResult.Rejected(RejectionReason.TooSoon,
                $"Hai riposato a lungo da poco: potrai farlo di nuovo dalle {from.Hour:00}:{from.Minute:00}.");
        var remaining = HitDiceOf(actor) is { } dice ? dice.Max - actor.HitDiceSpent : 0;
        if (!command.Long && (command.HitDice < 0 || command.HitDice > remaining))
            return CommandResult.Rejected(RejectionReason.InvalidAmount, $"Puoi spendere da 0 a {remaining} Dadi Vita.");

        var rest = new RestAction
        {
            Id = World.AllocateActionId(), Actor = actor.Id, StartedAt = World.Now,
            CompletesAt = World.Now.Plus(command.Long ? LongRestTime : ShortRestTime),
            Long = command.Long, HitDice = command.Long ? 0 : command.HitDice,
            Description = command.Long ? "Dorme" : "Riposa",
        };
        Begin(actor, rest);
        World.RecordFact("RestStarted", $"{actor.Name} {(command.Long ? "si mette a dormire" : "si ferma a riposare")}.", actor.Id);
        if (actor.MapArea is { } area)
            NoteSensesChange(World.Maps[area]); // asleep, it stops seeing
        return CommandResult.Started(rest.Id, rest.CompletesAt,
            command.Long ? $"{actor.Name} si mette a dormire per 8 ore." : $"{actor.Name} riposa per un'ora.");
    }

    /// <summary>The end of a rest: a Short Rest spends its Hit Point Dice; a Long Rest gives back everything.</summary>
    private void CompleteRest(Actor actor, RestAction rest)
    {
        if (rest.Long)
        {
            // SRD Long Rest: all lost Hit Points and all spent Hit Point Dice are regained.
            var healed = actor.Vitality.Heal(actor.Vitality.Maximum);
            actor.HitDiceSpent = 0;
            if (actor.Exhaustion > 0)
                AddExhaustion(actor, -1, "ha dormito"); // R2, SRD Exhaustion: a Long Rest takes a level away
            actor.LastLongRestEnd = World.Now;
            World.RecordFact("RestCompleted", $"{actor.Name} si sveglia riposato: recupera {healed} punti ferita.", actor.Id);
        }
        else
        {
            var (healed, spent) = SpendHitDice(actor, rest.HitDice);
            World.RecordFact("RestCompleted", spent == 0 ? $"{actor.Name} finisce di riposare."
                : $"{actor.Name} finisce di riposare: {spent} Dadi Vita, {healed} punti ferita.", actor.Id);
        }
        if (actor.MapArea is { } area)
            NoteSensesChange(World.Maps[area]);
    }

    /// <summary>
    /// SRD Hit Point Dice: each one spent is rolled, plus the Constitution modifier (ADAPTATION: at least 0), in Hit Points.
    /// ADAPTATION: the dice are spent one at a time and no more once the Hit Points are full.
    /// </summary>
    private (int Healed, int Spent) SpendHitDice(Actor actor, int dice)
    {
        if (HitDiceOf(actor) is not { } of)
            return (0, 0);
        var constitution = Abilities.Modifier(actor.Sheet.Constitution);
        var (healed, spent) = (0, 0);
        while (spent < dice && actor.HitDiceSpent < of.Max && actor.Vitality.Current < actor.Vitality.Maximum)
        {
            healed += actor.Vitality.Heal(Math.Max(0, World.Rng.Roll(of.Die) + constitution));
            actor.HitDiceSpent++;
            spent++;
        }
        return (healed, spent);
    }

    /// <summary>
    /// SRD: a rest stopped early. A Short Rest gives nothing; a Long Rest of at least an hour gives a Short Rest's
    /// benefits (no Hit Point Dice were chosen, so none are spent).
    /// </summary>
    private void InterruptRest(Actor actor, RestAction rest, string why)
    {
        World.RecordFact("RestInterrupted", $"{actor.Name} interrompe il riposo: {why}.", actor.Id);
        if (actor.MapArea is { } area)
            NoteSensesChange(World.Maps[area]);
    }

    /// <summary>R1: it took damage or was in a fight: a rest stops, and an NPC's recovery starts over.</summary>
    private void Harmed(Actor actor, string why)
    {
        actor.LastHarmAt = World.Now;
        if (actor.CurrentAction is RestAction)
            CancelAction(actor, why);
    }

    /// <summary>R1, ADAPTATION: a wounded, conscious NPC that went 8 hours without damage or fights has healed.</summary>
    private void RecoverNpc(Actor npc)
    {
        if (npc.IsPlayer || !npc.Conscious || npc.Vitality.Current >= npc.Vitality.Maximum || EncounterOf(npc) is not null)
            return;
        if (npc.LastHarmAt is { } harmed && World.Now.Since(harmed).Ticks < NpcRecovery.Ticks)
            return;
        npc.Vitality.Heal(npc.Vitality.Maximum);
    }
}
