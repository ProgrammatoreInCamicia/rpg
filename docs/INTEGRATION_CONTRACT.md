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
void           Save(Stream)                        // snapshot JSON autosufficiente, versione schema 12
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
- **Versione dello schema 12** (la 6 ha aggiunto luoghi illuminati, modo di percezione, debiti per furto, furti saldati e carico; la 7 mappe, posizioni e cammini; la 8 le sorgenti di luce delle mappe; la 9 la furtività; la 10 porte e torce; la 11 l'evidenza fissata all'inizio dei furti; la 12 uscite, posti e accessi ai depositi). Le versioni 1–11 vengono rifiutate con un messaggio chiaro.
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
- **StopCommand { Actor }**: ci si ferma sull'ultima casella raggiunta (`NotMoving` se non si sta camminando). Fermarsi non ferma il tempo: lo scorrere del tempo lo decide il client (vedi R1).
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
  - Riconoscimento: il ladro deve essere visto alla fine e il testimone deve averlo visto all'inizio. La lista dei testimoni che lo vedevano all'inizio è fissata nell'azione di furto (`SeenAtStart`); cambiare zona durante il furto non cancella quell'evidenza.
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
- **Nascondersi richiede di non essere visti** (correzione dopo la review di Codex):
  - un **nuovo** tiro di Furtività si fa solo se in quel momento nessuno vede il personaggio (regole normali); se qualcuno lo vede, il personaggio va all'andatura lenta ma **non** è nascosto (`Sneak` resta null) e il messaggio dice chi lo vede;
  - durante un cammino furtivo **ogni casella è una tappa**: se lì qualcuno vede il personaggio (luce piena in linea di vista, oppure luce fioca con Percezione −5 che raggiunge il totale), è **scoperto** (`Sneak` = null, fatto `SneakDiscovered`), come "an enemy finds you" dello SRD;
  - limiti: la distinzione completa tra andatura lenta e stato nascosto, e chi conta come osservatore "pertinente", arrivano con la T6c; il riparo parziale (Three-Quarters Cover) non esiste ancora nel modello delle mappe.
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
- **Evidenza dei furti** (correzione dopo la review di Codex): all'inizio di un furto sulla mappa si fissano **chi vedeva il ladro** (`TakeFoodAction.SeenAtStart`) e la luce sulla sua casella (`BestLight`).
  - Ogni cambio di luce durante il furto aggiorna la luce migliore: torcia accesa o spenta, porta aperta o chiusa, portatore di torcia che raggiunge una tappa.
  - Il ladro lo riconosce **solo** chi lo vedeva all'inizio: una torcia accesa dopo non rivela chi ha cominciato.
  - La luce del tratto osservato è il massimo tra la luce registrata, la luce del giorno nel tratto e la luce attuale.
  - Limite: un portatore di torcia in movimento conta solo alle tappe del suo cammino.
- **PlayerView**: `Doors` (tutte le porte della mappa, anche quelle fuori vista: approssimazione), `Torches`, `TorchLitUntil`, `VisibleActorView.Torch`.
- **Client**: il clic su una porta adiacente la apre o la chiude, il clic su una porta lontana ci fa camminare fino a lei. Il tasto T accende e spegne la torcia.
- **Limiti noti** (review di Codex):
  - da una casella adiacente non si può entrare nella casella della porta con un clic diretto: servirà un'interazione distinta quando camminare sulla porta diventerà utile;
  - lo stato delle porte fuori vista è noto al giocatore: va filtrato nella PlayerView prima delle routine NPC e del combattimento;
  - la penalità di luce del percorso furtivo si calcola alla partenza: andrà ricalcolata se una porta o una luce mobile la invalida.

## R1 — Tempo reale con pausa e ritmo (solo client)

- Il mondo scorre **sempre**, che il giocatore si muova o no, come in BG1/BG2: `VillageMap` fa avanzare la Sim di `_pace` secondi di gioco per ogni secondo reale. Il ritmo si sceglie tra 1×, 3× e 6×; il default è 3×.
- La **pausa** (Spazio) è l'unico modo per fermare l'orologio. X ferma il cammino, ma non il tempo.
- **Coda di ordini in pausa** (correzione dopo la review di Codex). In pausa ogni ordine del giocatore va in coda e il mondo non cambia: niente dadi, niente torce, niente porte, nessuna risorsa consumata.
  - Gli ordini in coda sono: cammino, avvicinarsi a qualcuno, porta, torcia, attività, Racconta, cambio di andatura durante un cammino.
  - Alla ripresa la coda si esegue in ordine. Un nuovo cammino sostituisce quello già in coda.
  - X in pausa annulla il cammino in coda.
  - Se un'attività in coda trova il personaggio ancora impegnato, non parte e un messaggio lo dice.
- Le attività a durata fissa scorrono più veloci, al massimo 3 secondi reali, e il mondo intanto va avanti.
- Le regole non cambiano: una casella costa sempre 1 secondo di gioco a Speed 30.
- In combattimento (proposta in chat) il tempo passerà a round da 6 secondi.

## C1 — Regole del combattimento (solo `RpgSandbox.Sim.Rules`, non ancora collegate al mondo)

- **`Weapons`**: armi con i valori della tabella SRD 5.2.1 (danni, proprietà, gittate, padronanze).
- **`AttackProfile`**: un attacco già calcolato.
  - Per i personaggi si ricava da scheda e arma: Finesse usa il migliore fra Forza e Destrezza; le armi a distanza usano la Destrezza; le altre, anche quelle lanciate da mischia, la Forza. La competenza aggiunge il bonus. Versatile a due mani usa il dado più grande. Heavy dà svantaggio sotto 13 di Forza o Destrezza.
  - Per le creature SRD si copia la riga della scheda.
  - Il colpo senz'armi colpisce con Forza + bonus di competenza e fa 1 + Forza danni.
- **`Attacks.Roll`**:
  - il 20 naturale colpisce sempre ed è critico; l'1 naturale manca sempre;
  - con `critOnHit` ogni colpo è critico (bersaglio privo di sensi entro 5 piedi);
  - il critico raddoppia i dadi del danno, non il bonus;
  - con la padronanza Graze un attacco mancato fa danni pari al modificatore;
  - `Describe()` produce la riga per il registro dei dadi.
- **`Vitality`**: punti ferita e stati (Vivo, Morente, Stabile, Morto), più Tramortito.
  - I mostri muoiono a 0 PF.
  - I personaggi a 0 PF diventano Morenti, a meno di danno massiccio (danno avanzato ≥ PF massimi: morte istantanea).
  - Il danno subito a 0 PF conta come uno o due fallimenti ai tiri contro la morte; se è ≥ PF massimi è morte.
  - Tiro contro la morte: 20 = 1 PF; 1 = due fallimenti; tre successi = Stabile; tre fallimenti = Morto.
  - Knocking Out: invece di scendere a 0 si resta a 1 PF, Tramortiti.
  - Le cure non superano il massimo e fanno rinvenire; un morto non si cura.
  - `Describe()` dà lo stato descrittivo (illeso, ferito, malconcio, tramortito, a terra), mai i PF esatti.
