# Contratto di integrazione Sim ↔ Godot

Responsabile: **Claude** (proprietà passata da Codex in `chat.txt`, Round 5).
Stato: **checkpoint 0–3, T5 ridotta e T4 (regole, dadi, luce, furto del giocatore) pubblicati e compilabili** in `src/RpgSandbox.Sim/Api/`, coperti da test (`tests/RpgSandbox.Sim.Tests/`).
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
void           Save(Stream)                        // snapshot JSON autosufficiente, versione schema 10
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
                 Home?, ArrivedAt, Knowledge, GuardDuty?, Vigil?, Sheet?, Claims }
ClaimView      { Thief, Store, Owed }
VigilView      { Store, Until }
ActionView     { Id, Kind ("Travel" | "DepositFood" | "TakeFood" | "Wait" | "Report" | "Guard" | "Confiscate"), StartedAt, CompletesAt, Description }
TravelView     { Action, Origin, Destination, DepartedAt, ArrivesAt }
AssignmentView { Kind ("Raid"), Faction, Target, Home, Amount, AssignedAt, TakeAttempted, Aborted }
GuardDutyView  { Store, Since, Until }
ObservationView{ Id, Origin, Kind ("Theft"), Store, StoreName, Location, Amount, Thief?, ThiefName?, ObservedAt, LearnedAt,
                 Source?, SourceName?, ToldTo, Perceived ("Seen" | "Heard") }
DecisionView   { At, Rule, Reason, Inputs }
StoreView      { Id, Name, Location, Food, Owner? }
FactionView    { Id, Name, HomeStore?, DailyUpkeep, Members, NextEvaluation?, LastDecision?, Authority?, AvoidedTargets }
AvoidedTargetView { Store, Until }
FactView       { Id, At, Kind, Description }
  // Kind: TravelStarted, TravelCompleted, FoodDeposited, FoodDepositFailed, FoodTaken, FoodStolen, FoodTakeFailed,
  //       FoodConsumed, RaidOrdered, RaidCompleted, RaidDeterred, RaidAborted, FoodTheftWitnessed, InformationShared,
  //       ReportFailed, GuardDutyStarted, GuardDutyEnded, VigilStarted, VigilEnded, Roll, FoodTheftUnnoticed,
  //       FoodConfiscated, ConfiscationFailed

PlayerView       { Now, Id, Location?, Food, Sheet, Action?, Travel?, Light, UnseenNearby, Area, Areas, Locations, Routes,
                   VisibleActors, VisibleStores, Observations, ReportOptions (superato), PeopleHere, RecentEvents }
