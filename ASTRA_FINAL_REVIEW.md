# ASTRA — review finale della vertical slice

Data: **23 ottobre 2026**. Codice esaminato: **`7f6f0f8`**. Review diagnostica: nessuna correzione a codice, scene, asset, test o roadmap. Questo documento distingue osservazioni di esecuzione, conclusioni da lettura del codice e verifiche mancanti.

## 1. Verdetto

**La micro-slice funziona nel percorso principale e conserva l'architettura prevista. Non è ancora corretto certificare tutti i criteri dei Task 1–9 senza riserve.**

Sono verificati il furto autonomo, la conoscenza filtrata, la causalità del rapporto, il presidio fisico, la consegna, l'attesa accelerata e i percorsi di save/load/retry richiesti. Build riuscita, **651 test superati**, due esecuzioni grafiche dello smoke composto riuscite e sei smoke mirati riusciti. Anche lo smoke esteso della scena Village preesistente passa.

Non ho trovato un blocco riprodotto del loop pacifico. Ho trovato **un difetto importante del riepilogo durante il combattimento**: la sua pausa non impedisce tutti i comandi e avanzamenti dello scontro. Il difetto risulta dal flusso di codice; non è stato riprodotto con una prova interattiva dedicata in questa review.

Il **Task 9 resta parziale**: durata di 5–10 minuti e comprensibilità autonoma per una persona nuova non sono verificate. La roadmap stessa lo dichiara nella scheda del task. È una mancanza di evidenza, non la dimostrazione che il gioco sia incomprensibile o troppo lungo.

Raccomandazione: una breve consegna di consolidamento sul riepilogo e sulle verifiche delle due regole generali cambiate, poi il collaudo umano quando disponibile. Nessuna riscrittura o nuova infrastruttura è giustificata da questa review.

## 2. Obiettivi originali e perimetro reale

La roadmap proponeva «Una sera al granaio»: una sola mappa e un protagonista, furto autonomo, scelta fra ignorare e riferire, conseguenza osservabile, consegna facoltativa di tre razioni, salvataggio e nuovo tentativo. Il villaggio rimane una validazione prima di Blackmarsh. Combattimento, progressione e nuovi sistemi non erano necessari per concludere la prova.

I vincoli rilevanti sono rispettati nel percorso principale:

- La factory prepara una sessione mediante il normale avanzamento della Sim; non inserisce un furto o una notizia artificiali.
- La scelta del giocatore modifica informazioni e azioni nel mondo, senza una quest parallela nel client.
- Il rapporto non restituisce magicamente le razioni e non implica che il presidio sia già raggiunto.
- «Non ho riferito» non viene tradotto in «nessun altro sa».
- Riprova ricrea la scena; il caricamento riprende la sessione salvata senza un secondo preroll.

### Stato Git e storia

All'inizio della review, `git status --short` mostrava soltanto otto file locali non tracciati: `game/Scenes/Body.glb`, le tre texture associate e i quattro file `.import`. Non sono stati modificati. Non ho trovato riferimenti a questi asset nel percorso della slice esaminato. Diff di lavoro e diff staged erano vuoti; `git diff --check` era pulito.

La roadmap iniziale fotografava `faf05e2`. Per attribuire le modifiche della slice ho confrontato anche **`f15055a..7f6f0f8`**, separando gli interventi precedenti su client/arredi: **45 file, 2703 inserimenti, 52 rimozioni**. Le modifiche Sim in questo intervallo sono limitate a factory, percezione adiacente, uscita da sovrapposizione e dato pubblico del presidio.

