using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R4b: weapons and shield in hand, and fighting with two weapons (SRD 5.2.1 Weapons: Light, Nick; Equipping and
// Unequipping as the turn's free object interaction; Two-Weapon Fighting feat).
internal sealed partial class Simulation
{
    /// <summary>
    /// Takes a carried weapon or shield in hand, or puts away what a hand holds. Two hands: a Two-Handed weapon takes both;
    /// the other hand holds a shield or a second weapon. ADAPTATION: nothing is put away by itself, a hand must be free first.
    /// </summary>
    private CommandResult Equip(EquipCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (command.Slot is not (EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.Shield))
            return CommandResult.Rejected(RejectionReason.UnknownCommand, "Le armature si indossano in minuti: non così.");
        var eq = actor.Equipment;
        var now = command.Slot switch { EquipmentSlot.MainHand => eq.MainHand, EquipmentSlot.OffHand => eq.OffHand, _ => eq.Shield };
        if (now == command.Item)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Item is null ? "Quella mano è già libera." : "Lo impugni già.");
        string? why = null;
        if (command.Item is { } id)
        {
            var item = World.Content.Item(id);
            var inUse = eq.Items.Count(i => i == id) - (now == id ? 1 : 0);
            if (item is null || actor.Carried(id) <= inUse)
                why = "Non lo porti con te.";
            else if (command.Slot == EquipmentSlot.Shield ? item.Armor is not { Type: ArmorType.Shield } : item.Weapon is null)
                why = command.Slot == EquipmentSlot.Shield ? "Non è uno scudo." : "Non è un'arma.";
        }
        var next = command.Slot switch
        {
            EquipmentSlot.MainHand => eq with { MainHand = command.Item },
            EquipmentSlot.OffHand => eq with { OffHand = command.Item },
            _ => eq with { Shield = command.Item },
        };
        bool TwoHanded(string? weapon) => weapon is not null && World.Content.Item(weapon)?.Weapon is { } w && w.Has(WeaponProperty.TwoHanded);
        why ??= next.OffHand is not null && next.Shield is not null ? "Lo scudo e la seconda arma vogliono la stessa mano: prima libera una mano."
            : TwoHanded(next.MainHand) && (next.OffHand is not null || next.Shield is not null) ? "Quell'arma vuole due mani: prima liberane una."
            : TwoHanded(next.OffHand) ? "Un'arma a due mani non sta nella mano secondaria."
            : null;
        if (why is not null)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, why);

        var e = EncounterOf(actor);
        string paid = "";
        if (e is not null)
        {
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            var me = CombatantOf(e, actor.Id);
            if (me.Interacted && me.ActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'interazione con un oggetto e l'azione in questo turno.");
            if (me.Interacted)
            {
                me.ActionUsed = true;
                paid = " (usi l'azione)";
            }
            else
                me.Interacted = true;
        }
        else if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");

        actor.Equipment = next;
        World.Refresh(actor);
        var text = command.Item is { } taken
            ? $"{actor.Name} impugna {World.Content.Item(taken)!.Name}{paid}."
            : $"{actor.Name} ripone {World.Content.Item(now!)?.Name ?? now}{paid}.";
        World.RecordFact("Equipped", text, actor.Id);
        return new CommandResult { Message = text };
    }

    /// <summary>
    /// SRD Light: why the extra attack with the weapon in the other hand cannot be made now (null if it can): after the
    /// Attack action with a Light weapon this turn, once; a Bonus Action, or part of the Attack action with Nick.
    /// </summary>
    private string? OffHandRefused(Encounter? e, Actor actor, AttackProfile attack)
    {
        if (!attack.OffHand)
            return "Non è l'arma della mano secondaria.";
        if (e is null || !IsTurnOf(e, actor))
            return "Solo nel tuo turno, durante uno scontro.";
        var me = CombatantOf(e, actor.Id);
        if (!me.AttackedWithLight)
            return "Prima attacca con un'arma leggera, con l'azione Attacco.";
        if (me.OffHandUsed)
            return "Hai già fatto l'attacco con la mano secondaria.";
        return attack.Mastery != WeaponMastery.Nick && me.BonusActionUsed ? "Hai già usato l'azione bonus in questo turno." : null;
    }

    /// <summary>Spends what the extra attack costs: nothing more with Nick (part of the Attack action), else the Bonus Action.</summary>
    private void TakeOffHandAttack(Encounter e, Actor actor, AttackProfile attack)
    {
        var me = CombatantOf(e, actor.Id);
        me.OffHandUsed = true;
        if (attack.Mastery != WeaponMastery.Nick)
            me.BonusActionUsed = true;
    }

    /// <summary>Whether the weapon of this attack has the Light property.</summary>
    private bool LightWeapon(AttackProfile attack) =>
        attack.Requires is { } id && World.Content.Item(id)?.Weapon is { } w && w.Has(WeaponProperty.Light);
}