PersonView       { Id, Name, Doing?, Topics }                         // chi è qui: "Parla con…"
TopicView        { Kind ("Tell"), Observation, Summary }              // cosa gli puoi dire
VisibleActorView { Id, Name, Faction?, Location?, Travel?, Doing? }   // Doing: solo il gesto, solo nello stesso Luogo
ReportOptionView { Recipient, RecipientName, Observation, Summary }
```

## Semantica

Le parti marcate ADATTAMENTO sono scelte del gioco che applicano lo SRD 5.2.1 alla simulazione: non sono regole dello SRD.

### Tempo e azioni
- **Ordine in ogni istante elaborato**:
  1. completamenti delle azioni, con la percezione del furto calcolata in quel momento;
  2. lavori di fazione in scadenza (consumo, valutazione della politica), in ordine di programmazione;
  3. decisioni degli NPC liberi, in ordine di ID.

  Le parità si risolvono con un ordine stabile. Ogni nuova azione termina in un istante futuro.
- **Advance(d)**: elabora in ordine ogni scadenza fino a `Now + d`. `Advance(a+b)` equivale a `Advance(a)` seguito da `Advance(b)`.
- **AdvanceUntilCompleted(id, max)**: si ferma alla fine dell'istante in cui `id` termina, dopo tutte le fasi. Esiti:
  - `Completed`: l'azione si è davvero completata;
  - `Cancelled`: l'azione è stata annullata, anche da un evento precedente nello stesso istante;
  - `NotPending`: l'azione non era in corso già alla chiamata;
  - `TimeLimitReached`: il limite è arrivato prima.

  Per il client, Completed, Cancelled e NotPending significano "attesa finita". Dopo un caricamento, l'azione da riprendere è `PlayerView.Action`.
- **Wait**: quella del giocatore non si interrompe. Quella di routine di un NPC ("Riposa", "Lavora", "Vigila") viene interrotta da un incarico di fazione, dalla percezione di un furto o da un rapporto ricevuto. Interrompere un'azione la cancella e ne rimuove la scadenza.

### Cibo
- **Travel**: valida tutto **prima** di mutare. Durante il viaggio `Location == null`.
- **DepositFood / TakeFood**: l'attore deve essere libero e nel Luogo del deposito, con `Amount > 0`.
  - Deposit dura 2 minuti, Take 3. Il cibo si sposta al completamento, dopo aver ricontrollato le precondizioni. Take prende `min(Amount, disponibili, capacità)`.
  - Il cibo non si crea né si distrugge: esce dal mondo solo con il consumo delle fazioni (`FoodConsumed`).
  - Un trasferimento che farebbe superare `int.MaxValue` viene rifiutato (`CapacityExceeded`).
  - Prendere da un deposito di un'altra fazione è un **furto**, e un deposito con un presidio presente è **sorvegliato** (`StoreGuarded`).

### Luce, Furtività e percezione (ADATTAMENTO)
- **Luce per Luogo** (`Light`):
  - all'aperto: piena dalle 07 alle 19, fioca alle 06 e alle 19, buio nelle altre ore;
  - i luoghi al chiuso illuminati (la Locanda) non scendono mai sotto la luce fioca.
- **Un furto fa una sola prova di Furtività**, all'inizio (fatto `Roll`, armatura con svantaggio). Il ladro conosce il proprio tiro (nel `Message`), mai chi l'ha notato.
- **Ogni presente valuta il furto con la luce migliore** nel tratto a cui ha assistito, da `max(inizio, suo arrivo)` alla fine.
- **Vista e udito sono separati.**
  - Vista: in luce piena si vede; in luce fioca si confronta la Percezione passiva −5 con la Furtività; al buio non si vede nulla.
  - Udito: Percezione passiva contro Furtività, con qualunque luce.
  - Ci si accorge del furto con la vista oppure con l'udito. Si vede chi è stato solo con la vista.
- **Riconoscere il ladro** richiede di averlo visto e di essere stato presente dall'inizio.
- **Le osservazioni sono autosufficienti** e registrano come è stato percepito il furto (`Perceived`: "Seen"/"Heard"), dato che si conserva nei rapporti. Sopravvivono alla potatura della cronaca.
- **Semplificazioni dichiarate:**
  - chi viene riconosciuto è identificato per nome (l'identità è un'etichetta);
  - la posizione dei depositi è nota a tutti;
  - la fazione dei banditi vede quante razioni ci sono nei depositi altrui.

### NPC e fazioni
- **Regole a priorità**, nell'ordine:
  1. passi della razzia (Riporta il bottino, Torna al campo, Razzia conclusa, Raggiungi il bersaglio, Desisti, Bersaglio vuoto, Ruba);
  2. Ferma il ladro;
  3. Riporta le razioni;
  4. Organizza il presidio;
  5. Presidia il deposito;
  6. Riferisci il furto / Cerca la guardia;
  7. Vigila;
  8. Routine (lavoro a turni, casa, riposo).

  Ogni decisione registra regola, motivo e dati letti (`LastDecision`).
- **Fazioni**: la politica "razzia" si valuta ogni `EvaluationInterval`. Sceglie il deposito altrui non vuoto più vicino, escludendo quelli che un membro, al suo rientro, ha segnalato come sorvegliati (evitati per 24 ore), e il primo membro disponibile.
- **Report**: dura 5 minuti e richiede lo stesso Luogo all'inizio e alla fine. Il contenuto è fissato all'inizio. Il destinatario riceve una copia con fonte, deduplicata per origine.
- **Politiche del villaggio** (T5; provvisorie e non universali):
  - un membro riferisce all'autorità solo ciò che ha visto di persona;
  - chi viene a sapere di un furto, anche per sentito dire, vigila sul deposito dalle 07 alle 18 per 3 giorni;
  - l'autorità agisce su qualunque rapporto ricevuto.
- **Presidio**: l'autorità che viene a sapere di un furto ai danni della fazione presidia il deposito per 3 giorni, a turni di 1 ora.
- **Debito e confisca**:
  - L'autorità apre **un debito per furto**, legato al fatto e non alla testimonianza, solo se conosce il ladro. Più testimonianze dello stesso furto non sommano nulla, e un furto saldato non si riapre.
  - È un **debito sul cibo posseduto**, non un tracciamento delle razioni rubate: restituire volontariamente non lo estingue.
  - **Contatto**: quando il ladro, con delle razioni, arriva dove si trova l'autorità (in guardia o a riposo), l'autorità decide subito. Se è l'autorità ad arrivare, decide al suo arrivo. Nessun inseguimento né conoscenza a distanza.
  - La confisca (2 minuti) prende al massimo il dovuto e ciò che il ladro ha. Il **carico** confiscato (`Cargo`) torna al deposito da cui era stato rubato; il cibo personale non viene mai toccato.

### Vista del giocatore
- **`GetPlayerView()`** contiene la mappa (conoscenza comune), le osservazioni del giocatore, la sua scheda, la luce e i fatti a cui ha preso parte (`RecentEvents`, che non dicono chi lo ha visto).
- **Chi vede**: gli attori della sua Area che si trovano in un Luogo con almeno luce fioca; i viaggiatori diretti a quell'Area o in partenza da essa, solo di giorno. Durante un viaggio l'Area è quella di partenza.
- **Al buio**: chi è accanto al giocatore conta solo come presenza (`UnseenNearby`), senza nome, fazione né gesto, e non compare in `PeopleHere`.
- **I depositi** si vedono se sono illuminati o se il giocatore si trova lì.
- **Degli altri** si vede solo il gesto (`Doing`), e solo nel proprio Luogo. Intenzioni, argomenti, decisioni e conoscenze altrui restano fuori.

### Save/Load
- **Versione dello schema 8** (la 6 ha aggiunto luoghi illuminati, modo di percezione, debiti per furto, furti saldati e carico; la 7 mappe, posizioni e cammini; la 8 le sorgenti di luce delle mappe; la 9 la furtività; la 10 porte e torce). Le versioni 1–9 vengono rifiutate con un messaggio chiaro.
- Lo snapshot è autosufficiente e contiene anche lo stato del generatore `SplitMix64`, con il nome dell'algoritmo.
- `TryLoad` valida forma, riferimenti, invarianti condivise con gli scenari (`Invariants.cs`), la corrispondenza 1:1 tra azioni e scadenze, i lavori periodici e la coerenza tra carico e cibo. Gli errori sono in italiano e leggibili.
- Testato: continuare senza interruzioni equivale a salvare e caricare a metà di viaggi, furti, rapporti, presidi, vigilanze e confische.
- `RecentFacts` è uno storico limitato (200 voci), dalla più vecchia alla più recente.

### Regole (SRD 5.2.1)
- **`RpgSandbox.Sim.Rules`**: `CharacterSheet`, `Abilities.Modifier`, `Bonus(skill)`, `PassivePerception(adv, dis)`, `D20Roll`. Attribuzione in `CREDITS.md`.
- **Personaggi**: il Protagonista è un **Paladino 1 (Accolito)**; gli NPC hanno schede originali.
- **Casualità**: un solo generatore. Stesso seme e stesse scelte danno la stessa partita (`ScenarioBuilder.WithSeed`).

## Oltre la slice

Da decidere con l'utente e con Codex. I candidati sono il combattimento a turni, il reclutamento di compagni, le voci in Locanda e la fiducia, le razzie notturne, e un'autorità che cerca attivamente il ladro.

## T6a — Mappe e movimento

- **Mappe** (`ScenarioBuilder.AddMap`): una griglia per Area, una casella da 5 piedi per carattere. `#` è bloccato, `.` è terreno aperto fuori dai Luoghi (la strada), una lettera indica una casella del Luogo corrispondente (le zone). Depositi e attori hanno una casella (`at`); un deposito occupa la sua casella.
- **Posizione**: `GridPos`, presente in `ActorView.Position`, `PlayerView.Position`, `VisibleActorView.Position` e `StoreView.Position`. Durante un cammino è l'ultima casella raggiunta. `Location` è la zona della casella (null sulla strada) e cambia nell'istante in cui si attraversa il confine.
- **MoveCommand { Actor, To }**: percorso A* deterministico su 8 direzioni, in cui ogni passo costa 1 e non si tagliano gli angoli dei muri (regole della griglia dello SRD). La casella i si raggiunge a `ceil(i·30/Speed)` secondi dalla partenza: con Speed 30 è 1 secondo per casella. Un nuovo ordine sostituisce il cammino in corso. Rifiuti: `NoMap`, `Unreachable`, `AlreadyThere`, `ActorBusy`.
- **StopCommand { Actor }**: ci si ferma sull'ultima casella raggiunta (`NotMoving` se non si sta camminando). Il tempo passa solo durante le azioni: fermarsi ferma l'orologio.
- **Portata sulle mappe**: depositi e conversazioni (`Report`, `PeopleHere`) richiedono una casella adiacente, controllata all'inizio e al completamento (`OutOfReach`). Dove non c'è una mappa resta valido "stesso Luogo".
- **Viste**: `PlayerView.Map` (`MapView` con righe, zone e `S` per i depositi) e `PlayerView.Move` / `ActorView.Move` (`MoveView`: From, Path, DepartedAt, Speed). Il client anima solo ciò che queste viste descrivono.
- **Scenario**: `MappedVillageScenario`, con strada, Locanda e Granaio. Non ci sono ancora i banditi: arrivano con la T6c. Lo scenario a Luoghi (`SliceScenario`) resta invariato.
- **Salvataggi**: schema 7, con le mappe, le posizioni, i cammini in corso e le loro tappe.

