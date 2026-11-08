# Blackmarsh: la prima piccola regione persistente

Analisi originaria del 29 ottobre 2026. Base esaminata: commit `d4c9465`. **Alla data dell'analisi non era stata eseguita alcuna implementazione regionale.** Le due vertical slice sono considerate concluse; questo incremento costruisce gioco continuativo sui loro risultati.

**Aggiornamento 8 novembre 2026:** Task 1 e 2 implementati e approvati; Task 3 implementato nel commit `da59622` e approvato da Claude. Task 4 implementato da Codex e approvato da Claude. Task 5 implementato e verificato da Codex, pronto per review. Task 6 implementato da Claude e approvato nella review indipendente di Codex; dettagli e limiti sotto. Task 7–10 non avviati. Il testo di analisi sottostante conserva la diagnosi originale; risultati e review sono registrati sotto i rispettivi task.

## 1. Decisione

Costruire **l'isola della vedetta, esagono 0912**, come un unico mondo con **tre piccole mappe collegate**: il nucleo dei pescatori, la rimessa già giocabile e una caletta con il deposito dei Vasan. Si percorrono sentieri; non occorre una barca. Il giocatore può partire dal nucleo abitato, scegliere una destinazione, cambiare piano, tornare, salvare, chiudere e continuare la stessa partita.

Il nuovo elemento di gioco è **la distanza tra opportunità, persone e conseguenze**. Mentre esploro la caletta, qualcuno può raggiungere la rimessa; mentre torno a raccontare un fatto, il suo autore può rientrare a casa. Le porte, le provviste, gli oggetti, la conoscenza e le persone ritrovate non ripartono dallo stato iniziale.

Non propongo tre scenari caricati uno dopo l'altro. Propongo **un solo scenario regionale, una sola `SimulationSession`, un solo orologio e un salvataggio dell'intero mondo**. Il client presenta di volta in volta una delle mappe. I vecchi scenari restano riproducibili separatamente come regressioni.

È il momento giusto: interni, interazione con i depositi, percezione e autonomia locale sono già sufficienti. La prova mancante coincide adesso con il prodotto desiderato: abitare e rivisitare un piccolo mondo. Un'altra situazione isolata non risolverebbe il limite più importante del client.

**Limite della promessa:** 20–30 minuti di gioco libero interessante, con possibilità di continuare e riprendere; non un'economia capace di generare attività nuove per settimane. La persistenza conserva anche la fine di un conflitto e l'esaurimento di una risorsa.

## 2. Cosa ho verificato nel repository

### Fonti e stato

- Letti roadmap e documentazione delle slice, `docs/BLACKMARSH_WATCHHOUSE.md`, decisioni architetturali, scenario JSON, factory, implementazioni Sim/client, test pertinenti e storia recente.
- `git status`: nessuna modifica tracciata all'inizio; otto asset/import `Body.*` e texture in `game/Scenes/` già non tracciati. Non sono una dipendenza della proposta e non sono stati modificati.
- Le modifiche recenti includono timer delle azioni (`bcd2072`), valutazione del difensore e interruzione del prelievo a deposito svuotato (`23ba662`), collisione con occupante non visibile e valutazione conseguente (`1313303`). La proposta considera questi comportamenti attuali, non soltanto il resoconto iniziale della rimessa.
- La vecchia roadmap descrive anche stati storici ormai superati. Per esempio il loader legge già `obstacles`; il manifest registra già Blackmarsh; l'autonomia del predone è stata ampliata dopo la prima misurazione dei due rami.

### Inventario delle capacità, con decisione di riuso

| Ambito | Evidenza attuale | Conseguenza per la regione |
|---|---|---|
| Più aree e mappe | `WorldState.cs`, `Scenarios/ScenarioBuilder.cs`, `ScenarioFile.cs`: collezioni di aree, luoghi, mappe, uscite e percorsi; JSON con più elementi `maps`. | Riutilizzo diretto. Nessun nuovo formato di mondo o loader per collegare aree. |
| Viaggio | `Simulation.Commands.cs`, `Simulation.Travel.cs`: partenza da un'uscita, durata, destinazione, passo, arrivo all'uscita della mappa di destinazione. | Esiste già il trasferimento Sim fra mappe. Mancano composizione verificata e presentazione client completa. |
| Spostamento NPC | `Simulation.Going.cs`, `Simulation.Npcs.cs`: percorsi verso posti di lavoro, depositi e persone; scelta di uscite raggiungibili; cammino locale e viaggio. | Riutilizzare routine e compiti. Correggere la lacuna nel costo regionale descritta sotto, non introdurre un planner nuovo. |
| Tempo fuori schermo | `Simulation.cs`: scheduler globale; azioni, fazioni, contatti, routine e scontri non dipendono dalla mappa renderizzata. | Tenere tutte e tre le mappe nella Sim. Nessuna simulazione semplificata o caricamento a distanza. |
| Persistenza | `Persistence/SaveGame.cs`, schema 43: tutte le mappe, attori, azioni, scheduler, conoscenze, inventari, fazioni, porte, vetri, esplorazione, oggetti e trappole. Contenuti incorporati con identità e hash verificati. | Il salvataggio del mondo esiste. Adattare il contratto del client e l'ingresso nella partita, non costruire una seconda persistenza. |
| Percezione e informazioni | `Api/SimulationSession.cs`, `PlayerView.cs`, conoscenze e opzioni di rapporto. | Usare le viste filtrate. Verificare esplicitamente il transito e l'isolamento tra mappe. |
| Fazioni e risposta sociale | `Simulation.Factions.cs`, `Simulation.Npcs.cs` e relativi moduli: consumo, razzie, rapporto, confisca/restituzione, presidio, ostilità. | Due fazioni bastano. Collegare fisicamente deposito, testimone e autorità; niente quest o reputazione a punteggio. |
| Edifici e interni | `VillageMap.Buildings.cs`, `InteriorArt.cs`, `Presentation.cs`, edge/indoor/ostacoli nella Sim. La rimessa già usa il client comune. | Aggiungere solo profili per area e altri piccoli edifici rettangolari. |
| Esplorazione e equipaggiamento | JSON `ground`, serrature, finestre, effrazione, torce, inventario, pickup/drop, armi e munizioni già presenti. Esistono anche trappole e relativi comandi. | Un'opportunità di equipaggiamento può essere contenuto puro. Le trappole non diventano obbligatorie solo perché disponibili. |
| Riposo e bisogni | `Simulation.Needs.cs`, riposi, acqua nei luoghi, razioni, esaurimento, giorno/notte; viaggio prolungato. | Usare acqua, luce e riposo esistenti. Fame e marcia forzata non sono pressioni credibili su tre sentieri brevi. |
| Contenitori ed economia | `Store.Contents` contiene oggetti generici, ma il loop di trasferimento/razzia e il pannello giocabile sono centrati sulle razioni; esistono denaro e inventario. | Non confondere questi dati con un negozio o un sistema completo di casse saccheggiabili. Per l'equipaggiamento usare oggetti a terra. |
| Produzione | Le routine di lavoro portano al posto di lavoro e attendono; il lavoro non produce pesce o razioni. | Niente promessa di economia rinnovabile. Scorte finite dichiarate e consumi calibrati. |

### Verifica eseguita in questa analisi

Comando dalla radice, senza modifica di codice:

```powershell
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --filter "FullyQualifiedName~TravelTests|FullyQualifiedName~TravelPaceTests|FullyQualifiedName~MapLifeTests|FullyQualifiedName~NpcRoutineTests|FullyQualifiedName~SaveLoadTests|FullyQualifiedName~BlackmarshWatchhouseTests|FullyQualifiedName~CombatWorldTests" --verbosity minimal
```

**94 superati, 0 falliti, 0 ignorati**; compilazione dei progetti Sim/test riuscita. Non sono stati eseguiti in questa analisi nuovi smoke Godot o un playtest regionale: la regione non esiste ancora. Questi test confermano componenti esistenti, non il loro futuro assemblaggio. In particolare i casi di viaggio/routine letti in `MapLifeTests` e `NpcRoutineTests` collegano una mappa a un luogo astratto non mappato: non bastano a certificare tre mappe complete nel client.

## 3. Limiti concreti da affrontare

### A. Il client costruisce una sola mappa

`VillageMap._Ready()` crea la sessione, sceglie un profilo e chiama `BuildMap()`/`BuildR5Map()`. `Refresh()` gestisce presenza/assenza del giocatore sulla mappa, ma non ricostruisce il mondo disegnato quando cambia `AreaId`. `_builtMap`, pavimento, tetti, porte, picking, token, trappole, luci e diversi dizionari per `GridPos` appartengono alla geometria costruita all'avvio.

Serve un ciclo esplicito di **rimozione e costruzione della presentazione locale**. Conservare sessione e HUD; liberare nodi e riferimenti della vecchia area, selezionare il profilo della nuova, costruire dalla vista Sim e ricentrare la camera. Anche un caricamento nella stessa area deve invalidare lo stato visivo precedente. Non basta cambiare `_builtMap` o nascondere il pavimento.

Il nome `VillageMap` può restare. Non serve riscrivere il client in un framework di scene o creare `BlackmarshMap` copiando la classe.

### B. Le uscite non offrono ancora una scelta completa

`BeginExitJourney()` in `VillageMap.cs` esegue la partenza solo quando `exit.Routes.Count == 1`; con più percorsi mostra un messaggio senza offrire la scelta. `ReturnToVillage()` è ancora fissato a `MappedVillageScenario.Ids.ForestRoad`. Esiste già il pannello di scelta del passo in `VillageMap.R5.cs`.

