using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R4c: Help, Hide, Search and Ready in a fight (SRD 5.2.1 Rules Glossary).</summary>
public class TacticsTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>
    /// A yard: the Paladin at (3,3), a guard at (<paramref name="foeX"/>,3). The Paladin throws a javelin first (or strikes
    /// from next to it); the first seed where it is then its fresh turn with the guard up and far as placed.
    /// </summary>
    private static SimulationSession Fight(int foeX = 4, int hour = 12, Action<SimulationSession>? before = null)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 3), torches: 1)
                .AddActor("foe", "Guardia", "yard", combat: "guard", at: (foeX, 3))
                .Build());
            s.AdvanceTo(At(1, hour));
            before?.Invoke(s);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = foeX == 4 ? Sword : "Giavellotto (lanciato)" });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true } && Me(s).Conscious && Of(s, Foe).Conscious)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Encounter E(SimulationSession s) => s.Engine.World.Encounters.Values.Single();

    private static CommandResult Do(SimulationSession s, CombatAction action, ActorId? target = null) =>
        s.Execute(new CombatActionCommand { Actor = s.Player, Action = action, Target = target });

    [Fact]
    public void Help_distracts_an_enemy_next_to_the_helper_for_an_ally_but_not_for_the_helper()
    {
        var s = Fight();
        Assert.True(Do(s, CombatAction.Help, Foe).Success);
        Assert.Equal(s.Player, Of(s, Foe).DistractedBy);
        Assert.DoesNotContain("è distratto", s.Engine.ConditionEdges(Me(s), Of(s, Foe), withinFiveFeet: true).Reasons);

        // An ally on the Paladin's side would strike it with Advantage.
        var e = E(s);
        var friend = new Actor { Id = new ActorId("friend"), Name = "Amico", Location = Me(s).Location, IsPlayer = false, Sheet = Me(s).Sheet };
        s.Engine.World.Actors[friend.Id] = friend;
        e.Order.Add(new Combatant { Actor = friend.Id, Side = e.Order.Single(c => c.Actor == s.Player).Side, Initiative = 0, InitiativeBonus = 0 });
        Assert.Contains("è distratto", s.Engine.ConditionEdges(friend, Of(s, Foe), withinFiveFeet: true).Reasons);
    }

    [Fact]
    public void Hiding_needs_to_be_unseen_then_gives_invisibility_until_it_attacks()
    {
        var seen = Fight();
        Assert.Equal(RejectionReason.NotHeld, Do(seen, CombatAction.Hide).Rejection);

        // The only enemy cannot see the Paladin (blinded): it may hide.
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Blinded);
        Me(s).Sheet = Me(s).Sheet with { Dexterity = 40 }; // +15: the DC 15 is beaten whatever the roll
        Assert.False(s.Engine.SeesActor(Of(s, Foe), Me(s)));
        var hid = Do(s, CombatAction.Hide);
        Assert.True(hid.Success, hid.Message);
        Assert.True(Simulation.Hidden(Me(s)));
        Assert.True(s.Engine.Has(Me(s), ConditionKind.Invisible));
        Me(s).Sneak = null; // as an attack or a spell does
        Assert.False(s.Engine.Has(Me(s), ConditionKind.Invisible));
    }

    [Fact]
    public void Search_finds_the_hidden_one_it_beats()
    {
        var s = Fight();
        Of(s, Foe).Sneak = 1;
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Invisible, Of(s, Foe));
        Assert.False(s.Engine.SeesActor(Me(s), Of(s, Foe)));
        var found = Do(s, CombatAction.Search);
        Assert.True(found.Success, found.Message);
        Assert.Contains("trova Guardia", found.Message);
        Assert.True(s.Engine.SeesActor(Me(s), Of(s, Foe)));
    }

    [Fact]
    public void A_readied_blow_lands_on_the_enemy_that_steps_into_reach()
    {
        var s = Fight(foeX: 7, before: x => x.Engine.World.Actors[Foe].Profile = x.Engine.World.Actors[Foe].Profile with
        {
            Attacks = x.Engine.World.Actors[Foe].Profile.Attacks.Where(a => a.Melee).ToArray(), // it must come to strike
        });
        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(7, 3))); // out of reach, so it must step in
        Assert.True(Do(s, CombatAction.Ready).Success);
        Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
        var before = E(s).Log.Count;
        s.Execute(new EndTurnCommand { Actor = s.Player });
        for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));
        var log = (s.Engine.World.Encounters.Values.SingleOrDefault()?.Log ?? s.Engine.World.LastFight!.Log).Skip(before).ToList();
        Assert.Contains(log, l => l.Kind == CombatLogKind.Move && l.Actor == Foe);
        Assert.Contains(log, l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
    }

    [Fact]
    public void Help_and_a_readied_action_survive_save_and_load()
    {
        var s = Fight();
        Assert.True(Do(s, CombatAction.Help, Foe).Success);
        CombatantOf(s).Readied = true;
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(s.Player, loaded.Engine.World.Actors[Foe].DistractedBy);
    }

    private static Combatant CombatantOf(SimulationSession s) => E(s).Order.Single(c => c.Actor == s.Player);
}
