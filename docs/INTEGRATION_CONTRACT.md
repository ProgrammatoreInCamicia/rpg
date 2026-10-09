# Contratto di integrazione Sim ↔ Godot

Responsabile: **Claude** (proprietà passata da Codex in `chat.txt`, Round 5).
Stato: **checkpoint 0–3 (intera vertical slice) pubblicati e compilabili** in `src/RpgSandbox.Sim/Api/`, coperti da test (`tests/RpgSandbox.Sim.Tests/`).
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
AreaId LocationId ActorId StoreId FactionId (string)   ActionId FactId ObservationId (long)

// GameTime.cs: unità = secondo
Duration(long Seconds)  FromSeconds/FromMinutes/FromHours, Zero
GameTime(long Seconds)  Plus(Duration), Since(GameTime), Day/Hour/Minute/Second, Start

// Scenarios
Scenario SliceScenario.Create();
Scenario SliceScenario.Build(farmerShiftStart, farmerShiftEnd, granaryFood = 40)   // varianti per i test
SliceScenario.Ids.{Village, Forest, Inn, Granary, BanditCamp, Player, Raider, Farmer, Guard,
                   GranaryStore, CampStore, VillageFaction, Bandits}
ScenarioBuilder: AddArea, AddLocation, AddRoute, AddFaction(..., authorityId), AddRaidingFaction, AddStore(..., owner),
                 AddActor(..., food, faction, workLocationId, shiftStart, shiftEnd), Build()

// SimulationSession
SimulationSession.Create(Scenario)                 // esegue subito il primo istante: fazioni valutate, NPC decidono
GameTime Now; ActorId Player
CommandResult  Execute(Command)                    // avvia, NON avanza il tempo
void           Advance(Duration)                   // Duration < 0 o overflow => ArgumentOutOfRangeException, nessun effetto
AdvanceResult  AdvanceUntilCompleted(ActionId, Duration maxWait)
WorldView      GetWorldView()                      // vista completa/onnisciente: SOLO debug
PlayerView     GetPlayerView()                     // ciò che il giocatore può sapere: usarla per l'UI di gioco
void           Save(Stream)                        // snapshot JSON autosufficiente, versione schema 3
static LoadResult TryLoad(Stream)                  // NUOVA sessione; quella corrente non viene toccata

// Comandi
TravelCommand      { Actor, Destination }
DepositFoodCommand { Actor, Store, Amount }
TakeFoodCommand    { Actor, Store, Amount }        // da un deposito di un'altra fazione = furto
WaitCommand        { Actor, Duration }
ReportCommand      { Actor, Recipient, Observation }

CommandResult { Success, Rejection, Action?, CompletesAt?, Message }
RejectionReason { None, ActorNotFound, DestinationNotFound, ActorBusy, AlreadyThere, RouteNotFound, UnknownCommand,
                  StoreNotFound, NotAtStore, InvalidAmount, InsufficientFood, StoreEmpty, InvalidDuration,
                  RecipientNotPresent, UnknownObservation, StoreGuarded, CapacityExceeded }
AdvanceResult { Outcome: Completed | TimeLimitReached | NotPending | Cancelled, Now }
LoadResult    { Success, Session?, Error? }

// Viste
WorldView      { Now, Player, Areas, Locations, Routes, Actors, Stores, Factions, RecentFacts }
AreaView       { Id, Name }
LocationView   { Id, Name, Area }
RouteView      { From, To, TravelTime }                 // una voce per direzione
ActorView      { Id, Name, IsPlayer, Faction?, Location?, Food, Action?, Travel?, Assignment?, LastDecision?,
                 Home?, ArrivedAt, Knowledge, GuardDuty? }
