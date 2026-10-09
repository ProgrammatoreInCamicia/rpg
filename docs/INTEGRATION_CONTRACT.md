# Contratto di integrazione Sim ↔ Godot

Responsabile: **Claude** (proprietà passata da Codex in `chat.txt`, Round 5).
Stato: **checkpoint 0 pubblicato e compilabile** in `src/RpgSandbox.Sim/Api/`, coperto da test (`tests/RpgSandbox.Sim.Tests/`).
Il codice in `Api/` è la fonte autorevole. Questo documento ne descrive la semantica.

## Regole del confine

- Il client usa solo i tipi pubblici di `RpgSandbox.Sim.Api` e `RpgSandbox.Sim.Scenarios`. `WorldState` e tutto il resto sono `internal`.
- Lo stato cambia solo tramite `Execute(command)`. Il tempo passa solo tramite `Advance` e `AdvanceUntilCompleted`. Frame, animazioni e lettura delle viste non consumano tempo.
- Le viste sono **record nominali** con proprietà `init`, non posizionali. Aggiungere una proprietà non rompe il client, ma le aggiunte vanno comunque **annunciate prima** in `chat.txt`. Rinomine e rimozioni richiedono un accordo esplicito.
- Le collezioni nelle viste sono copie `ReadOnlyCollection`, ordinate per ID e scollegate dallo stato: una vista già consegnata non cambia più.
- Il client decide in base a `RejectionReason` e ai campi delle viste, **mai** in base a `Message`, che è solo testo da mostrare.

## API — checkpoint 0

```csharp
// Ids.cs: record struct con stringa (ID di scenario) o long (ID allocati dalla sim)
AreaId(string) LocationId(string) ActorId(string) ActionId(long) FactId(long)

// GameTime.cs: unità = secondo
Duration(long Seconds)  FromSeconds/FromMinutes/FromHours, Zero
GameTime(long Seconds)  Plus(Duration), Since(GameTime), Day/Hour/Minute/Second, Start

// Scenarios
Scenario SliceScenario.Create();             // Villaggio: Locanda <-> Granaio (5 min), Protagonista alla Locanda
SliceScenario.Ids.{Village, Inn, Granary, Player}
ScenarioBuilder (AddArea/AddLocation/AddRoute/AddActor/Build); valida i riferimenti in Build()

// Api/SimulationSession.cs
SimulationSession SimulationSession.Create(Scenario)
GameTime Now; ActorId Player
CommandResult  Execute(Command)                                  // avvia, NON avanza il tempo
void           Advance(Duration)                                 // Duration < 0 o overflow => ArgumentOutOfRangeException, nessun effetto
AdvanceResult  AdvanceUntilCompleted(ActionId, Duration maxWait)
WorldView      GetWorldView()                                     // vista completa/onnisciente (rendering prototipo + debug)

// Comandi
TravelCommand { Actor, Destination }

CommandResult { Success, Rejection, Action?, CompletesAt?, Message }
RejectionReason { None, ActorNotFound, DestinationNotFound, ActorBusy, AlreadyThere, RouteNotFound, UnknownCommand }
AdvanceResult { Outcome: Completed | TimeLimitReached | NotPending, Now }

// Viste
WorldView    { Now, Player, Areas, Locations, Routes, Actors, RecentFacts }
AreaView     { Id, Name }
LocationView { Id, Name, Area }
RouteView    { From, To, TravelTime }          // una voce per direzione
ActorView    { Id, Name, IsPlayer, Location?, Travel? }
TravelView   { Action, Origin, Destination, DepartedAt, ArrivesAt }
FactView     { Id, At, Kind, Description }     // Kind: "TravelStarted", "TravelCompleted"
```

## Semantica

- **Travel**: valida attore, destinazione, disponibilità, "già lì" e collegamento diretto, **prima** di mutare. In caso di rifiuto non c'è nessun effetto. Durante il viaggio `ActorView.Location == null` e `Travel != null`. L'arrivo avviene esattamente a `ArrivesAt`.
- **Advance(d)**: elabora in ordine ogni scadenza fino a `Now + d` compreso, poi imposta `Now = Now + d`. `Advance(a+b)` equivale a `Advance(a)` seguito da `Advance(b)`, ed è testato.
- **AdvanceUntilCompleted(id, max)**: elabora le scadenze, quindi anche gli altri attori continuano ad agire, e si ferma alla fine dell'istante in cui `id` si completa (`Completed`). Se il limite arriva prima, si ferma lì con `Now = Now + max` (`TimeLimitReached`). Se l'azione non è in corso restituisce `NotPending` e il tempo non si muove. Per il giocatore: `Execute(Travel)` → `AdvanceUntilCompleted(result.Action, limite)`.
- Le parità tra scadenze si risolvono con un ordine stabile (istante, poi ordine di programmazione).
- `RecentFacts` è uno storico limitato (200 voci), dalla più vecchia alla più recente.

## Prossime estensioni (da annunciare in `chat.txt` prima dell'implementazione)

1. **Checkpoint 1**: depositi di cibo (`StockView`), comando di trasferimento con durata e nuovi `RejectionReason` (quantità non valida o insufficiente).
2. **Checkpoint 2**: azioni autonome degli NPC e ultima decisione (`DebugNpcView`); `Save(Stream)` / `Load(Stream) -> LoadResult`, dove un caricamento fallito lascia intatta la sessione. Godot sceglie il percorso del file e mostra gli errori.
3. **Checkpoint 3**: vista del giocatore filtrata dal core (`PlayerView`), distinta da `GetWorldView()`; comando di rapporto.
