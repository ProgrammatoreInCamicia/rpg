using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// The watch island's store shed (Blackmarsh 0912, adapted), shortly before the Vasan raid: the scenario file loaded and
/// advanced as any game would be. Nothing is staged: the raid comes from the Vasan's own upkeep and policy.
/// </summary>
public static class BlackmarshWatchhouse
{
    public const string ScenarioId = "blackmarsh-watchhouse";

    /// <summary>The time of day the situation opens at, before the Vasan upkeep (19:59) and their evaluation (20:00), with about three minutes to act before the raider reaches the store.</summary>
    public static readonly Duration Opening = Duration.FromMinutes(19 * 60 + 57);

    public static SimulationSession CreateSession()
    {
        var session = SimulationSession.Create(ScenarioFile.Load(ScenarioId));
        session.Advance(Opening);
        return session;
    }

    /// <summary>The ids of the scenario file, for tests and the client's presentation.</summary>
    public static class Ids
    {
        public static readonly AreaId Island = new("bm_watch_island");
        public static readonly LocationId Path = new("bm_watch_path");
        public static readonly LocationId Shed = new("bm_watch_shed");
        public static readonly LocationId Landing = new("bm_watch_landing");
        public static readonly FactionId Islanders = new("bm_watch_islanders");
        public static readonly FactionId Vasan = new("bm_watch_vasan");
        public static readonly StoreId ShedStore = new("bm_watch_shed_store");
        public static readonly StoreId VasanCache = new("bm_watch_vasan_cache");
        public static readonly ActorId Player = new("bm_watch_player");
        public static readonly ActorId Raider = new("bm_watch_vasan_raider");
        public const string ShedKey = "bm-watch-shed-key";
        public static readonly GridPos PlayerStart = new(1, 6);
        public static readonly GridPos ShedDoor = new(6, 6);
        public static readonly GridPos InnerDoor = new(9, 6);
        public static readonly GridPos WindowInside = new(11, 6);
        public static readonly GridPos WindowOutside = new(12, 6);
        public static readonly GridPos StoreAccess = new(11, 5);
    }
}