ActionView     { Id, Kind ("Travel" | "DepositFood" | "TakeFood" | "Wait" | "Report" | "Guard"), StartedAt, CompletesAt, Description }
TravelView     { Action, Origin, Destination, DepartedAt, ArrivesAt }
AssignmentView { Kind ("Raid"), Faction, Target, Home, Amount, AssignedAt, TakeAttempted, Aborted }
GuardDutyView  { Store, Since, Until }
ObservationView{ Id, Origin, Kind ("Theft"), Store, StoreName, Location, Amount, Thief?, ThiefName?, ObservedAt, LearnedAt,
                 Source?, SourceName?, ToldTo }
DecisionView   { At, Rule, Reason, Inputs }
StoreView      { Id, Name, Location, Food, Owner? }
FactionView    { Id, Name, HomeStore?, DailyUpkeep, Members, NextEvaluation?, LastDecision?, Authority?, AvoidedTargets }
AvoidedTargetView { Store, Until }
FactView       { Id, At, Kind, Description }
  // Kind: TravelStarted, TravelCompleted, FoodDeposited, FoodDepositFailed, FoodTaken, FoodStolen, FoodTakeFailed,
  //       FoodConsumed, RaidOrdered, RaidCompleted, RaidDeterred, RaidAborted, FoodTheftWitnessed, InformationShared,
  //       ReportFailed, GuardDutyStarted, GuardDutyEnded

PlayerView       { Now, Id, Location?, Food, Action?, Travel?, Area, Areas, Locations, Routes,
                   VisibleActors, VisibleStores, Observations, ReportOptions }