- **`CombatProfiles`**:
  - Paladino 1: 11 PF, CA 18, spada lunga +4 1d8+2 con padronanza Sap, giavellotto +4 1d6+2 con padronanza Slow;
  - Bandito, Guardia e Popolano: valori copiati dalle schede SRD.
- **`Initiative`**: prova di Destrezza, con svantaggio se sorpreso. Ordine: totale più alto; a parità, bonus più alto e poi id (ADATTAMENTO).
- **Requisiti per la C2** (dalla review di Codex):
  - `Vitality` avrà uno snapshot/restore validato (stato, KO, contatori dei tiri contro la morte); ogni `Actor` ne tiene una copia propria, mai un'istanza condivisa con uno snapshot;
  - i comandi e l'equipaggiamento validano `twoHanded` (solo con l'altra mano libera). Lanciare un'arma senza la proprietà Lancio **non è vietato**: è un'arma improvvisata (SRD Improvised Weapons: niente bonus di competenza, 1d4, gittata 20/60; il modificatore di Forza resta). Lo stesso vale per un'arma a distanza usata in mischia (`AttackProfile.Improvised`);
  - un colpo da 0 danni non ha alcun effetto (`NoEffect`), anche a 0 PF.
- **Fuori dalla C1**:
  - gli effetti delle padronanze diverse da Graze;
  - reazioni e attacchi di opportunità;
  - il recupero dello Stabile (1 PF dopo 1d4 ore);
  - il Riposo Breve del Tramortito;
  - il collegamento con gli attori e con i salvataggi (C2).

## T6c-1 — Tappe, uscite, posti, accessi

- **Tappe**: ogni casella di un cammino sulle mappe è una tappa. In ogni istante l'ordine è fisso:
  1. tappe e completamenti dei cammini, in ordine di ID dell'attore e, per lo stesso attore, prima le tappe in ordine di passo e poi il completamento (un camminatore veloce può raggiungere più caselle e l'arrivo nello stesso secondo). Si aggiornano solo casella, zona e porte attraversate;
  2. valutazione dei contatti, una sola volta (`EvaluateContacts`): luce della torcia per i furti in corso; arrivo in un Luogo (NoticeArrival e `ArrivedAt`) solo se la zona a fine istante è diversa da quella a inizio istante (attraversare una zona e tornare nello stesso secondo non è un arrivo); scoperta dei nascosti da parte di chiunque ora li veda, anche se è l'osservatore a muoversi;
  3. le fasi di prima: altri completamenti, fazioni, decisioni.

  L'ultima casella è il completamento, quindi non c'è una doppia valutazione. Un Advance lungo equivale a tanti Advance brevi. Un cammino sostituisce anche un'attesa di routine interrompibile. Al caricamento ogni cammino in corso deve avere esattamente le sue tappe future, una per passo ancora da fare.
- **Uscite** (`AddExit(area, luogo, casella)`): un Luogo di bordo della mappa ("Strada per il bosco") con la sua casella di uscita, che deve stare sul **bordo** della mappa.
  - `TravelCommand` da un Luogo mappato richiede di stare sulla casella di uscita (`NotAtExit`); si lascia la mappa durante il viaggio.
  - All'arrivo in un Luogo mappato si compare sulla sua uscita e l'arrivo si nota con i contatti.
  - Ogni Route che tocca un Luogo di un'Area mappata richiede la sua uscita: è validato negli scenari e nei salvataggi.
  - `MapView.Exits` elenca le uscite con le loro Route.
- **Posti** (`AddPost(area, luogo, PostKind, casella)`; `PostKind`: Work, Home, Guard, Rest): caselle percorribili nella zona del Luogo, uno per tipo. Sono destinazioni delle routine: non danno né portata né vista.
- **Accessi** (`AddStore(..., access: caselle)`): caselle percorribili adiacenti al deposito, con la diagonale libera. Se sono dichiarati, il deposito si usa **solo** da lì; altrimenti da qualunque casella adiacente, come prima. `StoreView.Access` li espone.

## T6c-2 — Routine degli NPC sulla mappa

- **Andare in un posto** (`StepTowards`, un passo alla volta, uguale per tutte le regole):
  - sulla mappa, verso un luogo della stessa mappa: si cammina fino al **posto** del tipo richiesto (Work per il turno, Home per tornare a casa e, se manca, Rest; Guard per il presidio). Senza posti si va alla prima casella percorribile del luogo;
  - verso un'altra Area: si cammina fino all'uscita più conveniente (costo minimo del viaggio a valle, poi ID) e si parte lungo la Route;
  - fuori mappa: si viaggia lungo le Route; se la destinazione è su un'Area mappata, il viaggio punta alla sua uscita più conveniente e si prosegue a piedi.
- **Arrivato** (`IsAt`): sul posto, se il luogo ne ha uno di quel tipo; altrimenti basta essere nel luogo. Un NPC già in un luogo senza posti resta dov'è.
- **Solo destinazioni raggiungibili** (review di Codex): si sceglie un posto o una casella del luogo raggiungibile dalla posizione dell'NPC, e un'uscita raggiungibile a piedi, anche se un'uscita isolata da muri sarebbe più conveniente. Se non c'è alcuna via (un luogo fatto solo dal deposito, o tagliato fuori dai muri), l'NPC esita e riprova più tardi, senza errori.
- **Routine**: il turno porta al posto Work, il resto del tempo al posto Home (o Rest); il presidio porta al posto Guard. Un NPC libero su strada aperta, fuori da ogni luogo, torna a casa.
- Le porte si aprono camminando e restano aperte (la chiusura da parte degli NPC verrà dopo).
- **Restano a "nel luogo"** fino alle prossime tappe: la razzia (T6c-3), le segnalazioni, la vigilanza e la confisca (T6c-4).
- Villaggio vivo: il contadino lavora al Granaio dalle 13 alle 18.

## T6c-3 — Il Razziatore sulla mappa

- **Bersaglio**: per un deposito su un'Area mappata la fazione considera il costo del viaggio fino all'uscita più conveniente di quella mappa (`JourneyCost`).
- **Andare al deposito** (`StepToStore`): sulla mappa si cammina fino alla casella più vicina da cui il deposito si usa (accessi dichiarati, altrimenti caselle adiacenti con la diagonale libera). Da fuori mappa si arriva dall'uscita. Arrivato = `InReach`.
- **Furtività**: sulla mappa il Razziatore cammina sempre di soppiatto, all'andata e al ritorno, con le regole F1: si nasconde solo se nessuno lo vede e può essere scoperto a ogni casella.
- **Attesa del buio** (ADATTAMENTO): finché c'è luce piena del giorno il Razziatore non parte verso un bersaglio mappato; riposa fino alle 20:00 (`Tuning.RaidAfterDark`). Al crepuscolo parte.
- **Presidio fisico sulle mappe** (`Covers`, `IsGuarded`): una guardia di presidio è su una casella adiacente a un accesso del deposito (o al deposito, se non ha accessi) e ha la linea di vista su quell'accesso. La stessa zona non basta. Fuori mappa resta la regola per Luogo.
- **Desisti**: il Razziatore desiste solo se vede (regola F3, luce compresa) una guardia che copre il deposito (`RaidDeterred`).
  - Se non la vede, tenta il furto: il comando viene rifiutato (`StoreGuarded`) e lui viene fermato (`RaidFoiled`).
  - In entrambi i casi torna al campo e la fazione evita il bersaglio (`AvoidUntil`).
  - La reazione della guardia a chi ha provato a rubare arriva con la T6c-4.
