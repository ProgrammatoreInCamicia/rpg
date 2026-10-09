using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>The vertical-slice scenario. Grows with each increment.</summary>
public static class SliceScenario
{
    public static class Ids
    {
        public static readonly AreaId Village = new("village");
        public static readonly AreaId Forest = new("forest");
        public static readonly LocationId Inn = new("inn");
        public static readonly LocationId Granary = new("granary");
        public static readonly LocationId BanditCamp = new("bandit_camp");
        public static readonly ActorId Player = new("player");
        public static readonly ActorId Raider = new("raider");
        public static readonly ActorId Farmer = new("farmer");
        public static readonly ActorId Guard = new("guard");
        public static readonly StoreId GranaryStore = new("granary_store");
        public static readonly StoreId CampStore = new("camp_store");
        public static readonly FactionId VillageFaction = new("village");
        public static readonly FactionId Bandits = new("bandits");
    }

    /// <summary>
    /// Without the player: the bandits eat 6 rations at 08:00 (14 -> 8, below their threshold of 10),
    /// and their 09:00 evaluation sends the raider to steal 8 rations from the granary at 09:30–09:33.
    /// The farmer only works at the granary in the afternoon, so nobody in the village sees the morning
    /// raids: whether the guard ever learns about them depends on the player.
    /// </summary>
    public static Scenario Create() => Build(farmerShiftStart: Duration.FromHours(13), farmerShiftEnd: Duration.FromHours(18));

    /// <summary>The slice with a different farmer shift: used to stage witnesses in tests.</summary>
    public static Scenario Build(Duration farmerShiftStart, Duration farmerShiftEnd, int granaryFood = 40) =>
        new ScenarioBuilder()
            .AddArea(Ids.Village.Value, "Villaggio")
            .AddArea(Ids.Forest.Value, "Bosco")
            .AddLocation(Ids.Inn.Value, "Locanda", Ids.Village.Value)
            .AddLocation(Ids.Granary.Value, "Granaio", Ids.Village.Value)
            .AddLocation(Ids.BanditCamp.Value, "Campo dei banditi", Ids.Forest.Value)
            .AddRoute(Ids.Inn.Value, Ids.Granary.Value, Duration.FromMinutes(5))
            .AddRoute(Ids.Granary.Value, Ids.BanditCamp.Value, Duration.FromMinutes(30))
            .AddFaction(Ids.VillageFaction.Value, "Villaggio", homeStoreId: Ids.GranaryStore.Value, authorityId: Ids.Guard.Value)
            .AddRaidingFaction(Ids.Bandits.Value, "Banditi", homeStoreId: Ids.CampStore.Value,
                dailyUpkeep: 6, upkeepTimeOfDay: Duration.FromHours(8),
                foodThreshold: 10, raidAmount: 8, evaluationInterval: Duration.FromHours(3))
            .AddStore(Ids.GranaryStore.Value, "Scorte del granaio", Ids.Granary.Value, food: granaryFood, ownerFactionId: Ids.VillageFaction.Value)
            .AddStore(Ids.CampStore.Value, "Scorte del campo", Ids.BanditCamp.Value, food: 14, ownerFactionId: Ids.Bandits.Value)
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, sheet: Sheets.Paladin())
            .AddActor(Ids.Raider.Value, "Razziatore", Ids.BanditCamp.Value, factionId: Ids.Bandits.Value, sheet: Sheets.Raider())
            .AddActor(Ids.Farmer.Value, "Contadino", Ids.Inn.Value, factionId: Ids.VillageFaction.Value,
                workLocationId: Ids.Granary.Value, shiftStart: farmerShiftStart, shiftEnd: farmerShiftEnd, sheet: Sheets.Farmer())
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value, sheet: Sheets.VillageGuard())
            .Build();
}
