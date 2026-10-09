# Review Codex — checkpoint 0–3

Data: 9 ottobre 2026. Revisione del commit `f4f128e`, inclusi client Godot, simulazione, API, persistenza, test, contratto e proposte successive in `chat.txt`.

La base compila e i test passano, ma servono correzioni prima di considerare chiusi i checkpoint e iniziare nuove meccaniche. I problemi più immediati riguardano il pannello del client, la conclusione delle razzie e l'affidabilità dello smoke test. Non ho modificato codice di produzione: questo documento separa difetti riprodotti, limiti dichiarati e proposte ancora da concordare con Claude.

## Verifiche effettuate

- `dotnet build RpgSandbox.sln --no-restore --verbosity minimal`: riuscita, zero warning e zero errori.
- `dotnet test RpgSandbox.sln --no-restore --verbosity minimal`: **79/79**.
- Persistenza: 2.000 confronti fra avanzamento continuo e salvataggio/ricaricamento ogni sette minuti, con testimone attivo: stati serializzati identici.
- Avvio reale di Godot e smoke originale, più harness dedicato per crescita del pannello e caricamento durante un viaggio.
- Riproduzioni API per deposito svuotato durante una razzia, cancellazione delle attese, informazioni del PlayerView, overflow e snapshot incoerenti.

Gli harness sono artefatti locali di revisione, non test già integrati nella suite:

```powershell
dotnet run --project .review/domain/ReviewDomain.csproj
dotnet run --project .review/persistence/Review.csproj
```

Ulteriori prove client sono in `tools/review/client/` (directory ignorata da Git). Non sono stati modificati salvataggi utente. `IMPLEMENTATION_PLAN.md` e `docs/WORK_ALLOCATION.md` avevano già modifiche locali e sono stati preservati.

## Difetti nei percorsi validi

### F1 — P2: le opzioni di rapporto spingono i comandi fuori dalla finestra

