# ASTRA — percorso minimo verso una slice giocabile

Analisi del 21 ottobre 2026. Repository esaminato sul commit `faf05e2`, con file locali non versionati già presenti. Questo documento propone il prossimo incremento; **non certifica che la slice descritta sotto sia già realizzata**. La sessione iniziale ha analizzato e verificato i sistemi senza implementare gameplay. Aggiornamento del 22 ottobre: Task 1 completato nella Sim con factory e test; Task 2 completato nel client con scena d'ingresso e smoke; Task 3 completato nel client con attesa accelerata e smoke; Task 4 completato nel client con indicazioni, accesso al deposito e smoke; Task 5 completato nel client con dialogo e diario del rapporto; Task 6–9 non iniziati.

## 1. Decisione principale

Il repository è molto più avanti di un progetto da iniziare: contiene un simulatore ampio, un client utilizzabile e molte prove di regressione. Il percorso più breve è **comporre un'esperienza breve con i sistemi esistenti**, non aggiungere un altro sistema RPG.

Propongo una micro-slice di 5–10 minuti: **«Una sera al granaio»**. Il giocatore raggiunge il granaio, osserva una razzia autonoma, sceglie se riferirla alla guardia e vede una conseguenza fisica nel mondo. Può anche consegnare alcune proprie razioni. Salva, ricarica e riprova una scelta diversa.

La decisione storica in `docs/ARCHITECTURE_DECISIONS.md`, §§14–15, resta importante: il villaggio è un banco di prova; la prima ambientazione prevista è Blackmarsh. Questa proposta è una **tappa di validazione giocabile prima di Blackmarsh**, non una sostituzione dell'ambientazione e non il completamento della sandbox promessa. Città, isola, mappa regionale e tre fazioni attive allargherebbero troppo questo primo traguardo.

Il combattimento esiste già e va preservato, ma non è obbligatorio per chiudere questa micro-slice. Dimostrare insieme razzia, rapporti, conseguenze, duello bilanciato e progressione introdurrebbe un secondo percorso critico. Il primo incremento successivo potrà verificare un intervento armato nello stesso scenario, senza nuove regole.

## 2. Fotografia verificata

### Verifiche eseguite

Da root del repository:

```powershell
dotnet --version
dotnet build RpgSandbox.sln --no-restore --verbosity minimal
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --verbosity minimal
& 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path game --log-file C:/Progetti/rpg/.tmp/astra-boot.log --quit-after 10
```

Risultati: SDK .NET `8.0.412`; build riuscita, zero warning e zero errori; **643 test superati, zero falliti e zero ignorati**. Godot locale si identifica come `4.7.2.stable.mono` e termina con codice 0. Il log headless contiene però `Failed to read the root certificate store`: l'avvio non è completamente privo di diagnostica. Non è stata verificata la causa e non è emerso un errore applicativo C# nel breve avvio.

**Limite della verifica:** nessuna sessione interattiva completa né smoke grafico eseguito in questa analisi. Headless e unit test non certificano leggibilità, picking, animazioni, prestazioni o divertimento. Le valutazioni UX qui derivano da codice e documentazione locale; vanno confermate con il Task 9.

### Struttura e autorità

| Percorso | Contenuto e ruolo reale |
|---|---|
| `src/RpgSandbox.Sim/` | Libreria C# senza Godot. Stato, scheduler, regole, NPC, percezione, persistenza. |
| `src/RpgSandbox.Sim/Api/` | `SimulationSession`, comandi, viste e identificatori. Confine da mantenere. |
| `src/RpgSandbox.Sim/Scenarios/` | Builder, loader JSON, vecchia slice a luoghi, villaggi su mappa e prove di interni. |
| `src/RpgSandbox.Sim/Rules/` | Schede, attacchi, oggetti, classi, RNG e caricamento contenuti. |
| `game/` | Client Godot .NET, input, camera, disegno e UI. Referenzia Sim. |
| `game/content/` | Manifest, contenuti SRD/originali e scenario `living-village.json`. |
| `tests/RpgSandbox.Sim.Tests/` | xUnit: regole, causalità sandbox, movimento, visibilità, salvataggi e regressioni. |
| `docs/`, documenti root, `chat.txt` | Visione, decisioni successive, contratti, piani, revisioni e cronologia. Non sono tutti aggiornati allo stesso stato. |
| `tools/godot/` | Editor portabile locale ignorato da Git; `nuget.config` dipende dal suo percorso per i pacchetti Godot. |

Flusso principale: input Godot → `SimulationSession.Execute(Command)` → validazione/azione → avanzamento del tempo → nuove viste → rendering. `WorldState` e le classi partial `Simulation.*` sono l'autorità dello stato. Non introdurre collisioni o regole parallele in Godot.

`SimulationSession.GetPlayerView()` distingue informazioni del giocatore da `GetWorldView()`, che è onnisciente. Il client usa quest'ultima anche per costruire elementi statici dei depositi, oltre che negli smoke: non basta quindi presumere che ogni dato passato al renderer sia già filtrato. I nuovi suggerimenti e riepiloghi devono usare informazioni pubbliche/viste appropriate.

### Scene e percorsi effettivi

| Scena | Stato e uso |
|---|---|
| `Village.tscn` | Main scene in `project.godot`. Nodo `Node2D` con `VillageMap.cs`; avvia `ScenarioFile.Load("living-village")`. |
| `Main.tscn` | Vecchio client a luoghi/grafo, `Main.cs` e `SliceScenario`; utile come regressione, non base della nuova esperienza. |
| `InteriorPrototype.tscn` | Stesso `VillageMap`, flag esportato, scenario separato per confini sottili, luce e movimento. |
| `InteriorArtPilot.tscn` | Stesso client con altro flag; casa arredata, ostacoli nella Sim, tetto e trattamento visivo specifici. |
| `CombatPreview.tscn` | Anteprima UI con dati finti: non è prova di combattimento completo. |
| `StyleStudyPreview.tscn` | Confronto di immagini/concept, non gameplay. |
| `Paladin3DPrototype.tscn` | Esperimento grafico del personaggio. |

La maggior parte di mappa e UI è costruita da codice, non dall'albero `.tscn`. Cambiare soltanto la scena nell'editor non basta per cambiare lo scenario. Il villaggio principale è 24×17 caselle; il campo dei banditi è un luogo fuori mappa, non una seconda area isometrica esplorabile già pronta.

