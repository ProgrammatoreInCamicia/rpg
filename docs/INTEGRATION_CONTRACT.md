# Contratto di integrazione Sim ↔ Godot

Responsabile: **Claude** (proprietà passata da Codex in `chat.txt`, Round 5).
Stato: **checkpoint 0, 1 e 2 pubblicati e compilabili** in `src/RpgSandbox.Sim/Api/`, coperti da test (`tests/RpgSandbox.Sim.Tests/`).
Il codice in `Api/` è la fonte autorevole. Questo documento ne descrive la semantica.

## Regole del confine

- Il client usa solo i tipi pubblici di `RpgSandbox.Sim.Api` e `RpgSandbox.Sim.Scenarios`. Lo stato e il motore (`WorldState`, `Simulation`) sono `internal`.
- Lo stato cambia solo tramite `Execute(command)`. Il tempo passa solo tramite `Advance` e `AdvanceUntilCompleted`. Frame, animazioni e lettura delle viste non consumano tempo.
- Le viste sono **record nominali** con proprietà `init`, non posizionali. Le aggiunte non rompono il client ma vanno **annunciate prima** in `chat.txt`. Rinomine e rimozioni richiedono un accordo esplicito.
- Le collezioni nelle viste sono copie `ReadOnlyCollection`, ordinate per ID e scollegate dallo stato.
- Il client decide in base a `RejectionReason`, a `Kind` e ai campi delle viste, **mai** in base ai testi (`Message`, `Description`, `Reason`), che servono solo a essere mostrati.

## API

```csharp
// Ids.cs: record struct con stringa (ID di scenario) o long (ID allocati dalla sim)
AreaId LocationId ActorId StoreId FactionId (string)   ActionId FactId (long)

// GameTime.cs: unità = secondo
Duration(long Seconds)  FromSeconds/FromMinutes/FromHours, Zero
GameTime(long Seconds)  Plus(Duration), Since(GameTime), Day/Hour/Minute/Second, Start

// Scenarios
Scenario SliceScenario.Create();
SliceScenario.Ids.{Village, Forest, Inn, Granary, BanditCamp, Player, Raider, GranaryStore, CampStore, VillageFaction, Bandits}
ScenarioBuilder: AddArea, AddLocation, AddRoute, AddFaction, AddRaidingFaction, AddStore(..., owner), AddActor(..., food, faction), Build()

// SimulationSession
SimulationSession.Create(Scenario)                 // esegue subito il primo istante: fazioni valutate, NPC decidono
GameTime Now; ActorId Player
CommandResult  Execute(Command)                    // avvia, NON avanza il tempo
void           Advance(Duration)                   // Duration < 0 o overflow => ArgumentOutOfRangeException, nessun effetto
AdvanceResult  AdvanceUntilCompleted(ActionId, Duration maxWait)
WorldView      GetWorldView()                      // vista completa/onnisciente (rendering prototipo + debug)
void           Save(Stream)                        // snapshot JSON autosufficiente, versione schema 1
static LoadResult TryLoad(Stream)                  // NUOVA sessione; quella corrente non viene toccata

// Comandi
TravelCommand      { Actor, Destination }
DepositFoodCommand { Actor, Store, Amount }
TakeFoodCommand    { Actor, Store, Amount }        // da un deposito di un'altra fazione = furto
WaitCommand        { Actor, Duration }

CommandResult { Success, Rejection, Action?, CompletesAt?, Message }
RejectionReason { None, ActorNotFound, DestinationNotFound, ActorBusy, AlreadyThere, RouteNotFound, UnknownCommand,
                  StoreNotFound, NotAtStore, InvalidAmount, InsufficientFood, StoreEmpty, InvalidDuration }
AdvanceResult { Outcome: Completed | TimeLimitReached | NotPending, Now }
LoadResult    { Success, Session?, Error? }

// Viste
WorldView      { Now, Player, Areas, Locations, Routes, Actors, Stores, Factions, RecentFacts }
AreaView       { Id, Name }
LocationView   { Id, Name, Area }
RouteView      { From, To, TravelTime }                 // una voce per direzione
ActorView      { Id, Name, IsPlayer, Faction?, Location?, Food, Action?, Travel?, Assignment?, LastDecision? }
ActionView     { Id, Kind ("Travel" | "DepositFood" | "TakeFood" | "Wait"), StartedAt, CompletesAt, Description }
TravelView     { Action, Origin, Destination, DepartedAt, ArrivesAt }
AssignmentView { Kind ("Raid"), Faction, Target, Home, Amount, AssignedAt, TakeAttempted }
DecisionView   { At, Rule, Reason, Inputs }
StoreView      { Id, Name, Location, Food, Owner? }
FactionView    { Id, Name, HomeStore?, DailyUpkeep, Members, NextEvaluation?, LastDecision? }
FactView       { Id, At, Kind, Description }
  // Kind: TravelStarted, TravelCompleted, FoodDeposited, FoodDepositFailed, FoodTaken, FoodStolen, FoodTakeFailed,
  //       FoodConsumed, RaidOrdered, RaidCompleted
```