| Commit | Cambiamento rilevante |
|---|---|
| `6d8af0a`, `5746251` | Factory/test del Task 1 e ingresso dedicato del Task 2. |
| `9fcc7e8`, `7348de0` | Attesa, guida, consegna e leggibilità del marcatore. |
| `91ae9d5` | Dialogo/diario del rapporto e launcher. |
| `6c0ac1d` | Regola di percezione adiacente, uscita da corpi sovrapposti, targeting e feedback del tempo. |
| `cb1cbb5` | Riepilogo e `VisibleActorView.GuardingStore`. |
| `ff6dddf`, `360319c` | Focus/Spazio, interazione in combattimento, protezione della pausa del riepilogo e icone. |
| `4d39bae`, `c3c7583` | Save separato, reset, smoke di persistenza corretto per salvare dopo un passo reale. |
| `c7636f3` | Smoke composto, documentazione di avvio, scheda di playtest e correzioni di layout. |
| `7f6f0f8` | Annotazione in cronologia, non ulteriore implementazione del loop. |

Non ho usato le dichiarazioni dei commit come prova di correttezza: ho letto implementazioni e assert, quindi eseguito le verifiche sotto.

## 3. Architettura, causalità e stato

### Sim autoritativa e confine con Godot

`src/RpgSandbox.Sim/Scenarios/PlayableSlice.cs` carica `living-village` e avanza di 19 ore e 27 minuti. Non modifica JSON, seed, inventari, conoscenze o incarichi a mano. Lo scenario e il suo builder gemello non sono stati duplicati per la demo.

In `VillageMap.Slice.cs`, consegna e movimento passano dai comandi esistenti. La distanza usata per mostrare il suggerimento non decide la validità del trasferimento: decide la Sim, compreso il rifiuto `OutOfReach`. Non ho trovato una seconda implementazione di furtività, rapporto, presidio o conservazione del cibo nel client.

Attesa, azione di rapporto seguita per il feedback, apertura del riepilogo e conclusione esplicita sono stato transitorio di interfaccia. Non sono una copia persistente della verità del mondo. La verità del rapporto viene da `Observations.ToldTo`, quella del presidio da `VisibleActors.GuardingStore`.

I nuovi partial di produzione della slice non leggono `GetWorldView()` per decidere notizie o risultati. I relativi smoke lo leggono negli assert: è appropriato. Rimane l'uso preesistente di `GetWorldView().Stores` nella costruzione statica della mappa (`VillageMap.cs`, circa riga 1411). Non va confuso con una nuova fuga informativa introdotta dal riepilogo; non è però corretto descrivere tutto il renderer storico come già esclusivamente basato su `PlayerView`.

Anche il conteggio delle scorte merita precisione: il client lo mostra soltanto dalla casella d'accesso, ma il valore è già disponibile nella vista del deposito visibile. È una restrizione di presentazione, non un nuovo confine di segretezza imposto dall'API. Non occorre ridisegnare l'API per questa slice; occorrerà definirne il contratto se contare a distanza diventerà una regola generale.

### Furto, rapporto e conseguenza

La prova causale più forte resta `Reporting_an_observed_theft_changes_the_guard_at_the_same_checkpoint`, in `tests/RpgSandbox.Sim.Tests/PlayableSliceTests.cs`:

1. Due sessioni iniziano identiche alle 19:27, con 40 razioni e nessuna notizia.
2. Entrambe eseguono lo stesso percorso, chiudono la porta, accendono la torcia e osservano da `(16,7)` verso nord.
3. Il furto avviene alle **19:33:12**: 8 razioni sottratte, 32 rimaste, osservazione diretta con autore identificato.
4. Entrambe tornano dalla guardia; gli snapshot sono ancora uguali prima della scelta.
5. Soltanto una riferisce. Prima del completamento la guardia non ha ancora ricevuto la notizia. Dopo, la fonte è il player.
6. Alle **20:00**, il ramo rapporto ha presidio e conoscenza, il ramo ignora no; il cibo rimane 32 in entrambi.

I test verificano anche che il furto avvenga senza che il protagonista lo percepisca e che la guardia possa vederlo autonomamente con la porta aperta. Non è stata soppressa la sua percezione per attribuire artificialmente ogni reazione al giocatore.

