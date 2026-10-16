using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R5b: travel pace and extended travel (SRD 5.2.1 Travel Pace, Extended Travel), for the player's journeys.
internal sealed partial class Simulation
{
    /// <summary>SRD Extended Travel: more than 8 hours of travel a day calls for Constitution saving throws.</summary>
    private const long TravelDaySeconds = 8 * 3600;

    /// <summary>SRD Travel Pace: Fast covers 4 miles an hour, Normal 3, Slow 2: a route's time (at Normal) is 3/4 or 3/2 of it.</summary>
    private static Duration PacedTime(Duration normal, TravelPace pace) => pace switch
    {
        TravelPace.Fast => Duration.FromTicks(normal.Ticks * 3 / 4),
        TravelPace.Slow => Duration.FromTicks(normal.Ticks * 3 / 2),
        _ => normal,
    };

    /// <summary>
    /// R5b: the player's travel time between <paramref name="from"/> and <paramref name="to"/> counts towards today's;
    /// at the end of each hour past the 8th, a Constitution saving throw (DC 10 + 1 for each hour past 8) or a level of
    /// Exhaustion (SRD Extended Travel).
    /// </summary>
    private void AccountTravel(Actor actor, GameTime from, GameTime to)
    {
        if (!actor.IsPlayer || to <= from)
            return;
        var before = actor.TravelSecondsToday;
        actor.TravelSecondsToday += to.Seconds - from.Seconds;
        for (var hour = Math.Max(before / 3600 + 1, 9); hour * 3600 <= actor.TravelSecondsToday; hour++)
        {
            var dc = 10 + (int)(hour - 8);
            if (!SavingThrow(null, actor, Ability.Constitution, dc, "la marcia forzata"))
                AddExhaustion(actor, 1, "la marcia forzata");
        }
    }

    /// <summary>At midnight: the part of a journey in progress that fell on the day ending counts for it; the count starts over.</summary>
    private void TravelAtMidnight(Actor actor)
    {
        var dayStart = GameTime.Start.Plus(Duration.FromDays(Math.Max(0, World.Now.Day - 1))); // the day that just ended
        if (actor.CurrentAction is TravelAction travel)
            AccountTravel(actor, travel.StartedAt > dayStart ? travel.StartedAt : dayStart, World.Now);
        actor.TravelSecondsToday = 0;
    }

    /// <summary>At the arrival: the part of the journey since the start, or since midnight, counts for today.</summary>
    private void TravelAtArrival(Actor actor, TravelAction travel)
    {
        var midnight = GameTime.Start.Plus(Duration.FromDays(World.Now.Day));
        AccountTravel(actor, travel.StartedAt > midnight ? travel.StartedAt : midnight, World.Now);
    }
}
