using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P4b: broken glass, climbing through windows, bars.</summary>
public class WindowTests
{
    private static readonly AreaId Area = new("a");
    private static readonly GridPos A = new(3, 1), B = new(4, 1);
    private static readonly ActorId Guard = new("guard");

    /// <summary>
    /// A yard at noon split by a wall down x = 3|4, a window in it between (3,1) and (4,1): no other way across. The player
    /// at (3,1), by the window; the village guard (an authority) at (6,5), beyond the wall.
    /// </summary>
    private static SimulationSession Yard(BarsSpec? bars = null, bool open = false, WindowGlass glass = WindowGlass.Intact, int hour = 12)
    {
        var b = new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (3, 1))
            .AddFaction("village", "Villaggio", authorityId: "guard")
            .AddActor("guard", "Guardia", "yard", factionId: "village", combat: "guard", at: (6, 5));
        for (var y = 0; y < 6; y++)
            b.AddEdge("a", (3, y), (4, y), y == 1 ? MapEdgeKind.Window : MapEdgeKind.Wall, open: y == 1 && open,
                bars: y == 1 ? bars : null, glass: y == 1 ? glass : WindowGlass.Intact);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, hour));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static GridMap Map(SimulationSession s) => s.Engine.World.Maps[Area];

    private static MapEdgeView Window(SimulationSession s) => s.GetPlayerView().Map!.Edges.Single(e => e.A == A && e.B == B);

    private static CommandResult Done(SimulationSession s, CommandResult started)
    {
        Assert.True(started.Success, started.Message);
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromMinutes(5));
        return started;
    }

    [Fact]
    public void Breaking_the_glass_takes_six_seconds_is_heard_lets_shots_through_and_never_shuts_again()
    {
        var s = Yard(hour: 23); // in the dark: the guard, a few steps beyond the wall, hears what it cannot see
        Of(s, Guard).PlaceAt(Spot.CenterOf(new GridPos(5, 2)));
        Assert.False(s.Engine.SeesActor(Of(s, Guard), Of(s, s.Player)));
        var started = s.Execute(new BreakGlassCommand { Actor = s.Player, A = A, B = B });
        Assert.Equal(started.CompletesAt, s.Now.Plus(Duration.FromSeconds(6)));
        Done(s, started);

        Assert.Equal(WindowGlass.Broken, Window(s).Glass);
        Assert.False(Window(s).Open); // its frame is shut, but nothing stops a shot
        Assert.True(Map(s).LetsThrough(A, B));
        Assert.True(SpotPlanner.LineClear(Map(s), Spot.CenterOf(A), Spot.CenterOf(B)));
        Assert.Equal(RejectionReason.Broken, s.Execute(new WindowCommand { Actor = s.Player, A = A, B = B, Open = true }).Rejection);
        Assert.Equal(RejectionReason.Broken, s.Execute(new WindowCommand { Actor = s.Player, A = A, B = B, Open = false }).Rejection); // review P4b
        Assert.Equal(RejectionReason.AlreadyDone, s.Execute(new BreakGlassCommand { Actor = s.Player, A = A, B = B }).Rejection);
        var heard = Assert.Single(Of(s, Guard).Knowledge, o => o.Kind == DeedKind.BreakIn);
        Assert.Equal(PerceptionMode.Heard, heard.Perceived);
    }

    [Fact]
    public void Through_an_open_window_one_climbs_in_ten_feet_of_walking_and_never_through_a_closed_one()
    {
        var s = Yard();
        Assert.Equal(RejectionReason.Closed, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }).Rejection);
        Assert.True(s.Execute(new WindowCommand { Actor = s.Player, A = A, B = B, Open = true }).Success);

        var climb = s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B });
        Assert.Equal(climb.CompletesAt, s.Now.Plus(Duration.FromSeconds(2))); // 10 feet at Speed 30
        var hp = Of(s, s.Player).Vitality.Current;
        Done(s, climb);
        Assert.Equal(B, s.GetPlayerView().Position);
        Assert.Equal(hp, Of(s, s.Player).Vitality.Current);
    }

    [Fact]
    public void Shards_cut_who_climbs_through_and_once_cleared_they_do_not()
    {
        var s = Yard(glass: WindowGlass.Broken);
        var hp = Of(s, s.Player).Vitality.Current;
        Done(s, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }));
        Assert.Equal(B, s.GetPlayerView().Position);
        Assert.InRange(hp - Of(s, s.Player).Vitality.Current, 1, 4);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "CutByGlass" && f.Description.Contains("danni taglienti"));

        Of(s, s.Player).Sneak = 12; // review P4b: clearing the shards is quiet, it does not end sneaking
        var clear = s.Execute(new ClearGlassCommand { Actor = s.Player, A = A, B = B });
        Assert.Equal(12, Of(s, s.Player).Sneak);
        Done(s, clear);
        Assert.Equal(WindowGlass.Cleared, Window(s).Glass);
        Assert.Equal(RejectionReason.NotHeld, s.Execute(new ClearGlassCommand { Actor = s.Player, A = A, B = B }).Rejection);
        hp = Of(s, s.Player).Vitality.Current;
        Done(s, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }));
        Assert.Equal(A, s.GetPlayerView().Position);
        Assert.Equal(hp, Of(s, s.Player).Vitality.Current);
    }

    [Fact]
    public void Bars_stop_a_climber_let_a_shot_through_with_half_cover_and_once_forced_off_one_climbs()
    {
        var s = Yard(bars: new BarsSpec(ForceDc: 1, Clue: "le sbarre sono arrugginite, una è smossa"), open: true);
        Assert.True(Window(s).Barred);
        Assert.Equal(RejectionReason.Barred, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }).Rejection);
        Of(s, Guard).PlaceAt(Spot.CenterOf(new GridPos(6, 1)));
        var aim = s.PreviewAttack(s.Player, Guard, "Giavellotto (lanciato)");
        Assert.True(aim.Valid, aim.Message);
        Assert.Equal(CoverDegree.Half, aim.Cover);

        Of(s, Guard).PlaceAt(Spot.CenterOf(new GridPos(6, 5)));
        Done(s, s.Execute(new ForceCommand { Actor = s.Player, A = A, B = B }));
        Assert.False(Window(s).Barred);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "BarsTorn");
        Assert.Equal("le sbarre sono arrugginite, una è smossa", Assert.Single(s.GetPlayerView().KnownBarriers).Clue);
        Done(s, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }));
        Assert.Equal(B, s.GetPlayerView().Position);
    }

    [Fact]
    public void A_walk_never_goes_through_a_window_and_one_climbs_only_onto_free_ground()
    {
        var s = Yard(open: true);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(5, 1) }).Rejection);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(330, 90) }).Rejection);

        Of(s, Guard).PlaceAt(Spot.CenterOf(B));
        Assert.Equal(RejectionReason.NoRoom, s.Execute(new ClimbCommand { Actor = s.Player, A = A, B = B }).Rejection);
        Assert.Equal(RejectionReason.NotADoor, s.Execute(new ClimbCommand { Actor = s.Player, Door = A }).Rejection);
    }

    [Fact]
    public void Glass_and_bars_survive_save_and_load_and_older_saves_have_whole_glass_and_no_bars()
    {
        var s = Yard(bars: new BarsSpec(), glass: WindowGlass.Broken);
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(WindowGlass.Broken, Window(loaded).Glass);
        Assert.True(Window(loaded).Barred);

        var data = JsonNode.Parse(s.SaveToString())!;
        AsSchema(data, 29);
        var old = LoadFromString(data.ToJsonString());
        Assert.True(old.Success, old.Error);
        Assert.Equal(WindowGlass.Intact, Window(old.Session!).Glass);
        Assert.False(Window(old.Session!).Barred);
    }

    [Fact]
    public void In_the_living_village_the_farmhouse_has_a_barred_window()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var edge = s.Engine.World.Maps[MappedVillageScenario.Ids.Village].Edges.Single(e => e.A == new GridPos(4, 11) && e.B == new GridPos(4, 12));
        Assert.True(edge.Barred);
        Assert.Equal(WindowGlass.Intact, edge.Glass);
    }
}