Sono presenti anche `CharacterPrototypeV2.tscn` e `CharacterRigPrototype.tscn`, esplicitamente ignorate da Git: esperimenti locali, non dipendenze riproducibili. All'inizio dell'analisi erano non versionati `Body.glb`, relative texture/import e `VillageMap.BarrierApproach.cs.uid`. Non eliminarli o incorporarli incidentalmente. Non ho trovato un riferimento a `Body.glb` nei partial di `VillageMap` esaminati; la presenza del file non dimostra un personaggio integrato.

### Sistemi già implementati

| Sistema | Evidenza nel codice | Valutazione per la slice |
|---|---|---|
| Tempo e autonomia | `Simulation.cs`, `Scheduler.cs`, `Simulation.Factions.cs`, `Simulation.Npcs.cs` | Base da riusare. Routine, consumi, incarichi e razzie funzionano senza input. |
| Movimento | `GridMap.cs`, `SpotPlanner.cs`, `Simulation.Movement.cs`, `Simulation.Going.cs`, `VillageMap.FreeMovement.cs` | Griglia logica più posizioni continue `Spot`, percorsi e ingombri. Non rifare con NavigationServer. |
| Informazione | `Perception.cs`, `Simulation.Senses.cs`, `Simulation.Deeds.cs`, `Api/PlayerView.cs` | Vista, luce, udito, furtività, memoria e racconti; non solo log globale. |
| Conseguenze sociali | `Simulation.Npcs.cs`, `Simulation.Deeds.cs`, test `Knowledge`, `Wanted`, `PlayerTheft` | Rapporti, presidio, ostilità, confisca e restituzione: abbastanza per il loop. |
| Combattimento | `Simulation.Combat.cs`, `Simulation.FreeCombat.cs`, `Api/Combat.cs`, `VillageMap.Combat.cs` | Turni, movimento, attacchi, reazioni e integrazione con il mondo; già molto oltre la necessità della micro-slice. |
| Personaggio | `Rules/`, `Simulation.Items.cs`, `Equipment`, `Advancement`, `Spells`, `ClassFeatures` | Inventario, equipaggiamento, XP, capacità e magie. Usare il protagonista predefinito. |
| Ambiente/sopravvivenza | Locks, BreakIn, Traps, Rest, Needs, Travel | Porte, finestre, scasso, trappole, riposo e bisogni esistono. Nessuna espansione richiesta. |
| Persistenza | `Persistence/SaveGame.cs`, `SaveDtos.cs`, `SimulationSession.TryLoad` | Snapshot completi; schema corrente 42, lettura versioni 20–42. Conservare, non migrare per una schermata introduttiva. |
| Client | Partial `VillageMap.*` | Pausa, ritmi 1×/3×/6×, camera, quickbar, targeting, inventario, diario, menu e salvataggio già presenti. |
| Verifiche grafiche | `VillageSmoke.cs`, `InteriorSmoke.cs`, `InteriorArtPilotSmoke.cs` | Utili, ma molte prove preparano/caricano snapshot ad hoc. Non equivalgono a una partita da Nuova partita. |

Il clock attuale usa **60 tick al secondo** (`Api/GameTime.cs`), non soltanto secondi interi come nel piano iniziale. Durante il turno del giocatore in combattimento il tempo si arresta. In esplorazione scorre anche da fermi; le attività a durata fissa possono accelerarlo per durare circa tre secondi reali. Questo contratto va rispettato anche da qualsiasi nuova attesa.

### Asset e documentazione

Riutilizzare pavimenti/figure di `Assets/G15`, icone in `Assets/Ui/ActionIcons` e renderer corrente. `VillageMap.Visuals.cs` attiva per default il paladino 3D live e gestisce figure NPC tramite viewport 3D composte nella scena 2D; ci sono anche sprite direzionali e un atlas di camminata. Non scegliere una nuova pipeline durante questa slice.

`Assets/StyleStudy` contiene concept e props del pilot. Gli arredi hanno un utilizzo concreto nel pilot, ma non sono un sistema di contenitori/loot. Le tavole illustrative non sono mappe navigabili. `CREDITS.md`, README degli asset e `docs/SRD_NOTES.md` restano riferimenti di provenienza; non serve altra ricerca o produzione di asset per questo traguardo.

Ordine di lettura consigliato: questo documento → codice citato nel task → test relativi → `docs/INTEGRATION_CONTRACT.md`. Consultare `docs/ARCHITECTURE_DECISIONS.md` per la direzione, inclusi i paragrafi finali. `RPG_SANDBOX_CONTEXT.md` e `IMPLEMENTATION_PLAN.md` conservano frasi come «non esiste ancora un'implementazione»: sono materiale storico, non una lista delle funzionalità mancanti. Anche alcuni commenti API sono storici, per esempio quello sulle porte onniscienti in `PlayerView`, mentre i test verificano memoria/visibilità.

`docs/UX_RESEARCH_PLAN.md` descrive problemi e proposte recenti, non tutti lavori già eseguiti. `docs/WORK_ALLOCATION.md` documenta una vecchia divisione Claude/Sim e Codex/client: non implica sessioni esterne ancora attive e non deve impedire l'assegnazione singola dei task qui proposta. Se altri agent stanno lavorando davvero, coordinare i file prima di modificarli.

## 3. Valutazione critica

### Mantenere

- Separazione Sim/client, comandi tipizzati, viste, snapshot e RNG persistente. Sono utili oggi, non infrastruttura da giustificare in futuro.
- NPC a priorità esplicite e decisioni ispezionabili. Non serve GOAP, behavior tree generale o LLM.
- Percezione e conoscenza distinte dallo stato reale: sono il tratto sandbox più interessante già disponibile.
- Mappa, movimento, pausa, quickbar e flussi esistenti di dialogo/rapporto. Migliorare solo i passaggi del loop.
- Test e fixture storiche. Non cancellare gli esperimenti per ottenere una falsa sensazione di semplicità.

### Dove c'è troppo investimento rispetto al prossimo risultato

La sovra-ingegnerizzazione è soprattutto **di ampiezza funzionale**: molte regole di classe, magia, mani, trappole, vetri, scasso e sopravvivenza precedono una breve esperienza guidata e verificata. Non sono necessariamente implementazioni sbagliate; continuare a estenderle ora dilaziona il playtest.