VisibleActorView { Id, Name, Faction?, Location?, Travel?, Doing? }   // Doing: solo il gesto, solo nello stesso Luogo
ReportOptionView { Recipient, RecipientName, Observation, Summary }
```

## Semantica

- **Ordine in ogni istante elaborato**:
  1. completamenti delle azioni, con la **percezione** del furto calcolata nel momento in cui avviene;
  2. lavori di fazione in scadenza (consumo, valutazione della politica), in ordine di programmazione;
  3. decisioni degli NPC liberi, in ordine di ID.

  Le parità si risolvono con un ordine stabile. Ogni nuova azione termina in un istante futuro.
- **Travel**: valida attore, destinazione, disponibilità, "già lì" e collegamento diretto, **prima** di mutare. Durante il viaggio `Location == null`.
- **DepositFood / TakeFood**: l'attore deve essere libero e nel Luogo del deposito, con `Amount > 0`.
  - Deposit dura 2 minuti e sposta il cibo **al completamento**, dopo aver ricontrollato le precondizioni.
  - Take dura 3 minuti e al completamento prende `min(Amount, disponibili)`; se le razioni disponibili sono 0 il prelievo fallisce (`FoodTakeFailed`).
  - Il cibo si sposta senza crearsi né distruggersi. Esce dal mondo **solo** con il consumo giornaliero delle fazioni (`FoodConsumed`).
  - Nessun overflow: un deposito o un attore che supererebbe `int.MaxValue` rifiuta all'avvio (`CapacityExceeded`) o non trasferisce al completamento.
- **Wait**: un'azione con durata. Quella del giocatore non si interrompe. Quella di routine di un NPC ("Riposa", "Lavora") viene interrotta da un incarico di fazione, dalla percezione di un furto o da un rapporto ricevuto. **Interrompere un'azione la cancella e ne rimuove la scadenza**: nello scheduler non restano voci orfane.
- **Fazioni**: la politica "razzia" si valuta ogni `EvaluationInterval`. Se è già in corso una razzia, o le scorte di casa sono ≥ soglia, non fa nulla. Altrimenti sceglie il deposito altrui non vuoto più vicino e il primo membro disponibile, a cui assegna l'incarico.
- **NPC**: regole a priorità. Passi della razzia (Riporta il bottino, Torna al campo, Razzia conclusa, Raggiungi il bersaglio, Desisti, Bersaglio vuoto, Ruba; qualunque rifiuto del prelievo chiude la razzia) > Organizza il presidio > Presidia il deposito > Riferisci il furto / Cerca la guardia > Routine (lavoro a turni, casa, riposo). Ogni decisione registra regola, motivo e dati letti (`LastDecision`).
- **Percezione** (deterministica): chi è nel Luogo al momento del furto lo vede. Lo **riconosce** solo se era lì da prima che iniziasse (`ArrivedAt <=` inizio). Ogni testimone riceve un'osservazione autosufficiente, che sopravvive alla potatura della cronaca.
- **Report**: dura 5 minuti e richiede lo stesso Luogo all'inizio e alla fine (altrimenti `ReportFailed`). Il contenuto è fissato all'inizio. Il destinatario riceve una copia con fonte, deduplicata per origine. Se era in un'attesa interrompibile, decide subito.
- **Autorità e presidio**: quando l'autorità della fazione viene a sapere di un furto ai danni della fazione, presidia il deposito per 3 giorni a turni di 1 ora. Un deposito con un presidio presente è **sorvegliato**: il furto viene rifiutato all'avvio (`StoreGuarded`) o fallisce al completamento. Il Razziatore che lo vede desiste, e la sua fazione lo viene a sapere solo al suo rientro: per 24 ore evita quel bersaglio.
- **Vista del giocatore**: `GetPlayerView()` mostra attori e depositi della sua Area (più chi viaggia da o verso di essa), le sue osservazioni e cosa può riferire a chi è presente. Durante un viaggio l'Area è quella di **partenza**, fino all'arrivo. Degli altri si vede solo il **gesto** (`Doing`: "armeggia con le scorte", "parla con X", "sorveglia il deposito"…) e solo se sono nello stesso Luogo, che deve essere un Luogo vero: due viaggiatori non sono mai "nello stesso posto". Intenzioni, argomenti delle conversazioni, decisioni e conoscenze altrui restano fuori. Le decisioni degli NPC leggono le proprie conoscenze e ciò che vedono nel proprio Luogo; restano due semplificazioni: la posizione dei depositi è nota a tutti, e la fazione dei banditi vede quante razioni ci sono nei depositi altrui.
- **Advance(d)**: elabora in ordine ogni scadenza fino a `Now + d`. `Advance(a+b)` equivale a `Advance(a)` seguito da `Advance(b)`, ed è testato anche con NPC attivi.
- **AdvanceUntilCompleted(id, max)**: elabora le scadenze (gli altri attori continuano ad agire) e si ferma alla fine dell'istante in cui `id` termina, dopo tutte le fasi. L'esito è `Completed` solo se l'azione si è davvero completata, `Cancelled` se è stata annullata (anche da un evento precedente nello stesso istante), `NotPending` se l'azione non era in corso già alla chiamata, `TimeLimitReached` se il limite arriva prima. Per il client Completed, Cancelled e NotPending significano tutti "attesa finita"; solo TimeLimitReached lascia l'azione in corso. Dopo un caricamento, l'azione da riprendere è `PlayerView.Action` (S3).
- **Save/Load**:
  - **Versione dello schema 3**: le versioni 1 e 2 vengono rifiutate con un messaggio chiaro. La v3 richiede una corrispondenza 1:1 tra azioni in corso e scadenze (ID unici, nessuna scadenza orfana), oltre ai lavori periodici obbligatori delle fazioni e alle stesse invarianti degli scenari (`Invariants.cs`).
  - Lo snapshot contiene tutto: stato, azioni in corso, incarichi, presidi, conoscenze (con a chi sono state riferite), scadenze con i numeri di sequenza, contatori, ultime decisioni e cronaca.
  - `TryLoad` valida versione, riferimenti, coerenza tra posizione e viaggio, e che ogni azione in corso abbia la sua scadenza. Errori in italiano, leggibili.
  - Testato: continuare senza interruzioni equivale a salvare e caricare a metà di un viaggio, di un furto o di una consegna.
- `RecentFacts` è uno storico limitato (200 voci), dalla più vecchia alla più recente.

## Oltre la slice

Ancora da decidere con l'utente e con Codex. I candidati concordati sono la furtività giocabile (prime prove 5e e RNG serializzabile), il combattimento a turni e il reclutamento di compagni.