- Villaggio vivo: i Banditi sono una fazione predona con i parametri della slice (consumo 6 alle 8:00, soglia 10, razzia di 8, valutazione ogni 3 ore).

## T6c-4 — Contatti sulla mappa (segnalazioni, confisca, vigilanza)

Sulle mappe "nello stesso luogo" diventa contatto fisico; fuori mappa non cambia nulla.
- **Segnalazione**:
  - si riferisce solo stando accanto (`CanTalk`) all'inizio e alla fine dei 5 minuti; se chi ascolta si allontana, la segnalazione fallisce (`ReportFailed`);
  - per raggiungere l'autorità: se il testimone la vede, le va accanto (`StepNextTo`, puntando alla posizione vista ora); se non la vede, va al suo posto abituale (Home/Rest), che è noto a tutti;
  - a ogni fine cammino si decide di nuovo, quindi si ripunta solo su ciò che si vede.
- **Confisca**:
  - l'autorità con un credito verso un ladro che ha ancora razioni, se lo vede, gli va accanto;
  - la confisca si avvia e si completa solo con il ladro accanto (`ConfiscationFailed` altrimenti); niente sottrazione a distanza né per onniscienza.
- **Riconsegna**: le razioni recuperate tornano al deposito passando dalla casella da cui si usa (`StepToStore`).
- **Contatto** (`NoticeContacts`, dentro `EvaluateContacts`):
  - quando un'autorità e un ladro con debito e razioni diventano adiacenti e l'autorità lo vede, l'autorità interrompe presidio o riposo e decide subito, chiunque dei due si sia avvicinato;
  - sulle mappe l'ingresso in una zona non fa più scattare `NoticeArrival`.
- **Vigilanza**: chi vigila va al posto Work del luogo del deposito.
- **Tentativo fermato**: `RaidFoiled` registra anche la guardia che ha fermato il ladro. Affrontarlo sarà uno scontro (C2).

## T6c-5 — Luce per testimone, memoria delle porte, presenze udite

- **Luce dei furti per testimone**:
  - ogni furto in corso su una mappa tiene il registro della luce di lampade e torce sulla casella del ladro, con l'istante di ogni cambio (`SourceLightLog`);
  - per ogni attore tiene anche gli intervalli in cui ha avuto la linea di vista sul ladro (`Sight`);
  - questi registri si aggiornano a ogni movimento, torcia, porta e partenza;
  - alla fine la luce migliore conta solo dentro gli intervalli in vista, unendo luce del giorno e lampade. `SeenAtStart` resta l'evidenza per il riconoscimento.
  - Sostituisce `BestLight` e l'approssimazione con `ArrivedAt`.
