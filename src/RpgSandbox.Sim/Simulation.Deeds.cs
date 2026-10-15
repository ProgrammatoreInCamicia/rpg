using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>C4: how a deed is told, whatever its kind (the words of reports, journals and decisions).</summary>
internal static class Deeds
{
    /// <summary>"del furto da Scorte del granaio", "dello scontro al Granaio", "dell'uccisione al Granaio".</summary>
    public static string About(Observation o) => o.Kind switch
    {
        DeedKind.Theft => $"del furto da {o.StoreName}",
        DeedKind.Assault => o.Perceived == PerceptionMode.Heard ? $"dei rumori di lotta{Where(o)}" : $"dello scontro{Where(o)}",
        DeedKind.BreakIn => o.Perceived == PerceptionMode.Heard ? $"dei colpi contro una porta{Where(o)}" : $"dello scasso{Where(o)}",
        _ => $"dell'uccisione{Where(o)}",
    };

    /// <summary>"furto da Scorte del granaio", "scontro", "rumori di lotta per strada", "uccisione".</summary>
    public static string Title(Observation o) => o.Kind switch
    {
        DeedKind.Theft => $"furto da {o.StoreName}",
        DeedKind.Assault => o.Perceived == PerceptionMode.Heard ? $"rumori di lotta{Where(o)}" : $"scontro{Where(o)}",
        DeedKind.BreakIn => o.Perceived == PerceptionMode.Heard ? $"colpi contro una porta{Where(o)}" : $"scasso{Where(o)}",
        _ => $"uccisione{Where(o)}",
    };

    /// <summary>"del furto", "dello scontro", "dell'uccisione".</summary>
    public static string AboutBriefly(Observation o) => o.Kind switch
    {
        DeedKind.Theft => "del furto",
        DeedKind.Assault => "dello scontro",
        DeedKind.BreakIn => "dello scasso",
        _ => "dell'uccisione",
    };

    /// <summary>Who did what to whom, as far as known: "Razziatore", "ladro sconosciuto", "Paladino contro Contadino".</summary>
    public static string Who(Observation o) => o.Kind switch
    {
        DeedKind.Theft => o.CulpritName ?? "ladro sconosciuto",
        DeedKind.Assault when o.CulpritName is { } c => $"l'ha cominciato {c}{(o.VictimName is { } v ? $" contro {v}" : "")}",
        DeedKind.Assault when o.Involved.Count > 0 => $"si battevano {string.Join(" e ", o.Involved.Select(i => i.Name))}, non si sa chi ha cominciato",
        DeedKind.Assault => "nessun nome, solo rumori",
        DeedKind.BreakIn => o.CulpritName is { } c ? $"forzava {c}" : "nessun nome, solo colpi",
        _ => $"{o.CulpritName ?? "qualcuno"} ha ucciso {o.VictimName ?? "qualcuno"}",
    };

    private static string Where(Observation o) => o.Location is null ? " per strada" : "";

    /// <summary>The copy a listener keeps of what it was told: the same content, its own id, who told it and when.</summary>
    public static Observation CopyFor(Observation told, ObservationId id, GameTime now, ActorId source)
    {
        var copy = new Observation
        {
            Id = id, Origin = told.Origin, Kind = told.Kind, Store = told.Store, StoreName = told.StoreName, Amount = told.Amount,
            Location = told.Location, Square = told.Square, Culprit = told.Culprit, CulpritName = told.CulpritName,
            Victim = told.Victim, VictimName = told.VictimName, Encounter = told.Encounter,
            ObservedAt = told.ObservedAt, Perceived = told.Perceived, LearnedAt = now, Source = source, Fact = told.Fact,
        };
        copy.Involved.AddRange(told.Involved);
        return copy;
    }
}