Lo smoke composto conferma la giocabilità dei due percorsi, ma **non è da solo un esperimento causale controllato**: usa torcia diversa nei due rami e conclude il ramo ignora prima del checkpoint del ramo rapporto. Il test Sim al checkpoint comune copre precisamente questa differenza.

### Percezione e informazione nel riepilogo

La nuova condizione `closeSeen` in `Simulation.Senses.cs:300` rende visibile il furto a un testimone adiacente che soddisfa `CanSee`, non cieco e con ladro non invisibile. `CanSee` mantiene luce, linea di vista e orientamento. Il riconoscimento continua a richiedere `SeenAtStart`: vedere il completamento non identifica retroattivamente chi non si era riconosciuto all'inizio.

È una **modifica generale della regola**, non una condizione sugli ID della slice. Risolve l'incoerenza segnalata dal playtest fra vedere il ladro armeggiare e non acquisire alcuna notizia. Non era necessaria per il percorso originale con torcia, già funzionante nel Task 1; è stata introdotta successivamente per un percorso spontaneo. Non va nascosta fra i soli cambiamenti cosmetici.

`SimulationSession.cs:347` costruisce `GuardingStore` soltanto per attori già filtrati come visibili e con azione corrente `GuardAction`; non legge l'incarico futuro per anticipare il risultato. Il riepilogo legge questo dato, le osservazioni e `ToldTo`, senza parsing del log italiano. La fonte raccontata rimane distinta da visto/udito. Non ho trovato una nuova conoscenza onnisciente nella UI della slice.

### Attesa accelerata

`ProcessSliceWaitFrame` avanza al massimo un secondo simulato per frame, aggiorna la vista e interrompe per nuova osservazione conosciuta, combattimento, morte/incoscienza, attività incompatibile, arresto del clock o limite di cinque minuti. Pausa e nuovi ordini cancellano l'attesa. Il valore restituito impedisce anche al clock ordinario di avanzare nello stesso frame.

Lo smoke WAIT verifica interruzione per pausa/movimento, assenza di notizie fuori vista, arresto alla notizia e uguaglianza dello snapshot rispetto all'avanzamento ordinario a pari checkpoint. È una verifica comportamentale sostanziale. L'uguaglianza è dimostrata sul caso coperto, non su ogni combinazione di eventi e comandi.

La velocità reale dipende dai frame e dal costo di `Refresh()`: un secondo per frame limita l'incremento simulato, non garantisce un budget CPU costante. È accettabile qui. Le condizioni di morte/incoscienza e ingresso in combattimento sono presenti nel codice, ma lo smoke WAIT non esercita individualmente tutti questi stop.

### Save/load/retry

`VillageMap.cs:791` valida il caricamento prima di sostituire la sessione. Il percorso valido cancella attesa, code, targeting, pannelli e feedback transitori; riprende `Action`/`Move` dalle viste salvate. La slice usa `user://playable-slice.json`; Village mantiene `user://village.json`. Il retry ricarica davvero `PlayableSlice.tscn`.

Lo smoke di persistenza salva **dopo uno spostamento reale e prima dell'arrivo**, riprende fino alla destinazione e controlla che un ordine accodato successivamente non venga rieseguito. Verifica inoltre rapporto completato, caricamento corrotto senza sostituzione della sessione, separazione del file Village e due retry con snapshot iniziale identico. Gli smoke che scrivono il save ripristinano l'eventuale file precedente in `finally`.

Non ho rilevato duplicazione del rapporto o nuovo preroll al load. Non è invece provata dalla slice la ripresa di ogni intenzione composta del client, per esempio «cammina e poi parla/apri/attacca»: i riferimenti al seguito dell'avvicinamento vengono cancellati, mentre l'azione Sim di movimento si conserva. Il Task 7 richiedeva cammino e salvataggio dopo rapporto, entrambi verificati; non promettere persistenza generica di tutte le intenzioni UI. Il feedback transitorio di un rapporto salvato a metà non è coperto dagli smoke attuali.

## 4. Problemi bloccanti