- **Memoria delle porte** (solo del giocatore, `Actor.DoorMemory`):
  - `PlayerView.Doors` è `DoorView { At, Known, Open (bool?), SeenAt }` con `Known` uguale a `Seen` (vista ora: accanto, o in linea di vista con almeno luce fioca), `Remembered` (stato e istante dell'ultima volta) oppure `Unknown` (`Open` null);
  - la memoria si aggiorna solo negli istanti elaborati e dopo ogni comando, mai alla fine di un `Advance` qualsiasi, così spezzare il tempo non cambia il risultato;
  - una porta che il giocatore apre o chiude da sé è nota anche al buio.
  - Limite: una porta che diventa visibile solo per la luce del giorno, senza alcun evento, viene ricordata al primo istante elaborato successivo.
- **UnseenNearby**: conta solo le presenze adiacenti non viste che il giocatore sente. Chi è furtivo con una Furtività superiore alla sua Percezione passiva non conta.
- **Limite aperto** (Codex): `PlayerView.MapLight` mostra la luce reale di tutta la mappa, quindi un cambio fuori vista può trasparire dalla luce. Si risolve con una tappa di nebbia di guerra (luce e oggetti percepiti).
- Schema dei salvataggi 13.

## C2 — Lo scontro nel mondo

Contratto completo in chat.txt ("CONTRATTO C2" e le due note successive). In sintesi:
- **Chi combatte**: ogni attore ha un profilo C1 (`paladin`, `bandit`, `guard`, `commoner`) e la sua `Vitality` (le ferite restano). Gli attacchi vengono solo dal profilo. LIMITE: munizioni e armi lanciate non si consumano (C3).
- **Apertura** (ADATTAMENTO): `AttackCommand` fuori dallo scontro, bersaglio visibile e a portata o a gittata.
  - Se il bersaglio non se lo aspetta (non vede chi attacca, o non lo considera ostile) il colpo si risolve subito, poi l'iniziativa; il bersaglio è sorpreso e chi attacca ha già speso l'azione del primo turno.
  - Se se lo aspetta (lo vede e lo sa ostile: fazione del Razziatore, un Claim aperto, uno scontro precedente) si tira prima l'iniziativa e l'attacco parte al turno di chi attacca, se il bersaglio è ancora a portata (`PendingOpeningAttack`).
  - In C2 lo scontro è fra chi attacca e chi è attaccato; gli altri entrano in C4.
- **Tempo**: i turni di un round si risolvono tutti all'istante T in cui il round inizia; il round dopo inizia a T+6 s (un lavoro dello scheduler), e intanto il mondo intorno vive. Durante il turno del giocatore il mondo è fermo: `Advance` non fa passare tempo e `AdvanceUntilCompleted` si ferma con `AdvanceOutcome.YourTurn`. Il client può quindi continuare a far scorrere il tempo come sempre: fra un round e l'altro (`CombatView.NextRoundAt`) passano i 6 s, oppure ci salta subito. I turni degli NPC si risolvono dopo `EndTurnCommand`. Chi combatte può solo muoversi, attaccare o finire il turno (`InCombat`); chi è a terra non fa nulla (`ActorBusy`).
- **Turno**: movimento (Velocità 30 = 6 caselle, 5 piedi a casella) e un'azione (`AttackCommand`). `MoveCommand` in scontro è istantaneo e si può spezzare. Lo spazio di un alleato si attraversa liberamente; quello di un'altra creatura che si può attraversare è terreno difficile (salvo Tiny); non si attraversa un avversario cosciente; non ci si ferma su una creatura.
- **Attacchi**: mischia a 5 piedi; distanza con linea di vista, Svantaggio oltre la gittata normale e con un avversario adiacente che ti vede; Vantaggio se il bersaglio non ti vede o è privo di sensi; critico automatico contro un privo di sensi entro 5 piedi; `NonLethal` solo in mischia.
- **Attacchi di Opportunità** (dalla C3 alla C2): una Reazione, recuperata all'inizio del proprio turno; si attacca chi esce dalla propria portata muovendosi, prima che esca. Gli NPC la usano sempre; quella del giocatore in C2 è automatica.
- **Anteprima**: `SimulationSession.PreviewMove` usa lo stesso calcolo del `MoveCommand`, non tocca il mondo, ed elenca in `Provokes` solo le minacce note al giocatore.
- **Vita e morte**: tiri contro la morte all'inizio del turno, e ogni 6 s fuori dallo scontro; lo Stabile torna a 1 PF dopo 1d4 ore; il Tramortito rinviene dopo un Riposo Breve di 1 ora. `PlayerView.Dead`.
- **Fine**: un lato senza nessuno cosciente; oppure un round intero senza che nessuno veda un avversario e senza attacchi.
- **Vista**: `PlayerView.Combat` (ordine, turno, i propri numeri esatti, avversari solo con il descrittore C1, caselle raggiungibili, bersagli con motivo, registro strutturato filtrato per percezione, con il percorso dei movimenti); `VisibleActorView.Condition`.
- **IA**: attacca l'avversario cosciente visibile più vicino; se non può, si avvicina; senza avversari visibili va dove l'ha visto l'ultima volta. Fuga e resa in C4.
- **Chi è a terra non percepisce né decide**: niente vista, udito, testimonianze, presidio, rapporti o incarichi di fazione. Un morto non agisce più.
- **Limiti C2**: nessuno accorre, testimonia o reagisce allo scontro (C4); chi attacca a sorpresa non viene ancora giudicato; gli NPC non finiscono chi è a terra, non fuggono e non si arrendono (C4); la Reazione del giocatore è automatica; munizioni infinite; le porte chiuse bloccano il movimento in scontro (C3); una lotta fra NPC si vede solo se si vedono i combattenti (sentirla è C4).
- Schema dei salvataggi 14: profilo e `Vitality` di ogni attore (ricostruita e validata), avversari già affrontati, scontri in corso con iniziativa, turno, round, apertura in attesa e registro, `DownedAction` e il lavoro `CombatRound`.

## C4 — Il mondo reagisce agli scontri (API pubblicata; regole in arrivo con C4a, C4b, C4c)

Contratto completo in chat.txt ("CONTRATTO C4" e le note successive). In sintesi:
- **Fatti generici** (C4a): un'osservazione riguarda un furto (`Theft`), uno scontro (`Assault`) o un'uccisione (`Killing`).
  - `Culprit` di un `Assault` è chi ha APERTO lo scontro, noto solo a chi ha percepito l'apertura; chi arriva dopo sa chi ha visto battersi (`Involved`), non chi ha cominciato.
  - `Killing` è un fatto a sé, collegato allo stesso scontro: chi ha dato il colpo mortale non è per forza chi ha cominciato.
  - Chi sente soltanto sa "rumori di lotta", senza nomi, con una casella approssimata (il centro del blocco 5×5).
  - I furti si comportano come prima; `Thief`/`ThiefName` restano come alias deprecati di `Culprit`.
- **Ostilità come rapporto** (C4b): atteggiamento di ogni fazione verso gli estranei (neutrale o ostile), fazioni nemiche, ricercati.
  - Diventa ricercato chi è riconosciuto come INIZIATORE di un'aggressione (o di un'uccisione che ne segue) contro chi non era nemico della fazione. Chi uccide difendendosi non è colpevole: decidono l'iniziatore noto e i fatti, non soltanto la fazione della vittima.
  - La guardia affronta un ricercato a vista (colpi per tramortire se non è di una fazione nemica); i popolani se ne tengono lontani.