Occorre collegare destinazioni effettive dell'uscita, scelta del passo e comando esistente. Nella regione nessun ritorno speciale al villaggio: si torna scegliendo un percorso valido. Prima della partenza si annulla la scelta; durante il viaggio pausa e avanzamento rispettano la semantica Sim esistente. Non aggiungere un teletrasporto «torna indietro».

### C. Il costo usato dalle razzie non normalizza l'origine mappata

`Simulation.Factions.DecideRaid()` valuta `JourneyCost(home.Location, target.Location)`. In `Simulation.Going.cs`, `JourneyCost()` espande il **bersaglio** nelle uscite della sua mappa, ma passa l'**origine** direttamente a `PathCost()`. Il Dijkstra in `Simulation.Npcs.cs` segue esclusivamente `World.Routes`.

Se il deposito di casa è dentro la caletta e il suo `LocationId` è diverso dal luogo di uscita, nessun arco parte da quel deposito: una razzia verso un'altra mappa risulta senza costo/percorso. `StepTowards()` gestisce già l'uscita da una mappa, ma quella capacità non viene raggiunta se la fazione non assegna il compito.

È una lacuna dedotta direttamente dal codice, **non una nuova regressione riprodotta durante questa analisi**. Primo intervento futuro: test comportamentale con due mappe e depositi interni, poi correzione minima del calcolo tra uscite compatibili. Non rinominare il deposito come uscita, non aggiungere rotte fittizie attraverso i muri, non assegnare manualmente la razzia per far passare il test. Conservare i casi di origine/destinazione non mappata e il costo zero nella stessa mappa.

Per questa regione bastano collegamenti diretti tra le tre aree e un'uscita per area. Non serve risolvere ora un navigatore mondiale capace di attraversare automaticamente una catena di mappe con molte uscite interne.

### D. Viaggiare non equivale a vedere l'area di partenza

In `SimulationSession.GetPlayerView()`, durante `TravelAction`, `Area` può essere l'area d'origine e `Map` può ancora contenerne la geometria, mentre `Position` è nulla. Il fallback `VisibleByPlace()` e quello dei depositi possono inoltre selezionare contenuti dell'area d'origine in base alla luce del luogo.

Questa semantica proviene anche dal prototipo a luoghi astratti. **Non usare `Map != null` come prova che il giocatore sia fisicamente sulla mappa**, e non trasformare la schermata di viaggio in una finestra live su persone/depositi remoti. Isolare il caso con test negativi; per viaggi fra mappe correggere nella vista Sim l'eventuale esposizione di stato remoto, preservando i contratti legacy verificati. Nascondere soltanto i token non basta se pannelli e log continuano a leggere quelle informazioni.

Le uscite e i nomi dei tre sentieri possono essere conoscenza geografica iniziale esplicita. Non implicano conoscenza di occupanti, provviste o incidenti. Chiamare la destinazione «Caletta», non «Predone attualmente assente».

### E. Il caricamento protegge la vecchia geometria, non una regione

`VillageMap.Persistence.cs:SaveMatchesBuiltScene()` confronta identità di attori/aree/depositi e la mappa caricata con `_builtMap`. Un salvataggio regionale in un'altra mappa verrebbe confrontato con quella sbagliata. La scrittura temporanea seguita da `File.Move` esiste già e va conservata.

Il salvataggio Sim incorpora e verifica il content pack: **non manca un sistema di versionamento dei contenuti**. Manca una piccola identità dell'esperienza/presentazione nel client: lo stesso pack contiene più scenari, quindi il suo hash non identifica la partita attiva. Per la nuova partita usare un contenitore client minimo con ID della regione e revisione della presentazione più l'intero snapshot Sim, lasciando alla Sim la propria validazione. Non duplicare lì attori, mappe o inventari. Rifiutare revisioni non supportate con messaggio comprensibile; nessuna migrazione generalizzata richiesta in questo incremento.

### F. Altri limiti da verificare senza anticipare grandi sistemi

- L'arrivo mappato chiama `Place()` sull'uscita; quel metodo non cerca una casella alternativa occupata. Verificare arrivi simultanei e uscita occupata: non lasciare sovrapposizioni permanenti, attori bloccati o combattimenti non comandabili. Se serve una regola di attesa/posizionamento, deve stare nella Sim ed essere minima e deterministica. Non teletrasportare gli NPC nel client.
- La politica delle razzie sceglie fra depositi e scorte globali: è una semplificazione Sim già presente, non un sistema di scoperta dei bersagli. Accettabile con tre depositi noti nella piccola isola; non usarla per promettere ricognizione strategica o rivelare scorte remote alla UI.
- `SizeUp` si attiva nel contesto dell'accesso al deposito: non equivale a una nuova pattuglia aggressiva che attacca chiunque entri nella caletta. Il contenuto e i testi devono rispettarlo.
- Un unico accesso stretto è accettabile per una cassa, ma più NPC e nuovi arrivi richiedono prove di contesa. Non estendere un hack di occupazione per tenere in piedi la scena.

## 4. La regione proposta: «L'isola della vedetta»

### Fonte e adattamento

`docs/BLACKMARSH_WATCHHOUSE.md` conserva già nel repository la sintesi attribuita di *Blackmarsh* v11, esagono 0912. Nell'archivio locale `docs/black_marsh.zip`, il testo descrive pescatori incaricati della sorveglianza contro i Vasan e una torre lignea a sud che segnala verso Castle Blackmarsh. È stato ricontrollato il paragrafo nella copia estratta `.tmp/blackmarsh-analysis/blackmarsh-v11.txt`.

La rimessa, la caletta, i tre piccoli luoghi e i loro abitanti giocabili sono **adattamenti originali del progetto**. Rappresentiamo un frammento dell'isola, non tutti i suoi circa cento abitanti. Castle Blackmarsh, il faro 0712 e le altre località restano fuori dal perimetro. Nessun effetto di allarme a distanza associato alla torre: la relativa meccanica non esiste. Riutilizzare attribuzione e politica delle immagini già stabilite in `CREDITS.md`.

### Geografia e budget

```text
                  Nucleo dei pescatori
                  /                  \
         6 minuti/                    \12 minuti
                /                      \
          Rimessa ------- 8 minuti ----- Caletta
```

Durate iniziali **proposte, in tempo Sim a passo normale**, da misurare e tarare. Sono tragitti fra piccoli punti d'interesse, non una scala ufficiale dell'esagono. Tutti i collegamenti sono percorribili nei due sensi. Un'uscita ai margini di ciascuna mappa rappresenta il bivio dei sentieri: offre due destinazioni. Ogni percorso diretto costa meno del giro attraverso il terzo luogo; non occorre transito automatico in un'area intermedia.

Budget: tre mappe circa 18×14–24×18, quattro personaggi totali iniziali (**giocatore, custode/autorità, pescatore testimone, predone**), due fazioni, tre depositi. Tre edifici compatti a un piano: rimessa, casa comune, capanno alla caletta. Nessun altro interno obbligatorio.

| Luogo | Cosa si fa | Perché tornarci |
|---|---|---|
| Nucleo dei pescatori | Ci si orienta, si usa il pozzo, si incontrano custode e pescatore quando presenti, si riposa e si consegnano razioni al deposito comune. La casa è un riparo, non un servizio di locanda da implementare. | Raccontare fatti effettivamente conosciuti; ritrovare scorte e persone dopo le proprie decisioni. L'autorità può essersi spostata. |
| Rimessa | Si riusa l'interno, si controllano/trasferiscono scorte, si osservano ingressi e comportamento del predone, si decide se intervenire o allontanarsi. | Riscontrare porte o vetri cambiati, scorte mancanti, presenza del lavoratore o presidio successivo. Il vecchio approdo/deposito Vasan viene trasferito nella caletta della versione regionale. |
| Caletta | Si può raggiungere il deposito rivale, osservare il rientro del predone, sottrarre provviste tramite le regole esistenti oppure esplorare un piccolo capanno laterale. | Recuperare ciò che è arrivato dal furto, cambiare equipaggiamento, verificare se l'area è ancora occupata. Non ripopolare o riempire automaticamente il luogo. |

Il pescatore vive al nucleo e lavora alla rimessa: attraversa realmente il percorso. Il custode è l'autorità della fazione dei pescatori e parte dal nucleo. Il predone ha deposito e casa alla caletta. Le condizioni di rapporto, risposta e razzia restano quelle del motore. Non aggiungere un accompagnatore per far vedere le routine.

### Un'opportunità diversa dal trasferimento del cibo

Nel capanno laterale della caletta mettere **un arco corto e una piccola quantità di frecce**, oggetti già definiti in `game/content/srd/items.json`, a terra. È un'opzione di equipaggiamento diversa dal corredo iniziale, non una ricompensa attribuita da una quest. Il capanno ha una barriera da valutare e un ingresso alternativo ottenibile con le regole attuali; nessun tiro singolo fallito deve chiudere l'unico percorso di ritorno. Usare luce e geometria per invitare a guardare dentro, senza un indicatore onnisciente di bottino.

Non serve una cassa generica, il saccheggio dei cadaveri, una trappola nuova o un oggetto magico. Il predone non deve ricevere un comportamento aggiuntivo di «difendi l'arco». Se il contenuto risulta ornamentale nel playtest, modificare collocazione, accessi e utilità dell'equipaggiamento prima di introdurre un nuovo sistema.