**Nessun difetto bloccante riprodotto nel percorso principale richiesto.** Non risultano build rotte, furto impossibile, rapporto simulato dalla UI, save contaminati o softlock nel percorso verificato.

Esistono però due impedimenti a una dichiarazione di completamento senza riserve: il difetto F1 sotto e la verifica umana ancora mancante del Task 9. Il secondo è un criterio non verificato, non un bug osservato.

## 5. Problemi importanti ma non bloccanti per il loop pacifico

### F1 — Il riepilogo non congela davvero lo scontro

**Priorità: correggere nel prossimo intervento circoscritto. Evidenza: flusso di codice verificato; riproduzione grafica dedicata non eseguita.**

Riferimenti:

- `VillageMap.SliceSummary.cs:80`: apertura disponibile anche durante lo scontro, con `SetPaused(true)`; il pannello intercetta il mouse solo nel proprio rettangolo.
- `VillageMap.cs:578`: impedisce la ripresa tramite `SetPaused(false)` a riepilogo aperto.
- `VillageMap.cs:608`: `Order` esegue comunque subito se esiste `Combat`, anche quando `_paused` è vero.
- `VillageMap.Combat.cs:585`: `EndCombatTurn` invia direttamente `EndTurnCommand`, senza controllo del riepilogo.
- `VillageMap.Quickbar.cs:480`: Fine turno è disabilitato in base al turno, non alla presenza del riepilogo.
- `VillageMap.cs:164`: l'avanzamento a `NextRoundAt` non controlla `_paused` o il riepilogo.

Ne consegue che «riepilogo aperto» e perfino «Prova conclusa» non costituiscono una barriera alle azioni di combattimento. La protezione aggiunta contro Spazio/hotbar risolve solo la ripresa dell'esplorazione. Si può impartire Fine turno dal controllo esterno al pannello, far risolvere gli NPC e poi avanzare il round; anche gli ordini di combattimento aggirano la coda della pausa. L'esatta sequenza visiva e gli effetti sul personaggio vanno ancora misurati in esecuzione.

**Riproduzione da aggiungere prima della correzione:** avviare un combattimento reale, aprire Riepilogo durante il turno del player, tentare Fine turno e movimento sul terreno esterno al pannello; ripetere dopo Termina la prova. Confrontare tempo, round, posizione, risorse e log del combattimento prima/dopo. Chiudendo il riepilogo, il normale controllo deve tornare disponibile.

**Modifica minima proposta:** impedire l'invio delle azioni di gioco e l'avanzamento del round mentre il riepilogo è aperto, coprendo sia pulsanti sia input sulla mappa; mantenere attivi soltanto i controlli del riepilogo previsti. Non cambiare la semantica generale del combattimento in pausa e non costruire un framework di finestre modali. Lo smoke SUMMARY attuale prova Spazio e pausa soltanto fuori combattimento, quindi passa senza rilevare F1.

### F2 — La copertura dichiarata delle modifiche generali supera alcuni assert reali

**Priorità: rafforzare pochi casi mirati prima di estendere queste regole ad altri scenari. Non sono regressioni dimostrate.**

- Il nuovo test della percezione adiacente copre il caso positivo in luce fioca, senza torcia. Non esercita la nuova diramazione con buio, muro, cecità, invisibilità e arrivo tardivo. Le condizioni si leggono nel codice, ma il verde della suite non prova da solo tutti questi confini.
- `Player_overlapped_by_the_raider_at_the_access_can_step_away_but_not_through_him` verifica una preview valida allontanandosi e una preview invalida **sulla posizione del razziatore**. Non verifica un percorso verso una destinazione oltre il corpo né la sua geometria. Lo smoke COMBAT conferma uno spostamento effettivo in uscita, non ogni percorso di attraversamento.
- Nel test di `GuardingStore`, l'assert durante l'incarico in viaggio usa `SingleOrDefault(...)?`. Può passare perché la guardia non è visibile. La nota della roadmap dichiara più precisamente «in servizio ma ancora in cammino»: per provare quel caso informativo serve garantire che l'attore sia visibile prima di verificare il campo nullo. I casi al posto, fuori vista e visibile non di guardia sono già coperti.