## T6b — Vista e udito sulle mappe (ADATTAMENTO)

Vale solo per le Aree mappate; altrove restano le regole per Luogo descritte sopra.

- **Linea di vista**: retta di Bresenham tra i centri delle caselle. La bloccano i muri e i passaggi diagonali stretti tra due muri. È simmetrica: basta che uno dei due versi sia libero. I depositi non bloccano la vista.
- **Luce di una casella** (T6b-bis): la migliore fra la luce del giorno e le **sorgenti di luce** della mappa (SRD 5.2.1: Lamp luce piena 15 ft + fioca per altri 30; Torch 20+20; Candle 5+5). La distanza si conta in caselle da 5 ft e la luce è fermata dai muri come la vista: esce dalla Locanda solo dalla porta. Il flag `Lit` dei Luoghi vale solo per le Aree senza mappa. **Si vede** chi è in linea di vista e su una casella con almeno luce fioca, a qualunque distanza.
- **Udito**: il suono percorre le caselle aggirando i muri e si sente entro **6 caselle** (30 piedi, valore di bilanciamento).
- **Furti sulle mappe**: possono notarli tutti gli attori della stessa mappa.
  - Vista: linea di vista sul ladro e luce della sua casella, considerando la luce migliore nel tratto osservato; in luce fioca Percezione passiva −5.
  - Udito: entro il raggio, Percezione passiva contro Furtività.
  - Riconoscimento: il ladro deve essere visto alla fine e in linea di vista anche dalla posizione che il testimone aveva all'inizio. Quella posizione si ricava dalla sua azione in corso; chi ha cambiato zona dopo l'inizio non riconosce. È un'approssimazione: non c'è una storia completa delle posizioni.