### Avvio e pressione temporale

Un nuovo mondo parte al nucleo, prima del momento critico della sera, con tutte le persone già presenti nel mondo e nessun evento attivato dalla visita del giocatore. Come ipotesi iniziale usare circa **19:35**, consumo Vasan e valutazione serali già vicini alle **20:00**, e un turno del pescatore che gli consenta sia di raggiungere sia di lasciare la rimessa. Sono valori di contenuto da verificare, non nuovi vincoli hardcoded.

Il giocatore deve poter completare almeno un tragitto con un'interazione senza conoscere un orario segreto. I due ordini di visita devono restare interessanti anche se il furto è già avvenuto. L'evento non aspetta il giocatore e la partita non fallisce perché ha scelto prima la caletta.

Scorte, consumo e quantità delle razzie vanno dimensionati per la prima sera e per un ritorno il mattino successivo. La scorta del nucleo impedisce che l'unica cassa esaurita elimini immediatamente ogni scelta, ma non deve cancellare ogni conseguenza. Non svuotare lo zaino del protagonista solo per costringerlo a partecipare; non attribuire alla fame una rilevanza che le regole giornaliere non le danno.

## 5. Il loop giocabile e la durata attesa

**Orientarsi → scegliere un luogo → viaggiare mentre il mondo avanza → esplorare/interagire → decidere cosa portare o raccontare → tornare e osservare conseguenze → preparare una nuova uscita o salvare.**

Un'apertura breve descrive un'appartenenza e una situazione: sei sull'isola dei pescatori, esistono una rimessa comune e una caletta raggiungibile, le provviste e la sorveglianza contano. Non assegna una sequenza di obiettivi né svela dove si trova ora il predone. Le informazioni geografiche note possono stare nella schermata dei percorsi; i fatti accaduti appartengono al diario filtrato.

Tre itinerari di collaudo, **non tre missioni né tre finali programmati**:

1. **Occuparsi della rimessa.** Partire verso la rimessa, osservare o proteggere le scorte, eventualmente portarle al nucleo, raccontare solo quanto appreso. La pressione può spostarsi sull'altro deposito o interrompersi secondo le regole della fazione. Non garantire una vittoria o una guardia teletrasportata.
2. **Approfittare della distanza.** Andare prima alla caletta, esplorare il capanno e decidere se toccare il deposito Vasan. Nel frattempo la rimessa cambia o resta intatta per ragioni effettive. Tornare passando dalla rimessa, distinguere ciò che si osserva da ciò che non si è visto accadere, scegliere se recuperare/trasferire provviste.
3. **Seguire informazioni e conseguenze.** Arrivare dopo un evento, incontrare una persona che sa qualcosa oppure trovare segni materiali; recarsi all'autorità soltanto con conoscenze effettive. Attendere, riposare o allontanarsi e verificare il successivo spostamento dei personaggi. Il sistema non deve inventare un colpevole a partire da una cassa vuota.

Sono esiti validi scorte conservate o trasferite, razzia riuscita e bottino recuperabile, predone respinto, guardia mobilitata dopo un rapporto, conflitto risolto con la forza o lasciato aperto. Non sono flag salvati dal client. Se un intervento risolve definitivamente il conflitto, il mondo resta in quello stato: esplorare e riprendere resta possibile, non ricomincia una prova.

### Da dove devono venire 20–30 minuti reali

| Attività nella prima sessione | Budget orientativo reale |
|---|---|
| Orientamento, prima scelta, incontro con il nucleo | 2–4 minuti |
| Esplorazione di rimessa e accessi, prima interazione significativa | 4–6 minuti |
| Caletta, capanno, inventario e scelta del rischio | 5–8 minuti |
| Rientro, rapporto/trasferimento, riscontro delle conseguenze | 4–6 minuti |
| Una rivisita, riposo o cambio di piano, salvataggio | 3–5 minuti |

È un'ipotesi di densità, **non una durata misurata né una sequenza obbligatoria**. I viaggi e le azioni lunghe devono poter avanzare rapidamente tramite lo scheduler; non contare venti minuti di attesa come venti minuti di gioco. Non obbligare a combattere, a ripetere il medesimo trasferimento o a vedere tutti gli esiti. Se il mondo è già esaurito dopo dieci minuti, la milestone di gioco non è raggiunta anche se tutti i test passano: prima migliorare alternative spaziali, opportunità e chiarezza dei tre luoghi.

## 6. Edifici: quanto generalizzare adesso

- **Riutilizzo diretto:** confini sottili, porte, finestre, serrature, luce, indoor, ostacoli, tetto e avvicinamento alle aperture. Il mobile occupa spazio perché lo dichiara la Sim, non perché collide una mesh Godot.
- **Generalizzazione necessaria:** `PresentationFor` deve selezionare anche per area, non solo per `SceneEntry`. Pavimenti, props, etichette e camera sono locali alla mappa; l'aspetto di uno stesso attore deve restare coerente quando viaggia. Le coordinate da sole non identificano un oggetto fra mappe diverse.
- **Authoring minimo:** dati scenario per ogni pianta; profilo di presentazione piccolo per ogni area. Una zona interna distinta per ciascun edificio separato. I tetti attuali usano limiti geometrici per zona: scegliere edifici rettangolari compatibili, senza assegnare la stessa zona a edifici disgiunti.
- **Da completare nel contenuto:** casa comune e capanno, pochi arredi utili, aperture selezionabili, uscita leggibile, tetti corretti entrando/uscendo e dopo il ritorno da un'altra mappa. Riutilizzare gli asset del pilot.
- **Da rinviare:** editor, catalogo di prefab parametrico, generazione di case, tetti arbitrari, piani sovrapposti, scene separate per ogni interno, scale, distruzione strutturale, città completa, kit artistico definitivo.

Due case nuove non giustificano un sistema di costruzione urbana. Il secondo livello di riuso necessario è il ciclo di vita della mappa, non un modello universale di edificio.

## 7. Persistenza e tempo: contratto di prodotto

1. **Nuova partita:** crea l'intera regione una volta. Non fonde i due vecchi scenari e non importa il protagonista da due salvataggi. Un solo ID giocatore nel nuovo scenario.
2. **Transizione:** emette `TravelCommand`; durante l'azione avanza il medesimo mondo. All'arrivo cambia la presentazione. Mai richiamare la factory per costruire l'area visitata.
3. **Fuori schermo:** tutti gli NPC continuano tramite la Sim. Pausa e un turno che attende input del giocatore fermano il tempo secondo il contratto esistente. Non simulare di nascosto un secondo orologio durante i turni.
4. **Salva/Continua:** un solo slot regionale separato dalle slice è sufficiente. Conservare lo snapshot completo e supportare ripresa da ogni area, da un viaggio e da un'azione in corso. Riutilizzare la scrittura temporanea già presente. Caricare prima in una sessione candidata, validare, poi sostituire quella attiva.
5. **Applicazione chiusa:** il tempo non passa. Non servono eventi offline o sincronizzazione con l'orologio del computer.
6. **Nuovo mondo ≠ caricamento:** comando distinto con conferma prima di sostituire la partita. Niente `Retry` per area, niente reset automatico dopo una razzia. Morte: caricare un salvataggio oppure iniziare esplicitamente un nuovo mondo, senza resurrezione implicita.
7. **Persistenza minima:** salvataggio manuale con riscontro chiaro e «Salva ed esci»; «Continua» riapre lo slot valido. Autosalvataggi multipli, elenco campagne e cloud non sono necessari ora. Non sovrascrivere una partita valida durante un avvio o caricamento fallito.

`project.godot` avvia ancora `Village.tscn`; `gioca-blackmarsh.cmd` avvia la situazione isolata. Alla consegna il percorso principale deve aprire la nuova partita/continuazione regionale senza dover conoscere un nome di scena. Le vecchie entrate restano strumenti di regressione.

## 8. UI/UX da rivedere trasversalmente

Questa volta servono interventi comuni, non altre istruzioni specifiche per la rimessa:

| Priorità | Revisione concreta | Vincolo |
|---|---|---|
| Necessaria per navigare | Nome dell'area corrente, uscite cliccabili, destinazioni e durata prevista, stato di viaggio, arrivo riconoscibile. Un elenco di tre luoghi basta; nessuna mappa a esagoni. | Nomi/percorsi sono conoscenza geografica dichiarata; nessun contatore di nemici o cibo remoto. Durate dal contratto Sim, senza ricalcolo di regole nel client. |
| Necessaria per continuare | Nuova partita, Continua, Salva, Salva ed esci; errori leggibili; caricamento ripristina area e attività. | Nessun reset implicito o salvataggio nello slot di una slice. |
| Necessaria per capire il tempo | Un unico stato coerente per pausa, attività, attesa accelerata, viaggio e turno. Timer attuali riutilizzati; Stop/interruzione disponibili soltanto secondo la Sim. | Estrarre l'uso dell'attesa dall'introduzione/riepilogo della slice: oggi `BuildSliceWaitUi` costruisce anche il riepilogo. Niente avanzamento a salti che perda contatti o turni. |
| Necessaria per interagire | Coerenza fra clic, avvicinamento, pannello deposito, apertura, conversazione e oggetti a terra. Chiudere selezioni e comandi pendenti cambiando area. | Niente continuazione di un comando su una porta con le stesse coordinate nell'altra mappa. |
| Necessaria per il sandbox | Diario esistente e rapporti distinguono osservato, raccontato e ignoto; un deposito ricordato non è un sensore remoto. | `WorldView` resta debug/struttura statica dove già necessario; nessun nuovo pannello di stato globale. Niente diario delle quest. |
| Miglioramento mirato | Gerarchia di pannelli, log meno invasivo, scorciatoie riconoscibili, gestione input sotto finestre, leggibilità a 1280×720 e 1920×1080. | Correggere attriti osservati, non rifare tema grafico, inventario o intero HUD. |