Sono test utili, non una suite da scartare. L'intervento opportuno è precisare gli assert e aggiungere pochi controesempi sul comportamento pubblico, senza rispecchiare righe private del planner o del calcolo percettivo.

### F3 — La roadmap contiene istruzioni di stato ormai contraddittorie

**Priorità: correggere prima del prossimo passaggio di consegne. Solo documentazione.**

L'intestazione di `ASTRA_ROADMAP.md:3` dice ancora Task 6–9 non iniziati; l'ultima sezione, circa riga 442, raccomanda di iniziare Task 6. Le schede successive registrano invece il lavoro effettivo. Le note «Task 2 non iniziato» dentro il resoconto storico del Task 1 vanno lette come storia, non stato attuale.

Un nuovo agent potrebbe duplicare lavoro seguendo il suggerimento finale. Occorre aggiornare sintesi iniziale e prossimo passo, preservando i risultati storici e il limite umano del Task 9. Questa review non modifica la roadmap.

## 6. Debito accettabile e rischi da non trasformare in refactoring preventivo

| Aspetto | Valutazione e momento per intervenire |
|---|---|
| ID del granaio/guardia e testi specifici nel client | Coerenti con una sola situazione. Generalizzare al secondo contenuto concreto. Nessun ID della slice aggiunto alle nuove regole generali del motore. |
| Preroll di 19:27 | Semplice, riproducibile, usa il vero scheduler. Non serve un bootstrap alternativo dello stato. |
| `VillageMap` partial e molti flag | Accoppiamento reale, ma ampiamente preesistente. I nuovi partial mantengono separabili i temi. Rifattorizzare tutto rallenterebbe senza risolvere F1 automaticamente. |
| Smoke come partial della stessa classe | Anche `PlayableSliceSmoke.cs` condivide campi e helper con altri smoke. È comodo ma aumenta la dipendenza dall'implementazione della UI. Accettabile ora; non considerarlo un collaudo esterno dell'app. |
| Campi transitori di attesa/rapporto/riepilogo | Necessari al feedback. Non costituiscono di per sé duplicazione delle regole; load e retry li azzerano. |
| Uscita da sovrapposizione nel planner | Mitigazione generale e delimitata al primo tratto in allontanamento. La causa, NPC che raggiunge un punto già occupato, resta. Prima di folle/compagni va affrontata l'occupazione; per questa prova lo smoke dimostra che si può uscire. |
| Conteggi visibili solo all'accesso | Piccola policy di presentazione ripetuta in etichetta e riepilogo. Tenerle coerenti; non serve un sistema di conoscenza dei contenitori adesso. |
| Accelerazione dipendente dai frame | Adeguata ai tempi misurati localmente. Profilare soltanto se emerge un problema su hardware target. |
| Tre icone pausa/ripresa/fine turno | Piccolo scostamento dal vincolo letterale «nessuna nuova icona» del Task 4. Non sono un'espansione artistica o architetturale significativa e non giustificano un rollback. |
| Launcher e tool locali | README dichiara SDK, cartella Godot e build iniziale. È una consegna da repository, non un prodotto esportato autonomo. Packaging multipiattaforma non richiesto qui. |

Non ho trovato un `QuestManager` mascherato, un timer che fabbrica il furto, un presidio scritto dal client o una UI che anticipa un rapporto completato. Le stringhe italiane negli assert verificano la presentazione; quelle verifiche sono fragili ai cambi di testo, ma non significano che il gameplay dipenda dal parsing delle frasi.

## 7. Task 1–9: dichiarato, verificato e limite