`VillageMap` è una classe partial con molti pannelli, modalità, flag e stato transitorio condiviso. Il file principale pesa circa 64 KB; `VillageSmoke.cs` circa 96 KB; `SaveGame.cs` circa 91 KB. Sono segnali di costo di orientamento e accoppiamento, non prove automatiche di cattiva architettura. Un nuovo framework UI, ECS o una riscrittura del salvataggio costerebbero più della slice.

JSON e builder C# rappresentano entrambi il villaggio ampliato; `ScenarioFileTests` ne verifica l'equivalenza. Cambiarne uno solo rompe il contratto. Per il prossimo passo conviene **riusare lo scenario JSON invariato e preparare una sessione iniziale**, evitando una terza copia della mappa.

### Mancanze concrete per una prova consegnabile

1. Un ingresso deliberato nel loop: il main avvia a mezzanotte; il test `RaidOnMapTests` trova il razziatore sulla mappa attorno alle 19:30. A 3×, ore simulate non sono un'attesa ragionevole per un nuovo giocatore.
2. Un invito chiaro, pochi comandi spiegati e un modo controllato di aspettare. Il diario non spiega da solo cosa provare.
3. Feedback esplicito che distingua rapporto avviato, completato e conseguenza osservata. Cliccare «racconta» non è la stessa cosa che vedere la guardia presidiare.
4. Nuova partita/riprova accessibile e salvataggio dedicato: oggi Village e pilot condividono `user://village.json`; il load ripristina la sessione ma non ricostruisce genericamente ogni mappa.
5. Un percorso verificato da avvio pulito, senza JSON di salvataggio manipolati, e un confronto fra due scelte realmente giocabili.

### Rischi da contenere

- **La guardia può apprendere senza il giocatore.** Un test comparativo deve tenere conto delle routine del contadino e dei rapporti autonomi, non attribuire al player tutto ciò che succede.
- **L'osservazione può fallire.** Posizione, sguardo, luce e furtività contano. Non rendere il diario onnisciente per far passare una demo.
- **Una seconda razzia non è immediata.** Dopo il furto il campo ha più cibo; consumi e valutazioni ritardano il prossimo incarico. Non promettere un secondo assalto entro pochi minuti né svuotare il deposito da uno script UI.
- **Accoppiamento agli ID.** Consegna/Prendi sono fissati su `GranaryStore`, vari visuali sui luoghi esistenti. Riutilizzare quegli ID per questa tappa; generalizzare soltanto quando si implementerà una seconda mappa reale.
- **Costo grafico non misurato.** Viewport 3D per NPC e aggiornamenti frequenti di UI/luci meritano misure se il playtest scatta, non ottimizzazione preventiva.
- **Build locale non portabile automaticamente.** Editor ignorato, sorgente NuGet locale e possibili asset non versionati: documentare l'avvio prima di dichiarare consegnabile la slice.

Rifarei ora soltanto un eventuale piccolo punto di ingresso/reset della sessione, se necessario per avviare e riprovare lo stesso scenario. Niente refactoring esteso di Sim, persistenza, renderer o pannelli.

## 4. Contratto della micro-slice

### Contenuto e ritmo

Una sola mappa giocabile già esistente, il villaggio; un protagonista predefinito; guardia, contadino e razziatore esistenti; due fazioni e cibo. Il campo resta un luogo simulato fuori mappa. Nessuna nuova stanza o missione con albero di dialogo.

Inizializzare con `ScenarioFile.Load("living-village")`, creare una sessione e farla avanzare normalmente fino a poco prima della razzia. **19:27 del primo giorno è il candidato da verificare nel Task 1**, non un dato già garantito: orario, punto d'osservazione e sguardo devono essere ricavati dal comportamento reale. La schermata parte in pausa, così leggere non fa perdere l'evento. Nessun bootstrap tramite editing del JSON di un save o scritture dirette a `WorldState`.

Invito proposto: «Le scorte del villaggio attirano i ladri. Raggiungi il granaio e osserva. Se scopri qualcosa, puoi raccontarlo alla guardia alla locanda». È contesto della prova, non un rapporto già conosciuto e non rivela una posizione corrente nascosta.

Percorso previsto:

1. Leggere due righe, riprendere e camminare al granaio. Mostrare clic per camminare, Spazio per pausa e il comando di attesa.
2. Osservare il furto da una posizione valida; l'NPC agisce per le sue regole, anche se il giocatore non interviene.
3. Scegliere: lasciare correre oppure raggiungere la guardia e riferire l'osservazione disponibile.
4. Nel ramo rapporto, seguire la guardia e vederla raggiungere il posto di presidio. Facoltativamente consegnare 3 razioni per mostrare un costo personale e un cambiamento di scorte.
5. Consultare diario/scorte visibili, salvare e ricaricare, riprovare l'altra scelta.

Il ramo «ignora» è un esito valido, non una sconfitta. Il ramo «rapporto» non restituisce magicamente il cibo già rubato. La consegna non impedisce automaticamente altre razzie. Un presidio osservato conclude la prova della reazione: non occorre aspettare giorni per un secondo furto.

### Criteri globali di completamento

- Da avvio pulito, senza console o inspector, una persona riesce a percorrere il ramo rapporto in 5–10 minuti; primo evento rilevante entro circa 2 minuti dopo la ripresa, usando l'attesa se necessario.
- Un secondo avvio permette di ignorare la notizia. Al checkpoint concordato nel Task 1 i due mondi differiscono nel presidio/conoscenza della guardia, con le stesse condizioni iniziali.
- La razzia può avvenire senza player; una notizia non percepita non appare nel diario né nei racconti.
- La consegna di 3 razioni sottrae 3 al player e ne aggiunge 3 al deposito, senza duplicazioni.
- Nessun nuovo sistema di quest/reputazione/AI. Stato reale, conoscenza e interfaccia restano distinti.
- Save/load conserva le conseguenze e le azioni in corso; riprovare riparte davvero dallo stesso stato iniziale.
- Se il giocatore esce dal percorso, può continuare o riprovare senza softlock. Eventuali combattimenti usano il sistema esistente; la loro vittoria non è necessaria.
- Build/test pertinenti e verifica interattiva a 1280×720 e 1920×1080. Nessun asset nuovo necessario.

## 5. Istruzioni operative per il prossimo coding agent