I riepiloghi «prova conclusa», il bottone «Riprova» e gli obiettivi fissati al checkpoint sono strumenti delle slice. Non abilitarli nella regione per ottenere gratuitamente altri pannelli. Un eventuale resoconto deve provenire dalle conoscenze e non dichiarare una vittoria globale.

## 9. Roadmap incrementale assegnabile

I percorsi citati sotto sono relativi alla radice. I nuovi file sono proposti, salvo quelli indicati nei risultati dei task eseguiti. Ogni task deve registrare comando di verifica, risultato reale e limiti in questo documento. Alla progettazione iniziale nessuna fase era completata; il 3 novembre è stato implementato solo il Task 1. Non accorpare automaticamente i task in una riscrittura del client.

### Task 1 — Rendere affidabile il tragitto Sim fra due mappe

**Obiettivo:** consentire a giocatore e predone di lasciare una mappa, raggiungere un deposito interno a un'altra e tornare, con le stesse regole locali.

**File/sistemi:** nuovo `tests/RpgSandbox.Sim.Tests/RegionalTravelTests.cs`; `Simulation.Going.cs`, `Simulation.Commands.cs`, `Simulation.Movement.cs`, `Api/SimulationSession.cs` solo per correzioni dimostrate.

**Dipendenze:** nessuna. Usare piccoli scenari di test con ID estranei a Village/Blackmarsh, due mappe reali, uscite e depositi con ID distinti; nessun nuovo contenuto grafico.

**Verifica:** partenza possibile solo dall'uscita; arrivo e ritorno corretti; la politica assegna spontaneamente la razzia e il predone consegna il bottino; origine/destinazione non mappata non regrediscono. Aggiungere casi negativi di contenuto remoto visibile durante il viaggio, stessa coordinata in aree diverse e arrivo occupato. Le eventuali correzioni restano Sim, precedute dal test che fallisce.

**Completato quando:** i comportamenti pubblici sono verificati, nessuna assegnazione/posizione/scorta viene forzata dopo l'avvio per ottenere l'esito, e non servono ID di scenario nel motore. Documentare ogni regola minima introdotta per l'arrivo occupato; non nascondere la difficoltà disponendo soltanto uscite sempre vuote.

**Reasoning:** alto; è il task da implementare per primo. Riduce il rischio di costruire una regione bella ma immobile.

#### Risultato effettivo del Task 1 — 3 novembre 2026

**Stato: implementato, verifiche automatiche superate, approvato da Claude. Task 2 non avviato.** Base Git `d4c9465`; alla consegna le modifiche erano nel working tree. Claude le ha successivamente registrate nel commit `8d47e0c`, con nota di review in `41db58f`.

File dell'intervento:

- `tests/RpgSandbox.Sim.Tests/RegionalTravelTests.cs`: **16 casi** (12 metodi, incluse due theory). Solo builder iniziale, comandi pubblici, avanzamento, viste e save/load; nessun accesso a `Engine`, modifica di JSON salvato o assegnazione manuale di un compito.
- `Simulation.Going.cs`: `JourneyCost` considera le uscite di entrambe le mappe, mantenendo il caso non mappato e il costo zero nella stessa mappa. Il percorso locale continua a essere deciso da `StepTowards` e dal pathfinding esistente.
- `Api/SimulationSession.cs`: durante un viaggio con almeno un estremo mappato non espone attori e depositi remoti tramite `VisibleActors`/`VisibleStores`. Conserva il comportamento legacy dei percorsi interamente tra luoghi astratti. `Map`/`Area` possono ancora descrivere l'origine come contesto geografico: non significano presenza fisica.
- `Simulation.Commands.cs`, `Simulation.Travel.cs`: arrivo senza sovrapposizione, rinvio dell'arrivo se lo spazio manca, conteggio della sola marcia effettiva. Nessun cambio dello schema save (resta 43), del client o dei file di contenuto.

**Difetti riprodotti prima della correzione:** prima esecuzione dei 10 casi iniziali: 5 fallimenti e 5 successi. Fallivano due combinazioni della razzia con origine mappata, la visibilità di persone in transito, la visibilità dei depositi in transito e l'arrivo su un residente. Due ulteriori test di arrivo simultaneo/ingresso completamente occupato fallivano anch'essi prima della modifica. La nuova attesa ha poi rivelato un effetto collaterale isolato con due test: veniva conteggiata come marcia, anche oltre mezzanotte. Corretto prima della consegna.

**Regola minima d'arrivo adottata (adattamento del progetto, nella Sim):**

1. Usare la casella d'uscita se percorribile, aperta e senza attori.
2. Se occupata, provare soltanto le caselle immediatamente adiacenti, in ordine stabile riga/colonna. Escludere muri, porte chiuse, angoli non attraversabili e angoli occupati. Non cercare un posto lontano nella mappa e non spostare il residente.
3. Se non c'è spazio, il viaggiatore resta fuori mappa con la medesima azione/ID. La prossima scadenza è fra un secondo Sim; nessun `TravelCompleted` prematuro. Lo scheduler, gli altri attori e il limite di `AdvanceUntilCompleted` continuano a funzionare. Alla liberazione dell'accesso l'arrivo si completa una sola volta.
4. L'attesa e il rinvio sono conservati dal normale snapshot dell'azione e dello scheduler. La durata di marcia si ricava dal percorso e dal passo originali; il tempo trascorso aspettando spazio non aumenta marcia forzata/esaurimento.

| Criterio | Esito osservato |
|---|---|
| Partenza solo dall'uscita, arrivo, accesso al deposito interno, ritorno | Verificato tramite comandi del giocatore; due razioni prelevate rimangono nello zaino al ritorno e il deposito di destinazione resta modificato. |
| Razzia autonoma tra due mappe | Verificato: compito assegnato dalla politica, viaggio, furto delle quattro razioni e consegna al deposito interno di origine; compito concluso e cibo conservato. |
| Estremi non mappati | Verificate tutte e quattro le combinazioni mappato/non mappato della razzia, compreso il percorso interamente astratto. |
| Save/load di un NPC in viaggio | Verificato per le quattro combinazioni: stessa continuazione e snapshot finale uguale senza ripreparare il mondo. |
| Nessuna vista live remota durante il viaggio | Verificati attori e depositi inizialmente visibili che scompaiono dalla vista in transito. Nessuna opzione di rapporto o oggetto a terra remoto. |
| Coordinate coincidenti in mappe diverse | Verificati esclusione dalla vista, rifiuto del prelievo remoto e furto realmente avvenuto nell'altra mappa che non diventa osservazione/rapporto/evento del giocatore. Il furto locale fra depositi della stessa mappa continua a funzionare. |
| Uscita occupata e arrivi simultanei | Verificati posizionamento distinto e successivo movimento del giocatore. |
| Accesso completamente occupato | Verificati permanenza in transito, limite temporale rispettato, load durante attesa, arrivo unico quando il giocatore occupante parte. |
| Barriere intorno all'uscita | Verificato che l'attesa non venga aggirata piazzando il viaggiatore attraverso un muro o una porta chiusa. |
| Avanzamento frazionato e load | Verificata uguaglianza dello snapshot per un arrivo occupato con avanzamento in blocco rispetto a passi di un secondo. |
| Attesa e marcia giornaliera | Verificato che un tragitto di due minuti rimanga tale dopo l'attesa e che l'attesa oltre mezzanotte non aggiunga marcia al giorno successivo; verificata anche la ripresa da save. |

Verifiche finali sul codice consegnato:

```powershell
dotnet build RpgSandbox.sln --no-restore --verbosity minimal
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-build --no-restore --verbosity minimal
git -c safe.directory=C:/Progetti/rpg diff --check
```

**Build solution (Sim, test, client Godot): 0 warning, 0 errori. Suite completa: 696 superati, 0 falliti, 0 ignorati (680 preesistenti + 16 regionali). Diff check senza errori.** Nessuno smoke grafico o playtest regionale eseguito: questo task riguarda il contratto Sim, la regione e il cambio mappa nel client sono ancora da implementare.

**Limiti espliciti per la review e i prossimi task:** un ingresso senza alcuno spazio libero resta bloccato finché il mondo non ne libera uno; non è stata introdotta un'azione nuova per annullare il viaggio, né un teletrasporto di ritorno. Il controllo ogni secondo è una soluzione locale per piccoli scenari, non un sistema di code di folla. L'ordine degli arrivi simultanei è quello deterministico dello scheduler. Questa regola gestisce gli arrivi, non riscrive le collisioni generali di tutti i camminatori. Il costo di fazione rimane una stima sui percorsi fra uscite: non aggiunge il costo del cammino interno né un pianificatore multi-area nuovo. L'interfaccia dell'attesa all'ingresso e il rendering della mappa d'arrivo restano non verificati e appartengono ai task client successivi.

#### Review di Claude — 3 novembre, recepita il 4 novembre 2026

**Esito: APPROVATO, nessuna correzione bloccante richiesta.** Fonte: ultimo messaggio di `chat.txt` nel commit `41db58f`. Le verifiche seguenti sono quelle dichiarate da Claude nel suo worktree pulito; non sono state rieseguite durante questo aggiornamento documentale:

