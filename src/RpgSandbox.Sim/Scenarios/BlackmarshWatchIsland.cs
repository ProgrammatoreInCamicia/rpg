using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// The watch island (Blackmarsh 0912, adapted) as one small region: the fishers' hamlet, the store shed and the cove,
/// three maps joined by paths in a single world. The scenario file is loaded and run from midnight as any game would be:
/// the fisher has really walked to his shift at the shed, and the Vasan raid will come from their own upkeep and policy.
/// </summary>
public static class BlackmarshWatchIsland
{
    public const string ScenarioId = "blackmarsh-watch-island";

    /// <summary>The time of day a new game opens at: the fisher still at the shed, the Vasan upkeep (19:59) and evaluation (20:00) near.</summary>
    public static readonly Duration Opening = Duration.FromMinutes(19 * 60 + 35);

    public static SimulationSession CreateSession()
    {
        var session = SimulationSession.Create(ScenarioFile.Load(ScenarioId));
        session.Advance(Opening);
        return session;
    }

    /// <summary>The ids of the scenario file, for tests and the client's presentation.</summary>
    public static class Ids
    {
        public static readonly AreaId Hamlet = new("bm_isle_hamlet");
        public static readonly AreaId ShedArea = new("bm_isle_shed_area");
        public static readonly AreaId Cove = new("bm_isle_cove");

        public static readonly LocationId HamletFork = new("bm_isle_hamlet_fork");
        public static readonly LocationId CommonHouse = new("bm_isle_common_house");
        public static readonly LocationId Well = new("bm_isle_well");
        public static readonly LocationId ShedFork = new("bm_isle_shed_fork");
        public static readonly LocationId Shed = new("bm_isle_shed");
        public static readonly LocationId CoveFork = new("bm_isle_cove_fork");
        public static readonly LocationId Landing = new("bm_isle_landing");

        public static readonly FactionId Islanders = new("bm_isle_islanders");
        public static readonly FactionId Vasan = new("bm_isle_vasan");
        public static readonly StoreId Pantry = new("bm_isle_pantry");
        public static readonly StoreId ShedStore = new("bm_isle_shed_store");
        public static readonly StoreId VasanCache = new("bm_isle_vasan_cache");

        public static readonly ActorId Player = new("bm_isle_player");
        public static readonly ActorId Keeper = new("bm_isle_keeper");
        public static readonly ActorId Fisher = new("bm_isle_fisher");
        public static readonly ActorId Raider = new("bm_isle_raider");
        public const string ShedKey = BlackmarshWatchhouse.Ids.ShedKey;

        public static readonly GridPos HamletExit = new(19, 6);
        public static readonly GridPos ShedExit = new(0, 6);
        public static readonly GridPos CoveExit = new(0, 6);
        public static readonly GridPos HouseDoor = new(7, 6);
        public static readonly GridPos PantryAccess = new(3, 3);
        /// <summary>The shed keeps the watchhouse plan: its doors, window and store access are on the same squares.</summary>
        public static readonly GridPos ShedDoor = BlackmarshWatchhouse.Ids.ShedDoor;
        public static readonly GridPos InnerDoor = BlackmarshWatchhouse.Ids.InnerDoor;
        public static readonly GridPos StoreAccess = BlackmarshWatchhouse.Ids.StoreAccess;
        public static readonly GridPos CacheAccess = new(16, 11);
    }
}
