using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R5a: eating and drinking, for the player (SRD 5.2.1 Rules Glossary: Malnutrition, Dehydration; Equipment: Rations,
// Waterskin). NPCs live on their factions' stores.
internal sealed partial class Simulation
{
    /// <summary>SRD Waterskin: it holds up to 4 pints.</summary>
    private const int WaterskinPints = 4;

    /// <summary>SRD Water Needs per Day: 1 gallon (8 pints) for a Medium creature; less than half brings Exhaustion.</summary>
    private const int DailyWaterPints = 8;

    /// <summary>SRD Malnutrition: a creature that eats nothing for 5 days gains Exhaustion at the end of the 5th and each day after.</summary>
    private const int FastingDays = 5;

    private const string Waterskin = "waterskin";

    internal int WaterCapacity(Actor actor) => actor.Carried(Waterskin) * WaterskinPints;

    private bool AtWater(Actor actor) => actor.Location is { } here && World.Locations[here].Water;

    /// <summary>At a place with water the player drinks its fill and fills its waterskins.</summary>
    private void TakeWater(Actor actor)
    {
        if (!actor.IsPlayer || !AtWater(actor))
            return;
        actor.DrankToday = true;
        actor.WaterPints = WaterCapacity(actor);
    }

    internal WaterView WaterOf(Actor actor) =>
        new() { Pints = Math.Min(actor.WaterPints, WaterCapacity(actor)), Capacity = WaterCapacity(actor), DrankToday = actor.DrankToday };

    /// <summary>The first midnight after <paramref name="t"/>.</summary>
    private static GameTime NextMidnight(GameTime t) => GameTime.Start.Plus(Duration.FromDays(t.Day + 1));

    /// <summary>
    /// R5a, at the end of a day: the player eats a ration if it has one (ADAPTATION: by itself; a ration is a day's food),
    /// and drinks: where there is water it already has; otherwise from its waterskins. Fasting from the 5th day and drinking
    /// less than half the need bring Exhaustion, which no Long Rest takes away until a full day's food or water.
    /// </summary>
    private void EndOfDay()
    {
        World.Scheduler.Schedule(NextMidnight(World.Now), new DayEnds());
        var player = World.Actors[World.Player];
        if (player.Vitality.State == LifeState.Dead)
            return;
        TakeWater(player); // still at the water at midnight
        TravelAtMidnight(player); // R5b
        if (player.Food > 0)
        {
            player.Food--;
            player.DaysWithoutFood = 0;
            player.HungerExhaustion = 0; // a full day's food: its Exhaustion can go with rest
            World.RecordFact("Ate", $"{player.Name} mangia una razione: gliene restano {player.Food}.", player.Id);
        }
        else
        {
            player.DaysWithoutFood++;
            World.RecordFact("Fasted", $"{player.Name} non ha di che mangiare: {player.DaysWithoutFood} giorni senza cibo.", player.Id);
            if (player.DaysWithoutFood >= FastingDays)
            {
                player.HungerExhaustion++;
                AddExhaustion(player, 1, "la fame");
            }
        }

        var drunk = player.DrankToday ? DailyWaterPints : Math.Min(DailyWaterPints, Math.Min(player.WaterPints, WaterCapacity(player)));
        if (!player.DrankToday)
            player.WaterPints -= drunk;
        if (drunk >= DailyWaterPints)
            player.ThirstExhaustion = 0;
        else if (drunk < DailyWaterPints / 2)
        {
            player.ThirstExhaustion++;
            AddExhaustion(player, 1, "la sete");
        }
        player.DrankToday = false;
        TakeWater(player); // a new day starting by the water
    }

    /// <summary>R5a: the Exhaustion a Long Rest may take away: not the levels owed to hunger or thirst.</summary>
    private bool RestEases(Actor actor) => actor.Exhaustion > actor.HungerExhaustion + actor.ThirstExhaustion;
}
