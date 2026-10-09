using RpgSandbox.Sim.Persistence;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Api;

/// <summary>
/// The only entry point the client uses. It reads the world through views and changes it only
/// through commands. Time passes only through <see cref="Advance"/> and <see cref="AdvanceUntilCompleted"/>.
/// </summary>
public sealed class SimulationSession
{
    private readonly Simulation _sim;

    private SimulationSession(Simulation sim) => _sim = sim;

    private WorldState World => _sim.World;

    public static SimulationSession Create(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var sim = new Simulation(WorldState.FromScenario(scenario));
        sim.Bootstrap();
        return new SimulationSession(sim);
    }

    public GameTime Now => World.Now;
    public ActorId Player => World.Player;

    /// <summary>Validates and starts the action requested by <paramref name="command"/>. Never advances time.</summary>
    public CommandResult Execute(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _sim.Execute(command);
    }

    /// <summary>
    /// Advances time by <paramref name="duration"/>, processing every deadline in between in order.
    /// Advance(a + b) is equivalent to Advance(a) followed by Advance(b).
    /// </summary>
    public void Advance(Duration duration)
    {
        var target = _sim.TargetAfter(duration);
        _sim.ProcessUntil(target, stopAfter: null);
        World.Now = target;
    }

    /// <summary>
    /// Advances time until <paramref name="action"/> completes, processing every deadline in between
    /// (other actors keep acting). Stops at the end of the instant in which the action completes, or
    /// after <paramref name="maxWait"/>, whichever comes first.
    /// </summary>
    public AdvanceResult AdvanceUntilCompleted(ActionId action, Duration maxWait)
    {
        var limit = _sim.TargetAfter(maxWait);
        if (!World.Scheduler.Contains(action) || !World.Actors.Values.Any(a => a.CurrentAction?.Id == action))
            return new AdvanceResult { Outcome = AdvanceOutcome.NotPending, Now = World.Now };

        if (_sim.ProcessUntil(limit, stopAfter: action))
            return new AdvanceResult { Outcome = AdvanceOutcome.Completed, Now = World.Now };

        World.Now = limit;
        return new AdvanceResult { Outcome = AdvanceOutcome.TimeLimitReached, Now = World.Now };
    }

    /// <summary>Writes a complete, self-contained snapshot of the game. Call between steps, never during one.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        SaveGame.Write(World, stream);
    }

    /// <summary>
    /// Reads a snapshot written by <see cref="Save"/> into a NEW session. The caller's current session is
    /// never touched, so a failed load leaves the running game intact.
    /// </summary>
    public static LoadResult TryLoad(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return new LoadResult { Session = new SimulationSession(new Simulation(SaveGame.Read(stream))) };
        }
        catch (SaveGameException e)
        {
            return new LoadResult { Error = e.Message };
        }
    }

    public WorldView GetWorldView()
    {
        var w = World;
        return new WorldView
        {
            Now = w.Now,
            Player = w.Player,
            Areas = Freeze(w.Areas.Values.Select(a => new AreaView { Id = a.Id, Name = a.Name })),
            Locations = Freeze(w.Locations.Values.Select(l => new LocationView { Id = l.Id, Name = l.Name, Area = l.Area })),
            Routes = Freeze(w.Routes.Select(r => new RouteView { From = r.Key.From, To = r.Key.To, TravelTime = r.Value })),
            Actors = Freeze(w.Actors.Values.Select(ToView)),
            Stores = Freeze(w.Stores.Values.Select(s => new StoreView
            {
                Id = s.Id, Name = s.Name, Location = s.Location, Food = s.Food, Owner = s.Owner,
            })),
            Factions = Freeze(w.Factions.Values.Select(f => new FactionView
            {
                Id = f.Id,
                Name = f.Name,
                HomeStore = f.HomeStore,
                DailyUpkeep = f.DailyUpkeep,
                Members = Freeze(w.MembersOf(f.Id).Select(m => m.Id)),
                NextEvaluation = f.NextEvaluation,
                LastDecision = ToView(f.LastDecision),
            })),
            RecentFacts = Freeze(w.RecentFacts.Select(f => new FactView { Id = f.Id, At = f.At, Kind = f.Kind, Description = f.Description })),
        };
    }

    private static ActorView ToView(Actor actor) => new()
    {
        Id = actor.Id,
        Name = actor.Name,
        IsPlayer = actor.IsPlayer,
        Faction = actor.Faction,
        Location = actor.Location,
        Food = actor.Food,
        Action = actor.CurrentAction is { } a
            ? new ActionView { Id = a.Id, Kind = a.Kind, StartedAt = a.StartedAt, CompletesAt = a.CompletesAt, Description = a.Description }
            : null,
        Travel = actor.CurrentAction is TravelAction t
            ? new TravelView { Action = t.Id, Origin = t.Origin, Destination = t.Destination, DepartedAt = t.StartedAt, ArrivesAt = t.CompletesAt }
            : null,
        Assignment = actor.Assignment is { } r
            ? new AssignmentView
            {
                Kind = "Raid", Faction = r.Faction, Target = r.Target, Home = r.Home, Amount = r.Amount,
                AssignedAt = r.AssignedAt, TakeAttempted = r.TakeAttempted,
            }
            : null,
        LastDecision = ToView(actor.LastDecision),
    };

    private static DecisionView? ToView(DecisionTrace? trace) => trace is null
        ? null
        : new DecisionView { At = trace.At, Rule = trace.Rule, Reason = trace.Reason, Inputs = Freeze(trace.Inputs) };

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
}

public sealed record LoadResult
{
    public bool Success => Session is not null;

    /// <summary>The loaded game, when successful.</summary>
    public SimulationSession? Session { get; init; }

    /// <summary>Readable reason, when the load failed.</summary>
    public string? Error { get; init; }
}