| Task e obiettivo | Risultato dichiarato | Valutazione su codice e prove attuali |
|---|---|---|
| **1. Ingresso e prova causale Sim** | Factory 19:27, due mondi comparabili, casi negativi, 648 test allora. | **Completato.** Test comportamentali rieseguiti nella suite da 651. Causalità al checkpoint comune, autonomia e filtro fuori vista verificati. I numeri storici della suite non sono quelli correnti. |
| **2. Scena dedicata in pausa** | Riusa VillageMap, intro, main invariata, Village e pilot avviabili. | **Completato nel perimetro tecnico.** Due avvii composti e retry coerenti; Village grafico passa; InteriorArtPilot avvia headless. Rendering del pilot e smoke ENTRY dedicato non rieseguiti separatamente in questa review. |
| **3. Attesa accelerata** | Stop su notizia/input, niente doppio clock, snapshot equivalente. | **Completato per i casi esercitati.** WAIT passa; nuova notizia, pausa, movimento, fuori vista ed equivalenza verificati. Gli altri stop sono ispezionati nel codice, non tutti riprodotti singolarmente. |
| **4. Guida e consegna** | Comandi leggibili, rifiuto innocuo, trasferimento 10→7 e 40→43. | **Parte funzionale completata.** GUIDE e catture confermano. Comprensibilità senza spiegazioni esterne rinviata alla verifica umana. Il vincolo letterale sulle icone ha lo scostamento minore descritto sopra. |
| **5. Notizia/rapporto/esito** | Topic solo dopo conoscenza, avviato distinto da completato, fonte e deduplica. | **Completato.** REPORT e composto passano; `ToldTo` guida l'esito, una sola origine ricevuta, niente presidio annunciato prematuramente. |
| **6. Conclusione dalla conoscenza** | Riepilogo corretto, presidio filtrato, conclusione esplicita e continuazione. | **Funzione principale verificata, con riserva F1.** Riepiloghi dei due rami corretti; termine/continuazione del ramo ignora esercitati. Nel ramo rapporto gli smoke verificano il riepilogo ma non premono anche Termina: quel passaggio usa lo stesso handler, non è una prova separata. Visibilità negativa del presidio da precisare in F2. |
| **7. Save/load/retry** | Save separato, cammino e rapporto ripresi, corrotto innocuo, due retry. | **Completato per i criteri circoscritti della scheda.** Tutti questi percorsi passano. Intenzioni composte, save a metà rapporto e caricamento arbitrario fra mappe non sono certificati. |
| **8. Smoke composto da avvio pulito** | Due factory reali, handler UI, catture, nessuna preparazione di save. | **Completato.** Due processi grafici riusciti a risoluzioni diverse; ciascuno esegue entrambi i rami con reload reale. Pulito significa stato di gioco iniziale, non macchina/checkout privi di cache. |
| **9. Avvio autonomo e collaudo umano** | Parte tecnica completa; playtest indipendente esplicitamente non svolto. | **Parziale.** README/launcher presenti e comando equivalente funziona; layout esaminato. Doppio clic del launcher, durata e comprensione di un nuovo giocatore non verificati qui. Non dichiarare 9/9 integralmente certificati. |

### Criteri globali ancora aperti

- **Non verificato:** una persona nuova percorre autonomamente il ramo rapporto in 5–10 minuti e incontra il primo evento entro circa due minuti. Gli 8,7 secondi dello script non misurano orientamento, lettura, tentativi o comprensione.
- **Verificato parzialmente:** leggibilità alle due risoluzioni mediante screenshot; non un collaudo manuale completo del picking, del focus, degli input e di tutte le combinazioni di pannelli.
- **Non ripetuto in questa review:** checkout nuovo con import/cache assenti e dipendenze da predisporre. Le precedenti note di worktree pulito sono evidenza storica, non un risultato prodotto in questa sessione.
- **Verificato per percorsi concreti, non universalmente:** possibilità di uscire dal combattimento da sovrapposizione e di riprovare. L'assenza di softlock in ogni deviazione libera non segue dal passaggio degli smoke.

## 8. Risultati effettivi di build, test e smoke