Implementare **un task per sessione/consegna**, iniziando dal Task 1. Non prendere questo documento come autorizzazione a realizzare tutti i task in un unico passaggio. I nomi dei file nuovi sotto sono proposte: non esistono ancora.

Prima di modificare: leggere stato Git e istruzioni locali eventualmente aggiunte, rileggere i file citati nel task e verificare le dipendenze già completate. Non cancellare modifiche o asset dell'utente. Nessun upgrade SDK, nuova dipendenza o produzione artistica richiesto.

Ogni consegna deve riportare: task, file modificati, comando di verifica, risultato, limite noto e successivo task sbloccato. Se una capacità esiste già, verificarla e adattare il minimo indispensabile; non ricostruirla. Aggiornare lo stato del task qui senza dichiarare completati passi non eseguiti.

I task hanno dipendenze esplicite: «indipendente» significa una modifica circoscritta e verificabile separatamente, non assenza di prerequisiti. L'ordine numerico è sempre valido. Dopo Task 2, Task 4 e Task 7 possono procedere separatamente da Task 3, ma non editare contemporaneamente lo stesso partial senza coordinamento.

## 6. Task assegnabili

### Task 1 — Preparare e provare l'ingresso nel loop nella Sim

**Stato:** completato il 22 ottobre 2026, nel perimetro Sim descritto sotto. **Tipo:** reasoning forte. **Dipendenze:** nessuna.

**Obiettivo:** ottenere una sessione riproducibile poco prima del furto e dimostrare due esiti con le API reali, prima di spendere lavoro nella UI.

**File/sistemi:** nuovi `src/RpgSandbox.Sim/Scenarios/PlayableSlice.cs` e `tests/RpgSandbox.Sim.Tests/PlayableSliceTests.cs`; leggere `ScenarioFile`, `MappedVillageScenario`, `RaidOnMapTests`, `KnowledgeTests`, `Simulation.Npcs`, `Perception`. Nessuna modifica al villaggio JSON o al gemello C# prevista.

**Lavoro circoscritto:** factory `PlayableSlice.CreateSession()` che crea il villaggio e lo avanza normalmente al momento scelto. Partire dal candidato 19:27. Definire nel test percorso, punto d'osservazione, orientamento e tempi usando comandi di movimento/sguardo/attesa/rapporto. Il test può leggere `GetWorldView()` per verificare, ma non modificare stato interno o save. Preferire il seed già esistente; un seed esplicito va documentato, non usato per nascondere una meccanica impossibile. Non introdurre un nuovo campo globale start-time se basta la factory.

**Risultato verificabile:** alla partenza il furto non è ancora avvenuto; entro una finestra breve è osservabile. Due sessioni identiche, una con rapporto e una senza, mostrano il cambiamento della guardia al medesimo checkpoint. Verificare anche una posizione fuori vista senza osservazione. Usare piccoli avanzamenti quando il tempo dell'evento è significativo.

**Completato quando:** test mirati verdi; nessuna mutazione artificiale; annotati qui orario esatto, coordinate/sguardo, osservazione ottenuta, tempo di presidio e checkpoint comparativo. Se il contadino interviene autonomamente, scegliere una finestra precedente oppure documentare il conflitto e limitarne la correzione a questo scenario: non riscrivere l'AI. Se non si può ottenere il ramo con le regole correnti, consegnare il test che isola il problema e proporre una correzione minima prima di procedere ai task UI.

#### Risultati effettivi del Task 1 — 22 ottobre 2026

Implementati `src/RpgSandbox.Sim/Scenarios/PlayableSlice.cs` e `tests/RpgSandbox.Sim.Tests/PlayableSliceTests.cs`. La factory carica `living-village` invariato e avanza normalmente di 19 ore e 27 minuti. Il seed resta quello dello scenario esistente. Nessuna modifica a regole, contenuti, client, snapshot o stato interno; i test cambiano il mondo soltanto tramite API pubbliche. Il salvataggio viene serializzato esclusivamente per confrontare la riproducibilità, mai manipolato o usato per preparare la situazione.

**Il loop è ottenibile con le regole correnti.** Il percorso verificato richiede però luce e isolamento visivo della guardia: non basta raggiungere genericamente il cortile. Questa precisazione integra il percorso proposto nella sezione 4 e dovrà guidare i futuri task UI.

Sequenza riproducibile, coordinate di casella a base zero, movimento tramite `MoveToSpotCommand` verso `Spot.CenterOf(...)`:

1. `PlayableSlice.CreateSession()`: primo giorno alle **19:27:00**, player `(2,2)`, 40 razioni nel granaio, nessuna osservazione del player e nessun presidio.
2. Camminare a `(8,3)`, subito fuori dalla locanda. Chiudere la porta `(7,3)` con `DoorCommand`. È un'azione effettiva del protagonista, non uno stato inserito dalla factory.
3. Camminare a **`(16,7)`**, guardare verso **`(0,-1)`**, ossia verso l'accesso al granaio `(16,5)`.
4. Accendere la torcia con `TorchCommand { Lit = true, StowShield = true }`: il paladino ripone realmente lo scudo per liberare la mano. Arrivo e preparazione alle **19:27:15 + 3 tick**.
5. Avanzare per intervalli di massimo un secondo fino all'osservazione. Furto effettivo alle **19:33:12**; rilevato dal polling alle **19:33:12 + 3 tick**. Notizia `Theft`, `Perceived = Seen`, autore `raider`, quantità 8, fonte nulla (osservazione diretta). Scorte 40 → 32.
6. In **entrambi** i rami camminare a `(4,2)` e guardare `(1,0)`, verso la guardia. Il percorso riapre normalmente la porta. Le sessioni sono ancora identiche prima della scelta; non si confrontano due percorsi fisici differenti.
7. Solo nel ramo rapporto, scegliere dalla vista `PeopleHere` il topic con l'ID dell'osservazione e inviare `ReportCommand`. Un secondo prima del completamento la guardia è ancora senza conoscenza/presidio. Il rapporto termina alle **19:38:25 + 3 tick** e trasmette l'origine della notizia con `Source = player`.
8. La guardia viene rilevata al posto **`(15,5)` alle 19:38:35 + 3 tick**. Questo è il tempo campionato con passo di un secondo, non una misura più precisa dell'arrivo fisico.
9. Checkpoint comune **20:00:00**: con rapporto, guardia al presidio del granaio e notizia ricevuta dal player; senza rapporto, guardia ancora a `(5,2)`, nessun presidio e conoscenza vuota. Granaio a **32** in entrambi i rami. Il contadino non annulla la differenza nella finestra verificata.

