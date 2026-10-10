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

    /// <summary>For engine-level tests only.</summary>
    internal Simulation Engine => _sim;

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
    /// Advances time until <paramref name="action"/> ends, processing every deadline in between
    /// (other actors keep acting). Stops at the end of the instant in which the action completes
    /// (<see cref="AdvanceOutcome.Completed"/>) or is cancelled (<see cref="AdvanceOutcome.Cancelled"/>),
    /// or after <paramref name="maxWait"/>, whichever comes first.
    /// </summary>
    public AdvanceResult AdvanceUntilCompleted(ActionId action, Duration maxWait)
    {
        var limit = _sim.TargetAfter(maxWait);
        if (!_sim.IsActive(action))
            return new AdvanceResult { Outcome = AdvanceOutcome.NotPending, Now = World.Now };

        if (_sim.ProcessUntil(limit, stopAfter: action) is { } ended)
            return new AdvanceResult { Outcome = ended, Now = World.Now };

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
            Actors = Freeze(w.Actors.Values.Select(a => ToView(a, w))),
            Stores = Freeze(w.Stores.Values.Select(s => new StoreView
            {
                Id = s.Id, Name = s.Name, Location = s.Location, Food = s.Food, Owner = s.Owner, Position = s.Position, Access = Freeze(s.Access),
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
                Authority = f.Authority,
                AvoidedTargets = Freeze(f.AvoidUntil.Select(a => new AvoidedTargetView { Store = a.Key, Until = a.Value })),
            })),
            RecentFacts = Freeze(w.RecentFacts.Select(f => new FactView { Id = f.Id, At = f.At, Kind = f.Kind, Description = f.Description })),
        };
    }

    private static ActorView ToView(Actor actor, WorldState w) => new()
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
                AssignedAt = r.AssignedAt, TakeAttempted = r.TakeAttempted, Aborted = r.Aborted,
            }
            : null,
        LastDecision = ToView(actor.LastDecision),
        Home = actor.Home,
        ArrivedAt = actor.ArrivedAt,
        Knowledge = Freeze(actor.Knowledge.Select(o => ToView(o, w))),
        GuardDuty = actor.GuardDuty is { } g ? new GuardDutyView { Store = g.Store, Since = g.Since, Until = g.Until } : null,
        Vigil = actor.Vigil is { } v ? new VigilView { Store = v.Store, Until = v.Until } : null,
        Sheet = actor.Sheet,
        Position = PositionOf(actor, w),
        Sneak = actor.Sneak,
        Torches = actor.Torches,
        TorchLitUntil = actor.TorchLitUntil,
        Move = MoveOf(actor),
        Claims = Freeze(actor.Claims.Select(c => new ClaimView { Thief = c.Thief, Store = c.Store, Owed = c.Owed })),
    };

    /// <summary>
    /// What the player can know right now: the map, what is visible in its area, its own observations
    /// and whom it could tell them to. Intentions, decisions and other actors' knowledge are not included.
    /// </summary>
    public PlayerView GetPlayerView()
    {
        var w = World;
        var player = w.Actors[w.Player];
        var travel = player.CurrentAction as TravelAction;
        // While travelling the player is still "on the road" from where it left: the origin's area until arrival.
        var area = player.Location is { } at ? w.Locations[at].Area
            : player.MapArea is { } mapped ? mapped
            : w.Locations[travel!.Origin].Area;

        bool InArea(LocationId? location) => location is { } l && w.Locations[l].Area == area;

        // Seeing takes light where the seen one is (ADAPTATION): a place in the area is visible when it is at least
        // dimly lit, the road only by daylight. In the dark, people right next to the player are only a presence.
        bool PlaceVisible(LocationId l) => Perception.LightAt(w.Locations[l], w.Now) >= Light.Dim;
        bool RoadVisible() => Perception.DaylightAt(w.Now) >= Light.Dim;
        // On a map (T6b) seeing takes line of sight and light on the other's square, at any distance.
        var map = player.MapArea is { } mapArea ? w.Maps[mapArea] : null;
        var me = PositionOf(player, w);
        bool SeenOnMap(GridPos square) => map is not null && me is { } eye && _sim.CanSee(map, eye, square);
        bool Visible(Actor a) => map is not null && me is not null
            ? a.MapArea == map.Area && PositionOf(a, w) is { } there && SeenOnMap(there)
            : VisibleByPlace(a);
        bool VisibleByPlace(Actor a) => a.Location is { } at
            ? InArea(at) && PlaceVisible(at)
            : a.MapArea is { } open ? open == area && RoadVisible() // open ground of a map: daylight only
            : a.CurrentAction is TravelAction t && (InArea(t.Origin) || InArea(t.Destination)) && RoadVisible();

        // Within talking distance: an adjacent square on a mapped area, the same place elsewhere.
        var nearby = w.Actors.Values.Where(a => a.Id != player.Id && _sim.CanTalk(player, a)).ToList();
        var present = nearby.Where(Visible).ToList();
        var light = map is not null && me is { } mine ? _sim.LightOn(map, mine)
            : player.Location is { } spot ? Perception.LightAt(w.Locations[spot], w.Now) : Perception.DaylightAt(w.Now);
        var options = present
            .SelectMany(recipient => player.Knowledge
                .Where(o => !o.ToldTo.Contains(recipient.Id))
                .Select(o => new ReportOptionView
                {
                    Recipient = recipient.Id,
                    RecipientName = recipient.Name,
                    Observation = o.Id,
                    Summary = Summary(o),
                }));

        return new PlayerView
        {
            Now = w.Now,
            Id = player.Id,
            Location = player.Location,
            Food = player.Food,
            Position = PositionOf(player, w),
            Move = MoveOf(player),
            Map = w.Maps.TryGetValue(area, out var areaMap) ? MapOf(areaMap, w) : null,
            MapLight = map is null ? null : Freeze(Enumerable.Range(0, map.Height).Select(y =>
                new string(Enumerable.Range(0, map.Width).Select(x => (char)('0' + (int)_sim.LightOn(map, new GridPos(x, y)))).ToArray()))),
            Sheet = player.Sheet,
            Action = player.CurrentAction is { } a
                ? new ActionView { Id = a.Id, Kind = a.Kind, StartedAt = a.StartedAt, CompletesAt = a.CompletesAt, Description = a.Description }
                : null,
            Travel = travel is null
                ? null
                : new TravelView { Action = travel.Id, Origin = travel.Origin, Destination = travel.Destination, DepartedAt = travel.StartedAt, ArrivesAt = travel.CompletesAt },
            Area = area,
            Light = light,
            Daylight = Perception.DaylightAt(w.Now),
            Sneaking = player.Sneak,
            Torches = player.Torches,
            TorchLitUntil = _sim.TorchLit(player) ? player.TorchLitUntil : null,
            Doors = map is null ? Array.Empty<DoorView>() : Freeze(map.Doors.Select(d => new DoorView { At = d.Key, Open = d.Value })),
            UnseenNearby = nearby.Count - present.Count,
            Areas = Freeze(w.Areas.Values.Select(x => new AreaView { Id = x.Id, Name = x.Name })),
            Locations = Freeze(w.Locations.Values.Select(l => new LocationView { Id = l.Id, Name = l.Name, Area = l.Area })),
            Routes = Freeze(w.Routes.Select(r => new RouteView { From = r.Key.From, To = r.Key.To, TravelTime = r.Value })),
            VisibleActors = Freeze(w.Actors.Values.Where(x => x.Id != player.Id && Visible(x)).Select(x => new VisibleActorView
            {
                Id = x.Id,
                Name = x.Name,
                Faction = x.Faction,
                Location = x.Location,
                Travel = x.CurrentAction is TravelAction t
                    ? new TravelView { Action = t.Id, Origin = t.Origin, Destination = t.Destination, DepartedAt = t.StartedAt, ArrivesAt = t.CompletesAt }
                    : null,
                Doing = map is not null || (player.Location is { } here && x.Location == here) ? OutwardDoing(x, w) : null,
                Position = PositionOf(x, w),
                Move = MoveOf(x),
                SeesYou = map is not null && me is { } mine && PositionOf(x, w) is { } theirs
                    ? Simulation.Sees(map, x, theirs, player, mine, _sim.LightOn(map, mine))
                    : null,
                Torch = _sim.TorchLit(x),
            })),
            VisibleStores = Freeze(w.Stores.Values.Where(s => s.Position is { } sp && map is not null
                ? w.Locations[s.Location].Area == map.Area && (SeenOnMap(sp) || me is { } m && m.IsAdjacentOrSame(sp))
                : InArea(s.Location) && (s.Location == player.Location || PlaceVisible(s.Location))).Select(s => new StoreView
            {
                Id = s.Id, Name = s.Name, Location = s.Location, Food = s.Food, Owner = s.Owner, Position = s.Position, Access = Freeze(s.Access),
            })),
            Observations = Freeze(player.Knowledge.Select(o => ToView(o, w))),
            ReportOptions = Freeze(options),
            RecentEvents = Freeze(w.RecentFacts.Where(f => f.Participants.Contains(player.Id)).TakeLast(10)
                .Select(f => new FactView { Id = f.Id, At = f.At, Kind = f.Kind, Description = f.Description })),
            PeopleHere = Freeze(present.Select(p => new PersonView
            {
                Id = p.Id,
                Name = p.Name,
                Doing = OutwardDoing(p, w),
                Topics = Freeze(player.Knowledge
                    .Where(o => !o.ToldTo.Contains(p.Id))
                    .Select(o => new TopicView { Kind = "Tell", Observation = o.Id, Summary = Summary(o) })),
            })),
        };
    }

    /// <summary>
    /// What an onlooker in the same place sees someone doing: the gesture, never its meaning. A theft looks like
    /// handling the stores; a conversation shows who talks, not what about. Knowing more takes perception.
    /// </summary>
    private static string? OutwardDoing(Actor actor, WorldState w) => actor.CurrentAction switch
    {
        null => null,
        TravelAction => "è in cammino",
        DepositFoodAction or TakeFoodAction => "armeggia con le scorte",
        ReportAction r => $"parla con {w.Actors[r.Recipient].Name}",
        GuardAction => "sorveglia il deposito",
        ConfiscateAction c => $"parla con {w.Actors[c.Target].Name}",
        WaitAction wait => wait.Description switch
        {
            "Lavora" => "lavora",
            "Riposa" => "riposa",
            _ => "sta fermo",
        },
        _ => "sta facendo altro",
    };

    private static GridPos? PositionOf(Actor a, WorldState w) => a.CurrentAction is MoveAction m ? m.PositionAt(w.Now) : a.Position;

    private static MoveView? MoveOf(Actor a) => a.CurrentAction is MoveAction m
        ? new MoveView { From = m.From, Path = Freeze(m.Path), DepartedAt = m.StartedAt, Speed = m.Speed, Stealthy = m.Stealthy }
        : null;

    private static MapView MapOf(GridMap map, WorldState w)
    {
        var rows = map.Rows.Select(r => r.ToCharArray()).ToArray();
        foreach (var store in w.Stores.Values.Where(s => s.Position is { } p
            && w.Locations[s.Location].Area == map.Area && map.InBounds(p)))
            rows[store.Position!.Value.Y][store.Position.Value.X] = 'S';
        return new MapView
        {
            Area = map.Area, Width = map.Width, Height = map.Height,
            Rows = Freeze(rows.Select(r => new string(r))),
            Zones = new SortedDictionary<char, LocationId>(map.Zones.ToDictionary(z => z.Key, z => z.Value)).AsReadOnly(),
            Exits = Freeze(map.Exits.Select(e => new ExitView
            {
                Location = e.Key, Name = w.Locations[e.Key].Name, At = e.Value,
                Routes = Freeze(w.Routes.Where(r => r.Key.Item1 == e.Key).OrderBy(r => r.Key.Item2.Value, StringComparer.Ordinal)
                    .Select(r => new RouteView { From = r.Key.Item1, To = r.Key.Item2, TravelTime = r.Value })),
            })),
        };
    }

    private static string Summary(Observation o) =>
        $"furto di {o.Amount} razioni da {o.StoreName}, giorno {o.ObservedAt.Day + 1} alle {o.ObservedAt.Hour:00}:{o.ObservedAt.Minute:00}" +
        (o.ThiefName is { } thief ? $" ({thief})" : " (ladro sconosciuto)");

    private static ObservationView ToView(Observation o, WorldState w) => new()
    {
        Id = o.Id,
        Origin = o.Origin,
        Kind = "Theft",
        Store = o.Store,
        StoreName = o.StoreName,
        Location = o.Location,
        Amount = o.Amount,
        Thief = o.Thief,
        ThiefName = o.ThiefName,
        ObservedAt = o.ObservedAt,
        LearnedAt = o.LearnedAt,
        Source = o.Source,
        SourceName = o.Source is { } s ? w.Actors[s].Name : null,
        Perceived = o.Perceived.ToString(),
        ToldTo = Freeze(o.ToldTo),
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