- **Reazioni** (C4c): chi vede un alleato o un innocente aggredito entra nello scontro (ADATTAMENTO: iniziativa tirata subito e inserita nell'ordine senza spostare il turno in corso; agisce dal round successivo; voce `Joined`); un'autorità che sente soltanto va a vedere; i popolani si allontanano, e in scontro fuggono sotto metà PF (`Fled`).
- **Vista**: `VisibleActorView.Hostile` (ADATTAMENTO di interfaccia alla BG: l'atteggiamento si legge, non il motivo).
- Resa e prigionia: non in C4.
- Stato attuale: C4 completa (C4a, C4b, C4c). C4c: chi combatte (profilo non `Flees`) entra nello scontro dalla parte opposta a chi considera ostile, se vede lo scontro e un solo lato va bene; un'autorità che sente soltanto va verso il rumore; chi non combatte (`CombatProfile.Flees`, il Popolano: ADATTAMENTO di comportamento) si allontana verso casa da uno scontro o da un ricercato entro 6 caselle e ci resta finché lo scontro che conosce dura; in scontro, sotto metà PF scappa dove nessun nemico lo vede e, fuori vista alla chiusura del round, esce dallo scontro (se un lato resta vuoto lo scontro finisce). Chi vede o sente uno scontro interrompe riposo, presidio o (la prima volta) il cammino per decidere. C4a: fatti generici, testimoni e rumori degli scontri, rapporti per fatti che riguardano la fazione (un furto, uno scontro con un suo membro, rumori di lotta sentiti). C4b: `AddEnmity` e `HostileToStrangers` negli scenari (i Banditi ostili a tutti, Villaggio e Banditi in guerra); `HoldsHostile` reciproco (si considera ostile chi ti considera ostile); `Faction.Wanted` deciso dall'autorità solo sull'iniziatore riconosciuto di un'aggressione a un suo membro; la guardia nota un ricercato a vista (anche mentre riposa o presidia), lo raggiunge e attacca per tramortirlo. ADATTAMENTO provvisorio fino alla prigionia: tramortirlo chiude la questione (`WantedSettled`). `FactionView` mostra ostilità, nemici e ricercati (debug). Limite: chi ha combattuto contro di te resta ostile verso di te (memoria dello scontro). Schema dei salvataggi 15.

## C3a — Scatto, Disimpegno, Schivata (SRD 5.2.1 Actions)

- `CombatActionCommand { Actor, Action }` con `CombatAction` = `Dash` | `Disengage` | `Dodge`: usa l'azione, solo nel proprio turno di uno scontro (`NotInCombat`, `NotYourTurn`, `NoActionLeft`).
- **Scatto**: per il resto del turno il movimento aumenta della propria Velocità (`MovementLeftFeet` lo include; il cammino lo rispetta passo per passo).
- **Disimpegno**: per il resto del turno il proprio movimento non provoca attacchi di opportunità (`PreviewMove.Provokes` vuoto).
- **Schivata**: fino all'inizio del proprio turno successivo, gli attacchi contro chi schiva hanno Svantaggio se chi schiva vede l'attaccante; finisce se cade privo di sensi. I tiri salvezza su Destrezza con Vantaggio verranno con i tiri salvezza.
- Vista: `CombatView.Actions` (ognuna valida o con il motivo), `CombatSelfView.Disengaged`/`Dodging`, `CombatantView.Dodging`; voci di registro `Dash`, `Disengage`, `Dodge`.
- IA: chi fugge si disimpegna se ha un nemico accanto, altrimenti scatta; chi non arriva al bersaglio nel turno e non ha un attacco utile scatta per avvicinarsi. Nessun NPC schiva per ora (scegliere quando conviene richiede di valutare il pericolo: C5).
- Schema dei salvataggi 16.

## C3b — Inventario e denaro

- **Catalogo** (`Rules/Items.cs`): torcia, giavellotto, lancia, dardo da balestra (id, nome, peso in libbre, costo in rame). Cresce quando serve.
- **Inventario**: `Actor.Inventory` (oggetto → quantità) e `Actor.Coins` in monete di rame (1 mo = 10 ma = 100 mr; il penny d'argento di Blackmarsh vale 1 ma). Le torce sono l'oggetto `torch` (`PlayerView.Torches` resta). Le razioni restano `Food` finché i depositi non conterranno oggetti (D1).
- **Corredo**: ogni profilo ha oggetti e monete di partenza (`CombatProfile.Kit`/`Coins`); lo scenario può sostituirli (`AddActor(items:, coins:)`). Paladino: 6 giavellotti e 9 mo (equipaggiamento iniziale A dello SRD); Bandito: 20 dardi (ADATTAMENTO: la scheda non indica munizioni); Guardia: la sua lancia.
- **Attacchi con oggetti** (`AttackProfile.Item`/`Fate`): senza l'oggetto l'attacco non è valido (`NoAmmo`). Un'arma da lancio finisce a terra sulla casella del bersaglio, colpito o mancato; una munizione è consumata. LIMITE: lo SRD permette di recuperare metà delle munizioni cercando dopo lo scontro (da fare). Un NPC non lancia la sua ultima arma se ha un attacco in mischia (la Guardia tiene la lancia).
- **A terra**: gli oggetti stanno sulle caselle delle mappe. `PickUpCommand` dalla propria casella o da una adiacente: fuori dallo scontro immediato (non mentre si è impegnati in altro), nello scontro usa l'interazione gratuita con un oggetto del turno (una sola, poi `NoActionLeft`). `DropCommand` sulla propria casella, gratuito. Raccogliere e lasciare non tolgono la furtività.
- **Vista**: `PlayerView.Inventory`, `Coins`, `GroundItems` (solo ciò che si vede o si ha accanto); `AttackOptionView.Uses`/`Remaining`; `ActorView.Inventory`/`Coins` (debug).
- Fuori: negozi (B1), saccheggio e furto di oggetti, equipaggiamento indossato che cambia il profilo (D1), ingombro.
- Schema dei salvataggi 17 (inventari e monete negli attori, oggetti a terra nelle mappe, interazione del turno).

## D1a — Contenuti come dati: una definizione per attore, profilo calcolato

- **File di dati** in `game/content/`, elencati in `manifest.json` (id, versione, file in ordine; errori su file mancanti, ripetuti o non elencati): `srd/items.json` (armi, armature, scudo, torcia, munizioni con pesi e costi SRD), `srd/classes.json` (il Paladino), `srd/creatures.json` (Bandito, Guardia, Popolano dello SRD), `original/creatures.json` (Razziatore, Guardia del villaggio, Contadino: adattamenti dichiarati), `original/characters.json` (il Paladino pregenerato). Ogni file dichiara la sua fonte e licenza.
- **Caricamento**: `ContentPack.Load(nome → testo)` valida tutto con messaggi leggibili. La Sim non apre percorsi: il pacchetto di default è compilato nella sua DLL dagli stessi file (`ContentPack.Default`), quindi Godot e i test non leggono nulla da disco. Uno scenario si gioca con un pacchetto (`new ScenarioBuilder(pack)`, di default quello del gioco).
- **Una definizione per attore** (`AddActor(build: …)`; `combat:` resta come vecchio nome): una creatura (scheda di statistiche: caratteristiche, competenze, CA, PF, attacchi, corredo) o un personaggio pregenerato (classe, livello, caratteristiche, equipaggiamento). Scheda e profilo ne derivano entrambi; `sheet:` resta solo per i test che vogliono una scheda propria.
- **Personaggi calcolati** con le regole SRD: CA da armatura (Destrezza intera, al massimo 2 o nessuna) e scudo; PF dal dado vita più la Costituzione (poi la media per livello); bonus di competenza per livello; attacco con l'arma in mano (competenza per categoria, padronanza se sbloccata, Versatile a due mani senza scudo, munizioni per le armi a distanza); armi da lancio portate; colpo senz'armi. Armatura pesante senza la Forza richiesta: Velocità −10. Il Paladino esce identico a prima (CA 18, 11 PF, spada +4 1d8+2, giavellotto +4 1d6+2).
- **Equipaggiamento** (`Actor.Equipment`: armatura, scudo, arma in mano): gli oggetti stanno nell'inventario. Lasciare un pezzo indossato o impugnato è rifiutato (`Equipped`) finché non c'è un comando per toglierlo. Un'arma lanciata via lascia la mano e il profilo si ricalcola. Un attacco può richiedere un oggetto che tiene (`AttackProfile.Requires`: l'arma in mano, la balestra) e uno che consuma (`Item`/`Fate`): senza il primo `NoWeapon`, senza il secondo `NoAmmo`. La Guardia: «Lancia» e «Lancia (lanciata)» dipendono dalla stessa lancia.
- **Vista**: `PlayerView.Equipment` e `ActorView.Equipment` (slot tipizzati), `ItemStackView.Equipped`.
- **Salvataggi** (schema 18): contengono l'intero pacchetto usato (id, versione, hash e testi); al caricamento le regole usano solo quello, mai i file installati; se i testi non corrispondono all'hash il salvataggio è rifiutato.
- **Debito noto**: la torcia è ancora riconosciuta dal suo id (`torch`) nel motore; diventerà una proprietà dell'oggetto (luce) quando servirà un'altra fonte di luce portatile.

## D1b — Scenari come dati

- Uno scenario può essere un file del pacchetto di contenuti (`game/content/scenarios/*.json`, elencato nel manifest): aree, luoghi, percorsi, mappe (righe, zone, uscite, posti, luci, porte), fazioni (con razzie e inimicizie), depositi e persone (definizione, casella, lavoro con orari "HH:MM", torce, oggetti e monete). Caselle come `[x, y]`.
- `ScenarioFile.Load(id, pack)` lo traduce nelle stesse chiamate di `ScenarioBuilder`, quindi uno scenario in file e uno in C# rispettano le stesse regole e danno gli stessi errori; `ScenarioFile.Ids(pack)` elenca gli scenari di un pacchetto.
- Il villaggio vivo esiste in entrambe le forme (`scenarios/living-village.json` e `MappedVillageScenario.CreateLivingVillage()`): danno lo stesso salvataggio iniziale, byte per byte, e vivono gli stessi due giorni. Il client può passare alla versione in file quando vuole; quella in C# resta per i test.

## D1c — Razioni come oggetti, depositi come contenitori

- La razione è un oggetto del catalogo (`ration`, «Razione»: SRD Rations, 2 lb, 5 ma). Le razioni di una persona sono quelle nel suo inventario: `Food` (attore e `PlayerView`) è il loro conteggio; compaiono in `Inventory` come ogni altro oggetto e si possono lasciare a terra e raccogliere (lasciate a terra non sono più cibo portato).
- Un deposito è un contenitore: `StoreView.Contents` elenca ciò che contiene (oggi solo razioni); `StoreView.Food` resta il conteggio delle razioni.
- Furti, razzie, confische, consegne e consumo spostano gli stessi oggetti: la loro semantica e i loro test sono invariati.
- Uno scenario con razioni richiede che i contenuti definiscano l'oggetto `ration`.
- Salvataggi (schema 19): le razioni di una persona stanno nel suo inventario (non c'è più un campo a parte), il deposito salva il suo contenuto; oggetti sconosciuti o quantità non positive nel contenuto sono rifiutati.
- Debito noto: come la torcia, la razione è riconosciuta dal suo id nel motore.

## C5a — Ciò che il giocatore sa delle creature

Decisioni dell'utente: il mondo non si adatta al giocatore; tono duro, vicino all'OSR, con il piccolo aiuto che darebbe un DM. Nessun numero del nemico e nessuna etichetta di difficoltà a schermo, se non imparati.

1. **Registro senza numeri del nemico.** Il giocatore vede i propri tiri («d20 4 +4 = 8: mancato») ma non la CA del bersaglio; i tiri dei nemici restano dietro lo schermo («ti colpisce: 7 taglienti», «ti manca»; tra altri: «colpisce», «manca»). Vale per `CombatLogEntry` (i campi numerici restano solo dove il tiro è del giocatore; `ArmorClass` mai), per il testo del registro e per `PlayerView.RecentEvents`. L'iniziativa dei nemici mostra l'ordine, non il tiro. La `WorldView` (debug) resta completa.
2. **Conoscenza delle creature** (solo il giocatore, nel salvataggio), su due livelli:
   - sull'**individuo**: la CA dedotta dai propri tiri (un mancato con totale t: CA > t; un colpito: CA ≤ t; il 20 e l'1 naturali non insegnano nulla; se un nuovo esito contraddice ciò che si sapeva, si riparte da quello), gli attacchi con arma visti usare, gli indizi osservati;
   - sul **tipo** (dai dati: `kind`, ad esempio umano, lupo): gli attacchi naturali visti usare (morso, artigli) e gli indizi sul tipo. Si impara solo ciò che il giocatore ha percepito: un attacco fuori vista non insegna nulla.
3. **«Osserva»** (`ObserveCommand { Actor, Target }`) su un attore che il giocatore vede: la Sim sceglie le prove come un DM. Intuizione (Search, Saggezza) sempre, per l'individuo; Study (Intelligenza) solo se il personaggio è competente nell'abilità che quel tipo richiede (Storia per umanoidi, Natura per bestie, Arcano per mostri, Religione per non morti: tabella SRD Areas of Knowledge). Ogni prova rivela gli indizi della sua abilità con CD non superiore al totale (`tells` nei dati della creatura: abilità, CD, testo, individuo o tipo). Un tentativo di Intuizione per individuo e uno di Study per tipo. Il fallimento non rivela nulla. In combattimento costa l'azione; fuori è immediato (ADAPTATION).
4. **Invarianti (Codex):** l'esito di «Osserva» è conoscenza personale e voce del registro del giocatore, non un fatto che gli NPC percepiscono o raccontano; si impara da attacchi e comportamenti solo se il giocatore li ha percepiti e attribuiti. La scheda non mostra identificativi interni, numeri di combattimento non dedotti né descrizioni non ancora apprese.
5. **Vista:** per ogni attore visibile una scheda tipizzata (ciò che sai dell'individuo e del suo tipo, la CA come intervallo dedotto); `PlayerView` elenca anche i tipi conosciuti.

Implementazione (C5a, Sim):
- **Dati**: `original/kinds.json` elenca i tipi (`id`, `name` come appare, `type` SRD, `tells` da Study); ogni creatura e personaggio ha `kind`; le creature hanno `tells` per l'Intuizione (`{ "dc", "text" }`, CD 1-30). Nuove abilità: Arcana, History, Investigation, Nature.
- **API**: `ObserveCommand { Actor, Target }`; rifiuti `NotVisible`, `AlreadyDone` (nulla di nuovo da tentare), `NotYourTurn`, `NoActionLeft`. Il messaggio e il fatto `Observe` (partecipante solo il giocatore) riportano i tiri e ciò che si nota.
- **Viste**: `VisibleActorView.Card` (`CreatureCardView`: `Kind`, `Tells`, `Attacks`, `ArmorClassAbove`/`ArmorClassAtMost`/`ArmorClass`, `Observed`, `KindLore`) e `PlayerView.KnownKinds` (`KindCardView`: `Name`, `Tells`, `Attacks`, `Studied`).
- **Salvataggi** (schema 20): la conoscenza del giocatore; creature e tipi sconosciuti sono rifiutati.
- Fuori per ora: le voci degli NPC, i comportamenti visti (fuga, resa) nella scheda, il grado di pericolo per zona.

## Prova interni (Codex, rivista da Claude) e client C5a

- **Confini sottili** tra caselle ortogonali (`MapEdgeView`: `A`, `B`, `Kind` Wall/Window/Door, `DoorAt` per le porte): in `MapView.Edges`. Il muro blocca passo e vista; la finestra lascia passare vista e luce, non il passo; la porta su un confine blocca la vista da chiusa e si apre camminando (come le porte su casella). Scenario: `AddEdge`, `AddInterior` (zone al chiuso, dove la luce del giorno entra solo dalle aperture). Prova in `InteriorPrototypeScenario` e `game/Scenes/InteriorPrototype.tscn` (vedi `docs/INTERIOR_PROTOTYPE.md`).
- **Angoli**: un passo, una portata in mischia o un'interazione in diagonale richiedono entrambe le caselle d'angolo aperte; la vista passa se ne basta una.
- **Esplorazione** (solo mappe con interni): `PlayerView.MapVisibility` ('2' vista ora, '1' ricordata, '0' mai vista); la memoria si aggiorna nella simulazione a ogni istante e dopo ogni comando (come le porte ricordate), mai leggendo una vista. Vista ora = la stessa regola di `CanSee` (linea di vista e almeno luce fioca). `MapLight` è '0' fuori dalle caselle viste ora. Limite: `MapView.Rows` contiene ancora la pianta intera.
- **C5a nel client**: tasto destro su un attore visibile → Informazioni/Attacca; scheda dai soli dati `Card`; «Osserva» attivo solo se `Card.CanObserve` (e, in combattimento, nel proprio turno con l'azione disponibile).
- **Salvataggi** (schema 21): confini, zone interne ed esplorato nella mappa; si legge anche lo schema 20.

## Giocatore privo di sensi fuori da un combattimento

- `PlayerView.Unconscious`: il giocatore è a terra privo di sensi (morente, stabile o stordito con 1 PF), anche in un
  combattimento.
- `SimulationSession.AdvanceWhileUnconscious(maxWait)`: fuori da un combattimento fa scorrere il tempo finché il
  giocatore resta privo di sensi, senza nessuno che possa curarlo: i tiri contro la morte ogni 6 s, poi (SRD) 1 PF
  dopo 1d4 ore se stabile, oppure il risveglio dopo 1 ora se stordito. Il mondo va avanti nel frattempo.
  - `Completed`: il giocatore è cosciente, oppure morto (`PlayerView.Dead`).
  - `YourTurn`: è cominciato un combattimento ed è il turno del giocatore.
  - `TimeLimitReached`: è passato `maxWait`.
  - `NotPending`: il giocatore non è a terra fuori da un combattimento.
- Il client lo chiama da solo, senza aspettare quelle ore al ritmo di esplorazione, quando `Unconscious` è vero,
  `Combat` è nullo, il giocatore non è morto e il gioco non è in pausa. Il risveglio compare negli eventi recenti
  ("riprende i sensi con 1 punto ferita").
- ADATTAMENTO: non ci sono ancora compagni. Quando arriveranno, un compagno in grado di aiutare fermerà questo
  avanzamento.

## C5b: progressione (esperienza e livelli)

Regole SRD 5.2.1: Level Advancement, Experience Points by Challenge Rating, Gaining a Level.

- Contenuti: ogni creatura ha `"xp"`, obbligatorio e ≥ 0: i punti esperienza del suo blocco statistiche (Popolano e
  Contadino 10; Bandito, Guardia, Razziatore e Guardia del villaggio 25).
- Chi sconfigge: quando una creatura cade (morente, tramortita o morta) o scappa dallo scontro, il giocatore guadagna
  i suoi XP una volta sola (`Actor.Defeated`), se ha preso parte alla sconfitta: o ha dato il colpo lui, o è nello
  stesso scontro, dalla parte opposta. Averla affrontata in passato non basta: se una guardia uccide lontano da lui un
  razziatore con cui aveva combattuto ieri, il giocatore non guadagna nulla (review di Codex).
  - ADATTAMENTO: non c'è ancora un gruppo, quindi il giocatore prende tutti gli XP di uno scontro a cui partecipa,
    chiunque abbia dato l'ultimo colpo.
  - Una creatura scappata conta come sconfitta; se torna non conta di nuovo.
  - Un personaggio pregenerato non vale ancora XP.
- Livello: segue gli XP secondo la tabella Character Advancement (`Builds.ExperienceFor`, `Builds.LevelFor`). Un
  personaggio parte con gli XP del livello della sua definizione; il paladino di livello 1 parte da 0.
- Salire di livello: avviene subito.
  - PF massimi: + PF fissi per classe (Paladino 6 + Cos = 7).
  - PF attuali: +7 anche quelli, se il personaggio è sopra 0 (ADATTAMENTO).
  - Bonus di competenza del nuovo livello, con tutti i numeri che lo usano.
  - Nessuna capacità di classe nuova per ora (ADATTAMENTO).
  - Evento `LevelUp` per il giocatore: "… raggiunge il livello N: …".
  - Nessun evento dice quanti XP ha dato una creatura: sarebbe un indizio sul suo grado di sfida.
- `PlayerView.Level` (null se il giocatore è una creatura), `PlayerView.Experience`, `PlayerView.NextLevelAt` (gli XP del
  livello successivo; null al 20). La scheda (`Sheet`) e i PF massimi sono già quelli del livello.
- Salvataggio, schema 22:
  - Ogni attore ha `Experience` e `Defeated`.
  - Uno schema 20 o 21 si carica: i personaggi hanno gli XP del livello della loro definizione e nessuna creatura
    sconfitta.
  - Rifiutati: uno schema 22 senza questi campi; XP a una creatura; `Defeated` su chi non è il giocatore o con
    creature mai affrontate.

## P1a: movimento libero, prova fuori dal combattimento

Prova separata. Il movimento a caselle (`MoveCommand`) e le mappe esistenti non cambiano.

- `Spot(X, Y)`: un punto esatto della mappa, in pollici dall'angolo in alto a sinistra. Una casella è 60 pollici
  (5 piedi), 12 pollici sono un piede.
  - `Spot.Cell`: la casella del punto.
  - `Spot.CenterOf(casella)`: il centro di una casella.
- Il corpo di una creatura Media è un disco con raggio di 18 pollici (`SpotPlanner.BodyRadius`, 3 piedi di
  diametro dentro i suoi 5 piedi). Così passa con margine da una porta o un corridoio di una casella.
- `MoveToSpotCommand { Actor, To: Spot, Stealthy }`: cammina fino al punto lungo una via breve per il corpo: la più
  corta passando per punti fissi poco fuori dagli angoli, non sempre la più corta in assoluto attorno a ogni angolo
  (qualche pollice in più per svolta; review di Codex).
  - Aggira caselle bloccate, depositi, muri sottili, finestre, il bordo della mappa e le creature ostili coscienti.
  - Attraversa le porte, aperte o chiuse: le apre passando, come oggi.
  - Si passa attraverso chi non è ostile (SRD), ma ci si ferma a non meno di 36 pollici da chiunque.
  - Rifiuti:
    - `NoRoom` (nuovo): lì il corpo non ci sta;
    - `Unreachable`: non c'è una via;
    - `AlreadyThere`;
    - `InCombat`: in combattimento il comando non vale ancora (arriva con P1b).
  - Furtivo: passo lento come `MoveCommand`; in questa prova non cerca il buio.
- Costo geometrico: una diagonale costa la sua lunghezza (circa 7,1 piedi per casella). La via any-angle non è mai più
  lunga del percorso a caselle con diagonali pesate √2, quando quel percorso è praticabile dallo stesso corpo.
- Checkpoint (`SpotPath.Checkpoints`):
  - ogni casella in cui si entra, ogni punto di svolta e ogni 5 piedi;
  - ciascuno con il suo punto esatto (arrotondato al pollice, dentro la sua casella) e il secondo intero in cui lo si
    raggiunge (per eccesso, a Velocità/6 piedi al secondo);
  - porte e contatti vengono valutati lì, come per i passi a caselle;
  - quantizzazione dichiarata: il tempo resta a secondi; nello stesso secondo l'ordine è quello lungo il percorso.
- `SimulationSession.PreviewMoveToSpot(actor, spot, stealthy)` → `SpotMovePreview`. Stesso codice del comando, nessun
  effetto.
  - `Valid`, `Reason`, `Message`.
  - `Way`: i punti di svolta, dalla posizione attuale all'arrivo.
  - `Feet`: lunghezza in piedi, arrotondata per eccesso al decimo.
  - `Seconds`: la durata.
- Viste:
  - `PlayerView.Spot` e `VisibleActorView.Spot`: il punto esatto, a metà cammino l'ultimo checkpoint raggiunto.
    `Position` resta la casella di quel punto.
  - `MoveView`: per un cammino libero anche `FromSpot`, `Spots` (uno per elemento di `Path`, che può ripetere una
    casella) e `Seconds` (dopo `DepartedAt`). Sono null per un cammino a caselle.
  - Il client muove la figura solo tra questi punti.
  - Fermarsi (`StopCommand`) lascia il personaggio sul punto raggiunto. Un passo a caselle lo riporta al centro di una
    casella.
- Salvataggio, schema 23:
  - `Spot` dell'attore, se non è al centro, e dentro la sua casella;
  - `FromSpot`, `Spots` e `Seconds` di un cammino libero, coerenti con `Path` e con la sua fine;
  - gli schemi da 20 a 22 si caricano ancora, con tutti al centro della propria casella.

## P1b: movimento libero in combattimento

- `MoveToSpotCommand` vale anche nel proprio turno di combattimento: lo spostamento avviene subito, dentro il turno,
  senza un cammino nel tempo.
  - Rifiuti: `InCombat` se furtivo; `NotYourTurn`; `NoMovementLeft`; `AlreadyThere`; `NoRoom`; `Unreachable`;
    `TooFar` ("servono 11,2 piedi, te ne restano 8,6").
- Via: la stessa di P1a, con due differenze.
  - Le porte chiuse sono muri: aprirle è un'interazione, come sulle caselle.
  - Aggira le creature che chi si muove percepisce. Si attraversa un alleato; nessuna altra creatura cosciente si
    attraversa; non ci si ferma su nessuno.
  - ADATTAMENTO: una creatura incapacitata si attraversa senza il terreno difficile che sarebbe sulle caselle.
- Costo: la lunghezza della via in pollici, arrotondata per eccesso, sul movimento del turno. Il movimento speso è in
  pollici (`Combatant.MovedInches`); i movimenti a caselle spendono 60 pollici per casella. `CombatSelfView` ha anche
  `MovementLeftInches`; `MovementLeftFeet` resta, arrotondato per difetto.
- Esecuzione pollice per pollice contro le creature davvero presenti:
  - una creatura cosciente non alleata che chi si muove non percepiva lo ferma prima di toccarla;
  - uscire dalla portata di un nemico provoca il suo attacco di opportunità nell'ultimo punto ancora in portata, con
    le regole di sempre (lo vede, ha la Reazione, Disimpegno lo evita);
  - chi cade resta lì;
  - chi finirebbe su qualcuno che non vedeva torna indietro lungo la via. I pollici fatti in avanti restano spesi,
    come sulle caselle.
- Portata in mischia (`InReach`): in portata solo se i corpi distano al massimo la portata da bordo a bordo (per
  5 piedi: 96 pollici tra i centri), senza nulla di solido in mezzo.
  - Niente di solido: caselle bloccate, muri sottili, finestre, porte chiuse; toccare uno spigolo blocca.
  - Ai centri delle caselle è la regola delle caselle per 5 piedi: caselle vicine, diagonali comprese; due caselle,
    fuori portata.
  - Review di Codex: niente più "le caselle vicine bastano", che faceva valere cinque un corpo a sette piedi e mezzo.
  - Un PNG che si avvicina a un bersaglio fermo fuori centro sceglie una casella dal cui centro il colpo arriva.
    Da qualsiasi punto, una delle otto caselle attorno va bene, se è libera.
  - Gittate a distanza, svantaggio da mischia e colpo critico automatico restano a caselle: da migrare.
- `TooFar`: il costo è arrotondato per eccesso al decimo di piede, il residuo per difetto (225 pollici = 18,7 piedi).
- `PreviewMoveToSpot` in combattimento:
  - `Valid` (falso anche per `TooFar`, con la via comunque in `Way`), `Feet`, `FeetLeft` (prima della mossa),
    `Seconds` = 0;
  - `Provokes`: chi farebbe un attacco di opportunità, tra le minacce che il giocatore conosce, con il punto (`At`) e
    i piedi percorsi (`AfterFeet`).
- I PNG si muovono ancora a caselle.
- Salvataggio, schema 24: `MovedInches` di ogni combattente, obbligatorio. Gli schemi precedenti lo ricavano da
  `MovedFeet` × 12.

## P1c: orologio fine

Decisione dell'utente: precisione vera. Un solo orologio della Sim, a tick.

- `GameTime` e `Duration` contano tick, 60 al secondo (`GameTime.TicksPerSecond`).
  - `Ticks`, `GameTime.FromTicks`, `Duration.FromTicks`.
  - `new GameTime(s)`, `new Duration(s)` e `FromSeconds`/`Minutes`/`Hours`/`Days` restano in secondi.
  - `Seconds` sono i secondi interi per difetto: `Day`, `Hour`, `Minute` e `Second` non cambiano. Uguaglianza e ordine
    sono sui tick.
- Scheduler, `Advance` e `AdvanceUntilCompleted` lavorano a tick. Le durate di regola (round, viaggi, attese delle
  routine, torce, svenimento) restano secondi esatti, contati dal tick in cui cominciano.
- Cammini:
  - **a caselle:** la casella i si raggiunge al tick ceil(i × 1800 / Velocità), cioè 60 tick a casella a Velocità 30.
    Fra due centri la posizione è interpolata in proporzione ai tick del passo, anche in diagonale: la diagonale dura un
    passo pur essendo lunga circa 85 pollici.
  - **liberi:** si percorrono Velocità/30 pollici a tick lungo la via (`Vertices`). I checkpoint di P1a si raggiungono
    al loro tick, per eccesso.
  - `MoveAction.SpotAt(t)`, `PlayerView.Spot` e `VisibleActorView.Spot` danno il punto esatto al tick corrente,
    arrotondato al pollice.
  - Al ritmo furtivo (Velocità 20) una casella dura 1,5 s esatti, non più 2 s: cambio voluto.
- Stop o nuovo ordine: il camminatore si ferma nel punto esatto in cui è a quel tick, nella casella di quel punto, e i
  contatti si valutano lì. Un nuovo ordine parte da lì. Niente arretramenti.
- Il combattimento è invariato: il turno del giocatore congela il tempo.
- Rinominati:
  - `MoveView.Seconds` → `MoveView.Ticks`. `MoveView` ha anche `Vertices` (la via di un cammino libero).
  - `SpotMovePreview.Seconds` → `SpotMovePreview.Ticks`.
- Salvataggio, schema 25:
  - ogni istante e durata in tick (anche fatti, scheduler e azioni), una via libera con `Vertices`;
  - gli schemi da 20 a 24 si leggono in secondi × 60; i loro cammini a caselle mantengono gli istanti al secondo con
    cui erano stati programmati.