Le rappresentazioni `Day 0` nei log corrispondono al primo giorno; 3 tick sono 0,05 secondi. Il furto arriva 6 minuti e 12 secondi simulati dopo l'ingresso (circa 124 secondi reali a 3× senza accelerazione); il presidio viene rilevato dopo circa 11 minuti e 35 secondi simulati. Il limite UX di due minuti al primo evento resta da verificare nel client: Task 1 non implementa l'attesa accelerata e non certifica un playtest di 5–10 minuti.

**Controlli che impediscono una dimostrazione ingannevole:**

- Due creazioni della factory danno snapshot identici; nessun furto è già avvenuto all'ingresso.
- Il ramo senza rapporto ripete movimento, chiusura porta, torcia e ritorno alla locanda. L'unica differenza causale introdotta dopo il confronto delle sessioni è il rapporto.
- Restando nella locanda fino alle 19:35, il furto avviene ugualmente (32 razioni), ma il player non ottiene osservazioni o topic e non vede il razziatore.
- Con torcia e **porta lasciata aperta**, la guardia vede direttamente il furto e ottiene il presidio senza rapporto. Il test verifica una fonte nulla: non attribuire questa reazione al player che racconta.
- Nello specifico percorso con porta chiusa e **senza torcia**, il player ottiene invece una notizia `Heard`, senza autore identificato. Non confondere ascolto e identificazione visiva. Questo controllo verifica il caso con il seed corrente, non una garanzia su ogni tiro/percorso.
- Il primo tentativo dal precedente lookout `(18,8)` senza torcia non produceva osservazioni entro le 19:35: lo smoke storico mostrava l'arrivo del razziatore, ma non provava da solo la disponibilità di un rapporto.

Non serve una modifica alle regole per il percorso sopra. La chiusura della porta e la torcia sono azioni giocabili esplicite, non scorciatoie nel codice; tuttavia rendono il percorso più specifico della proposta iniziale. Se in futuro si vuole una dimostrazione con porta aperta e identificazione illuminata, occorrerà riprogettare il posizionamento di scenario della guardia per interrompere la linea di vista, non sopprimerne artificialmente la percezione. Tale modifica non è stata implementata.

Verifiche eseguite:

```powershell
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --filter FullyQualifiedName~PlayableSliceTests --logger "console;verbosity=detailed"
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --verbosity minimal
dotnet build RpgSandbox.sln --no-restore --verbosity minimal
```

La suite finale comprende **648 test superati, zero falliti e zero ignorati**, inclusi i **5 nuovi casi**. Build della solution riuscita, zero avvisi/errori. Nessuna prova grafica richiesta/eseguita per questo task. Il ramo di rapporto, i casi di luce/porta e l'assenza di notizia fuori vista sono coperti dai nuovi test. **Task 2 non iniziato**: nessuna scena, UI, launcher o collegamento della factory al client è stato aggiunto.

### Task 2 — Avviare una scena dedicata nello stato preparato

**Stato:** completato il 22 ottobre 2026. **Tipo:** implementazione ordinaria. **Dipendenze:** 1.

**Obiettivo:** rendere la factory accessibile al giocatore senza alterare l'avvio dei banchi di prova.

**File/sistemi:** nuovo `game/Scenes/PlayableSlice.tscn`; `VillageMap.cs`; eventuale piccolo partial `VillageMap.Slice.cs`.

**Lavoro:** una modalità esplicita esportata, scelta della factory prima di costruire mappa/UI, avvio in pausa e inquadratura utile. Riutilizzare `VillageMap`; non copiarlo. Mostrare titolo, invito e istruzioni minime. Non aggiungere un selettore generico di campagne. Per ora avvio esplicito della scena; main scene invariata fino al collaudo.

**Risultato verificabile:** il comando sotto apre la scena con lo stato del Task 1 e non consuma tempo mentre si legge.

```powershell
& 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game res://Scenes/PlayableSlice.tscn
```

**Completato quando:** build client verde; avvio verificato; pausa/ripresa funzionano; Village e InteriorArtPilot continuano ad avviare il loro scenario. Nessun accesso UI a `WorldState`.

#### Risultati effettivi del Task 2 — 22 ottobre 2026

`game/Scenes/PlayableSlice.tscn` riusa `VillageMap` con `PlayableSliceMode = true`. La modalità sceglie `PlayableSlice.CreateSession()` prima di costruire mappa e UI, mostra l'introduzione «Una sera al granaio» e apre alle 19:27 in pausa. «Comincia» riprende il tempo e chiude il pannello; Spazio usa la pausa esistente. L'inquadratura iniziale comprende il protagonista presso la locanda e il granaio. La main scene resta `Village.tscn`.

Build client: zero avvisi/errori. `--slice-entry-smoke=<dir>` ha verificato che il tempo rimane fermo per 30 frame mentre si leggono le istruzioni e avanza dopo il pulsante; la cattura `entry-paused.png` è stata controllata a 1280×720. Smoke R5 di Village e INTERIOR ART PILOT riusciti con Vulkan. Nessun accesso UI a `WorldState`; nessuna prova del loop completo o dell'attesa accelerata, che restano ai task successivi. I file della factory e dei test del Task 1, inizialmente locali, sono stati poi committati con il client nei commit `6d8af0a` e `5746251`.

### Task 3 — Attendere senza perdere il furto

**Stato:** completato il 22 ottobre 2026. **Tipo:** reasoning mirato su tempo/input; modello forte consigliato. **Dipendenze:** 1, 2.

**Obiettivo:** rendere breve l'osservazione senza cambiare scheduler o regole.

**File/sistemi:** `VillageMap.Slice.cs`, punti minimi di `_Process`/input in `VillageMap.cs`; leggere `WaitCommand`, `Advance`, `AdvanceUntilCompleted`.