- Sim **696/696**, build client **0 warning / 0 errori**.
- Smoke OpenGL 1280×720 verdi: Blackmarsh entry/store/building/flow/persistence/composed; slice, entry, combat, guide, persistence, report, summary, wait; art pilot, prototipo e villaggio.
- Contro la Sim originale `d4c9465`, con i soli nuovi test, **10 dei 16 casi falliscono**: confermata la capacità dei test di rilevare i difetti. Confermato anche l'uso esclusivo di builder, comandi, viste e save/load, senza preparazioni tramite `Engine`.
- Approvati costo fra uscite, vista in transito, arrivo/attesa con ID stabile e conteggio della marcia. Verificato che l'attesa non venga interpretata come annullamento da `AdvanceUntilCompleted`.

Promemoria non bloccanti accolti:

1. **Task 3–4, presentazione dell'attesa all'ingresso:** il timer generico mostrerebbe un secondo residuo ripetuto e barra piena. Mostrare uno stato comprensibile, per esempio «Aspetti di poter entrare», basato su un contratto Sim esplicito da definire durante quel task. Non inferire lo stato da un timer che sembra fermo.
2. **Limite accettato per la piccola isola:** l'NPC in attesa non ridecide e riprova ogni secondo; rinuncia e percorsi alternativi restano fuori da questo incremento finché non servono a un caso concreto.
3. **Occupazione:** anche i corpi a terra impediscono l'arrivo sulla loro casella. Tenerne conto nell'authoring e nelle verifiche successive.

L'approvazione di Claude rende il Task 2 tecnicamente avviabile. Questo aggiornamento registra soltanto la sua review: nessuna modifica al codice e nessun task successivo iniziato. La review indipendente di Codex è registrata sotto.

#### Review indipendente di Codex — 4 novembre 2026

**Esito: APPROVATO per il Task 1; Task 2 può partire.** Riletti il commit `8d47e0c`, le modifiche Sim, i 16 casi regionali e la review di Claude. Build completa: 0 warning, 0 errori. Dopo la build, suite Sim completa: **696/696**. Un primo `--no-build` aveva eseguito DLL precedenti e dato 10 fallimenti; quel risultato è stato scartato e non indica una regressione del codice attuale. La verifica indipendente conferma costo fra uscite, isolamento della vista durante il viaggio mappato, arrivo senza sovrapposizione, attesa con ID stabile e conteggio della marcia limitato al percorso.

**Follow-up non bloccante, rilevato per lettura del codice e non riprodotto da un test:** un viaggio lungo che termina esattamente a mezzanotte può saltare il conteggio dell'ultimo tratto utile alla marcia forzata: la fase 1 completa l'azione, `TravelAtArrival` usa la mezzanotte appena iniziata e la fase 2 `EndOfDay` non trova più il viaggio attivo. È un confine preesistente al Task 1 e non riguarda i sentieri brevi della regione. Aggiungere un test mirato quando si torna sul conteggio della marcia. Rimane da presentare chiaramente nel client l'attesa all'ingresso, come indicato da Claude per i Task 3–4.

### Task 2 — Definire il mondo unico e i tre luoghi reali

**Obiettivo:** caricare una regione completa nei dati, con geografia e identità stabili, ancora presentabile con asset esistenti.

**File/sistemi:** nuovo `game/content/scenarios/blackmarsh-watch-island.json`, `game/content/manifest.json`, nuova factory `Scenarios/BlackmarshWatchIsland.cs`, nuovi test di contenuto e `docs/BLACKMARSH_REGION.md`.

**Dipendenze:** Task 1.

**Verifica:** tre mappe e tre collegamenti bidirezionali, un giocatore, quattro attori totali, tre depositi, due fazioni; nessun duplicato del predone o del deposito trasferito alla caletta. Riutilizzare la pianta della rimessa nella nuova definizione senza modificare il vecchio scenario. Casa comune semplice e spazio riservato al capanno; NPC con casa/lavoro/autorità dichiarati. Percorrere tutti i collegamenti attraverso comandi Sim, senza teletrasporti.

**Completato quando:** il nuovo mondo si crea in modo deterministico, consente il giro completo e resta valido salvandolo/caricandolo nella Sim. Fonte/adattamenti, ID, scorte e orari sono documentati; i vecchi scenari continuano a caricarsi. Il seed non sostituisce la verifica di ordini di visita differenti.

**Reasoning:** medio; lavoro prevalentemente di contenuto e verifica.

#### Risultato effettivo del Task 2 — 4 novembre 2026 (Claude)

**Stato: implementato, verifiche automatiche superate, approvato nella review indipendente di Codex.** Contratto in `chat.txt` (commit `543ec16`) prima del codice. Nessun cambio di regole Sim, dello schema save (resta 43) o del client.

File:

- `game/content/scenarios/blackmarsh-watch-island.json` (id `blackmarsh-watch-island`), registrato in `game/content/manifest.json`. Tre aree con mappa (nucleo 20×14, rimessa 18×14, caletta 20×14), un bivio d'uscita sul bordo di ciascuna, tre sentieri bidirezionali (6, 8, 12 minuti). Prefisso `bm_isle_` per tutti gli ID; si riusa solo la chiave `bm-watch-shed-key`.
- `src/RpgSandbox.Sim/Scenarios/BlackmarshWatchIsland.cs`: `CreateSession()` (mondo dalla mezzanotte, avanzato fino alle 19:35) e `Ids`.
- `tests/RpgSandbox.Sim.Tests/BlackmarshWatchIslandTests.cs`: **11 casi** (9 metodi, due theory).
- `docs/BLACKMARSH_REGION.md`: fonte e adattamenti, piante, ID, persone, fazioni, scorte e cronologia misurata della prima sera.

Scelte di contenuto da conoscere:

1. **Rimessa:** stessa pianta del vecchio scenario (righe, muri, porte, serratura, finestra, lampada, tavolo). Cambiano solo le zone: il sentiero diventa il bivio, esteso al bordo perché il motore vuole l'uscita sul bordo; l'approdo diventa terreno libero. Un test confronta le due definizioni.
2. **Un posto per tipo per luogo:** custode e pescatore nella stessa casa starebbero sulla stessa casella. Il custode ha quindi il posto «casa» al Piazzale del pozzo (acqua, braciere), il pescatore nella casa comune.
3. **Caletta:** l'Approdo dei Vasan con le provviste; lo spazio x 11–16, y 1–5 resta libero per il capanno del Task 7. Nessun arco ora.

Prima sera senza input, osservata dai test: il pescatore va al lavoro alle 16:00 per il sentiero e rientra alle 19:51; i Vasan consumano alle 19:59; alle 20:00 il predone parte da solo verso la rimessa (8 contro 12 minuti), scassina, ruba le 4 razioni e alle ~20:24 le consegna alla caletta.

| Criterio | Esito |
|---|---|
| Tre mappe, tre collegamenti bidirezionali, un giocatore, quattro attori, tre depositi, due fazioni | Verificato; nessun ID `bm_watch_` fra attori e depositi; provviste Vasan alla caletta. |
| Casa, lavoro e autorità dichiarati | Verificati: casa del pescatore, del custode e del predone; autorità dei pescatori = custode; turno del pescatore percorso davvero. |
| Riuso della pianta della rimessa senza toccare il vecchio scenario | Verificato per confronto delle definizioni; `blackmarsh-watchhouse.json` invariato e i suoi test passano. |
| Giro completo tramite comandi, nei due ordini | Verificati nucleo→caletta→rimessa→nucleo e nucleo→rimessa→caletta→nucleo; partenza rifiutata a un passo dall'uscita (`NotAtExit`). |
| Creazione deterministica, save/load | Due creazioni con snapshot identico; save/load all'apertura, in viaggio e su un'altra mappa con snapshot finale uguale alla continuazione. |
| Razzia autonoma | Verificato che parte da sola verso la rimessa. |

Verifiche, in worktree pulito: Sim **707/707** (696 + 11), build client 0 warning / 0 errori, smoke Godot OpenGL 1280×720 di regressione tutti verdi (Blackmarsh entry/store/building/flow/persistence/composed; slice, entry, combat, guide, persistence, report, summary, wait; art pilot, prototipo, villaggio).

**Limiti:** nessuna scena o profilo client per la regione (Task 3). Durate e orari sono valori di contenuto non tarati sul gioco (Task 6 e 10). Il custode di notte resta al pozzo all'aperto. Se il pescatore e il predone si incrociano alla rimessa, l'esito non è stato provato: oggi il pescatore riparte alle 19:45, prima della razzia.

#### Review indipendente di Codex — 4 novembre 2026

**Esito: APPROVATO per il Task 2; Task 3 può partire.** Controllati commit `b97c75b`, scenario, manifest, factory, confronto con la rimessa, test e documento. Build completa con 0 warning/0 errori; suite Sim completa **707/707** dopo la build. Gli smoke Godot dichiarati da Claude non sono stati ripetuti in questa review. Risultato registrato anche in `chat.txt`.

**Copertura da completare nel Task 6:** i test regionali asseriscono che il predone riceva la razzia verso la rimessa, non ancora `FoodStolen`, `FoodDeposited`, quantità finali e ritorno alla caletta. La cronologia del furto e della consegna descritta nel documento resta da proteggere con asserzioni dirette nel task dedicato alla circolazione di persone e scorte. Non blocca la costruzione del client.

### Task 3 — Ricostruire la presentazione quando cambia l'area