Ambiente: Windows, SDK **.NET 8.0.412**, Godot locale **4.7.2.stable.mono.official.ed1daf0bf**. Smoke grafici con **OpenGL / `gl_compatibility`**. Non ho effettuato restore, upgrade di dipendenze o modifiche per far passare le prove.

| Verifica eseguita | Esito osservato |
|---|---|
| `dotnet build RpgSandbox.sln --no-restore --verbosity minimal` | PASS, zero warning/errori. |
| `dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --verbosity minimal` | PASS, **651/651**, zero falliti/ignorati. |
| Composto `--slice-smoke`, 1280×720 | PASS, entrambi i rami; furto 19:33:12, osservazione circa 8,7 s da Comincia; ignora circa 8,9 s; guardia visibile 19:38:35, circa 19,7 s. |
| Composto `--slice-smoke`, 1920×1080 | PASS, entrambi i rami; osservazione circa 8,7 s; ignora circa 8,9 s; guardia circa 19,7 s. |
| `--slice-wait-smoke`, 1280×720 | PASS; osservazione circa **4,0 s dalla posizione già preparata**, 19:33:12 + 3 tick. Non confrontare direttamente questo tempo con quello da Comincia. |
| `--slice-guide-smoke`, 1280×720 | PASS; rifiuto da fuori portata, consegna di tre razioni, dialogo. |
| `--slice-report-smoke`, 1280×720 | PASS; nessun topic prematuro, avvio/completamento distinti, evidenza conservata e non duplicata. |
| `--slice-summary-smoke`, 1280×720 | PASS; nessuna notizia iniziale inventata, riepiloghi corretti, conclusione ignora, protezione da Spazio/hotbar fuori scontro. Non copre F1. |
| `--slice-persistence-smoke`, 1280×720 | PASS; save cammino alle 19:27:00 + 23 tick, save rapporto alle 19:38:23 + 35 tick, corrotto innocuo, save separato, due retry. |
| `--slice-combat-smoke`, 1280×720 | PASS; uscita dalla sovrapposizione dopo attacco, movimento e fine turno. Non prova bilanciamento o riepilogo in combattimento. |
| `--village-smoke`, Village, 1280×720 | PASS, `VILLAGE SMOKE OK`; regressione estesa di movimento, interazioni, viaggio, percezione e combattimento. |
| InteriorArtPilot, headless, 30 frame | Exit 0. Prima esecuzione in sandbox con errore sul certificate store; ripetizione autorizzata fuori sandbox senza quell'errore. Nessun errore applicativo osservato. Non è uno smoke grafico del pilot. |
| `git diff --check` | Pulito. Nessuna modifica al codice della review. |

I due composti e i sette smoke grafici aggiuntivi hanno terminato con exit code 0. Le piccole differenze di tick fra esecuzioni derivano dal clock a frame del client; gli assert causali non si basano sull'identità di quei tempi reali.

### Riproduzione dei comandi grafici

Da root, dopo la build:

```powershell
$astraGodot = 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $astraGodot --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/PlayableSlice.tscn -- --slice-smoke=C:/Progetti/rpg/.tmp/astra-final-review/composed-1280
& $astraGodot --rendering-method gl_compatibility --resolution 1920x1080 --path game res://Scenes/PlayableSlice.tscn -- --slice-smoke=C:/Progetti/rpg/.tmp/astra-final-review/composed-1920
$astraChecks = @('wait', 'guide', 'report', 'summary', 'persistence', 'combat')
foreach ($astraCheck in $astraChecks) {
    & $astraGodot --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/PlayableSlice.tscn -- "--slice-$astraCheck-smoke=C:/Progetti/rpg/.tmp/astra-final-review/$astraCheck"
    if ($LASTEXITCODE -ne 0) { throw "Smoke $astraCheck failed: $LASTEXITCODE" }
}
& $astraGodot --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/Village.tscn -- --village-smoke=C:/Progetti/rpg/.tmp/astra-final-review/village
& $astraGodot --headless --path game res://Scenes/InteriorArtPilot.tscn --quit-after 30 --log-file C:/Progetti/rpg/.tmp/astra-final-review/interior-boot.log
```