**Lavoro:** aggiungere per la slice «Attendi fino a 5 minuti», accelerato e interrompibile. Processare piccoli intervalli di simulazione per frame, con lavoro limitato per frame; fermare l'accelerazione alla prima nuova osservazione del giocatore, a combattimento, morte/incoscienza, pausa o nuovo ordine. Al termine tornare al ritmo precedente. Non saltare direttamente di ore, non fermarsi su eventi segreti letti da `WorldView`, non accodare azioni duplicate. Un'eventuale `WaitCommand` deve rispettare le possibilità reali di cancellazione; è sufficiente un controllo client dell'avanzamento se non serve occupare l'attore.

**Risultato verificabile:** il giocatore in posizione vede il passaggio/furto e il diario si aggiorna; può interrompere e camminare; un giocatore fuori vista non riceve l'osservazione per effetto dell'attesa.

**Completato quando:** verifica di interruzione, pausa e mancato doppio avanzamento nello stesso frame; il comportamento finale della Sim coincide con l'avanzamento ordinario a parità di comandi/tempo. La durata reale soddisfa il budget della slice. Nessuna API temporale nuova salvo necessità dimostrata.

#### Risultati effettivi del Task 3 — 22 ottobre 2026

Nella sola `PlayableSlice` compare «Attendi fino a 5 minuti». Il client avanza la sessione tramite `SimulationSession.Advance` per un massimo di un secondo simulato per frame, poi aggiorna `PlayerView` e si ferma alla prima nuova osservazione conosciuta dal giocatore. Si ferma anche su combattimento, morte/incoscienza, pausa, pulsante «Interrompi attesa» o nuovo ordine; il ramo del clock normale non avanza nello stesso frame. L'attesa è stato UI transitorio, non un `WaitCommand` e non occupa l'attore: i comandi di movimento possono partire subito. Il ritmo esplorativo scelto resta invariato e riprende al termine. Nessuna API o regola Sim nuova.

Lo smoke `--slice-wait-smoke=<dir>` ha verificato: pulsante disabilitato durante l'introduzione; pausa che ferma il tempo; nuovo ordine di cammino che interrompe l'attesa e avvia il movimento; assenza di doppio avanzamento nei frame di attesa. Due attese dall'interno della locanda lasciano avvenire il furto autonomo (40 → 32 razioni) senza aggiungere osservazioni al giocatore. A pari checkpoint, gli snapshot completi coincidono con una copia avanzata con il normale `Advance`. Dal punto `(16,7)`, con porta chiusa, sguardo verso nord e torcia, la seconda attesa si interrompe alla notizia `Theft` vista e identificata, alle **19:33:12 + 3 tick**; il diario mostra le razioni sottratte. Dalla posizione preparata fino alla notizia, le due attese hanno impiegato **10,8 secondi reali** nello smoke Vulkan locale. Cattura controllata: `.tmp/astra-task3/theft-observed.png` a 1280×720.

Verifiche: `dotnet build game/RpgSandbox.Game.csproj --no-restore -v:q` senza avvisi/errori; 648/648 test Sim; smoke SLICE WAIT, SLICE ENTRY e R5 VILLAGE riusciti con Vulkan. Il posizionamento della prova automatica usa le API pubbliche della Sim, come il test del Task 1: non dimostra ancora che un nuovo giocatore trovi da solo il punto d'osservazione e le azioni necessarie, né sostituisce il playtest dei task successivi.

### Task 4 — Rendere raggiungibili e comprensibili i comandi essenziali

**Stato:** completato il 22 ottobre 2026. **Tipo:** implementazione ordinaria. **Dipendenze:** 2.

**Obiettivo:** trovare granaio, guardia, diario e consegna senza spiegazioni esterne.

**File/sistemi:** `VillageMap.Slice.cs`, `VillageMap.Hud.cs`, punti relativi a persone/depositi in `VillageMap.cs`; icone già disponibili.

**Lavoro:** testo breve per nomi/azioni e feedback di destinazione; indicare il punto raggiungibile presso il deposito quando noto. Usare i comandi di movimento e dialogo già presenti. Per Consegna, spiegare la portata e offrire 3 razioni quando disponibili, lasciando alla Sim la validazione. Se fuori portata, mostrare il rimedio; non serve costruire un sistema generale di avvicinamento automatico agli oggetti.

**Risultato verificabile:** si cammina al deposito, si trasferiscono 3 razioni e si raggiunge la guardia. Un rifiuto non cambia scorte e spiega cosa fare.

**Completato quando:** comandi leggibili a 1280×720; nessuna nuova icona; nessun pulsante coperto dal log; nessuna posizione nascosta di NPC rivelata dal suggerimento. Test di conservazione del trasferimento esistente riusato, non duplicato senza motivo.

#### Risultati effettivi del Task 4 — 22 ottobre 2026

Nella scena `PlayableSlice`, «Comincia» ha un aspetto da pulsante. Un riquadro breve indica la strada del granaio, come parlare alla guardia quando è visibile e dove aprire il diario; si nasconde durante il dialogo per non coprirlo. Il nome del deposito può apparire quando la sua cassa è visibile, mentre il numero corrente delle scorte compare solo dal suo accesso. Vicino alla cassa, se il deposito è nella vista, un rombo sul terreno segnala la casella d'accesso già conosciuta e un pulsante offre «Consegna 3 razioni». Il comando usa `DepositFoodCommand`; un rifiuto per distanza indica di raggiungere il rombo senza muovere automaticamente il personaggio. Nessuna nuova icona o modifica della Sim.

Verifiche effettive: build client senza avvisi/errori; 648/648 test Sim superati; smoke Godot Vulkan `--slice-guide-smoke` riuscito con cammino al deposito, rifiuto da un lato fuori portata senza variazioni delle due scorte, consegna di 3 razioni dall'accesso (giocatore 10 → 7, deposito 40 → 43), ritorno alla locanda e dialogo con la guardia. Smoke `--slice-entry-smoke`, `--slice-wait-smoke` e `--village-smoke` riusciti. Catture `.tmp/astra-task4/entry-guide.png`, `too-far.png`, `delivered.png`, `guard-talk.png` controllate a 1280×720: testo e pulsanti leggibili, log separato dal pulsante, dialogo non coperto dalle indicazioni. La guida della guardia usa solo `VisibleActors`; il riquadro di consegna compare solo vicino al deposito visibile. Resta utile un playtest umano per giudicare quanto intuitiva sia la ricerca del percorso; non è stato fatto in questa verifica.