**Obiettivo:** il client mostra la mappa dove si trova effettivamente il giocatore, mantenendo la stessa sessione.

**File/sistemi:** `VillageMap.cs`, `VillageMap.Presentation.cs`, `Buildings.cs`, `Visuals.cs`, `R5.cs` e moduli con nodi/cache locali; nuova scena `game/Scenes/BlackmarshRegion.tscn` collegata all'entry regionale.

**Dipendenze:** Task 2.

**Verifica:** costruzione/rimozione del solo albero locale; profilo scelto per area; token, luci, tetti, porte, picking, memoria visiva, trappole e props non contaminano la successiva. Testare due mappe con coordinate coincidenti e porte in stati differenti. Camera unica e attori con aspetto coerente dopo il cambio. Sessione non ricreata.

**Completato quando:** un giro e un ritorno ripetuti non duplicano nodi/handler e mostrano le geometrie/stati corretti, anche tornando in una stanza già esplorata. La casa comune riusa il renderer della rimessa; nessun renderer regionale duplicato. Lo smoke può guidare comandi pubblici, non scrivere lo stato della Sim.

**Reasoning:** alto per il ciclo di vita; implementazione circoscritta, nessun refactoring generale.

#### Esito implementazione — 4 novembre 2026, Codex

**Stato: implementato e verificato nel perimetro del Task 3; review indipendente ancora da svolgere.** Base: `b97c75b`, con la review del Task 2 già presente nelle modifiche locali alla roadmap e a `chat.txt`. Preservati tali aggiornamenti e gli asset `Body.*` non tracciati. Nessuna modifica a Sim, scenario, test Sim, schema save o scena principale del progetto.

**Cambiamenti:**

- `game/Scenes/BlackmarshRegion.tscn` apre la factory regionale alle 19:35 usando il client comune, con `SceneEntry.BlackmarshRegion`. Nessun renderer separato.
- `VillageMap.MapPresentation.cs` raccoglie pavimenti, edifici, tetti, visione, token/modelli, trappole, proiettili e overlay in un unico nodo `MapPresentation`. Il cambio di `AreaId` rimuove e libera il vecchio albero, svuota le cache locali e costruisce quello della mappa corrente. Camera e CanvasLayer/HUD restano gli stessi. Gli handler UI vengono creati all'avvio, non a ogni cambio mappa.
- Durante il viaggio l'intero albero locale è nascosto anche quando `PlayerView.Map` descrive ancora l'origine. Selezioni e intenzioni locali vengono cancellate alla partenza/arrivo. Un load accettato che sostituisce la sessione invalida il rendering anche se l'area ha lo stesso ID.
- `VillageMap.Presentation.cs` seleziona il profilo per area: casa comune in legno, rimessa con pianta e tavolo esistenti, approdo senza edifici aggiunti. Pozzo e focolare sono semplici blocchi sugli ostacoli già nei dati, senza asset nuovi. Le identità regionali degli NPC mantengono lo stesso aspetto nelle diverse mappe.
- Le modifiche ai partial `Buildings`, `Combat`, `FreeMovement`, `R5`, `Shots`, `Slice`, `Vision`, `Visuals` e `Barriers` riguardano la proprietà dei nodi locali; i controlli UI restano esterni all'albero sostituibile. `InteriorArt` riusa il renderer esistente per i due segnaposto.
- `VillageMap.RegionPresentationSmoke.cs` guida il giocatore attraverso comandi pubblici. La variante `--map-presentation-fixture` crea un piccolo mondo iniziale con `ScenarioBuilder`, senza alterare snapshot o stato di una Sim in esecuzione. Non è contenuto della regione.

| Criterio | Evidenza osservata |
|---|---|
| Avvio regionale e profili per area | Avvio alle 19:35 nel nucleo; attraversate tutte e tre le mappe, comprese dimensioni 20×14 e 18×14; acquisizioni separate. |
| Stessa sessione, camera e HUD | Asserzioni di identità per l'intero giro regionale; ogni viaggio avanza l'orologio e conserva la sessione. La sola variante fixture sostituisce esplicitamente la sessione nell'ultima prova di load. |
| Giri e ritorni ripetuti | Tre giri, nove cambi area: nucleo→rimessa→caletta→nucleo, poi ordine inverso, poi primo ordine. Dopo ogni arrivo il vecchio root non è più valido, esistono un solo root locale e una sola camera. |
| Geometrie e cache locali | Caletta priva dei tetti/porte/props della rimessa; indici delle porte corrispondenti alla nuova vista, celle nei limiti della nuova mappa. Gli alberi precedenti, inclusi i modelli 3D, sono liberati. Nessun nuovo collegamento di handler durante la ricostruzione, verificato anche per lettura del codice. |
| Porte/finestre nelle stesse coordinate | Fixture: porta e finestra chiuse nella prima area, aperte nella seconda; disegno e picking della porta verificati. Al ritorno la prima porta resta chiusa sia nella vista sia nel disegno. |
| Luce, trappole e oggetti | Fixture: luce più debole nella seconda area; fossa realmente scoperta con ricerca e giavellotto a terra nella prima non compaiono nella seconda; ricompaiono al ritorno e dopo load nella prima. |
| Pulizia delle interazioni e transito | Menu di una trappola nota aperto tramite il normale handler prima della partenza, poi chiuso con selezione cancellata. In transito root invisibile, posizione nulla, pannello fuori mappa visibile. |
| Interni, memoria e NPC | Cutaway della casa comune, tavolo visibile nella rimessa e memoria di celle esplorate al ritorno. Modello del pescatore verificato alla rimessa e poi a casa con lo stesso stile. Nessun ramo di rendering duplicato per la casa. |
| Load accettato sulla stessa area | Vecchio root liberato e marcatori locali ricostruiti. Passa anche la regressione di persistenza della vecchia rimessa. Non equivale a Continua regionale da un'altra area. |

**Risultati effettivamente eseguiti:**

- `dotnet build RpgSandbox.sln --no-restore --verbosity minimal`: **0 warning, 0 errori**.
- `dotnet test RpgSandbox.sln --no-restore --verbosity minimal`: **707/707**, 0 falliti, 0 ignorati.
- Godot 4.7.2 mono, OpenGL compatibility, **1280×720**, exit code **0** per tutti: smoke regionale reale; fixture coincidente; `--blackmarsh-composed-smoke`; `--blackmarsh-persistence-smoke`; `--slice-smoke`; `--village-smoke`.
- Controllate visivamente le immagini `common-house.png`, `shed-interior.png`, `hamlet-return.png` e `fixture-second.png`. Casa, rimessa e modello del pescatore leggibili; nessun residuo della mappa precedente nelle immagini controllate. I blocchi del pozzo/focolare restano segnaposto.
- `git diff --check`: nessun errore.

Riproduzione dalla radice del repository, dopo la build:

```powershell
$godotExe = 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/BlackmarshRegion.tscn -- --region-presentation-smoke=C:/Progetti/rpg/.tmp/region-task3/island
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/BlackmarshRegion.tscn -- --region-presentation-smoke=C:/Progetti/rpg/.tmp/region-task3/fixture --map-presentation-fixture
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/BlackmarshWatchhouse.tscn -- --blackmarsh-composed-smoke=C:/Progetti/rpg/.tmp/region-task3/regression-blackmarsh
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/BlackmarshWatchhouse.tscn -- --blackmarsh-persistence-smoke=C:/Progetti/rpg/.tmp/region-task3/regression-persistence
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/PlayableSlice.tscn -- --slice-smoke=C:/Progetti/rpg/.tmp/region-task3/regression-slice
& $godotExe --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/Village.tscn -- --village-smoke=C:/Progetti/rpg/.tmp/region-task3/regression-village
```

**Assunzioni corrette durante il collaudo:** la porta della casa può essere già aperta all'avvio dopo il preroll del pescatore e può venire riaperta dal suo rientro. Due prime esecuzioni hanno segnalato questi errori nelle aspettative dello smoke. Il mondo non è stato modificato per congelare gli NPC: il caso reale verifica la corrispondenza fra stato osservato e disegno, mentre la fixture senza NPC verifica separatamente la conservazione della porta chiusa. Le successive esecuzioni sono verdi.

**Limiti e consegna al Task 4:** la regione non è ancora percorribile liberamente da UI: `BeginExitJourney` gestisce ancora una sola destinazione, e i bivi regionali ne hanno due. Lo smoke usa `TravelCommand`, non dimostra il percorso UI completo. Scelta del tragitto, input in transito e messaggio esplicito per ingresso occupato restano al Task 4. Il percorso save è separato (`user://blackmarsh-region.json`) per non usare gli slot delle slice, ma il contratto di load rimane quello precedente basato sulla mappa costruita: Continua da qualunque area/viaggio, identità regionale e riavvio del processo restano al Task 5. Non verificati in questa consegna: playtest umano, 1920×1080, profiling prolungato delle risorse native. La liberazione dei nodi è asserita; non viene presentata come misura completa della memoria GPU. Nessun Task 4–10 implementato.

### Task 4 — Rendere giocabile la scelta del tragitto

**Obiettivo:** percorrere liberamente tutti e tre i luoghi usando soltanto UI e input normali.

**File/sistemi:** `VillageMap.cs:BeginExitJourney`, `VillageMap.R5.cs`, UI del viaggio e della regione; smoke dedicato.

**Dipendenze:** Task 3.

