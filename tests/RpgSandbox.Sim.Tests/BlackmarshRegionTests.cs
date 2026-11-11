using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>
/// Region Task 6: people, rations and news moving round the watch island. The order of the player's visits changes what
/// happens through the world's own rules (raid targets, sight, reports, guard duty); no branch flag decides it. The player
/// acts only by public commands; the world view is the oracle, never a source of clues.
/// </summary>
public class BlackmarshRegionTests
{
    /// <summary>Inside the shed's inner room, in the corner away from the door: in sight of the crate, not in its way.</summary>
    private static readonly GridPos ShedCorner = new(29, 18);

    /// <summary>Beside the keeper at its post by the well.</summary>
    private static readonly GridPos NextToTheKeeper = new(23, 15);

    /// <summary>On the beach of the cove, well away from the landing.</summary>
    private static readonly GridPos OnTheBeach = new(8, 12);

    [Fact]
    public void Left_alone_the_raider_robs_the_shed_carries_the_loot_home_and_no_one_knows()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);

        facts.AdvanceTo(At(1, 20, 30));

        Assert.Equal(new[] { "RaidOrdered", "FoodStolen", "FoodDeposited", "RaidCompleted" },
            facts.Kinds("RaidOrdered", "FoodStolen", "FoodDeposited", "RaidCompleted"));
        Assert.Contains(facts.All, f => f.Kind == "FoodStolen" && f.Description == "Predone vasan ruba 4 razioni da Cassa delle razioni d'emergenza (restano 0).");
        Assert.Contains(facts.All, f => f.Kind == "FoodDeposited" && f.Description == "Predone vasan consegna 4 razioni a Provviste dei Vasan (ora 4).");
        // Out there and back along the path: the shed is the nearer target (8 minutes against 12).
        Assert.Contains(facts.All, f => f.Kind == "TravelStarted" && f.Description == "Predone vasan parte da Bivio della caletta verso Bivio della rimessa.");
        Assert.Contains(facts.All, f => f.Kind == "TravelCompleted" && f.Description == "Predone vasan arriva a Bivio della caletta.");
        var world = session.GetWorldView();
        Assert.Equal(0, world.Store(ShedStore).Food);
        Assert.Equal(4, world.Store(VasanCache).Food);
        Assert.Equal(12, world.Store(Pantry).Food);
        // Back to rest at its own post, off the square the cache is used from.
        Assert.Equal(new GridPos(39, 23), world.Actor(Raider).Position);
        Assert.Null(world.Actor(Raider).Assignment);

        // The fisher had gone home at 19:45: nobody saw it, nobody told the keeper, the keeper stays at the well.
        Assert.DoesNotContain(facts.All, f => f.Kind is "FoodTheftWitnessed" or "InformationShared" or "GuardDutyStarted");
        Assert.Empty(world.Actor(Keeper).Knowledge);
        Assert.Equal(Well, world.Actor(Keeper).Location);
    }

    [Fact]
    public void The_next_evening_the_raid_comes_to_the_hamlet_and_the_news_reaches_the_keeper_by_word_of_mouth()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);

        facts.AdvanceTo(At(2, 21));

        // The shed is empty, so the nearest store with food is the pantry.
        Assert.Contains(facts.All, f => f.At.Day == 1 && f.Kind == "RaidOrdered" && f.Description.EndsWith("Dispensa della casa comune."));
        Assert.Contains(facts.All, f => f.At.Day == 1 && f.Description == "Predone vasan ruba 4 razioni da Dispensa della casa comune (restano 8).");
        // The fisher at home hears it, tells the keeper, the keeper walks over to guard the pantry.
        Assert.Contains(facts.All, f => f.Kind == "FoodTheftWitnessed" && f.Description.StartsWith("Pescatore sente qualcuno rubare da Dispensa della casa comune"));
        Assert.Contains(facts.All, f => f.Kind == "InformationShared" && f.Description.StartsWith("Pescatore racconta a Custode della vedetta del furto da Dispensa della casa comune"));
        var keeper = session.GetWorldView().Actor(Keeper);
        Assert.Equal(Pantry, keeper.GuardDuty?.Store);
        Assert.Equal(CommonHouse, keeper.Location);
        Assert.Equal(new GridPos(12, 13), keeper.Position);
        // Heard, not seen: there is no thief to ask the rations of.
        Assert.Empty(keeper.Claims);
        // The keeper on duty does not stand on the pantry's only access: the store stays usable.
        Assert.NotEqual(PantryAccess, keeper.Position);
        Walk(session, PantryAccess);
        Assert.Equal(PantryAccess, session.GetPlayerView().Position);
    }

    [Fact]
    public void Far_from_the_shed_the_player_does_not_learn_who_emptied_it_even_after_seeing_it_empty()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        session.AdvanceTo(At(1, 20, 30));

        GoTo(session, ShedFork);
        Walk(session, StoreAccess);

        var view = session.GetPlayerView();
        Assert.Equal(0, view.VisibleStores.Single(s => s.Id == ShedStore).Food);
        Assert.Empty(view.Observations);
        GoTo(session, HamletFork);
        Walk(session, NextToTheKeeper);
        view = session.GetPlayerView();
        Assert.Contains(view.PeopleHere, p => p.Id == Keeper);
        Assert.Empty(view.ReportOptions);
        Assert.Empty(session.GetWorldView().Actor(Keeper).Knowledge);
    }

    [Fact]
    public void What_the_player_sees_at_the_shed_and_tells_the_keeper_sends_the_keeper_to_guard_it()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        GoTo(session, ShedFork);
        Walk(session, ShedCorner);

        facts.AdvanceTo(At(1, 20, 30));

        Assert.Contains(facts.All, f => f.Kind == "FoodTheftWitnessed" && f.Description == "Protagonista vede Predone vasan rubare da Cassa delle razioni d'emergenza.");
        var seen = Assert.Single(session.GetPlayerView().Observations);
        Assert.Equal(Raider, seen.Culprit);

        GoTo(session, HamletFork);
        Walk(session, NextToTheKeeper);
        var option = Assert.Single(session.GetPlayerView().ReportOptions);
        Assert.Equal(Keeper, option.Recipient);
        Do(session, new ReportCommand { Actor = session.Player, Recipient = Keeper, Observation = option.Observation });
        facts.AdvanceTo(session.Now.Plus(Duration.FromMinutes(15)));

        // A real answer in the world: the keeper leaves the well, walks the path and stands guard in the shed.
        Assert.Contains(facts.All, f => f.Kind == "GuardDutyStarted" && f.Description == "Custode della vedetta decide di sorvegliare Cassa delle razioni d'emergenza.");
        Assert.Contains(facts.All, f => f.Kind == "TravelStarted" && f.Description == "Custode della vedetta parte da Bivio del nucleo verso Bivio della rimessa.");
        var keeper = session.GetWorldView().Actor(Keeper);
        Assert.Equal(Shed, keeper.Location);
        Assert.Equal(new GridPos(28, 14), keeper.Position);
        GoTo(session, ShedFork);
        Walk(session, StoreAccess);
        Assert.Equal(StoreAccess, session.GetPlayerView().Position);
        // A thief who was seen owes the rations back.
        var claim = Assert.Single(keeper.Claims);
        Assert.Equal((Raider, ShedStore, 4), (claim.Thief, claim.Store, claim.Owed));
    }

    [Fact]
    public void Taking_the_shed_rations_to_the_hamlet_turns_the_raid_on_the_pantry()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        GoTo(session, ShedFork);
        Walk(session, StoreAccess);
        Do(session, new TakeFoodCommand { Actor = session.Player, Store = ShedStore, Amount = 4 });
        GoTo(session, HamletFork);
        Walk(session, PantryAccess);
        Do(session, new DepositFoodCommand { Actor = session.Player, Store = Pantry, Amount = 4 });
        Assert.True(session.Now < At(1, 20), "the rations must be moved before the evening raid");

        facts.AdvanceTo(At(1, 20, 30));

        // Same rule, other world: the shed has nothing left, so the raider is sent to the pantry instead.
        Assert.Contains(facts.All, f => f.Kind == "RaidOrdered" && f.Description.EndsWith("Dispensa della casa comune."));
        Assert.DoesNotContain(facts.All, f => f.Kind == "RaidOrdered" && f.Description.EndsWith("Cassa delle razioni d'emergenza."));
        // It finds the player standing at the pantry, sizes the paladin up and gives up.
        Assert.Contains(facts.All, f => f.Kind == "RaidDeterred" && f.Description.StartsWith("Predone vasan trova Protagonista presso Dispensa della casa comune"));
        Assert.DoesNotContain(facts.All, f => f.Kind == "FoodStolen");
        var world = session.GetWorldView();
        Assert.Equal(16, world.Store(Pantry).Food);
        Assert.Equal(0, world.Store(VasanCache).Food);
    }

    [Fact]
    public void Going_to_the_cove_first_the_player_can_take_the_loot_back_and_the_hunger_brings_the_raider_again()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        GoTo(session, CoveFork);
        Walk(session, OnTheBeach);
        facts.AdvanceTo(At(1, 20, 40));
        Assert.Equal(4, session.GetWorldView().Store(VasanCache).Food); // the shed's rations, delivered

        Walk(session, CacheAccess); // free: the raider rests at its post, not on the access
        Do(session, new TakeFoodCommand { Actor = session.Player, Store = VasanCache, Amount = 4 });
        Assert.Equal(14, session.GetPlayerView().Food);
        Walk(session, OnTheBeach); // off the Vasan's ground before the day comes (Task 6A)

        facts.AdvanceTo(At(3, 21));

        // The Vasan are below their threshold again: an hour later the next raid goes for the pantry.
        Assert.Contains(facts.All, f => f.At.Day == 0 && f.At.Hour == 21 && f.Kind == "RaidOrdered" && f.Description.EndsWith("Dispensa della casa comune."));
        // Task 6B: that first night the raider's Stealth (15) beats the fisher, close by in the dim house: nobody notices.
        Assert.Contains(facts.All, f => f.At.Day == 0 && f.Kind == "FoodTheftUnnoticed" && f.Description.StartsWith("Pescatore non si accorge di nulla"));
        // The next evening it comes again; this time the fisher hears it and tells the keeper, who takes guard there.
        Assert.Contains(facts.All, f => f.At.Day == 1 && f.Kind == "FoodTheftWitnessed" && f.Description.StartsWith("Pescatore sente qualcuno rubare da Dispensa della casa comune"));
        Assert.Contains(facts.All, f => f.At.Day == 1 && f.Kind == "GuardDutyStarted" && f.Description.EndsWith("Dispensa della casa comune."));
        // The evening after, the raider sees the keeper on guard and gives up.
        Assert.Contains(facts.All, f => f.At.Day == 2 && f.Kind == "RaidDeterred" && f.Description == "Predone vasan vede la guardia a Dispensa della casa comune e desiste.");
        Assert.Equal(4, session.GetWorldView().Store(Pantry).Food);
        // Task 6A: in the dark the raider only hears the theft, and the beach is not the ground it defends: no answer.
        Assert.Contains(facts.All, f => f.Kind == "FoodTheftWitnessed" && f.Description == "Predone vasan sente qualcuno rubare da Provviste dei Vasan nel buio, ma non vede chi.");
        Assert.DoesNotContain(facts.All, f => f.Kind.StartsWith("Defense") || f.Kind == "CombatStarted");
    }

    // ---------------------------------------------------------------- Task 6A: the raider defends the landing

    /// <summary>On the landing, in the raider's sight, off the access to its rations.</summary>
    private static readonly GridPos OnTheLanding = new(37, 20);

    [Fact]
    public void On_the_landing_the_raider_sizes_up_the_paladin_warns_him_off_and_attacks_when_he_stays()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        GoTo(session, CoveFork);
        Walk(session, OnTheLanding);
        facts.AdvanceUntil("CombatStarted", Duration.FromMinutes(10));

        Assert.Equal(new[] { "SizedUp", "DefenseWarning", "DefenseAttack", "CombatStarted" },
            facts.Kinds("SizedUp", "DefenseWarning", "DefenseAttack", "DefenseWithdrawal", "CombatStarted"));
        var warning = facts.All.Single(f => f.Kind == "DefenseWarning");
        Assert.Equal("Predone vasan intima a Protagonista di andarsene da Approdo dei Vasan.", warning.Description);
        Assert.Contains(session.GetPlayerView().RecentEvents, f => f.Id == warning.Id); // the player hears it
        var attack = facts.All.Single(f => f.Kind == "DefenseAttack");
        Assert.Equal("Predone vasan perde la pazienza: Protagonista non se ne va da Approdo dei Vasan.", attack.Description);
        Assert.True(attack.At.Since(warning.At).Ticks >= Duration.FromMinutes(2).Ticks, "it waits before striking a paladin it is unsure of");
        Assert.Contains(facts.All, f => f.Kind == "CombatStarted" && f.Description == "Predone vasan attacca Protagonista.");
    }

    [Fact]
    public void Taking_the_Vasan_rations_under_the_raiders_eyes_makes_it_attack_before_the_take_is_done()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        GoTo(session, CoveFork);
        Walk(session, CacheAccess);
        Assert.True(session.Execute(new TakeFoodCommand { Actor = session.Player, Store = VasanCache, Amount = 4 }).Success);
        facts.AdvanceUntil("CombatStarted", Duration.FromMinutes(10));

        Assert.Contains(facts.All, f => f.Kind == "DefenseWarning");
        Assert.Contains(facts.All, f => f.Kind == "DefenseAttack" && f.Description == "Predone vasan vede Protagonista mettere le mani sulle provviste e attacca.");
        Assert.DoesNotContain(facts.All, f => f.Kind == "FoodStolen");
        Assert.Equal(10, session.GetPlayerView().Food);
        Assert.Equal(4, session.GetWorldView().Store(VasanCache).Food);
    }

    [Fact]
    public void A_protagonist_who_looks_harmless_is_attacked_at_once()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var facts = new FactLog(session);
        Do(session, new EquipCommand { Actor = session.Player, Item = null, Slot = EquipmentSlot.MainHand });
        Do(session, new EquipCommand { Actor = session.Player, Item = null, Slot = EquipmentSlot.Shield });
        GoTo(session, CoveFork);
        Assert.True(session.Execute(new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(OnTheLanding) }).Success);
        facts.AdvanceUntil("CombatStarted", Duration.FromMinutes(10)); // it strikes before the walk is over

        Assert.Equal(new[] { "SizedUp", "DefenseAttack", "CombatStarted" },
            facts.Kinds("SizedUp", "DefenseWarning", "DefenseAttack", "DefenseWithdrawal", "CombatStarted"));
        Assert.Contains(facts.All, f => f.Kind == "DefenseAttack" && f.Description == "Predone vasan si scaglia contro Protagonista per difendere Approdo dei Vasan.");
    }

    [Fact]
    public void A_save_taken_while_warned_off_resumes_into_the_same_fight()
    {
        var straight = BlackmarshWatchIsland.CreateSession();
        GoTo(straight, CoveFork);
        Walk(straight, OnTheLanding);
        var straightFacts = new FactLog(straight);
        straightFacts.AdvanceUntil("DefenseWarning", Duration.FromMinutes(10));
        var reloaded = straight.SaveAndReload();

        var reloadedFacts = new FactLog(reloaded);
        straightFacts.AdvanceUntil("CombatStarted", Duration.FromMinutes(10));
        reloadedFacts.AdvanceUntil("CombatStarted", Duration.FromMinutes(10));
        Assert.Equal(straight.SaveToString(), reloaded.SaveToString());
        Assert.Contains(reloadedFacts.All, f => f.Kind == "DefenseAttack");
    }

    [Fact]
    public void The_order_of_the_visits_leaves_different_worlds_by_the_next_evening()
    {
        var alone = BlackmarshWatchIsland.CreateSession();

        var told = BlackmarshWatchIsland.CreateSession();
        GoTo(told, ShedFork);
        Walk(told, ShedCorner);
        told.AdvanceTo(At(1, 20, 30));
        GoTo(told, HamletFork);
        Walk(told, NextToTheKeeper);
        Do(told, new ReportCommand { Actor = told.Player, Recipient = Keeper, Observation = told.GetPlayerView().ReportOptions.Single().Observation });

        var moved = BlackmarshWatchIsland.CreateSession();
        GoTo(moved, ShedFork);
        Walk(moved, StoreAccess);
        Do(moved, new TakeFoodCommand { Actor = moved.Player, Store = ShedStore, Amount = 4 });
        GoTo(moved, HamletFork);
        Walk(moved, PantryAccess);
        Do(moved, new DepositFoodCommand { Actor = moved.Player, Store = Pantry, Amount = 4 });

        foreach (var session in new[] { alone, told, moved })
            session.AdvanceTo(At(2, 21));
        string Outcome(SimulationSession s)
        {
            var w = s.GetWorldView();
            return $"pantry {w.Store(Pantry).Food}, cache {w.Store(VasanCache).Food}, keeper at {w.Actor(Keeper).Location?.Value} guarding {w.Actor(Keeper).GuardDuty?.Store.Value ?? "nothing"}";
        }

        Assert.Equal("pantry 8, cache 4, keeper at bm_isle_common_house guarding bm_isle_pantry", Outcome(alone));
        Assert.Equal("pantry 8, cache 4, keeper at bm_isle_shed guarding bm_isle_shed_store", Outcome(told));
        Assert.Equal("pantry 16, cache 0, keeper at bm_isle_well guarding nothing", Outcome(moved));
    }

    /// <summary>Every fact the world records while the test advances it, so none is lost to the recent-facts window.</summary>
    private sealed class FactLog(SimulationSession session)
    {
        private readonly List<FactView> _all = new(session.GetWorldView().RecentFacts);

        public IReadOnlyList<FactView> All
        {
            get
            {
                Collect();
                return _all;
            }
        }

        public void AdvanceTo(GameTime until)
        {
            while (session.Now < until)
            {
                session.Advance(Duration.FromMinutes(1));
                Collect();
            }
        }

        /// <summary>A second at a time until a fact of that kind appears (a fight waits for the player: time stops there).</summary>
        public void AdvanceUntil(string kind, Duration limit)
        {
            for (var second = 0L; second < limit.Seconds && All.All(f => f.Kind != kind); second++)
                session.Advance(Duration.FromSeconds(1));
            Assert.Contains(All, f => f.Kind == kind);
        }

        public string[] Kinds(params string[] kinds) => All.Where(f => kinds.Contains(f.Kind)).Select(f => f.Kind).ToArray();

        private void Collect()
        {
            foreach (var fact in session.GetWorldView().RecentFacts)
                if (_all.All(f => f.Id != fact.Id))
                    _all.Add(fact);
        }
    }

    /// <summary>To the fork of the map the player is on, then along the path to <paramref name="fork"/>.</summary>
    private static void GoTo(SimulationSession session, LocationId fork)
    {
        var here = session.GetPlayerView().Area;
        Walk(session, here == Hamlet ? HamletExit : here == ShedArea ? ShedExit : CoveExit);
        Do(session, new TravelCommand { Actor = session.Player, Destination = fork });
    }

    private static void Walk(SimulationSession session, GridPos to)
    {
        if (session.GetPlayerView().Position != to)
            Do(session, new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });
    }

    private static void Do(SimulationSession session, Command command)
    {
        var result = session.Execute(command);
        Assert.True(result.Success, result.Message);
        if (result.Action is { } action)
            Assert.Equal(AdvanceOutcome.Completed, session.AdvanceUntilCompleted(action, Duration.FromMinutes(20)).Outcome);
    }
}