Dopo la review di Claude, la scritta «Consegna qui» si nasconde durante l'attesa o se il protagonista/un attore visibile occupa l'accesso o una casella adiacente; il rombo resta. Build 0/0, smoke SLICE GUIDE e SLICE WAIT riusciti; `.tmp/astra-task4-followup-wait/theft-observed.png` controllata a 1280×720, senza sovrapposizione della scritta al nome del razziatore. La review di Claude su una copia pulita aveva già confermato Task 3 e 4 con 648/648 test, build 0/0 e smoke OpenGL SLICE ENTRY, SLICE WAIT, SLICE GUIDE e VILLAGE.

### Task 5 — Collegare chiaramente notizia, rapporto ed esito

**Stato:** completato il 22 ottobre 2026. **Tipo:** implementazione ordinaria con attenzione al contratto informativo. **Dipendenze:** 1, 2, 4.

**Obiettivo:** rendere il ramo rapporto comprensibile usando il dialogo già implementato.

**File/sistemi:** `VillageMap.cs` (`OpenTalk`, `RefreshPeople`, `Tell`), `VillageMap.Hud.cs`, `VillageMap.Deeds.cs`; leggere `PeopleHere.Topics`, `Observations`, `RecentEvents`.

**Lavoro:** evidenziare il racconto disponibile del furto; distinguere accettazione del comando da conclusione dell'attività. Dopo il rapporto, invitare a osservare la reazione, senza dichiarare che il presidio è già attivo. Niente dialoghi ramificati né nuovo `QuestManager`.

**Risultato verificabile:** senza osservazione non si può riferire quel furto; con osservazione il rapporto parte, termina e conserva fonte/contenuto. Raccontare di nuovo non duplica la prova.

**Completato quando:** il ramo del Task 1 si esegue via UI e il diario conserva la distinzione fra visto e riferito. Riusare la logica esistente di deduplicazione; aggiungere test soltanto per nuove condizioni effettivamente introdotte.

#### Risultati effettivi del Task 5 — 22 ottobre 2026

Nella sola `PlayableSlice`, il dialogo con la guardia spiega quando non esiste ancora una notizia del furto. Se la notizia è fra `PeopleHere.Topics`, offre il pulsante evidenziato «Riferisci il furto». Il clic mostra «Rapporto avviato» e il dialogo indica che il racconto è in corso; la conclusione è annunciata solo quando l'osservazione del giocatore contiene la guardia in `ToldTo`. Se il rapporto fallisce o si interrompe, il client non lo dichiara concluso. Dopo la conclusione il tema sparisce e il testo invita a osservare la reazione, senza anticipare un presidio. Il diario distingue «Furto visto», «Furto udito» o «Furto raccontato», conserva i dettagli e la fonte e indica separatamente lo stato del rapporto alla guardia. Dialogo e diario della slice hanno sfondi e posizioni leggibili a 1280×720. Nessuna modifica alla Sim, nessuna nuova quest e nessun riepilogo del Task 6.

Verifiche effettive: `dotnet build game/RpgSandbox.Game.csproj --no-restore -v:q` senza avvisi/errori; 648/648 test Sim; smoke Vulkan `--slice-report-smoke` riuscito. Lo smoke ha verificato che inizialmente la guardia non offre il furto, poi ha osservato il furto tramite la preparazione e l'attesa già usate nel Task 3, raggiunto la guardia con movimento e porta esistenti e premuto il pulsante del dialogo: all'avvio `ToldTo` è ancora vuoto, alla fine contiene la guardia; origine, fonte, quantità, deposito e responsabile dell'osservazione restano invariati. Il tema non è più disponibile e la guardia ha una sola osservazione con quell'origine. Smoke `--slice-entry-smoke`, `--slice-guide-smoke`, `--slice-wait-smoke` e `--village-smoke` riusciti. Catture `.tmp/astra-task5/no-report.png`, `report-started.png`, `report-complete.png` controllate a 1280×720; il diario mostra «Furto visto» e «Rapporto alla guardia: completato». Non è stato eseguito un playtest umano; la reazione del mondo dopo il rapporto resta da rendere comprensibile nei task successivi.

### Task 6 — Mostrare una conclusione basata su ciò che si sa

**Stato:** da fare. **Tipo:** implementazione ordinaria; reasoning forte solo se manca un dato pubblico necessario. **Dipendenze:** 3, 5.

**Obiettivo:** far percepire la conseguenza e permettere di fermarsi dopo una prova breve.

**File/sistemi:** `VillageMap.Slice.cs`, `VillageMap.Hud.cs`; solo se indispensabile, campo additivo in `Api/PlayerView.cs`/relative viste e costruzione in `SimulationSession.cs`.

**Lavoro:** piccolo riepilogo richiamabile: notizia osservata, rapporto completato se noto, razioni attuali del player e scorte attualmente visibili. Il presidio si conferma solo quando osservabile nella vista; se manca l'informazione, aggiungere un dato minimo filtrato, mai leggere `ActorView.GuardDuty` onnisciente dalla UI. «Non hai riferito» non implica «nessuno sa». Il ramo ignora può concludersi con una scelta esplicita di terminare la prova; non serve un trigger segreto sulla fine della razzia.

**Risultato verificabile:** i due rami hanno riepiloghi corretti, e il rapporto non viene presentato come restituzione o eliminazione dei banditi. Si può chiudere il riepilogo e continuare.

**Completato quando:** nessuna dipendenza dal parsing di frasi italiane o dal conteggio del log volatile per stabilire la verità del mondo. Se aggiunto un campo pubblico, test positivo/negativo di visibilità. Non aggiungere stato persistente di quest per una checklist: preferire dati già salvati e riepiloghi ricostruibili.

### Task 7 — Riprovare e salvare senza contaminare altri scenari

**Stato:** da fare. **Tipo:** implementazione ordinaria; review attenta del reset. **Dipendenze:** 2.

**Obiettivo:** «Riprova» e save/load affidabili per questa stessa mappa.

**File/sistemi:** `VillageMap.cs` (save/load e stato transitorio), `VillageMap.Quickbar.cs`, `VillageMap.Slice.cs`; `SaveGame.cs` solo in presenza di un bug riprodotto, non come prerequisito.

**Lavoro:** usare `user://playable-slice.json` per la modalità dedicata. Riprova ricrea la scena/sessione tramite la factory, azzerando ordini accodati, targeting, accelerazione, dialoghi e indicazioni transitorie; ricaricare la scena è preferibile a un grande reset manuale. Il load non esegue di nuovo il preroll. Conservare gli errori leggibili già presenti e la sessione corrente in caso di file invalido.