// C4a: who perceives a fight, and what it learns.
internal sealed partial class Simulation
{
    /// <summary>
    /// Everyone not fighting, awake, on the fight's map perceives this moment of it: who sees the attacker or the target
    /// learns of a fight between those it sees, and who started it only if this is the start (<paramref name="opening"/>);
    /// who only hears it (a fight is loud: no check) learns that there is fighting around there. One observation per
    /// fight and witness, growing as it watches.
    /// </summary>
    private void WitnessFight(Encounter e, Actor attacker, Actor target, bool opening, FactId fact)
    {
        var map = World.Maps[e.Area];
        var square = CurrentPosition(target)!.Value;
        // A combatant cannot watch its own fight from outside, but the one attacked at the start knows who attacked it
        // (if it saw them). Not who started it, nor who joined later (they saw it, if at all, as bystanders).
        var attacked = e.Order.FirstOrDefault(c => c.Side == 1 && c.JoinedInRound is null)?.Actor;
        foreach (var witness in World.Actors.Values.Where(a => a.MapArea == e.Area && a.Conscious
                     && (EncounterOf(a) is null || (a.Id == target.Id && a.Id == attacked)))
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
        {
            var seesAttacker = SeesActor(witness, attacker);
            var seesTarget = witness.Id == target.Id || SeesActor(witness, target);
            var heard = !seesAttacker && !seesTarget && CurrentPosition(witness) is { } ear && WithinEarshot(map, ear, square);
            if (!seesAttacker && !seesTarget && !heard)
                continue;
            var known = witness.Knowledge.FirstOrDefault(o => o.Kind == DeedKind.Assault && o.Encounter == e.Id && o.Source is null);
            var isNew = known is null;
            known ??= NewDeed(witness, DeedKind.Assault, e, fact);
            if (heard)
            {
                if (isNew)
                {
                    known.Perceived = PerceptionMode.Heard;
                    known.Square = Approximate(map, square);
                    known.Location = map.ZoneAt(square);
                }
            }
            else
            {
                // Where it happened is where the witness first saw it (a fight heard first is now placed exactly).
                if (isNew || known.Perceived == PerceptionMode.Heard)
                {
                    known.Square = square;
                    known.Location = map.ZoneAt(square);
                }
                known.Perceived = PerceptionMode.Seen;
                if (seesAttacker)
                    See(known, attacker);
                if (seesTarget)
                    See(known, target);
                if (opening && seesAttacker)
                    (known.Culprit, known.CulpritName) = (attacker.Id, attacker.Name);
                if (opening && seesTarget)
                    (known.Victim, known.VictimName) = (target.Id, target.Name);
            }
            Alarm(witness, isNew); // C4c: it stops to decide whether to join, look or keep away
            if (!isNew)
                continue;
            World.RecordFact(heard ? "FightHeard" : "FightWitnessed",
                heard ? $"{witness.Name} sente rumori di lotta{(known.Location is { } l ? $" verso {World.Locations[l].Name}" : " lì vicino")}."
                : witness.Id == target.Id ? $"{witness.Name} è aggredito: {Deeds.Who(known)}."
                : $"{witness.Name} vede uno scontro: {Deeds.Who(known)}.", witness.Id);
        }
    }

    /// <summary>A death blow seen: a deed of its own, with who struck it and who died, as far as the witness saw them.</summary>
    private void WitnessKilling(Encounter e, Actor killer, Actor victim, FactId fact)
    {
        var square = CurrentPosition(victim)!.Value;
        foreach (var witness in World.Actors.Values.Where(a => a.MapArea == e.Area && a.Conscious && EncounterOf(a) is null)
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
        {
            var seesKiller = SeesActor(witness, killer);
            var seesVictim = SeesActor(witness, victim) || (CurrentPosition(witness) is { } eye && CanSee(World.Maps[e.Area], eye, square));
            if (!seesKiller && !seesVictim)
                continue;
            var deed = NewDeed(witness, DeedKind.Killing, e, fact);
            deed.Square = square;
            deed.Location = World.Maps[e.Area].ZoneAt(square);
            if (seesKiller)
            {
                (deed.Culprit, deed.CulpritName) = (killer.Id, killer.Name);
                See(deed, killer);
            }
            if (seesVictim)
            {
                (deed.Victim, deed.VictimName) = (victim.Id, victim.Name);
                See(deed, victim);
            }
            World.RecordFact("KillingWitnessed", $"{witness.Name} vede un'uccisione: {Deeds.Who(deed)}.", witness.Id);
            Alarm(witness, firstNews: true);
        }
    }

    private Observation NewDeed(Actor witness, DeedKind kind, Encounter e, FactId fact)
    {
        var id = new ObservationId(World.NextObservationId++);
        var deed = new Observation
        {
            Id = id, Origin = id, Kind = kind, Encounter = e.Id, ObservedAt = World.Now, LearnedAt = World.Now, Fact = fact,
        };
        witness.Knowledge.Add(deed);
        return deed;
    }

    private static void See(Observation o, Actor who)
    {
        if (o.Involved.All(i => i.Id != who.Id))
            o.Involved.Add((who.Id, who.Name));
    }

    /// <summary>A sound's origin as a listener can tell it: the centre of the 5×5 block of squares it came from.</summary>
    private static GridPos Approximate(GridMap map, GridPos square) =>
        new(Math.Min(map.Width - 1, square.X / 5 * 5 + 2), Math.Min(map.Height - 1, square.Y / 5 * 5 + 2));
}
