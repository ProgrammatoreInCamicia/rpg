using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>F1: sneaking on maps (SRD Slow pace and Hide, adapted).</summary>
public class StealthTests
{
    private static readonly ActorId Watcher = new("watcher");
    private static readonly StoreId Store = new("store");

    /// <summary>An open yard with a lamp; a store and a watcher on the same row.</summary>
    private static Scenario Field(ulong seed, (int X, int Y) lamp, int bright, int dim, (int X, int Y) playerAt,
        (int X, int Y) watcherAt, (int X, int Y) storeAt, CharacterSheet? playerSheet = null, int width = 11) =>
        new ScenarioBuilder()
            .WithSeed(seed)
            .AddArea("a", "A")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', width), 5).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddLight("a", lamp, bright, dim)
            .AddFaction("village", "Villaggio", homeStoreId: Store.Value, authorityId: Watcher.Value)
            .AddStore(Store.Value, "Scorte", "yard", 40, "village", at: storeAt)
            .AddActor("player", "Protagonista", "yard", isPlayer: true, sheet: playerSheet ?? Sheets.Raider(), at: playerAt)
            .AddActor(Watcher.Value, "Guardia", "yard", factionId: "village", sheet: Sheets.VillageGuard(), at: watcherAt)
            .Build();

    private static SimulationSession Walk(SimulationSession s, GridPos to, bool stealthy)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to, Stealthy = stealthy });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromHours(1));
        return s;
    }

    [Fact]
    public void Sneaking_goes_at_the_slow_pace()
    {
        var normal = SimulationSession.Create(Field(1, (0, 0), 5, 5, (0, 2), (10, 0), (10, 4)));
        var walk = normal.Execute(new MoveCommand { Actor = normal.Player, To = new GridPos(6, 2) });
        Assert.Equal(6, walk.CompletesAt!.Value.Since(normal.Now).Seconds);

        var sneaky = SimulationSession.Create(Field(1, (0, 0), 5, 5, (0, 2), (10, 0), (10, 4)));
        var sneak = sneaky.Execute(new MoveCommand { Actor = sneaky.Player, To = new GridPos(6, 2), Stealthy = true });
        Assert.Equal(9, sneak.CompletesAt!.Value.Since(sneaky.Now).Seconds); // 20 ft per round: 1.5 s per square
        Assert.True(sneaky.GetPlayerView().Move!.Stealthy);
    }

    [Fact]
    public void One_stealth_check_lasts_while_sneaking_and_noise_ends_it()
    {
        var s = SimulationSession.Create(Field(3, (0, 0), 5, 5, (0, 2), (10, 0), (10, 4)));
        Walk(s, new GridPos(3, 2), stealthy: true);
        var total = s.GetPlayerView().Sneaking;
        Assert.NotNull(total);

        Walk(s, new GridPos(5, 2), stealthy: true);
        Assert.Equal(total, s.GetPlayerView().Sneaking); // no reroll on every click

        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = Store, Amount = 1 });
        Assert.False(take.Success); // not in reach: a rejected command changes nothing
        Assert.Equal(total, s.GetPlayerView().Sneaking);

        Assert.True(s.Execute(new WaitCommand { Actor = s.Player, Duration = Duration.FromMinutes(1) }).Success);
        Assert.Null(s.GetPlayerView().Sneaking); // waiting is not sneaking any more

        var again = SimulationSession.Create(Field(3, (0, 0), 5, 5, (0, 2), (10, 0), (10, 4)));
        Walk(again, new GridPos(3, 2), stealthy: true);
        Walk(again, new GridPos(5, 2), stealthy: false);
        Assert.Null(again.GetPlayerView().Sneaking); // a normal walk ends it
    }

    [Fact]
    public void A_sneaking_walk_keeps_to_the_dark_and_a_normal_one_does_not_care()
    {
        // A candle-like light in the middle of the bottom row: lit within two squares of (5,4).
        Scenario Night() => Field(1, (5, 4), 5, 5, (0, 4), (10, 0), (0, 0));
        var normal = SimulationSession.Create(Night());
        normal.AdvanceTo(At(1, 2));
        var sneaky = SimulationSession.Create(Night());
        sneaky.AdvanceTo(At(1, 2));
        var map = sneaky.Engine.World.Maps[new AreaId("a")];

        var straight = normal.Execute(new MoveCommand { Actor = normal.Player, To = new GridPos(10, 4) });
        var around = sneaky.Execute(new MoveCommand { Actor = sneaky.Player, To = new GridPos(10, 4), Stealthy = true });

        Assert.Contains(normal.GetPlayerView().Move!.Path, p => map.SourceLight(p) > Light.Dark);
        Assert.All(sneaky.GetPlayerView().Move!.Path, p => Assert.Equal(Light.Dark, map.SourceLight(p)));
        Assert.Equal(normal.GetPlayerView().Move!.Path.Count, sneaky.GetPlayerView().Move!.Path.Count); // here the detour is free
        Assert.True(around.CompletesAt > straight.CompletesAt); // but sneaking is slower
    }

    [Fact]
    public void You_can_tell_whether_those_you_see_can_see_you()
    {
        // Night. The lamp lights the watcher; the player stands in the dark beyond its reach.
        var s = SimulationSession.Create(Field(1, (8, 2), 5, 10, (2, 2), (9, 2), (0, 0)));
        s.AdvanceTo(At(1, 2));
        var watcher = Assert.Single(s.GetPlayerView().VisibleActors, a => a.Id == Watcher);
        Assert.False(watcher.SeesYou);

        // Into dim light without sneaking: seen.
        Walk(s, new GridPos(5, 2), stealthy: false);
        Assert.True(s.GetPlayerView().VisibleActors.Single(a => a.Id == Watcher).SeesYou);

        // Into bright light, sneaking or not: seen.
        Walk(s, new GridPos(7, 2), stealthy: true);
        Assert.True(s.GetPlayerView().VisibleActors.Single(a => a.Id == Watcher).SeesYou);
    }

    [Fact]
    public void In_dim_light_a_sneaking_player_is_seen_only_by_a_sharp_enough_eye()
    {
        var dimPassive = Sheets.VillageGuard().PassivePerception(disadvantage: true);
        var seen = 0;
        var unseen = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            var s = SimulationSession.Create(Field(seed, (8, 2), 5, 10, (2, 2), (9, 2), (0, 0)));
            s.AdvanceTo(At(1, 2));
            Walk(s, new GridPos(5, 2), stealthy: true); // dim light, 15 ft from the lamp
            var view = s.GetPlayerView();
            var seesYou = view.VisibleActors.Single(a => a.Id == Watcher).SeesYou!.Value;
            Assert.Equal(dimPassive >= view.Sneaking, seesYou);
            if (seesYou) seen++; else unseen++;
        }
        Assert.True(seen > 0 && unseen > 0);
    }

    [Fact]
    public void A_thief_who_crept_up_unseen_in_dim_light_is_never_recognised()
    {
        // Night: the store and the square next to it are in dim light; the watcher stands in the dark, in view.
        var dimPassive = Sheets.VillageGuard().PassivePerception(disadvantage: true);
        var noticedButNotRecognised = 0;
        var recognisedWhenWalkingOpenly = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            foreach (var stealthy in new[] { true, false })
            {
                var s = SimulationSession.Create(Field(seed, (1, 2), 5, 15, (2, 2), (8, 2), (5, 2)));
                s.AdvanceTo(At(1, 2));
                Walk(s, new GridPos(4, 2), stealthy);
                var sneak = s.GetPlayerView().Sneaking;
                var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = Store, Amount = 1 });
                Assert.True(take.Success, take.Message);
                s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));

                var knowledge = s.GetWorldView().Actor(Watcher).Knowledge;
                if (stealthy && sneak > dimPassive)
                {
                    Assert.All(knowledge, k => Assert.Null(k.Thief));
                    if (knowledge.Count > 0) noticedButNotRecognised++;
                }
                if (!stealthy && knowledge.Any(k => k.Thief is not null))
                    recognisedWhenWalkingOpenly++;
            }
        }
        Assert.True(noticedButNotRecognised > 0, "the theft should sometimes be noticed without recognising the thief");
        Assert.True(recognisedWhenWalkingOpenly > 0, "walking up openly, the thief should sometimes be recognised");
    }

    [Fact]
    public void Sneaking_survives_save_and_load_mid_walk()
    {
        var s = SimulationSession.Create(Field(2, (0, 0), 5, 5, (0, 2), (10, 0), (10, 4)));
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(8, 2), Stealthy = true });
        s.Advance(Duration.FromSeconds(4));

        var loaded = s.SaveAndReload();

        Assert.Equal(s.GetPlayerView().Sneaking, loaded.GetPlayerView().Sneaking);
        Assert.Equal(s.GetPlayerView().Move!.Path, loaded.GetPlayerView().Move!.Path);
        Assert.Equal(s.GetPlayerView().Move!.Speed, loaded.GetPlayerView().Move!.Speed);
        Assert.True(loaded.GetPlayerView().Move!.Stealthy);
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void The_paladin_in_chain_mail_sneaks_with_disadvantage()
    {
        var s = SimulationSession.Create(MappedVillageScenario.Create());
        var sneak = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(4, 3), Stealthy = true });

        Assert.Contains("svantaggio", sneak.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- evidence fixed when a theft starts (review F1/F2)

    private static readonly ActorId Bearer = new("bearer");

    /// <summary>Night, no lamps: the player steals at (5,2) from (4,2); the watcher looks on from (9,2); a villager with a
    /// torch stands next to the thief.</summary>
    private static SimulationSession TheftWithTorchBearer()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', 11), 5).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddFaction("village", "Villaggio", homeStoreId: Store.Value, authorityId: Watcher.Value)
            .AddStore(Store.Value, "Scorte", "yard", 40, "village", at: (5, 2))
            .AddActor("player", "Protagonista", "yard", isPlayer: true, sheet: Sheets.Raider(), at: (4, 2))
            .AddActor(Watcher.Value, "Guardia", "yard", factionId: "village", sheet: Sheets.VillageGuard(), at: (9, 2))
            .AddActor(Bearer.Value, "Contadino", "yard", sheet: Sheets.Farmer(), at: (4, 3), torches: 2)
            .Build();
        var s = SimulationSession.Create(scenario);
        s.AdvanceTo(At(1, 2));
        return s;
    }

    private static ActionId StartTheft(SimulationSession s)
    {
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = Store, Amount = 1 });
        Assert.True(take.Success, take.Message);
        return take.Action!.Value;
    }

    [Fact]
    public void A_torch_lit_after_the_theft_began_cannot_reveal_who_started_it()
    {
        var s = TheftWithTorchBearer();
        var theft = StartTheft(s); // in the dark: nobody sees the thief now
        s.Advance(Duration.FromSeconds(10));
        Assert.True(s.Execute(new TorchCommand { Actor = Bearer, Lit = true }).Success);
        s.AdvanceUntilCompleted(theft, Duration.FromHours(1));

        var seen = Assert.Single(s.GetWorldView().Actor(Watcher).Knowledge);
        Assert.Equal("Seen", seen.Perceived); // in torchlight at the end, the deed is seen...
        Assert.Null(seen.Thief);               // ...but who began it, in the dark, is not known
    }

    [Fact]
    public void A_torch_put_out_during_the_theft_does_not_erase_what_was_seen()
    {
        var s = TheftWithTorchBearer();
        Assert.True(s.Execute(new TorchCommand { Actor = Bearer, Lit = true }).Success);
        var theft = StartTheft(s); // in torchlight: the watcher sees the thief from the start
        s.Advance(Duration.FromSeconds(10));
        Assert.True(s.Execute(new TorchCommand { Actor = Bearer, Lit = false }).Success);
        s.AdvanceUntilCompleted(theft, Duration.FromHours(1));

        var seen = Assert.Single(s.GetWorldView().Actor(Watcher).Knowledge);
        Assert.Equal("Seen", seen.Perceived);
        Assert.Equal(new ActorId("player"), seen.Thief);
    }

    [Fact]
    public void The_evidence_of_a_theft_in_progress_survives_save_and_load()
    {
        var s = TheftWithTorchBearer();
        var theft = StartTheft(s);
        s.Advance(Duration.FromSeconds(10));
        s.Execute(new TorchCommand { Actor = Bearer, Lit = true });

        var loaded = s.SaveAndReload();
        s.AdvanceUntilCompleted(theft, Duration.FromHours(1));
        loaded.AdvanceUntilCompleted(theft, Duration.FromHours(1));

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Null(Assert.Single(loaded.GetWorldView().Actor(Watcher).Knowledge).Thief);
    }
}