**Verifica:** scelta fra due destinazioni dall'uscita, passo/durata, annullamento prima di partire, stato durante il viaggio, arrivo; ritorni senza `ForestRoad`. Nessuna informazione dinamica della mappa lasciata; nessun clic sulla sua geometria nascosta. La durata deve usare il contratto Sim/preview esistente, estendendolo minimamente se manca un dato necessario.

**Follow-up review Task 1:** verificare anche l'ingresso occupato, con uno stato di attesa leggibile al posto del conto alla rovescia «0:01» ripetuto; definire l'eventuale dato aggiuntivo nella vista Sim prima di collegarlo al client.

**Completato quando:** da avvio pulito il giocatore può fare nucleo → caletta → rimessa → nucleo senza console e senza cambiare scena manualmente; anche l'ordine inverso funziona. Il tempo globale avanza normalmente. Stato di transito riconosciuto da viaggio/posizione, non da `Map == null`.

**Reasoning:** medio. **Prima consegna giocabile:** luoghi collegati con azioni locali e ritorno effettivo, senza attendere il completamento della roadmap.

**Risultato Task 4 (8 novembre 2026, pronto per review):** il clic sull'uscita conduce alla sua casella e apre la scelta dei due sentieri regionali; la destinazione mostra la durata a passo normale, poi tre andature mostrano ciascuna la durata prevista. «Altre destinazioni», «Annulla» ed Esc consentono di tornare indietro senza partire. La conferma usa `TravelCommand` e la rotta singola delle scene precedenti resta raggiungibile. Durante il transito la mappa è nascosta, gli attori e i depositi remoti non vengono mostrati, i clic sulla geometria precedente sono ignorati e la pausa ferma il tempo. All'arrivo la mappa di destinazione ricompare. `TravelView.WaitingForEntry` deriva lo stato dall'orario di partenza e dalla durata originale del percorso: quando l'ingresso mappato è occupato il client mostra «Aspetti di poter entrare» invece del conto alla rovescia ripetuto. Nessun campo di salvataggio aggiunto.

**Verifiche Task 4:** build soluzione 0 errori/0 avvisi; 707/707 test Sim, compresa l'attesa a ingresso occupato prima/dopo save/load e la ripresa dopo la liberazione. Smoke Godot OpenGL 1280×720 `--region-travel-smoke` OK: nucleo → caletta → rimessa → nucleo e nucleo → rimessa → caletta → nucleo con input e pulsanti UI, annullamento/ritorno, tre passi e durata reale, pausa, clic ignorato in transito, arrivo nella stessa sessione. Smoke di regressione `--region-presentation-smoke` e `--village-smoke` OK; il secondo attraversa anche il viaggio a destinazione unica. Catture di scelta percorso, transito e arrivi esaminate. Restano da verificare con playtest umano la comodità dei pulsanti e un ingresso occupato riprodotto visivamente nel client; il suo stato è verificato nella Sim e il testo è collegato alla vista pubblica. Save/continua regionale resta Task 5.

### Task 5 — Riprendere la stessa regione dopo la chiusura

**Obiettivo:** rendere durevole la prima consegna giocabile.

**File/sistemi:** `VillageMap.Persistence.cs`, metodi save/load in `VillageMap.cs`, `VillageMap.Quickbar.cs`, nuova entrata/menu minimo, `game/project.godot` e launcher regionale. Conservare gli entry point delle slice.

**Dipendenze:** Task 4.

**Verifica:** slot regionale, identità/revisione client minima, snapshot Sim integro; Continua da ognuna delle mappe, da metà viaggio e da un'azione pendente. Chiudere e riavviare il processo, non solo riassegnare una variabile. Area, inventario, tempo, porta/vetro, contenuto dei depositi e conoscenze restano quelli salvati. Un load errato non cambia sessione, presentazione o file valido.

**Completato quando:** Nuova partita, Continua, Salva ed esci sono raggiungibili dal percorso principale; nessun reset al rientro in un'area; un salvataggio delle vecchie slice non viene accettato come regione; riprendere non rigioca l'introduzione né esegue il preroll. Conferma prima di abbandonare/sostituire una partita; niente nuova libreria di save o modifica gratuita dello schema Sim.

**Reasoning:** alto sul contratto, medio sull'interfaccia. **Seconda consegna:** piccolo mondo effettivamente persistente.

**Risultato Task 5 (8 novembre 2026, pronto per review):** `project.godot` e `gioca.cmd` aprono la regione; `gioca-regione.cmd` è un accesso esplicito. Il titolo offre Nuova partita e Continua, il menu in gioco Carica, Nuova partita, Torna al titolo e Salva ed esci. Carica, Nuova partita e Torna al titolo richiedono conferma prima di abbandonare lo stato corrente. Il gioco parte e riprende in pausa; il tempo non avanza sotto titolo/conferma. Scene e launcher delle due slice e del villaggio restano disponibili. Lo slot resta `user://blackmarsh-region.json`, separato dagli altri. Il client salva atomicamente un involucro con identità `rpg-blackmarsh-region`, revisione 1 e snapshot Sim integro; valida involucro e struttura regionale prima di sostituire la sessione. La verifica non dipende dalla mappa attualmente costruita: funziona dalle tre aree e in transito. Un file errato lascia invariati sessione, presentazione e slot valido. Il caricamento riprende l'azione Sim pendente, senza nuovo preroll né introduzione.

**Verifiche Task 5:** build soluzione 0 errori/0 avvisi; suite Sim **714/714** dopo il Task 6 parallelo. Smoke Godot OpenGL 1280×720 in **due processi distinti** `--region-persistence-smoke` seed/resume: Continua dal nucleo, dalla rimessa, dalla caletta, a metà viaggio e durante un prelievo; snapshot Sim identico dopo il riavvio, compresi inventario, tempo, conoscenze, porta e vetro, depositi e azioni. Viaggio e prelievo riprendono e terminano una sola volta. Testati rifiuto di JSON corrotto, revisione client non supportata, save raw della vecchia slice e snapshot estraneo con involucro regionale, mantenendo la sessione e lo slot valido; conferme annullate per Carica/Nuova partita/Torna al titolo; Salva ed esci scrive uno slot valido. Lo smoke con `--path game` senza scena esplicita ha aperto il nuovo ingresso. Smoke di regressione `--region-presentation-smoke` e `--region-travel-smoke` verdi. Catture del titolo e del menu controllate. Lo smoke usa uno slot isolato sotto `.tmp`; non è stato eseguito un playtest umano né un avvio con il reale `user://` fuori dal sandbox.

**Follow-up review Task 5:** Claude ha trovato un blocco nello smoke di presentazione con `--map-presentation-fixture`: il controllo dei save regionali confrontava sempre la sessione caricata con l'isola standard e rifiutava la fixture valida. Codex ha corretto il riferimento catturando una volta la struttura del mondo scelta all'avvio della scena; non ricrea più l'isola a ogni load. Ripetuti con esito verde: build 0/0, smoke fixture, persistenza seed/resume in due processi. Il menu regionale è stato accorciato; `.gitattributes` imposta CRLF per i launcher `.cmd` e i due launcher principali hanno CRLF nel working tree. In attesa della conferma della review di Claude.

### Task 6 — Far circolare persone, scorte e informazioni

**Obiettivo:** rendere significativo l'ordine delle visite, tarando il contenuto dei tre luoghi e i ruoli esistenti.

**File/sistemi:** scenario regionale, factory/orario iniziale, test `BlackmarshRegionTests.cs` e documento di contenuto. Moduli Sim soltanto se un caso comportamentale dimostra un difetto.

**Dipendenze:** Task 2; collaudo giocabile dopo Task 5.

**Verifica:** senza ordini del giocatore il predone parte dalla caletta, interagisce con la rimessa e rientra; il pescatore compie una routine tra aree; un fatto visto può raggiungere l'autorità attraverso rapporto reale e causare una risposta fisica. Caso negativo: il protagonista lontano non sa automaticamente chi ha svuotato il deposito. Caso alternativo: mettere in sicurezza/trasferire le provviste cambia la successiva decisione del predone senza flag di ramo.

**Completato quando:** i diversi ordini di visita producono almeno due sviluppi distinti e comprensibili, senza aspettare un pulsante di attivazione; testimoni, percorsi, orari e consumi sono misurati e documentati. Il guardiano non risolve ogni problema prima che il giocatore possa scegliere, ma neppure aspetta artificialmente il suo arrivo. Se il rapporto non emerge, controllare percezione, turni e accessibilità prima di inventare dialoghi o AI nuovi.

**Reasoning:** alto per la causalità e la taratura, non per il volume di codice.

