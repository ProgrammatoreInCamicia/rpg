using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// Rules a valid world must satisfy, shared by scenario building and save loading so that a loaded
/// game can never be in a state a scenario could not have produced. Each method returns the problem
/// in words, or null when the data is valid.
/// </summary>
internal static class Invariants
{
    public static string? Faction(string id, int dailyUpkeep, bool hasHomeStore, Duration upkeepTimeOfDay, RaidPolicy? policy)
    {
        if (dailyUpkeep < 0)
            return $"la fazione '{id}' ha un consumo negativo";
        if (dailyUpkeep > 0 && !hasHomeStore)
            return $"la fazione '{id}' consuma cibo ma non ha un deposito";
        if (upkeepTimeOfDay.Seconds is < 0 or >= 86_400)
            return $"l'ora del consumo della fazione '{id}' è fuori dalla giornata";
        if (policy is null)
            return null;
        if (!hasHomeStore)
            return $"la fazione '{id}' fa razzie ma non ha un deposito";
        if (policy.EvaluationInterval.Seconds <= 0)
            return $"l'intervallo di valutazione della fazione '{id}' deve essere positivo";
        if (policy.RaidAmount <= 0)
            return $"la quantità di una razzia della fazione '{id}' deve essere positiva";
        if (policy.FoodThreshold < 0)
            return $"la soglia della fazione '{id}' non può essere negativa";
        return null;
    }

    public static string? Sheet(string actorId, Rules.CharacterSheet sheet)
    {
        if (string.IsNullOrWhiteSpace(sheet.Title))
            return $"la scheda di '{actorId}' non ha un titolo";
        if (sheet.SkillProficiencies.Any(s => !Enum.IsDefined(s)))
            return $"la scheda di '{actorId}' contiene un'abilità sconosciuta";
        foreach (var ability in Enum.GetValues<Rules.Ability>())
            if (sheet.Score(ability) is < 1 or > 30)
                return $"la caratteristica {ability} di '{actorId}' è fuori dall'intervallo 1–30";
        if (sheet.ProficiencyBonus is < 0 or > 6)
            return $"il bonus di competenza di '{actorId}' non è valido";
        if (sheet.ArmorClass is < 1 or > 30)
            return $"la classe armatura di '{actorId}' non è valida";
        return null;
    }

    public static string? Shift(string actorId, bool isPlayer, Duration start, Duration end)
    {
        if (isPlayer)
            return "il giocatore non ha un turno di lavoro";
        if (start.Seconds < 0 || start.Seconds >= end.Seconds || end.Seconds > 86_400)
            return $"il turno di '{actorId}' non è valido (inizio < fine, entro la giornata)";
        return null;
    }
}
