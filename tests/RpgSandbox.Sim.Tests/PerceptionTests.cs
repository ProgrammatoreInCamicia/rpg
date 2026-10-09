using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T4b: light, one Stealth check per theft, and what witnesses notice, see and recognise.</summary>
public class PerceptionTests
{
    [Theory]
    [InlineData(0, Light.Dark)] [InlineData(5, Light.Dark)] [InlineData(6, Light.Dim)] [InlineData(7, Light.Bright)]
    [InlineData(12, Light.Bright)] [InlineData(18, Light.Bright)] [InlineData(19, Light.Dim)] [InlineData(20, Light.Dark)]
    public void Light_follows_the_time_of_day(int hour, Light expected) =>
        Assert.Equal(expected, Perception.LightAt(At(3, hour)));

    [Fact]
    public void In_bright_light_a_watching_witness_always_sees()
    {
        var outcome = Perception.Witness(Sheets.Farmer(), Light.Bright, stealthTotal: 30);

        Assert.True(outcome.Noticed);
        Assert.True(outcome.SawActor);
    }

    [Fact]
    public void In_dim_light_perception_suffers_disadvantage()
    {
        // Farmer: Passive Perception 11, 6 with Disadvantage.
        Assert.True(Perception.Witness(Sheets.Farmer(), Light.Dim, stealthTotal: 6).SawActor);
        Assert.False(Perception.Witness(Sheets.Farmer(), Light.Dim, stealthTotal: 7).Noticed);
    }

    [Fact]
    public void In_the_dark_a_theft_can_be_heard_but_the_thief_is_never_seen()
    {
        var heard = Perception.Witness(Sheets.Farmer(), Light.Dark, stealthTotal: 11);
        var missed = Perception.Witness(Sheets.Farmer(), Light.Dark, stealthTotal: 12);

        Assert.True(heard.Noticed);
        Assert.False(heard.SawActor);
        Assert.False(missed.Noticed);
    }

    /// <summary>
    /// The slice, but the farmer watches the granary at night and the bandits raid at 01:00: the theft takes place in
    /// the dark at 01:30–01:33, against the farmer's Passive Perception 11.
    /// </summary>
    private static Scenario NightRaid(ulong seed) =>
        new ScenarioBuilder()
            .WithSeed(seed)
            .AddArea(Village.Value, "Villaggio").AddArea(Forest.Value, "Bosco")
            .AddLocation(Inn.Value, "Locanda", Village.Value)
            .AddLocation(Granary.Value, "Granaio", Village.Value)
            .AddLocation(BanditCamp.Value, "Campo dei banditi", Forest.Value)
            .AddRoute(Inn.Value, Granary.Value, Duration.FromMinutes(5))
            .AddRoute(Granary.Value, BanditCamp.Value, Duration.FromMinutes(30))
            .AddFaction(VillageFaction.Value, "Villaggio", homeStoreId: GranaryStore.Value, authorityId: Guard.Value)
            .AddRaidingFaction(Bandits.Value, "Banditi", CampStore.Value, 6, Duration.FromMinutes(30), 10, 8, Duration.FromHours(1))
            .AddStore(GranaryStore.Value, "Scorte del granaio", Granary.Value, 40, VillageFaction.Value)
            .AddStore(CampStore.Value, "Scorte del campo", BanditCamp.Value, 14, Bandits.Value)
            .AddActor(Player.Value, "Protagonista", Inn.Value, isPlayer: true, sheet: Sheets.Paladin())
            .AddActor(Raider.Value, "Razziatore", BanditCamp.Value, factionId: Bandits.Value, sheet: Sheets.Raider())
            .AddActor(Farmer.Value, "Contadino", Granary.Value, factionId: VillageFaction.Value, sheet: Sheets.Farmer(),
                workLocationId: Granary.Value, shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(6))
            .AddActor(Guard.Value, "Guardia", Inn.Value, factionId: VillageFaction.Value, sheet: Sheets.VillageGuard())
            .Build();

    [Fact]
    public void A_night_theft_is_sometimes_heard_never_recognised_and_always_explained()
    {
        var heard = 0;
        var missed = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            var s = SimulationSession.Create(NightRaid(seed));
            s.AdvanceTo(At(1, 1, 34));
            var view = s.GetWorldView();
            Assert.Equal(32, view.Store(GranaryStore).Food); // the theft itself happens either way

            var knowledge = view.Actor(Farmer).Knowledge;
            if (knowledge.Count == 1)
            {
                heard++;
                Assert.Null(knowledge[0].Thief); // in the dark nobody is seen
                Assert.Contains(view.RecentFacts, f => f.Kind == "FoodTheftWitnessed" && f.Description.Contains("sente"));
            }
            else
            {
                missed++;
                Assert.Contains(view.RecentFacts, f => f.Kind == "FoodTheftUnnoticed" && f.Description.Contains("Percezione passiva 11"));
            }
            Assert.Contains(view.RecentFacts, f => f.Kind == "Roll" && f.Description.StartsWith("Razziatore, Furtività"));
        }

        // Raider Stealth +4 vs Passive Perception 11: heard on a d20 of 7 or less, about a third of the time.
        Assert.True(heard > 0, "never heard");
        Assert.True(missed > 0, "never missed");
    }

    [Fact]
    public void The_same_seed_gives_the_same_night()
    {
        var a = SimulationSession.Create(NightRaid(5));
        var b = SimulationSession.Create(NightRaid(5));
        a.AdvanceTo(At(2, 12));
        b.AdvanceTo(At(2, 12));

        Assert.Equal(a.SaveToString(), b.SaveToString());
    }

    [Fact]
    public void A_reload_in_the_middle_of_a_night_theft_keeps_its_stealth_check()
    {
        for (ulong seed = 0; seed < 10; seed++)
        {
            var continuous = SimulationSession.Create(NightRaid(seed));
            var reloaded = SimulationSession.Create(NightRaid(seed));
            reloaded.AdvanceTo(At(1, 1, 31)); // the raider is taking food
            reloaded = reloaded.SaveAndReload();

            continuous.AdvanceTo(At(3, 12));
            reloaded.AdvanceTo(At(3, 12));
            Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
        }
    }

    [Fact]
    public void A_paladin_in_chain_mail_steals_with_disadvantage_and_knows_his_roll()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));

        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 3 });

        Assert.True(take.Success);
        Assert.Contains("Furtività: d20", take.Message);
        Assert.Contains("svantaggio", take.Message);
    }

    [Fact]
    public void Taking_from_your_own_faction_is_not_a_theft_and_rolls_nothing()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        s.AdvanceTo(At(1, 13, 10)); // the farmer is at work at the granary
        var farmer = s.Engine.World.Actors[Farmer];
        farmer.CurrentAction = null; // free him to act directly (engine-level staging)
        var rngAfterDay = s.Engine.World.Rng.State;

        var take = s.Engine.Execute(new TakeFoodCommand { Actor = Farmer, Store = GranaryStore, Amount = 1 });

        Assert.True(take.Success);
        Assert.DoesNotContain("Furtività", take.Message);
        Assert.Equal(rngAfterDay, s.Engine.World.Rng.State); // no dice for a non-theft
    }
}