Riferimenti: [Main.cs:609](../game/Scripts/Main.cs#L609), [Main.cs:632](../game/Scripts/Main.cs#L632).

Il pannello è un unico `VBoxContainer` senza scorrimento. Le opzioni di rapporto precedono Attendi, Salva, Carica e diario. Assistendo ai cinque furti fino al giorno 9 e tornando alla locanda la sera del giorno 10, le dieci opzioni disponibili rendono inaccessibili i comandi essenziali nella finestra 1280×720. Già cinque osservazioni tagliano il diario.

Prova: [screenshot dei comandi inaccessibili](../tools/review/client/7-inaccessible-save-controls.png), [diario tagliato](../tools/review/client/6-journal-overflow.png).

Correzione proposta: tenere fissi i comandi essenziali e assegnare spazio limitato con scorrimento a rapporti, diario e debug. Verificare anche ridimensionamento e risoluzione minima supportata.

### F2 — P2: una razzia non termina se il deposito viene svuotato durante il viaggio

Riferimenti: [Simulation.Npcs.cs:101](../src/RpgSandbox.Sim/Simulation.Npcs.cs#L101), [Simulation.Commands.cs:82](../src/RpgSandbox.Sim/Simulation.Commands.cs#L82).

Riproduzione con scenario standard e API pubblica: il giocatore raggiunge il granaio; alle 09:26, dopo l'ordine di razzia delle 09:00, avvia il prelievo di tutte le 40 razioni. Finisce alle 09:29; il razziatore arriva alle 09:30 e trova il deposito vuoto. `TakeFood` viene rifiutato prima di impostare `TakeAttempted`. La regola continua quindi a riprovare dopo ogni attesa di ripiego: alle 10:00 e alle 24:00 l'NPC è ancora al granaio con incarico Raid e la fazione è ancora in «Razzia in corso».

L'interfaccia giocatore non espone ancora il prelievo, ma l'azione è già pubblica e la competizione fra attori è un caso valido della simulazione.

Correzione proposta: definire l'esito di un tentativo impossibile e passare a rientro/chiusura dell'incarico. Una momentanea mancanza di risorse non deve mantenere indefinitamente l'ordine attivo. Test mirato: deposito svuotato durante il viaggio, incarico chiuso e fazione capace di valutare ordini successivi.

### F3 — P2: il client non riprende un'azione presente nel salvataggio

Riferimenti: [Main.cs:273](../game/Scripts/Main.cs#L273), [Main.cs:108](../game/Scripts/Main.cs#L108).

`LoadGame` sostituisce la sessione e aggiorna la vista, ma non ricostruisce `_runningAction` e lo stato di avanzamento del client. Con uno snapshot valido del viaggio Locanda → Granaio a 120 secondi, la simulazione contiene ancora Travel, mentre il client risulta libero. Dopo 60 frame il tempo è invariato; Attendi viene rifiutato perché l'attore è occupato. Serve il salto temporale debug per recuperare.

Limite della riproduzione: il pulsante Salva attuale è disabilitato durante le azioni; lo snapshot è stato prodotto tramite API e applicato nel client con la stessa assegnazione del percorso di caricamento. Il contratto supporta esplicitamente azioni in corso nei salvataggi. Evidenza: [runtime.log](../tools/review/client/runtime.log).

Correzione proposta: derivare l'azione pendente dalla sessione caricata, ripristinare una velocità coerente e azzerare gli accumulatori del playback. Test: caricare a metà viaggio e terminare il viaggio normalmente senza debug.

### F4 — P2: lo smoke dichiara successo anche dopo un fallimento

Riferimenti: [SmokeRunner.cs:53](../game/Scripts/SmokeRunner.cs#L53), [SmokeRunner.cs:69](../game/Scripts/SmokeRunner.cs#L69).

L'assenza di un'opzione di rapporto stampa `SMOKE FAIL`, ma l'esecuzione prosegue fino a `SMOKE OK` e `Quit(0)`. Mancano asserzioni sugli effetti principali della sequenza; anche gli errori degli screenshot vengono soltanto stampati.

Riprodotto eseguendo lo smoke originale: il clic sintetico non ha avviato il viaggio, lo screenshot successivo mostra ancora la locanda alle 10:00 con diario vuoto, il rapporto fallisce, ma il processo termina con successo. La causa del clic può dipendere da finestra/focus; il falso esito positivo dipende certamente dal test. Lo screenshot finale mostra scorte del granaio a 24 e guardia inattiva, quindi la deterrenza attesa non è avvenuta.

Prove: [stato dopo il presunto avvistamento](../tools/review/client/2-witnessed-player-view.png), [stato finale](../tools/review/client/5-day3-raid-deterred-debug.png).

Correzione proposta: fallire subito con codice diverso da zero, timeout sulle attese e asserzioni su arrivo, osservazione, rapporto, incarico di guardia e scorte protette. Il test deve distinguere l'interazione tramite clic dagli effetti della simulazione, così da indicare quale passaggio fallisce. I precedenti `SMOKE OK` da soli non certificano la sequenza.

### F5 — P2: PlayerView espone intenzioni e contenuti non conosciuti

Riferimento: [SimulationSession.cs:202](../src/RpgSandbox.Sim/Api/SimulationSession.cs#L202).

`VisibleActorView.Doing` copia direttamente `CurrentAction.Description`. Alle 09:31, con giocatore alla locanda e zero osservazioni, restituisce «Furto di razioni da Scorte del granaio» per il razziatore al granaio. In un secondo scenario espone anche l'argomento del rapporto di un altro NPC alla guardia. La visibilità per area è una semplificazione dichiarata; conoscere automaticamente il contenuto interno delle azioni è un problema distinto.

L'attuale `Main` non visualizza `Doing`: il difetto è nel confine pubblico dei dati e diventerebbe visibile appena il client usasse quel campo.

Correzione proposta: descrizioni esteriori per le attività visibili, contenuti delle conversazioni e interpretazione come furto solo attraverso percezione/conoscenza. Test sul PlayerView con conoscenze vuote e attori nella stessa area ma in luoghi diversi.

### F6 — P2: un'attesa cancellata viene dichiarata completata

Riferimenti: [Simulation.cs:58](../src/RpgSandbox.Sim/Simulation.cs#L58), [Simulation.Commands.cs:362](../src/RpgSandbox.Sim/Simulation.Commands.cs#L362).

La cancellazione lascia la scadenza nello scheduler. Quando questa viene estratta, `CompleteActionJob` la ignora correttamente, ma `completedAwaited` viene comunque impostato confrontando soltanto l'ID della scadenza.

Riproduzione: il giocatore assiste al furto alle 09:34 e torna alla locanda alle 09:39. Il suo rapporto termina alle 09:44, interrompendo l'attesa della guardia prevista fino alle 10:00. `AdvanceUntilCompleted` sull'ID di quella vecchia attesa restituisce però `Completed` alle 10:00, mentre la guardia sta svolgendo un'altra azione.

Correzione proposta: distinguere completamento effettivo, cancellazione e azione non pendente. Eliminare la scadenza cancellata oppure invalidarla esplicitamente; anche l'helper di avanzamento deve riconoscere la cessazione dell'azione senza attendere una scadenza che non la rappresenta più. L'equivalenza continuo/save-load non verifica questa semantica.

## Difetti con snapshot corrotti o manipolati

I quattro casi seguenti non sono stati osservati con snapshot prodotti normalmente. Sono lacune del contratto di caricamento: il file viene accettato oppure genera un'eccezione invece di restituire un errore gestito. Non implicano divergenze nella persistenza valida, che ha superato i confronti descritti sopra.

### F7 — P2: un'azione senza `kind` esce da TryLoad con un'eccezione

Riferimento: [SaveGame.cs:34](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L34).

Rimuovendo il discriminatore `kind` da un'azione non nulla, il deserializzatore tenta di istanziare `ActionDto` astratto e solleva `NotSupportedException`. Il blocco intercetta soltanto `JsonException`, quindi `TryLoad` non restituisce `LoadResult.Error`. Anche il client non gestisce questa eccezione nel caricamento.

Correzione proposta: validare il discriminatore o gestire questa specifica condizione di deserializzazione. Testare discriminatore assente, sconosciuto e valido per ogni variante.

### F8 — P2: una politica invalida può bloccare o arrestare la simulazione dopo il caricamento

Riferimenti: [SaveGame.cs:203](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L203), [SaveGame.cs:263](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L263).

- Impostare `Policy.EvaluationInterval = 0`: caricamento riuscito, poi `Advance(3h)` riprogramma la valutazione indefinitamente allo stesso istante.
- Impostare `Policy = null` mantenendo una scadenza `EvaluateFaction`: caricamento riuscito, poi `Advance(4h)` genera `NullReferenceException`.

Correzione proposta: riusare le invarianti dello scenario e verificare la compatibilità dei lavori pianificati con le politiche ripristinate. La validazione deve precedere l'uso della sessione.

### F9 — P2: l'assenza dei lavori periodici disattiva consumi e razzie

Riferimento: [SaveGame.cs:270](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L270).

Rimuovendo soltanto `FactionUpkeep` ed `EvaluateFaction` dallo scheduler, il caricamento riesce. Dopo quattro giorni non si verificano consumi né razzie: i lavori non vengono ricreati. La validazione controlla le scadenze delle azioni, non quelle periodiche richieste dalle fazioni.

Correzione proposta: verificare presenza, unicità e coerenza delle scadenze periodiche obbligatorie, inclusa `NextEvaluation`; rifiutare snapshot incompleti con errore leggibile.

### F10 — P2: gli ID duplicati delle azioni falsano AdvanceUntilCompleted

Riferimento: [SaveGame.cs:275](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L275).

Assegnando all'attesa del contadino l'ID del viaggio del giocatore e aggiornando la relativa scadenza, lo snapshot viene accettato. `AdvanceUntilCompleted` sul viaggio restituisce `Completed` quando termina prima l'attesa del contadino; il giocatore è ancora in movimento.

Correzione proposta: unicità degli ID attivi e corrispondenza univoca fra ogni azione e il suo completamento. Tenere distinto questo controllo dagli eventi residui delle cancellazioni legittime, finché questi ultimi sono previsti dal formato corrente.

## Caso limite aggiuntivo

### F11 — P3: il trasferimento di razioni può produrre quantità negative per overflow

Riferimenti: [Simulation.Commands.cs:192](../src/RpgSandbox.Sim/Simulation.Commands.cs#L192), [Simulation.Commands.cs:217](../src/RpgSandbox.Sim/Simulation.Commands.cs#L217).

Uno scenario valido con deposito a `int.MaxValue` e giocatore con una razione accetta il deposito di quella razione: il risultato è deposito a `-2147483648`, giocatore a zero. Anche il prelievo usa una somma senza controllo. È un caso estremo, non riscontrato durante il normale scenario.

Correzione proposta: controllare la capacità numerica prima del trasferimento e garantire che un errore non applichi soltanto una metà della transazione; rivalidare al completamento se altri attori possono cambiare le quantità nel frattempo.

## Risposte ai punti R1–R7 di Claude

| Punto | Valutazione |
| --- | --- |
| R1 — attese cancellate | Da correggere ora: F6 dimostra un errore osservabile nell'API, anche se il determinismo resta valido. La rimozione o invalidazione deve avere semantica esplicita. |
| R2 — interruzione nei completamenti | Va bene decidere nella fase NPC dello stesso istante, purché ogni nuova azione abbia scadenza futura e le fasi restino ordinate. Non ho riprodotto una catena infinita su uno scenario valido; non serve aggiungere artificialmente un secondo di ritardo. |
| R3 — scorte nemiche note | Semplificazione accettabile per la slice già dichiarata. Prima di usare scarsità e furto come gameplay, distinguere stime/conoscenze dalle scorte reali: altrimenti le reazioni aggirano l'informazione che vogliamo far contare. |
| R4 — identità da presenza iniziale | Accettabile soltanto come regola provvisoria. La presenza non prova attenzione, vista o riconoscimento. Il prossimo modello deve separare accorgersi dell'evento, riconoscere l'attore e attribuire il furto. |
| R5 — visibilità per area | Semplificazione dichiarata; F5 resta un difetto distinto. Durante Travel l'API sceglie inoltre immediatamente l'area di destinazione: partendo per il campo rende visibili le sue scorte prima dell'arrivo. Propongo di mantenere l'ultima area osservata o rappresentare esplicitamente il tratto di viaggio. |
| R6 — catch NullReference | Preferisco validazioni esplicite dei DTO e delle invarianti condivise con lo scenario. Un catch ampio non deve trasformare errori di programmazione in generici salvataggi invalidi. F7–F10 indicano i controlli concreti mancanti. |
| R7 — debug predefinito | Propongo vista giocatore all'avvio e debug esplicito via F1/argomento. È una modifica di impostazione del prototipo, non un difetto nascosto. Lo smoke deve scegliere esplicitamente la vista necessaria. |

Documentazione da riallineare dopo i fix: il contratto conserva un riferimento allo schema 1 oltre allo schema 2; l'interrompibilità di Wait deve includere percezione e rapporto, oltre agli ordini di fazione.

## Proposte per le prossime scelte C1–C5

Queste sono proposte di Codex da discutere con Claude; non costituiscono un accordo già raggiunto. La scelta SRD 5.2.1/2024 dell'utente è acquisita.

**C1 — ordine.** Prima chiudere i difetti della review. Poi propongo una porzione piccola della tappa 5: «Parla con…» a guardia e contadino, stesso fatto noto, due conseguenze diverse e osservabili, mantenendo il costo temporale. Risponde subito alla richiesta dell'utente di interlocutori utili. Dopo, tappa 4 con rischio del furto. Fiducia generale, menzogne, nuovo locandiere e scheda estesa possono arrivare dopo questa verifica giocabile. Motivazione: introdurre insieme RNG, percezione, notte, identità e confisca allargherebbe molto il prossimo checkpoint senza risolvere l'interazione già richiesta.

**C2 — riconoscimento.** Non sceglierei né la sola presenza dall'inizio né un margine arbitrario del tiro. Servono informazioni osservabili: occasione di vedere il volto/attore, familiarità e condizioni sensoriali. Un tiro può contribuire, ma non deve inventare identità mai percepite. Conservare distintamente «ho visto sparire le razioni», «ho visto qualcuno» e «ho riconosciuto X».

**C3 — una prova per tre minuti.** Una sola prova memorizzata per tentativo è una buona scelta applicativa per evitare nuovi tiri a ogni frame o testimone. Non equivale però a un successo garantito per tutta la durata: nuove condizioni possono rendere visibile l'attore. Distinguere l'azione di nascondersi dal prelievo; salvare risultato e stato del tentativo in corso.

Precisazioni sullo SRD: Hide non è limitato al combattimento; richiede CD 15, oscuramento pesante o copertura adeguata e assenza dalla linea di vista nemica. Il totale diventa la CD per essere trovati; le condizioni previste possono terminare il nascondimento. La Percezione passiva prevede anche +5/−5 per vantaggio/svantaggio. Influence ammette per un nuovo tentativo un'attesa di 24 ore **o una durata stabilita dal GM**. Il buio limita la vista, non ogni senso, e richiede di considerare illuminazione e capacità sensoriali. Non basta «è notte» per garantire anonimato. Fonte: [SRD 5.2.1 ufficiale](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf), voci Hide, Influence, Passive Perception e visione. La procedura per il furto di tre minuti va documentata come adattamento del gioco.

**C4 — confisca.** Può bastare come prima conseguenza senza un punteggio universale di reputazione, purché la guardia debba ricevere informazioni, raggiungere il giocatore e interagire. Prima di chiamarla «restituzione delle razioni rubate» occorre decidere come gestire provenienza e cibo già consumato/depositato: la quantità del furto non coincide necessariamente con quella ancora posseduta. Evitare recuperi automatici da depositi remoti. Un primo comportamento può richiedere restituzione fino alla quantità effettivamente posseduta, con esito esplicito se insufficiente.

**C5 — RNG.** Per questa scala propongo un solo flusso per la simulazione, algoritmo e stato salvati con versione; casualità grafica separata. Fra le due opzioni propendo per SplitMix64 per mantenere contenuto lo stato, senza attribuirgli vantaggi di gameplay rispetto a PCG32. Vettori noti, conversione uniforme agli intervalli e identità continuo/save-load sono criteri d'uscita. Eviterei un test statistico probabilistico come gate della build: i controlli deterministici devono individuare gli errori senza fallimenti casuali. Flussi per sottosistema solo quando esiste un'esigenza concreta di indipendenza.

## Ripartizione proposta per chiudere la review

- **Codex, client:** F1, F3, F4 e impostazione debug; aggiornamento del client se cambia il contratto di avanzamento o PlayerView.
- **Claude, simulazione/API/persistenza:** F2, F5–F11, con test di regressione mirati e aggiornamento del contratto. F11 è meno urgente e può essere una correzione separata.
- Concordare prima soltanto la semantica di cancellazione/esito dell'avanzamento e i dati necessari per riprendere un'azione caricata. Poi i due gruppi possono procedere in parallelo senza modificare gli stessi file.
- Verifica finale con suite e smoke corretto, includendo salvataggio a metà azione e sessione lunga con molte conoscenze. Solo dopo dichiarare chiusi i checkpoint 0–3 e scegliere il prossimo incremento.

Non ho iniziato le correzioni o le tappe 4–5 durante questa review, né attribuito a Claude approvazioni che non ha ancora espresso.

## Aggiornamento dopo il confronto con Claude

Il 9 ottobre Claude ha accettato tutti i rilievi, la ripartizione e l'ordine fix → T5 ridotta → T4. Ha comunicato le correzioni indipendenti F2, F7, F8, F9, F11 e R6; Codex ha rieseguito la suite: **91/91 test passano**. Questo aggiornamento non sostituisce la verifica integrata finale dei checkpoint.

Codex ha confermato in `chat.txt` le proposte S1–S4: rimozione delle scadenze cancellate e validazione 1:1 nello schema v3; esito `Cancelled` distinto da `Completed` e `NotPending`; ripresa client tramite `PlayerView.Action`; descrizioni esteriori nello stesso luogo e area di partenza durante il viaggio. I salvataggi v2 saranno rifiutati con errore esplicito, senza modifica dei file esistenti.

Precisazioni per l'implementazione: verificare anche le cancellazioni di eventi già estratti nel lotto dello stesso istante; due posizioni nulle durante il viaggio non costituiscono lo stesso luogo; il client deve azzerare il playback precedente al caricamento. Restano da completare F5/F6/F10, le correzioni client F1/F3/F4/R7 e lo smoke con asserzioni attendibili. L'accordo è ora confermato; le sezioni precedenti conservano il contesto della review iniziale.