- **PlayerView sulle mappe**:
  - `VisibleActors` contiene chi il giocatore vede, con il suo gesto (`Doing`) anche a distanza;
  - `PeopleHere` contiene chi è adiacente e visibile;
  - `UnseenNearby` conta chi è adiacente ma non visibile;
  - `VisibleStores` contiene i depositi in vista o adiacenti.
- **Restano a zone fino alla T6c**: presidio, Desisti, vigilanza e contatto con l'autorità.
- **Luce nella PlayerView** (T6b-bis): `MapLight` riporta una riga per ogni riga della mappa, con un carattere per casella (`0` buio, `1` fioca, `2` piena). Sono i livelli usati dalle regole; la sfumatura tra caselle è solo grafica. `Light` è la luce della casella del giocatore, `Daylight` la luce del giorno all'aperto.
- **Avvicinarsi a qualcuno** (T6b-bis): con `MoveCommand { To, StopNextTo = true }` si percorre lo stesso cammino verso `To` fermandosi sulla casella precedente, sempre adiacente e con la diagonale libera. Se non serve alcun passo, il comando è rifiutato con `AlreadyThere`. Nel client, un clic sul corpo di una persona significa "parla con lei": se è accanto si apre la conversazione, altrimenti il personaggio si avvicina e la apre all'arrivo. La conversazione si chiude allontanandosi o con "Congedati".

## F1 — Furtività in movimento (ADATTAMENTO di SRD 5.2.1: Travel Pace e azione Hide)