**Risultato Task 6 (Claude, 8 novembre 2026), approvato da Codex.** Nessuna modifica ai moduli Sim: le sonde non hanno trovato difetti di comportamento, solo due difetti di contenuto, corretti nello scenario.
- **Predone fermo sull'accesso.** Dopo la consegna il predone restava sulla casella d'accesso delle provviste Vasan (16,11), e il giocatore non poteva più prenderle («Lì non c'è spazio»). Aggiunto il posto «casa» dell'approdo in (15,9).
- **Custode fermo sull'accesso.** Il custode di presidio si fermava sull'unico accesso della dispensa (3,3). Aggiunti posti «guardia» espliciti: casa comune (4,4), rimessa (10,8). Il presidio dissuade lo stesso, perché conta che il predone veda la guardia.

Test `BlackmarshRegionTests.cs` (7 casi, solo comandi pubblici del giocatore e orario del mondo):
- **Senza intervento del giocatore.** Ciclo completo della razzia: `RaidOrdered`, `FoodStolen` 4, `FoodDeposited` 4, `RaidCompleted`, quantità finali e ritorno al posto; nessun testimone. Il giorno dopo la razzia va alla dispensa: il pescatore sente il furto, lo riferisce al custode (rapporto reale) e il custode presidia la dispensa.
- **Caso negativo.** Il giocatore lontano non sa chi ha svuotato la cassa, neanche dopo averla vista vuota: nessuna osservazione e nessun rapporto possibile.
- **Fatto visto, rapporto, risposta fisica.** Il giocatore assiste al furto dalla stanza interna e lo racconta. Il custode percorre il sentiero, presidia la rimessa e pretende le 4 razioni dal predone.
- **Caso alternativo.** Portare le razioni della rimessa alla dispensa prima delle 20:00 cambia il bersaglio della razzia; il predone trova il paladino all'accesso e desiste.
- **Caletta per prima.** Le provviste riprese riportano i Vasan sotto soglia e provocano una razzia alle 21:00; la sera dopo il custode di guardia la fa desistere.
- **Confronto.** Tre ordini di visita portano a tre mondi diversi la sera del giorno 2.

Misure e tabella degli sviluppi: `docs/BLACKMARSH_REGION.md`. Verifiche in worktree pulito: Sim **714/714** (707 + 7), build client 0/0, smoke regionali e di regressione verdi.

**Limiti, comportamento del motore lasciato com'è:**
- il predone non reagisce a un intruso nel suo approdo (punto F);
- la porta della casa comune resta aperta dopo l'uscita del pescatore;
- il custode presidia il deposito derubato anche quando è già vuoto: col rapporto della rimessa, la sera dopo la dispensa resta scoperta. È uno sviluppo leggibile, non un errore da nascondere; va giudicato nel playtest del Task 10.

**Review indipendente Codex (8 novembre 2026): APPROVATO.** Esaminati commit `9a4b565`, scenario, sette test e documento: solo tre posti aggiunti, nessun flag di ramo o modifica Sim; il caso negativo di conoscenza e le risposte fisiche sono asseriti tramite viste/comandi pubblici. Aggiunta ai due test di presidio una verifica di cammino fino all'accesso reale del deposito, perché la sola differenza di coordinate non dimostrava raggiungibilità: entrambe passano. Build soluzione 0 errori/0 avvisi, sette test mirati e suite completa **714/714** verdi. Gli smoke regionali di viaggio e presentazione erano stati rieseguiti da Codex dopo il commit Task 6 durante il collaudo Task 5, entrambi verdi. I tre limiti sopra restano decisioni di contenuto/playtest, non blocchi per il Task 7.

### Task 7 — Aggiungere l'opportunità di esplorazione alla caletta

**Obiettivo:** offrire una ragione autonoma per esplorare, oltre a custodire o spostare razioni.

**File/sistemi:** scenario regionale, profilo della caletta in `VillageMap.Presentation.cs`, asset del pilot già disponibili, test di contenuto, `docs/BLACKMARSH_REGION.md`/`CREDITS.md` per gli adattamenti.

**Dipendenze:** Task 3; verifica della persistenza dopo Task 5.

**Verifica:** piccolo capanno, un ingresso alternativo, arco/frecce a terra visibili solo secondo percezione; raccolta, equipaggiamento e ritorno usando comandi ordinari. Porte, tetto e ostacoli coerenti. Il ritorno e il caricamento non rigenerano oggetti o serrature. Nessun oggetto indispensabile alla sopravvivenza posto dietro un tiro obbligatorio senza alternativa.

**Completato quando:** l'opportunità è trovabile giocando, offre una scelta pratica di equipaggiamento e può essere ignorata; il gioco continua nei due casi. Nessuna ricompensa assegnata dal client e nessun nuovo sistema di contenitori. Il test verifica raggiungibilità e conseguenze, non una sequenza di clic prestabilita.

**Reasoning:** basso/medio; contenuto e verifica spaziale.

### Task 8 — Dare alla regione un'interfaccia continua

**Obiettivo:** eliminare l'attrito delle modalità demo senza rifare tutta la UI.

**File/sistemi:** `VillageMap.Slice.cs`, `BlackmarshFlow.cs` solo per separare componenti riusabili, `Quickbar.cs`, `Stores.cs`, `Deeds.cs`, HUD/timer/conversazione; aiuto iniziale regionale minimo.

**Dipendenze:** Task 4–7.

**Verifica:** attendere, fermarsi, viaggiare, riposare, parlare, trasferire e gestire equipaggiamento presentano uno stato coerente; nuove osservazioni/combattimento interrompono l'accelerazione correttamente. Nessun riepilogo o retry della slice nella regione. Selezioni, tooltip e comandi pendenti non sopravvivono erroneamente a load/transizione. Diario e log distinguono conoscenze reali; nessun messaggio rivela un evento remoto per suggerire dove andare.

**Completato quando:** input e informazioni restano leggibili a 1280×720 e 1920×1080, senza clic che passano attraverso pannelli; il giocatore può spiegare cosa sta facendo e come interromperlo. Timer già implementati riusati, nessuna nuova logica di regole o progresso globale nel client.

**Reasoning:** medio; priorità agli attriti osservati.

### Task 9 — Verificare ritorni, tempo e ripresa come comportamento composto

**Obiettivo:** evitare che smoke locali verdi mascherino un mondo che si resetta o non simula lontano dal protagonista.

**File/sistemi:** test regionali Sim, smoke Godot regionale separato dalle vecchie slice, risultati in questo documento.

**Dipendenze:** Task 5–8.

**Verifica:** giro in entrambi gli ordini; modifica di un interno e di un deposito, uscita e ritorno; attori in viaggio; rapporto che attraversa un'area; salvataggio prima/durante/dopo la transizione; riavvio pulito. Confrontare una continuazione salvata/ricaricata con la stessa continuazione senza load, comprese conseguenze e RNG. Confrontare avanzamento a piccoli passi e accelerato per un intervallo che include routine, consumo e spostamenti. Far trascorrere almeno una notte con riposo e verificare che non si resetti il mondo.

**Completato quando:** suite Sim e build Godot passano; smoke della regione e regressioni pertinenti di Village/rimessa passano; evidenze separate fra test Sim, input UI e immagini. Smoke con soli comandi pubblici/controlli reali per il percorso giocatore; `WorldView` ammessa per gli assert diagnostici, non come fonte di indizi. Nessuna manipolazione dello snapshot per simulare un viaggio riuscito. Registrare eventuali problemi e non chiamarli risolti cambiando il test.

**Reasoning:** alto per scegliere i casi e gli oracoli, medio per implementarli. Non riscrivere test già sufficienti; aggiungere i confini non coperti.

### Task 10 — Verificare che ci sia mezz'ora di gioco, non soltanto un circuito funzionante

**Obiettivo:** giudicare la regione come primo prodotto giocabile.

**File/sistemi:** questo documento e note di playtest; eventuali piccoli aggiustamenti di contenuto/UI in task espliciti successivi.

**Dipendenze:** Task 9.

**Verifica:** almeno due sessioni da nuovo mondo con ordini iniziali diversi, di cui almeno una con giocatore non guidato passo passo; una sessione di ripresa dopo chiusura. Registrare tempo reale di decisione/esplorazione, attese, luoghi visitati per scelta, opportunità trovate, conseguenze comprese, motivi di una rivisita e punti in cui il giocatore non sa che fare. Il collaudatore non deve aspettare artificialmente che il timer raggiunga venti minuti.

**Completato quando:** il gioco sostiene circa 20–30 minuti di scelte e attività senza debug, guida esterna o ripetizione forzata, e almeno due sviluppi sono effettivamente osservati. Deve esistere un percorso soddisfacente senza combattimento obbligatorio. Un conflitto risolto non è un fallimento del sandbox. Se il playtest umano non è stato svolto, scrivere «non verificato»; test e smoke non lo sostituiscono.

**Reasoning:** alto di design/valutazione; richiede osservazione di giocatori, non un modello più grande per scrivere più codice. **Consegna finale:** regione giocabile persistente; nessun altro esagono prima di questo esito.

## 10. Cosa non costruire e quando rivalutare

**Fuori da questa roadmap:** Castle Blackmarsh, nove esagoni, navigazione navale, mondo continuo/streaming, procedural generation, incontri casuali, quest system, dialoghi ramificati, editor, città/interiori generici, produzione e commercio, crafting, reclutamento/gruppo, reputazione numerica, nuove fazioni politiche, albero di AI componibile, allarmi fra torri, multiplayer, migrazioni universali, cloud save e nuova direzione artistica.

Alcune capacità di base esistono già — per esempio monete, livelli, riposi, inventario, trappole — e possono continuare a funzionare. Non devono diventare nuovi progetti soltanto per riempire una lista di feature. Né occorre rimuoverle.

Rivalutare produzione/scorte rinnovabili quando il giocatore vuole davvero passare più giorni e l'esaurimento impedisce di proseguire; rivalutare nuovi comportamenti quando i tre luoghi sono percorribili e il playtest mostra precisamente quale scelta non è sostenuta. Rivalutare una seconda regione soltanto dopo aver dimostrato che vale la pena uscire, tornare e riprendere questa.

**Raccomandazione originaria: iniziare dal Task 1.** Il mondo e il salvataggio possiedono già gran parte dell'infrastruttura necessaria; l'incognita più economica da risolvere era la composizione di viaggio, selezione dei bersagli e percezione fra due mappe reali. Stato attuale sopra, sotto i singoli task.