I percorsi di output sopra sono locali a questa macchina. Gli artefatti sono ignorati da Git in `.tmp/astra-final-review/`; non sono una dipendenza del progetto.

### Ispezione visiva e limiti degli smoke

Catture esaminate direttamente: ingresso, osservazione, rapporto e guardia al presidio a 1280×720; conclusione ignora e ripresa da save a 1920×1080; consegna a 1280×720. Diario, messaggi e controlli essenziali risultano leggibili nelle immagini controllate. A 1920×1080 alcuni elementi mantengono posizioni poco adattive e resta molto spazio vuoto; non impedisce il percorso documentato.

Gli smoke forniscono evidenza reale, con questi limiti:

1. Il composto invoca gli stessi handler della UI, ma spesso direttamente: `MoveTo`, `TalkTo`, `ClickDoor`, segnali `Pressed`. Non prova che un utente trovi il bersaglio cliccabile o che ogni bottone sia raggiungibile con mouse/tastiera. Un segnale emesso dal test può anche aggirare la normale disabilitazione del controllo; vari punti verificano lo stato del bottone, non tutti.
2. Gli smoke mirati possono ricreare la sessione o prepararla con comandi Sim e avanzamenti diretti. In particolare SUMMARY attende il presidio leggendo il mondo onnisciente nel test. Sono prove di componente, non falsificazioni del mondo né percorsi completi dalla schermata iniziale. Il composto è necessario proprio per coprire l'integrazione.
3. COMBAT accelera deliberatamente il ritmo del test e prepara alcuni stati di input: verifica una regressione specifica, non un'esperienza tattica completa.
4. Confronti di snapshot sono appropriati per determinismo, retry ed equivalenza temporale. Assert su campi privati, stringhe e gerarchia dei controlli sono invece legati all'implementazione. Non sostituiscono osservabili quali cibo trasferito, notizia acquisita, movimento completato e guardia fisicamente al posto, che fortunatamente sono presenti.
5. Attese a numero limitato di frame evitano molti loop infiniti, ma non sono un budget di prestazione verificato in secondi su hardware diverso. I tempi stampati sono misure di questa esecuzione, non SLA.

## 9. Regressioni e prossimo passo

Non ho riprodotto regressioni del loop principale o dello smoke Village. Il renderer del pilot non è stato ricollaudato graficamente. Il problema F1 nasce dall'integrazione del nuovo riepilogo con il comportamento preesistente del combattimento: è una lacuna nuova della funzione di conclusione, non la prova che il motore di combattimento sia diventato errato.

Le modifiche alla percezione adiacente e al planner hanno portata generale; il loro passaggio nella suite è positivo, ma richiedono i controesempi indicati in F2 prima di basarsi su garanzie più ampie. L'overlap NPC/player resta una limitazione nota, ora recuperabile nel caso provato. Le variazioni comuni di focus, targeting, label delle scorte e reset del load meritavano regressione anche fuori slice: lo smoke Village passato riduce questo rischio senza azzerarlo.

**Singolo prossimo intervento consigliato: isolare e correggere F1, il riepilogo durante il combattimento.** È piccolo, riguarda una promessa già fatta dalla UI e offre una verifica netta: nessuna azione o avanzamento dietro il riepilogo, ripresa corretta alla chiusura. Prima aggiungere la riproduzione comportamentale che manca, poi la protezione minima; nessun altro sistema necessario.

In seguito, precisare i pochi test di F2 e la sintesi della roadmap. Mantenere esplicitamente aperto il criterio umano del Task 9 fino a una prova con una persona che non conosca il percorso, usando `docs/PLAYABLE_SLICE_PLAYTEST.md`. Non occorre chiedere all'utente di ripetere adesso il giro già svolto. Le decisioni su un intervento armato completo o una prima situazione di Blackmarsh dovrebbero seguire quei risultati, senza ampliare nel frattempo la slice con quest, nuove mappe o infrastruttura.