- `MoveCommand { Stealthy = true }`: si cammina all'andatura **Lenta** (velocità × 2/3: per Speed 30, una casella ogni 1,5 s).
  - Alla prima camminata furtiva si tira **una** prova di Furtività, con svantaggio se l'armatura lo impone. Il totale resta in `Actor.Sneak` finché si resta furtivi: un nuovo clic non ritira.
  - Il percorso furtivo considera la luce: ogni casella costa 1, più 1 se è in luce fioca e 3 se è in luce piena. Il personaggio allunga un po' il giro per restare al buio, ma il tempo si conta sempre in caselle percorse.
- **Fine della furtività**: una camminata normale, oppure qualunque comando riuscito diverso da muoversi, fermarsi o prendere razioni. Quindi conversazione, consegna, attesa.
- **Chi vede chi** sulle mappe (`Simulation.Sees`): serve la linea di vista, poi conta la luce sulla casella del bersaglio:
  - buio: non si vede nessuno;
  - luce fioca: si vede chi non è furtivo; un bersaglio furtivo si vede solo se la Percezione passiva −5 (svantaggio) raggiunge il suo totale di Furtività;
  - luce piena: si vede sempre.
- **Furti**: il ladro è riconosciuto solo se il testimone lo *vedeva*, secondo la regola sopra, nel momento in cui il furto è cominciato.
- **PlayerView**: `Sneaking` contiene il proprio totale; `VisibleActorView.SeesYou` dice se quella persona ti vede. Lo SRD stabilisce che se vedi una creatura sai se lei vede te. `MoveView.Stealthy` indica se il cammino in corso è furtivo.
- **Non ancora**: gli NPC non reagiscono al vederti girare di soppiatto (arriva con la T6c) e non esiste ancora l'azione Search per cercare chi si nasconde.

## F2 — Porte e torce (ADATTAMENTO di SRD 5.2.1: Interacting with Things, Torch)

- **Porte**: sono caselle percorribili della mappa, aperte o chiuse; lo stato è salvato con la mappa.
  - Una porta chiusa blocca vista e luce come un muro.
  - Una porta chiusa attutisce il suono: attraversarla costa `Tuning.ClosedDoorSoundSteps` (3) caselle di suono in più.
  - Chi cammina attraversa le porte chiuse: la apre gratis arrivando alla casella precedente (è una tappa del cammino) e la lascia aperta. Una porta adiacente alla partenza si apre subito.
- **`DoorCommand { At, Open }`**: istantaneo, da una casella adiacente e solo da fermi. Le possibili risposte di rifiuto sono:
  - `NotADoor`: in quella casella non c'è una porta;
  - `OutOfReach`: il personaggio non è adiacente alla porta;
  - `AlreadyDone`: la porta è già nello stato richiesto;
  - `DoorBlocked`: si sta chiudendo una porta su cui c'è qualcuno, o in cui qualcuno sta per entrare.
- **Torce**: `Actor.Torches` conta le torce spente che il personaggio porta; `Actor.TorchLitUntil` indica fino a quando brucia quella accesa.
  - **`TorchCommand { Lit }`** è istantaneo. Accendere consuma una torcia, che brucia per 1 ora. Spegnere la consuma comunque.
  - Una torcia accesa è una sorgente di luce mobile sulla casella di chi la porta: luce piena per 20 piedi più luce fioca per altri 20, fermata da muri e porte chiuse.
  - La torcia si spegne da sola alla scadenza: è considerata accesa solo finché `Now < TorchLitUntil`.
- **Furtività**: porte e torce non la interrompono, ma una torcia accesa mette il personaggio in luce piena, quindi chiunque abbia la linea di vista lo vede.
- **Approssimazione**: per la luce di un istante passato si usano i portatori di torcia attuali.
- **PlayerView**: `Doors` (tutte le porte della mappa, anche quelle fuori vista: approssimazione), `Torches`, `TorchLitUntil`, `VisibleActorView.Torch`.
- **Client**: il clic su una porta adiacente la apre o la chiude, il clic su una porta lontana ci fa camminare fino a lei. Il tasto T accende e spegne la torcia.

## R1 — Ritmo dell'esplorazione (solo client)

- Durante il cammino, `VillageMap` fa avanzare la Sim di `_pace` secondi di gioco per ogni secondo reale. Il ritmo si sceglie tra 1×, 3× e 6×; il default è 3×.
- Le regole non cambiano: una casella costa sempre 1 secondo di gioco a Speed 30. Cambia solo quanto aspetta il giocatore, come in Project Zomboid.
- Le attività a durata fissa durano al massimo 3 secondi reali, come prima.