**Risultato verificabile:** save durante movimento e dopo rapporto, load e prosecuzione; due Riprova producono lo stesso stato iniziale; il salvataggio Village resta separato.

**Completato quando:** nessuna azione fantasma dopo load/reset, nessun doppio rapporto, nessuna nuova migrazione schema. Testare il percorso dedicato e un file corrotto, oltre ai test esistenti di persistenza. Non promettere caricamento arbitrario tra mappe diverse.

### Task 8 — Smoke della slice da avvio pulito

**Stato:** da fare. **Tipo:** implementazione di test d'integrazione, reasoning medio. **Dipendenze:** 3, 4, 5, 6, 7.

**Obiettivo:** verificare l'esperienza composta senza fixture che saltino i passaggi fondamentali.

**File/sistemi:** nuovo `game/Scripts/PlayableSliceSmoke.cs`, solo hook strettamente necessari in `VillageMap.Slice.cs`; test del Task 1.

**Lavoro:** flag `--slice-smoke=<dir>`, ramo ignora e ramo rapporto avviati entrambi dalla factory; stessi handler usati dalla UI, attese limitate e fallimenti con exit code 1. Catture: ingresso, osservazione, rapporto, guardia al presidio, ripresa da save. Gli hook possono invocare i comandi UI, non modificare WorldState/JSON o iniettare viste finte. Non estendere il già grande `VillageSmoke.cs` con un'altra sequenza indipendente.

**Risultato verificabile:** smoke ripetibile, log con tempi/esiti e nessuna preparazione manuale di salvataggi. La comparazione omnisciente è ammessa negli assert, non nell'interfaccia del giocatore.

**Completato quando:** due esecuzioni pulite riuscite, build e suite Sim verdi, screenshot ispezionati. Il test rileva anche assenza di osservazione/rapporto invece di limitarsi a «la scena non crasha». Lo smoke non sostituisce il playtest umano del Task 9.

### Task 9 — Collaudare e rendere evidente l'avvio della prova

**Stato:** da fare. **Tipo:** collaudo e implementazione ordinaria. **Dipendenze:** 8.

**Obiettivo:** consegnare una prova che un'altra persona possa avviare e capire autonomamente.

**File/sistemi:** `README.md`, nuovo `gioca-slice.cmd`, eventuali correzioni minime localizzate; stato dei task in questo documento.

**Lavoro:** istruzioni di build/avvio, prerequisiti locali e comandi essenziali. Il launcher apre esplicitamente `PlayableSlice.tscn`, così non è necessario cambiare la main scene o `gioca.cmd`. Provare a 1280×720 e 1920×1080 entrambi i rami, consegna, pausa, attesa, save/load e riprova. Cronometrare tempo al primo evento e tempo al presidio. Far eseguire il ramo principale a una persona che non ha letto il codice; registrare dove esita.

**Risultato verificabile:** una piccola scheda di collaudo con tempi, passaggi riusciti e problemi. Un avvio da checkout pulito con prerequisiti documentati non dipende dagli esperimenti locali o da un save preesistente.

**Completato quando:** criteri globali soddisfatti, nessun softlock o errore applicativo nel percorso; il giocatore sa spiegare cosa ha cambiato il proprio rapporto. Se il playtest umano non è disponibile, dichiarare la verifica pendente: non marcarla come superata da uno smoke. Correggere soltanto problemi bloccanti della slice; rinviare la rifinitura estetica.

## 7. Allocazione del reasoning e dipendenze

```text
1 → 2 → 3 ────────────┐
      ├→ 4 → 5 → 6 ──┼→ 8 → 9
      └→ 7 ──────────┘
```

Task 6 richiede anche Task 3. Numerazione e dipendenze delle schede prevalgono sul diagramma sintetico.

| Task | Serve davvero reasoning forte? | Motivo |
|---|---|---|
| 1 | Sì | Incrocia scheduler, luce, furtività, routine autonome e causalità del rapporto. Una demo apparentemente riuscita può essere falsa. |
| 2 | No | Collegamento di factory e scena con UI minima. |
| 3 | Sì, su un problema circoscritto | Accelerazione, pausa, cancellazione e stop sugli eventi percepiti devono rispettare il clock reale. |
| 4 | No | Affordance, testi e collegamenti a handler già presenti. |
| 5 | No, se usa il contratto esistente | Non inventare nuovi stati sociali o regole di informazione. |
| 6 | Condizionale | Ordinario se le viste bastano; forte solo per definire correttamente un nuovo dato filtrato. |
| 7 | No, con review | Il rischio è dimenticare stato UI transitorio, non progettare una nuova persistenza. |
| 8 | Medio | Integrazione e sincronizzazione della prova, senza nuove meccaniche. |
| 9 | No | Osservazione del giocatore, misure e correzioni locali; un modello più potente non sostituisce il playtest. |

Claude Code, Codex o un'altra sessione possono prendere singolarmente ciascun task. Assegnare capacità in base al problema, non al nome del prodotto. Conservare i risultati del Task 1 nel repository per non ripagare la stessa analisi in ogni sessione.

## 8. Cosa resta esplicitamente fuori

Nessuna nuova classe, incantesimo, sistema di crafting/commercio, companion, creazione personaggio, faction planner, bus eventi, generatore di quest, editor scenari, localizzazione generale, mappa regionale, nuova ambientazione giocabile, import GLB, kit architettonico o revisione generale della UI.

Non completare prima l'intero piano UX: per questa prova servono soltanto chiarezza dei comandi utilizzati, ritmo e feedback. Non trasferire automaticamente tutti i vecchi TODO nella nuova roadmap.

Dopo il collaudo, decidere sulla base di ciò che il giocatore ha capito e trovato interessante: un intervento armato breve nello stesso loop, oppure il trasferimento della meccanica a una singola situazione di Blackmarsh. Nessuno dei due è un prerequisito della consegna qui definita.

## Primo task consigliato

**Task 6: mostrare una conclusione basata su ciò che si sa.** Il ramo del rapporto è ora leggibile; resta da offrire un riepilogo fedele alla vista del giocatore secondo la scheda del Task 6. Questo task non è stato iniziato nell'implementazione del Task 5.
