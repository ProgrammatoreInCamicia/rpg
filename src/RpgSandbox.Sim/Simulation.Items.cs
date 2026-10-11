using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// C3b: picking up and dropping items. Outside a fight both are instant; in a fight, picking up uses the turn's free
// interaction with an object (SRD), dropping is free.
internal sealed partial class Simulation
{
    private CommandResult PickUp(PickUpCommand command)
    {
        var actor = World.Actors[command.Actor];
        if (World.Content.Item(command.Item.Value) is not { } item)
            return CommandResult.Rejected(RejectionReason.NothingThere, "Oggetto sconosciuto.");
        if (command.Count <= 0)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, "Quanti?");
        if (MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here)
            return CommandResult.Rejected(RejectionReason.NoMap, "Qui non c'è niente da raccogliere.");
        if (!here.IsAdjacentOrSame(command.At) || !OpenDiagonal(map, here, command.At))
            return CommandResult.Rejected(RejectionReason.OutOfReach, "È troppo lontano: avvicinati.");
        if (map.OnGround(command.At, command.Item) < command.Count)
            return CommandResult.Rejected(RejectionReason.NothingThere, $"Lì non ci sono {command.Count} {item.Name}.");
        if (EncounterOf(actor) is { } e)
        {
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            var me = CombatantOf(e, actor.Id);
            if (me.Interacted)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'interazione con un oggetto in questo turno.");
            me.Interacted = true;
        }
        else if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        map.PutOnGround(command.At, command.Item, -command.Count);
        actor.Give(item.Id, command.Count);
        World.RecordFact("ItemPickedUp", $"{actor.Name} raccoglie {command.Count} × {item.Name}.", actor.Id);
        return new CommandResult { Message = $"Raccogli {command.Count} × {item.Name}." };
    }

    private CommandResult Drop(DropCommand command)
    {
        var actor = World.Actors[command.Actor];
        if (World.Content.Item(command.Item.Value) is not { } item)
            return CommandResult.Rejected(RejectionReason.NotCarried, "Oggetto sconosciuto.");
        if (command.Count <= 0)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, "Quanti?");
        if (actor.Carried(item.Id) < command.Count)
            return CommandResult.Rejected(RejectionReason.NotCarried, $"Non hai {command.Count} × {item.Name}.");
        // D1a: what one wears or wields stays on until there is a command to take it off.
        if (actor.Carried(item.Id) - command.Count < actor.Equipment.Items.Count(i => i == item.Id))
            return CommandResult.Rejected(RejectionReason.Equipped, $"{item.Name}: lo indossi o lo impugni, non puoi lasciarlo.");
        if (MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here)
            return CommandResult.Rejected(RejectionReason.NoMap, "Qui non puoi lasciare niente.");
        if (EncounterOf(actor) is { } e && !IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        actor.Give(item.Id, -command.Count);
        map.PutOnGround(here, command.Item, command.Count);
        World.RecordFact("ItemDropped", $"{actor.Name} lascia a terra {command.Count} × {item.Name}.", actor.Id);
        return new CommandResult { Message = $"Lasci a terra {command.Count} × {item.Name}." };
    }
}