## Semantica

- **Ordine in ogni istante elaborato**:
  1. completamenti delle azioni;
  2. (percezione, dal checkpoint 3);
  3. lavori di fazione in scadenza (consumo, valutazione della politica), in ordine di programmazione;
  4. decisioni degli NPC liberi, in ordine di ID.

  Le parità si risolvono con un ordine stabile. Ogni nuova azione termina in un istante futuro.
- **Travel**: valida attore, destinazione, disponibilità, "già lì" e collegamento diretto, **prima** di mutare. Durante il viaggio `Location == null`.
- **DepositFood / TakeFood**: l'attore deve essere libero e nel Luogo del deposito, con `Amount > 0`.
  - Deposit dura 2 minuti e sposta il cibo **al completamento**, dopo aver ricontrollato le precondizioni.
  - Take dura 3 minuti e al completamento prende `min(Amount, disponibili)`; se le razioni disponibili sono 0 il prelievo fallisce (`FoodTakeFailed`).
  - Il cibo si sposta senza crearsi né distruggersi. Esce dal mondo **solo** con il consumo giornaliero delle fazioni (`FoodConsumed`).
- **Wait**: un'azione con durata. Quella del giocatore non si interrompe; quella di routine di un NPC ("Riposa") può essere interrotta solo da un incarico di fazione.
- **Fazioni**: la politica "razzia" si valuta ogni `EvaluationInterval`. Se è già in corso una razzia, o le scorte di casa sono ≥ soglia, non fa nulla. Altrimenti sceglie il deposito altrui non vuoto più vicino e il primo membro disponibile, a cui assegna l'incarico.
- **NPC**: regole a priorità. Riporta il bottino > Torna al campo > Razzia conclusa > Raggiungi il bersaglio > Ruba > Routine: riposa. Ogni decisione registra regola, motivo e dati letti (`LastDecision`). Per ora le decisioni leggono lo stato oggettivo; le conoscenze arrivano con il checkpoint 3.
- **Advance(d)**: elabora in ordine ogni scadenza fino a `Now + d`. `Advance(a+b)` equivale a `Advance(a)` seguito da `Advance(b)`, ed è testato anche con NPC attivi.
- **AdvanceUntilCompleted(id, max)**: elabora le scadenze (gli altri attori continuano ad agire) e si ferma alla fine dell'istante in cui `id` si completa. Se l'azione non è in corso restituisce `NotPending`.
- **Save/Load**:
  - Lo snapshot contiene tutto: stato, azioni in corso, incarichi, scadenze con i numeri di sequenza, contatori, ultime decisioni e cronaca.
  - `TryLoad` valida versione, riferimenti, coerenza tra posizione e viaggio, e che ogni azione in corso abbia la sua scadenza. Errori in italiano, leggibili.
  - Testato: continuare senza interruzioni equivale a salvare e caricare a metà di un viaggio, di un furto o di una consegna.
- `RecentFacts` è uno storico limitato (200 voci), dalla più vecchia alla più recente.

## Prossima estensione (checkpoint 3, da annunciare prima)

Percezione deterministica (testimone del furto), osservazioni con autore anche ignoto, comando di rapporto e presidio della guardia, vista filtrata per il giocatore (`PlayerView`), distinta da `GetWorldView()`.
