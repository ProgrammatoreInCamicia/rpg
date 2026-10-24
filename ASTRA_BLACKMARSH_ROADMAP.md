# ASTRA — seconda vertical slice: La rimessa della vedetta

Analisi del **23 ottobre 2026**, repository a **`cca04bb`**. Documento di proposta. **Stato aggiornato:** Task 1 e 2 completati nella Sim; Task 3–6 completati nel client (esiti in fondo al documento); Task 7–10 non iniziati, nessun criterio umano certificato. La descrizione dell'analisi iniziale sotto resta storica.

## 1. Decisione

**È il momento giusto per una prima situazione di Blackmarsh, purché rimanga una sola mappa piccola.** Propongo **«La rimessa della vedetta»**, sull'isola dell'esagono **0912**: recuperare le quattro razioni di emergenza di una rimessa mentre un Vasan tenta autonomamente di raggiungerle e portarle al proprio deposito.

Un protagonista, un Vasan, un edificio di due ambienti e un tratto di riva. Si può entrare, usare la chiave e le aperture, mettere in salvo le provviste e tornare al punto iniziale; oppure rinunciare e osservare la sottrazione. Il combattimento esistente rimane possibile, ma non è necessario per nessuno dei due esiti richiesti.

La domanda da verificare è precisa: **una nuova pianta, nuove identità e un edificio utilizzabile cambiano davvero il modo di giocare usando lo stesso motore e lo stesso client?** Non basta cambiare i nomi del granaio e della guardia. La geometria e lo stato delle aperture devono influire sul percorso e sui tempi dell'NPC, e il giocatore deve poter intervenire direttamente sulle risorse contese.

Conservare il cibo come risorsa rende confrontabile il riuso. Le novità da mettere alla prova sono **interni percorribili, chiavi/serrature, percorso alternativo attraverso una finestra, effrazione autonoma e competizione temporale per una risorsa**. Non aggiungere un'altra risorsa, un contenitore generico o un sistema di missioni per renderla apparentemente diversa.

## 2. Fonti Blackmarsh effettivamente presenti

Ho letto `docs/ARCHITECTURE_DECISIONS.md`, §§14–15, le discussioni pertinenti in `chat.txt`, il manifest dei contenuti e **`Blackmarsh_Ver_11.rtf` dentro `docs/black_marsh.zip`**. Nell'archivio ci sono anche PDF e mappe di altre revisioni: questa proposta si riferisce alla v11, senza mescolarle implicitamente.

| Fonte | Contenuto rilevante e uso nella proposta |
|---|---|
| Blackmarsh v11, **0912** | Isola di circa cento abitanti dediti alla pesca, concessa in cambio della vigilanza contro i razziatori, specialmente Vasan. Una torre di legno a sud segnala il pericolo mediante fuochi, poi rilanciati verso Castle Blackmarsh. È l'ancoraggio geografico e sociale della slice. |
| v11, **0913** | Castle Blackmarsh e governo elfico. Rimangono contesto; nessuna città da costruire. |
| v11, **0712** | Faro in rovina, antica base di Atacyl, ritenuto vuoto dopo la spedizione di quarant'anni prima. Non lo scelgo: introdurvi un dungeon attivo richiederebbe più incontri, contenuti e adattamenti. |
| v11, **0814**, **0918**, **1113** | Case abbandonate dei maghi, rovine di Darkheart, Inuacus Keep. Confermano possibilità future, ma non servono a questo incremento. Non ridimensionare Darkheart o Causari per adattarli al paladino di livello 1. |
| `docs/ARCHITECTURE_DECISIONS.md`, §§14–15 | Motore separato dal contenuto; generalizzazione al secondo esempio; prima direzione città/isola di vedetta. La presente proposta ne realizza un frammento, non tutta la precedente ambizione città + isola + tre fazioni. |
| `game/content/manifest.json` | Oggi registra solo `living-village` come scenario JSON. Non esiste già una mappa Blackmarsh giocabile da collegare. |

**Distinzione fra fonte e adattamento:** la rimessa, la chiave, le quattro razioni, il singolo razziatore, la sua incursione e la pianta locale sono **aggiunte originali per questa situazione**, coerenti con 0912 ma non descritte nel paragrafo originale. Non presentarle come un episodio canonico di Conley. Il personaggio è autorizzato a recuperare le provviste: questo va espresso anche dalla proprietà/fazione del deposito, non solo nel testo iniziale.

La torre e i segnali di fuoco rimangono contesto fuori dall'area percorribile. **Non proporre «accendi il segnale e arrivano rinforzi»**: torce e lampade esistono, ma la propagazione di un allarme fra torri e la risposta conseguente non sono una funzione già implementata. Non attribuire quell'effetto a `TorchCommand`.

Il file v11 contiene la dichiarazione CC BY 4.0 per testo e mappe; il repository ha già scelto questa fonte. Nel task di contenuto aggiornare `CREDITS.md` e la provenienza dei nuovi dati con autore, opera, versione, fonte e adattamenti. La frase attuale dei crediti secondo cui l'intera ambientazione è originale non coprirà il nuovo materiale. Nessuna illustrazione dell'archivio è necessaria. L'archivio è locale e ignorato da Git: il documento di contenuto deve conservare nel repository una breve sintesi attribuita sufficiente a lavorare senza averlo installato.

## 3. È meglio restare ancora nel villaggio?

Il commit `13e15ab` e la review successiva `cca04bb` sono posteriori alla precedente analisi finale: affrontano il blocco del riepilogo in combattimento, rafforzano i test negativi e aggiornano lo stato della roadmap. **Non riaprire F1–F3 leggendo soltanto il vecchio verdetto di `ASTRA_FINAL_REVIEW.md`.** La suite attuale passa con 656 test. Accolgo la conclusione della prima slice come validazione del nucleo sandbox.

Rimane una prova non ancora completa: **un giocatore inesperto che affronti liberamente un breve scontro, ne capisca movimento/ritiro e termini la situazione senza spiegazioni**. Esistono motore, test e smoke di combattimento; non equivalgono a una validazione del suo bilanciamento e della sua comprensibilità. Anche il playtest indipendente del vecchio Task 9 non è registrato come svolto. Sono limiti reali, ma non un motivo per trattenere ogni nuovo contenuto nel villaggio.

| Alternativa | Cosa dimostrerebbe | Cosa non dimostrerebbe |
|---|---|---|
| Un intervento armato nel villaggio | Qualità del combattimento e della ritirata con meno variabili nuove; utile se il prossimo obiettivo fosse rendere lo scontro il centro del gioco. | Riuso del client con nuovi ID, nuovi depositi, altra pianta e interni come parte del loop. Gli accoppiamenti del villaggio resterebbero facilmente invisibili. |
| La rimessa in Blackmarsh | Trasferibilità di scenario, rendering, interazioni e persistenza; integra l'edificio già esistente in un obiettivo concreto. | Non certifica il bilanciamento generale degli scontri o una campagna Blackmarsh. |

**Scelgo Blackmarsh**, perché corrisponde alla domanda di questa sessione. Non emerge una nuova meccanica fondamentale da completare prima nel villaggio: emerge invece una prova di composizione ancora da fare, e la rimessa può ospitarla direttamente. Conservare Village e i pilot come regressioni; non consegnare un'altra slice del villaggio in aggiunta a quella proposta.

La condizione per proseguire è il **Task 1**: dimostrare nella Sim, senza preparazioni artificiali, l'incursione e i due esiti. Se servissero nuove politiche NPC o una modifica ampia del motore, fermare la roadmap e rivalutare la situazione. Non costruire prima la grafica sperando che il loop emerga dopo.

## 4. Il lavoro su edifici e interni: cosa esiste davvero

### Tre livelli diversi

1. **Studio visivo** — `docs/STYLE_STUDY.md`, `StyleStudyPreview.tscn`, `interior-concept.png`: immagini e direzione artistica. Non sono una mappa navigabile o una specifica esatta di geometria. Non richiedono un completamento per passare a Blackmarsh.
2. **Prototipo funzionale** — `InteriorPrototypeScenario.cs`, `InteriorPrototype.tscn`, `InteriorPrototypeTests.cs`, `InteriorSmoke.cs`: confini sottili, movimento, porte, finestre, sguardo, luce e memoria. La Sim decide passaggio e visibilità. Le successive regole di scavalcamento/effrazione ampliano il prototipo: la vecchia frase della documentazione «il corpo non attraversa la finestra» non va letta come assenza di `ClimbCommand` nel motore attuale.
3. **Pilot arredato** — `InteriorArtPilotScenario.cs`, `InteriorArtPilot.tscn`, `VillageMap.Buildings.cs`, `VillageMap.InteriorArt.cs`, parti di `VillageMap.Visuals.cs`: casa 19×14, due ambienti, porta esterna e interna, finestre, lampada, tetto, arredi con ostacoli Sim. È una scena giocabile nello stesso client, non un renderer alternativo da integrare da zero.

### Verifica attuale

In questa sessione:

- Build della solution: **0 warning, 0 errori**.
- Suite Sim: **656 superati, 0 falliti, 0 ignorati**.
- Smoke grafico `--art-pilot-smoke`, OpenGL, 1280×720: **`INTERIOR ART PILOT SMOKE OK`, exit 0**.
- Ispezionate le nuove catture delle due stanze e del tetto dopo l'uscita, in `.tmp/blackmarsh-analysis/art-pilot/`.

Lo smoke controlla ingresso reale, tetto sollevato e nascosto dall'interno, porta interna aperta dopo avvicinamento, finestra apribile/chiudibile, annullamento con Stop, rifiuto del cammino sul letto, clic sull'anta aperta, memoria della stanza e tetto richiuso fuori. Cattura anche la notte con torcia. Il passaggio notturno usa un avanzamento di test: non è un intero playtest serale spontaneo.

Le immagini confermano due stanze leggibili e arredi utilizzabili come riferimenti spaziali. Materiali e proporzioni restano da campione; il risultato non coincide con l'illustrazione di studio. Questo non blocca una prova di riuso. **La scena verificata contiene solo il protagonista:** non dimostra ancora un NPC autonomo che attraversi la casa arredata, cambi ingresso e contenda un deposito. È esattamente la lacuna che la nuova slice può mettere alla prova.

### Problema risolto, integrazione e limite

| Parte | Stato e valore concreto | Decisione per la rimessa |
|---|---|---|
| `AddInterior`, `AddEdge`, porte/finestre, luce | Regole Sim e viste già presenti; il client consuma i medesimi dati. Permettono spazi che occultano e limitano percorsi. | Riutilizzo diretto, una sola mappa e un solo piano. |
| Tetto e pareti in `Buildings.cs` | Costruzione da zone interne; apertura del tetto, copertura delle celle e attenuazione delle pareti davanti al player/bersaglio. | Riutilizzare. Un'unica zona interna per l'edificio, due ambienti divisi da edge: niente modello di piani o stanze gerarchiche. |
| Porte e finestre dettagliate | Rendering e picking delle ante/aperture presenti, diversi dettagli condizionati da `InteriorArtPilot`. | Separare l'abilitazione del piccolo stile visivo dalla selezione dello scenario pilot. Non accendere `InteriorArtPilot` per caricare Blackmarsh: oggi selezionerebbe anche la casa sbagliata. |
| Avvicinamento alle aperture | `VillageMap.BarrierApproach.cs` usa preview Sim e lati conosciuti; nuovi ordini/Stop annullano il seguito. | Riutilizzare. Verificare il nuovo assetto e non introdurre un secondo pathfinder Godot. |
| Arredi | `InteriorArt.cs` piazza cinque props a coordinate fisse; gli ostacoli corrispondenti sono scritti separatamente nel builder. Sono immagini, non contenitori o interazioni. | Solo **un tavolo** oltre alla cassa del deposito già funzionale. Posizione configurabile; collisione nella Sim. Nessun letto o arredo senza funzione per questo incontro. |
| Materiali e tappeto | Pavimento e decorazioni hanno controlli su `FarmerHouse`, `H`, `x <= 8` e coordinate del pilot. | Piccola mappatura di presentazione per zone/props; nessuna copia di quelle coordinate nella rimessa. Tappeto superfluo. |
| Ostacoli nei file scenario | `ScenarioBuilder.AddObstacle` funziona; **`ScenarioFile.Load` non legge un elenco `obstacles`**. Il pilot funziona perché usa un builder C#. | Aggiungere soltanto questo piccolo ponte al loader JSON se si usa il tavolo. Non è necessario un nuovo sistema di edifici. |
| Baule del pilot | Prop statico; non contiene loot, serratura o inventario. | Non promuoverlo implicitamente a contenitore. La cassa della rimessa è un `Store` di cibo esistente, con accesso dichiarato. |

**Da completare ora:** riuso del profilo visivo fuori dal flag del pilot, disposizione del solo arredo necessario, coerenza tra prop e ostacolo, chiarezza e clic sulle due aperture effettivamente usate. **Da lasciare aperto:** kit artistico finale, generatore di case, catalogo prefab, editor, livelli sovrapposti, tetti arbitrari, inventario degli arredi, decine di props, illuminazione pittorica avanzata e costruzione di una città.

Il tetto attuale raggruppa celle per zona e ne usa i limiti geometrici: basta una rimessa compatta rettangolare. Edifici disgiunti con lo stesso ID di zona e forme complesse non devono diventare il prossimo problema da risolvere.

## 5. Accoppiamenti del client e generalizzazione minima

Il nome `VillageMap` è meno importante dei riferimenti concreti al contenuto. **Non rinominare/spostare tutta la classe** e non duplicarla in `BlackmarshMap`.

| Punto del codice | Accoppiamento osservato | Intervento minimo o rinvio |
|---|---|---|
| `VillageMap.cs`, `_Ready`, circa 105–118 | Scelta diretta fra factory della slice, pilot/prototipo e `living-village`; ora, sguardo e pausa dipendono dalla modalità. | Un punto di selezione esplicito della sessione e dei parametri d'ingresso della nuova scena. Riutilizzare la stessa factory provata nel Task 1. Non aggiungere un quarto booleano che cambi scenario, grafica e gameplay insieme. |
| Save path e retry, `VillageMap.cs`, `Quickbar.cs` | Solo percorso Village/slice, retry fissato a `PlayableSlice.tscn`. | Percorso save e scena di retry associati alla scena attiva. `user://blackmarsh-watchhouse.json`; non migrare lo schema Sim. |
| `Deposit`/`Take`, circa 759–765; visibilità `_storeRow`, circa 1014 | Comandi e pulsanti puntano a `GranaryStore`. | Passare un `StoreId` effettivamente selezionato dalla vista. Per la nuova UI basta il deposito visibile della rimessa; è il primo vero secondo esempio che giustifica questa generalizzazione. |
| `Visuals.cs`, circa 437, 481, 640 | Il player viene riconosciuto dall'ID del villaggio; guardia/contadino hanno stili per ID; gli altri diventano razziatori. | Player da `_view.Id`/`_sim.Player`; piccola mappa di presentazione attore→stile, fuori dalle regole Sim. Nuovi ID non devono alterare logica o sparire dal renderer. |
| `BuildMap`, circa 1401; `ApplyGroundTexture` | Colori/materiali legati a `Inn`, `FarmerHouse`, `ForestRoad`; nel pilot anche alle coordinate. | Mappa di presentazione zona→materiale, più un fallback neutro. Nessuna inferenza di ruolo dai nomi italiani. |
| `Buildings.cs`, `Visuals.cs`, picking porte/finestre | Lo stile del pilot dipende dallo stesso flag che crea la sua sessione. | Un'opzione puramente visiva riutilizzabile; mantenere il pilot esistente come regressione. |
| `InteriorArt.cs` | Lista e posizioni fisse dei mobili. | Piccola lista di props statici della scena. Può essere un normale dato C#/Resource, senza framework o editor. Gli ingombri autorevoli restano nei dati Sim. |
| `Slice.cs`, `SliceReport.cs`, `SliceSummary.cs`, `Deeds.cs` | Guida, rapporto, consegna, conclusione e alcune etichette parlano del granaio e della guardia. | Non renderli un motore di quest. Mantenere la vecchia presentazione; riusare soltanto controllo temporale, pausa/conclusione e componenti elementari. Testo della rimessa ricostruito da viste appropriate. |
| Attesa e barriera della pausa del riepilogo | Il controllo del tempo è utile, ma oggi abilitato dalla modalità della prima slice. | Un solo percorso comune per accelerazione e blocco input, utilizzabile dalle due presentazioni. Non duplicare il loop `_Process` o il fix F1. |
| `ReturnToVillage`, pannello off-map | Ritorno a `ForestRoad`, controllo su `BanditCamp`; load non ricostruisce genericamente ogni mappa. | **Rinviare.** Rimessa e costa sono sulla stessa mappa; nessun viaggio del player. Nascondere le azioni fuori contesto, non simulare un trasporto con gli ID vecchi. |
| `BuildMap` legge depositi dal `WorldView` | Geometria statica preesistente, non un'API di conoscenza del player. | Non usarla per esiti/quantità dinamiche o nemici. Label, log e riepilogo restano filtrati. Non ampliare il problema costruendo un pannello onnisciente della razzia. |

Sono sufficienti pochi dati di scena: factory/selezione, ingresso, save/retry e presentazione (stili, materiali, pochi props). Il formato esatto è una scelta locale dell'implementazione. **Non servono registry di servizi, plugin di scenario, linguaggi di scripting o configurazione dei comportamenti in un nuovo DSL.**

### Riutilizzo diretto

`SimulationSession`, scheduler, RNG e salvataggi; `ScenarioFile`/`ScenarioBuilder`; fazioni e incarichi di razzia; `MoveToSpotCommand`, `DoorCommand`, `LockCommand` (con `Locked = true/false`), `WindowCommand`, `ClimbCommand`, `TakeFoodCommand`, `DepositFoodCommand`; percezione, luce, osservazioni e diario; azioni di effrazione NPC; interazioni con barriere; combattimento e inventario esistenti. Non creare equivalenti client di questi comandi.

### Limiti delle regole da rispettare

- `AddRaidingFaction` rende la fazione ostile agli estranei. **Non segue un attacco automatico a vista in esplorazione.** `JoinStep` reagisce a scontri esistenti; `ConfrontStep` è legato ai ricercati. Il Vasan della rimessa è interessato alle provviste; non promettere pattuglie aggressive, inseguimento o allarme universale.
- L'indagine sui rumori è riservata all'autorità di fazione e a tipi di osservazioni precisi. Non aggiungere un «sentinella viene a controllare ogni rumore» solo nel client.
- `BreakInStep` sceglie fra percorsi possibili, contando diversamente lo scavalcamento; non è un pianificatore tattico che conosce probabilità e costo atteso di ogni serratura. Un percorso pedonale disponibile ha precedenza; gli sbarramenti non garantiscono che l'NPC preferisca l'ingresso narrativamente più interessante.
- I test `BreakInTests` sono utili ma molti **iniettano direttamente l'incarico di razzia** o modificano stato interno. Non dimostrano che un nuovo scenario avvii autonomamente quel comportamento. Il Task 1 deve colmare questa differenza usando la vera politica di fazione.
- Le finestre richiedono interazioni specifiche per attraversarle. Non promettere che un clic su pavimento oltre la finestra avvii automaticamente tutto il percorso di scavalcamento.
- La mappa contiene già geometria completa per il renderer; la nebbia di guerra non è un confine contro client modificati. Il requisito è non mostrare al giocatore informazioni dinamiche non percepite.

## 6. Loop concreto e dimensione massima

### Situazione iniziale proposta

**Titolo:** La rimessa della vedetta.

**Invito:** «Sull'isola di vedetta i Vasan stanno cercando provviste. Nella rimessa sono rimaste quattro razioni d'emergenza. Hai la chiave: puoi recuperarle e tornare al sentiero, oppure rinunciare.»

È contesto iniziale noto, non una notizia di furto già avvenuto. Nessuna frase afferma dove sia ora un nemico nascosto o quanto manchi al suo arrivo.

- **Una mappa di circa 18×14 celle**, comunque non più grande del villaggio attuale.
- **Due attori:** protagonista paladino esistente e un Vasan con il profilo `raider` esistente. Nessun NPC decorativo necessario.
- Due fazioni: abitanti dell'isola e il piccolo gruppo Vasan rappresentato dall'incursore. Il protagonista appartiene alla prima per questa situazione.
- **Un edificio, un piano, due ambienti**, una porta esterna con chiave, una porta interna semplice, una finestra attraversabile con le regole esistenti; la finestra può avere telaio bloccato per mettere alla prova l'effrazione. Una lampada esistente illumina l'interno.
- Un tavolo come ostacolo e la cassa di provviste come vero `Store`, con casella d'accesso raggiungibile. Non introdurre un oggetto «baule inventario».
- Un deposito Vasan di ritorno sulla stessa mappa, lontano dalla rimessa, rappresentabile con l'asset cassa esistente. Nessuna barca navigabile o seconda mappa; un semplice nome di luogo «Approdo Vasan» basta.
- Player sul sentiero, con le 10 razioni del pregenerato e la chiave locale. Rimessa con **4 razioni**, incarico Vasan da **4**. Quantità e geometria sono parametri di contenuto da verificare, non risultati già ottenuti.
- Avvio in pausa, con casa e sentiero leggibili. Nessun requisito di aver concluso o caricato il villaggio.

**Temporizzazione candidata da provare:** factory poco prima delle 20:00; deposito Vasan inizialmente sopra soglia, consumo ordinario alle 19:59 e successiva valutazione alle 20:00. Per esempio 4 razioni iniziali, consumo 4, soglia 1, valutazione oraria, ingresso alle 19:58. Il preroll normale non deve aver già fatto avvenire la razzia. Questo usa dati e scheduler esistenti; non aggiungere «spawna il ladro dopo Comincia» o un timer di missione. Il Task 1 deve scegliere e registrare valori realmente funzionanti, inclusa una finestra non millimetrica per intervenire.

La configurazione mantiene entrambi i depositi sulla mappa e non dipende dal codice di viaggio/ritorno del villaggio. Se si verifica la conservazione del cibo bisogna includere il consumo di fazione programmato: non attribuirlo a una sottrazione nascosta.

### Cosa fa il giocatore

1. Legge l'invito, vede la rimessa e riprende. Può mettere in pausa in ogni momento.
2. Raggiunge l'ingresso, usa la chiave attraverso le interazioni esistenti, entra e trova l'accesso alla cassa. Può richiudere/serrare la porta, osservare la finestra o decidere di lasciare perdere.
3. Il Vasan riceve l'incarico dalla fazione. Se trova un percorso libero lo segue; se non lo trova tenta l'effrazione/scavalcamento consentiti dalla geometria. Non aspetta che il protagonista legga il diario o prema un pulsante di missione.
4. Il protagonista sceglie se dedicare il tempo dell'azione a prendere le quattro razioni oppure ritirarsi. Le azioni consumano il tempo Sim esistente; anche durante un prelievo il mondo continua.
5. Torna sul sentiero con ciò che possiede, oppure rimane a osservare l'esito. Apre il riepilogo e termina/riprova esplicitamente. Il sentiero **non è una zona invulnerabile** e non chiude automaticamente un combattimento.

**Due esiti minimi, da provare allo stesso checkpoint:**

| Esito | Stato reale atteso | Cosa può dire la UI |
|---|---|---|
| Provviste recuperate | Il prelievo del player finisce prima della sottrazione NPC; nel percorso senza altre spese player 10→14, rimessa 4→0. La razzia non porta quelle quattro razioni al deposito Vasan. Il player raggiunge di nuovo il sentiero. | Razioni attualmente portate, posizione conosciuta, eventuali osservazioni e stato visibile delle aperture. Non «hai sconfitto i Vasan». |
| Recupero mancato/rinuncia | Senza prelievo tempestivo del player, il Vasan entra, prende 4 e le deposita a casa; player resta a 10. La rimessa è vuota e le aperture effettivamente alterate restano tali. | Se visto, furto/effrazione osservati; se non visto, «non sai cosa sia successo». Scorte soltanto quando osservabili. Il dato nascosto del deposito Vasan serve agli assert, non al riepilogo. |

Risultati intermedi sono validi: prelievo parziale, porta sfondata, vetro rotto, attacco, ferita o ritiro. Non normalizzarli verso una delle due etichette e non salvare un enum di missione. I due percorsi sopra sono **campioni riproducibili**, non rami imposti da un dialogo.

La scelta delle aperture deve modificare almeno **percorso, lavoro compiuto o tempo di accesso** dell'NPC. Non è richiesto che chiudere una porta garantisca la vittoria: il Vasan può aggirarla. Se l'intero loop rimane identico togliendo muri e finestre, la slice non ha raggiunto il suo scopo.

### Budget e confini

Obiettivo di una prova umana: **5–8 minuti**, senza tutorial esterno; prima decisione spaziale subito, primo segnale percepibile dell'incursione entro circa due minuti dalla ripresa nel percorso guidato. Tempi da misurare, non promesse già certificate. Riutilizzare pausa, ritmi e attesa accelerata soltanto tramite API esistenti e stop sulla conoscenza del player.

Fuori scope: castello, città, torre esplorabile, segnali attivi, rinforzi, terza fazione, salvataggio importato dal villaggio, reputazione, dialoghi ramificati, nuovi mostri, pattuglie, crafting, nuoto/barche, mappa regionale, seconda area caricata, trappole aggiunte per mostrare un'altra feature, kit grafico completo. Non serve l'intera vecchia milestone W1 per un edificio nello stesso spazio della costa.

## 7. Come riconoscere il riuso riuscito

La consegna non è riuscita soltanto perché si apre `BlackmarshWatchhouse.tscn`. Occorrono queste prove:

- Tutti gli ID di scenario/area/luoghi/fazioni/attori/depositi sono nuovi e specifici, per esempio `bm_watch_*`, **anche l'ID del protagonista**. Rimangono riutilizzati gli ID dei profili e degli oggetti comuni. Non chiamare la casa `farmer_house` o la cassa `granary_store` per accontentare il renderer.
- Nessun nuovo controllo sugli ID Blackmarsh in `Simulation.Npcs`, `Simulation.BreakIn`, percezione o planner. L'NPC riceve l'incarico dalla politica, senza scritture a `Assignment` nei test della slice.
- La stessa geometria pubblicata dalla Sim guida percorso, luce, aperture e resa; il tavolo ha un ostacolo Sim corrispondente. Gli sprite non aggiungono collisioni Godot autorevoli.
- I due esiti provengono dalla stessa factory/seed e differiscono per comandi del player; entrambi raggiungono un checkpoint comune finito. Né seed scelti per mascherare impossibilità né scorte mutate dagli smoke.
- La prova delle aperture compara condizioni geometriche diverse prodotte dai comandi. Non basta vedere l'animazione di una porta.
- UI della rimessa senza indicazioni granaio/guardia/locanda o ritorni al bosco; azioni sul deposito corretto. Asset identici possono essere riutilizzati senza nomi e regole del villaggio.
- Vecchia slice e pilot rimangono avviabili; i nuovi save non li sovrascrivono. Nessun nuovo schema di salvataggio necessario.
- Il riepilogo usa viste filtrate e rimane fermo anche durante uno scontro, riusando la correzione già consolidata.

## 8. Roadmap assegnabile

**Non è autorizzazione a implementare in questa sessione.** Ogni task seguente è una consegna separata. I nuovi nomi di file sono proposte; leggere le istruzioni locali, lo stato Git e il codice corrente prima di iniziare. Non modificare gli asset locali `Body.*`. Aggiornare questo documento con risultati misurati, fallimenti e limiti dopo ogni futura consegna.

Ordine valido: **1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10**. Le dipendenze riportate permettono lavoro separato, ma molti task toccano gli stessi partial: non assegnarli contemporaneamente senza coordinamento.

### Task 1 — Provare nella Sim che la rimessa è una situazione, non un copione

- **Obiettivo:** contenuto minimo, ancora senza arredo cosmetico, che avvii la razzia autonomamente e permetta i due esiti e una variazione effettiva di ingresso.
- **File/sistemi:** nuovi `game/content/scenarios/blackmarsh-watchhouse.json`, `game/content/blackmarsh/items.json` per la sola chiave, `src/RpgSandbox.Sim/Scenarios/BlackmarshWatchhouse.cs`, `tests/RpgSandbox.Sim.Tests/BlackmarshWatchhouseTests.cs`; manifest, `CREDITS.md` e breve `docs/BLACKMARSH_WATCHHOUSE.md`. Leggere `Simulation.Factions`, `Simulation.Npcs`, `Simulation.BreakIn`, `ScenarioFile`, `LockTests` e `BreakInTests`.
- **Dipendenze:** nessuna nuova. Riusa sistemi esistenti; niente client.
- **Lavoro circoscritto:** una sola fonte della pianta nel JSON; factory carica e avanza normalmente. Nuovi ID, proprietà coerente, chiave nel corredo iniziale, percorsi dei due attori e due accessi utili. Il test esegue comandi pubblici; il WorldView è solo un oracolo di verifica.
- **Risultato verificabile:** factory riproducibile e precedente alla razzia; baseline senza intervento con rientro e deposito del bottino; recupero del player in un secondo avvio; checkpoint comune, bilancio delle razioni comprensivo del consumo; nessuna notizia fuori vista. Un confronto con porta lasciata accessibile e porta serrata dimostra un diverso ingresso/lavoro/tempo NPC.
- **Completato quando:** test verdi e annotati coordinate, orari di incarico/ingresso/prelievo/rientro, chiavi/utensili effettivi, margine di intervento e checkpoint. Non imporre che ogni tiro casuale faccia vincere; il percorso di recupero non deve dipendere da un tiro fortunato di scasso del player. Se occorre una nuova regola, consegnare prima un test che isola il limite e una proposta minima, **senza iniziare i task UI**. Non iniettare incarichi come nelle fixture storiche di `BreakInTests`.
- **Reasoning:** **forte**, soprattutto su scheduler, selezione bersaglio, accessi e causalità. È il primo task raccomandato.

### Task 2 — Caricare l'unico ostacolo di arredo dai dati

- **Obiettivo:** rendere il tavolo una parte del percorso, senza hardcoding della sua collisione nel client.
- **File/sistemi:** `ScenarioFile.cs`, `ScenarioBuilder.cs` da riusare, `ScenarioFileTests.cs`, JSON della rimessa e test del Task 1.
- **Dipendenze:** 1.
- **Lavoro:** supportare l'elenco opzionale di coordinate `obstacles` della mappa delegando a `AddObstacle`; aggiungere il solo tavolo, senza bloccare accessi o passaggi necessari. Nessun nuovo tipo di edificio o di fisica.
- **Risultato verificabile:** posizione sul tavolo rifiutata, aggiramento possibile; round-trip del save conserva l'ingombro; JSON esistenti senza `obstacles` invariati.
- **Completato quando:** test del loader pertinenti e prove dei due percorsi della rimessa passano con l'ingombro finale; eventuali orari aggiornati. Non aggiungere una seconda copia C# della pianta.
- **Reasoning:** implementazione ordinaria con attenzione alla validazione.

### Task 3 — Collegare una seconda esperienza senza copiare VillageMap

- **Obiettivo:** avvio della nuova factory in pausa, con save/retry propri e nessuna attivazione accidentale del villaggio/pilot.
- **File/sistemi:** `VillageMap.cs`, `VillageMap.Quickbar.cs`, nuova `Scenes/BlackmarshWatchhouse.tscn`; piccolo dato di configurazione della scena se necessario.
- **Dipendenze:** 1.
- **Lavoro:** separare selezione della sessione e parametri d'ingresso da stile grafico. Una scelta esplicita, non combinazioni di booleani indipendenti. Salvare a `user://blackmarsh-watchhouse.json`, ricaricare la scena corrente al retry. Mantenere `Village.tscn` come main e i vecchi ingressi funzionanti. Nessuna UI di selezione campagne.
- **Risultato verificabile:** scena mostra lo stato del Task 1, tempo fermo finché non si riprende, due retry ricreano lo stesso snapshot. Il save path non coincide con Village o prima slice.
- **Completato quando:** build e smoke di ingresso per la nuova scena verdi; avvio dei tre ingressi preesistenti interessati verificato. Un'eventuale configurazione contiene dati, non regole di missione.
- **Reasoning:** ordinario, review mirata del bootstrap/reset.

### Task 4 — Separare identità Sim e aspetto del contenuto

- **Obiettivo:** player, Vasan e pavimenti funzionano con tutti gli ID nuovi, mantenendo gli asset esistenti.
- **File/sistemi:** `VillageMap.Visuals.cs`, `BuildMap` in `VillageMap.cs`, `VillageMap.Buildings.cs` per i controlli di stile, piccolo dato di presentazione della scena.
- **Dipendenze:** 3.
- **Lavoro:** riconoscere il player dalla sessione; risolvere stile degli NPC e materiali delle zone da una mappa locale di presentazione. Separare il profilo grafico degli interni da `InteriorArtPilot`. Rimuovere le dipendenze da coordinate del pilot soltanto dal percorso riusato. Conservare fallback e presentazione delle vecchie scene.
- **Risultato verificabile:** `bm_watch_player` usa il paladino controllato; Vasan usa il modello esistente; pavimento interno corretto senza `FarmerHouse` o `Inn`. Ruotare e muovere il player continua ad aggiornare la figura giusta.
- **Completato quando:** catture leggibili della nuova scena e regressione visuale del pilot/Village; nessuna copia di renderer, nessuna modifica alle regole Sim. I riferimenti rimasti agli ID storici sono confinati alla loro configurazione o ai loro smoke.
- **Reasoning:** ordinario con review dei percorsi alternativi delle figure, non solo quello 3D predefinito.

### Task 5 — Interagire con la cassa effettivamente selezionata

- **Obiettivo:** prelevare/depositare sul nuovo Store senza chiamarlo granaio nel codice.
- **File/sistemi:** `VillageMap.cs` (azioni e `_storeRow`), eventuale piccolo partial per UI depositi; test/smoke delle interazioni.
- **Dipendenze:** 3.
- **Lavoro:** passare il bersaglio esplicito ricavato da `VisibleStores`; far conoscere alla UI della rimessa la sua cassa senza sostituire globalmente `GranaryStore`. Testo «Prendi 4 razioni», quantità reale e rifiuti della Sim; selezione cancellata/rivalidata dopo load o perdita della vista. Nessun avvicinamento automatico ai contenitori nuovo.
- **Risultato verificabile:** fuori portata nessun trasferimento; dall'accesso prelievo di quattro al completamento; un secondo deposito non riceve per errore lo stesso comando. Conservazione e proprietà restano Sim.
- **Completato quando:** operazione riuscita attraverso handler UI della nuova scena e consegna della vecchia slice ancora corretta. Non duplicare test matematici di conservazione già presenti; verificare il collegamento al bersaglio.
- **Reasoning:** ordinario.

### Task 6 — Usare il pilot per una rimessa completa, non per un kit di edifici

- **Obiettivo:** un edificio leggibile e attraversabile da player e Vasan, con aperture realmente utilizzabili e un arredo coerente.
- **File/sistemi:** `VillageMap.InteriorArt.cs`, `VillageMap.Buildings.cs`, parti mirate di `Visuals.cs`/picking, dato di presentazione; JSON della rimessa soltanto per eventuali aggiustamenti spaziali provati.
- **Dipendenze:** 2, 4.
- **Lavoro:** collocare il tavolo sulla footprint del Task 2; riusare cassa, pareti, ante, finestra, tetto e materiali. Una lista di props al posto delle coordinate fisse. Evitare di disegnare un secondo baule sullo Store. Inquadrare l'edificio senza sottrarre accessi dietro la UI.
- **Risultato verificabile:** da fuori nessun NPC nascosto rivelato; entrando si leggono pavimento e arredo, la stanza mai vista non rivela occupanti; uscendo il tetto torna. Clic su anta/telaio avvia l'interazione giusta, Stop annulla l'avvicinamento. Player e razziatore aggirano il tavolo; il Vasan usa l'ingresso atteso dai test Sim.
- **Completato quando:** smoke grafico mirato a 1280×720, catture esterno/interno/apertura alterata, e test Task 1 aggiornati verdi. Nessun intervento su piani, prefab o generazione. Documentare le eventuali imperfezioni puramente estetiche senza inseguire la reference.
- **Reasoning:** mirato su visibilità/picking, forte solo se emerge una discordanza Sim/rendering.

### Task 7 — Dare un ingresso e una conclusione comprensibili alla situazione

- **Obiettivo:** loop giocabile senza console, con istruzioni minime e un risultato basato su ciò che si conosce.
- **File/sistemi:** piccolo partial di presentazione della rimessa; parti comuni di attesa/pausa/riepilogo oggi nei `VillageMap.Slice*`, `Deeds.cs` se necessario.
- **Dipendenze:** 5, 6.
- **Lavoro:** invito iniziale, indicazione del sentiero e della rimessa, accesso ai comandi esistenti per chiave/cassa/diario. Riutilizzare un solo controllo dell'attesa, senza codice duplicato di avanzamento. Riepilogo di razioni attuali, fatti conosciuti e scorte visibili; «Termina», «Continua», «Riprova». Nessun campo persistente successo/fallimento e nessun indicatore che segue il Vasan nascosto.
- **Risultato verificabile:** si completa il recupero via UI; il ramo rinuncia non annuncia furti non percepiti; il riepilogo non fa avanzare tempo o scontro. La vecchia slice mantiene i propri testi e la propria guardia.
- **Completato quando:** entrambe le conclusioni e continuazione funzionano, attesa interrompibile, nessun doppio clock, nessun log interpretato come autorità. Il player può sapere di portare 14 razioni senza conoscere lo stato del deposito nemico.
- **Reasoning:** medio/forte sul confine informativo e sul riuso del controllo temporale; ordinario per i testi.

### Task 8 — Verificare persistenza delle nuove conseguenze

- **Obiettivo:** save/load/retry affidabili con questa pianta, proprietà e aperture.
- **File/sistemi:** nuovo smoke di persistenza della rimessa, punti di load/reset in `VillageMap.cs`; `SaveGame` solo se un difetto riprodotto lo rende necessario.
- **Dipendenze:** 7 e bootstrap del 3.
- **Lavoro:** save dopo un passo reale, durante un'azione Sim di prelievo, dopo un'apertura/effrazione e dopo l'esito; ripresa senza doppio cibo. Confronto col ramo continuo allo stesso checkpoint. Due retry reali e file corrotto. Verificare che un file di un'altra mappa non venga applicato a geometria già costruita incompatibile: basta rifiuto leggibile, non un caricatore multi-mappa.
- **Risultato verificabile:** stato di porte/vetri, posizioni, conoscenza e cibo conservati; nessun ordine fantasma, nessuna scrittura dei save Village/slice. Il vecchio limite degli intenti UI «cammina poi interagisci» è esplicito: non prometterne la persistenza implicita; dopo load non deve partire una vecchia interazione su un'altra apertura.
- **Completato quando:** tutti i casi passano senza migrazione schema o preroll ripetuto; smoke ripristina eventuali save dell'utente. Un limite non verificato rimane dichiarato, non convertito in PASS.
- **Reasoning:** medio, con review del reset e dei confini della mappa.

### Task 9 — Provare i due esiti composti e il riuso senza fixture ingannevoli

- **Obiettivo:** dimostrare che i pezzi dei task precedenti funzionano insieme dall'avvio della nuova scena.
- **File/sistemi:** nuovo `BlackmarshWatchhouseSmoke.cs`, hook UI minimi; test Task 1 e smoke esistenti.
- **Dipendenze:** 8 e tutti i task precedenti.
- **Lavoro:** due scene iniziali reali, handler usati dal giocatore, movimenti e aperture, recupero/ritiro e mancato recupero, load e retry. Niente scritture a WorldState, incarichi, JSON di save o teletrasporti. Assert onniscienti ammessi per causalità/conservazione, mai nella UI. Nel percorso previsto l'NPC deve effettuare almeno un'interazione fisica con l'edificio.
- **Risultato verificabile:** risultati distinti e tempi registrati; il ramo che non osserva non acquisisce informazioni; screenshot di ingresso, interno, effrazione osservata, recupero e conclusione. Controllo degli ID nuovi e assenza di etichette/azioni del villaggio. Smoke fallisce anche se il loop non avviene, non soltanto in caso di crash.
- **Completato quando:** due esecuzioni pulite riuscite, una a 1280×720 e una a 1920×1080; build/suite verdi; smoke della prima slice e ART PILOT passano. Verificare su un checkout senza i file locali `Body.*` come dipendenze. Distinguere test degli handler da picking manuale e test Sim causale da tempi umani.
- **Reasoning:** medio, soprattutto qualità degli oracoli e sincronizzazione.

### Task 10 — Collaudare il secondo contenuto e decidere cosa generalizzare dopo

- **Obiettivo:** capire se questa situazione si gioca e se l'edificio cambia le decisioni, non se mostra più comandi.
- **File/sistemi:** README, launcher dedicato, `docs/BLACKMARSH_WATCHHOUSE.md`, piccola scheda di playtest e questo documento. Eventuali correzioni locali UX solo dopo osservazione.
- **Dipendenze:** 9.
- **Lavoro:** una persona che non conosca il percorso avvia, individua rimessa/chiave/cassa, prova un esito e riprova l'altro. Registrare tempi, esitazioni, comprensione della porta/finestra e di chi possiede le razioni. Confrontare il risultato con gli obiettivi del §7, non con la bellezza del concept.
- **Risultato verificabile:** 5–8 minuti circa per un giro, nessun aiuto via inspector, spiegazione con parole proprie di come accessi e tempo hanno influenzato l'esito. Avvio documentato con prerequisiti, un solo comando/launcher; nessuna dipendenza da un save preesistente.
- **Completato quando:** risultati effettivi annotati. Se manca il tester, dichiarare completata solo la parte tecnica e lasciare aperto il criterio umano. Un coding agent non può sostituirlo con il cronometro dello smoke.
- **Reasoning:** non richiede un modello forte; richiede osservazione umana. Non estendere il contenuto per compensare un problema di leggibilità.

## 9. Verifiche eseguite in questa analisi e limiti

```powershell
dotnet test tests/RpgSandbox.Sim.Tests/RpgSandbox.Sim.Tests.csproj --no-restore --verbosity minimal
dotnet build RpgSandbox.sln --no-restore --verbosity minimal
& 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --rendering-method gl_compatibility --resolution 1280x720 --path game res://Scenes/InteriorArtPilot.tscn -- --art-pilot-smoke=C:/Progetti/rpg/.tmp/blackmarsh-analysis/art-pilot
```

Risultati: **656/656 test, build 0/0, ART PILOT OK**. SDK .NET 8.0.412 e Godot 4.7.2 .NET locali. L'esecuzione grafica è stata autorizzata fuori sandbox. L'estrazione del testo RTF per lettura è in `.tmp/blackmarsh-analysis/blackmarsh-v11.txt`; testo e screenshot sono artefatti locali ignorati, non nuovi contenuti di gioco.

Stato Git iniziale: soltanto gli otto file `Body.*`/texture/import non tracciati già presenti, nessuna modifica tracciata. Ho ispezionato la storia e il follow-up della review della prima slice. Non ho rieseguito in questa sessione tutti gli smoke della prima slice né una partita umana di combattimento; non li conto come nuove verifiche.

**Non sono ancora verificati**: orari e percorsi della rimessa proposta, bilanciamento della finestra di recupero, corretto innesco della nuova razzia, comportamento dell'NPC nei nuovi interni, durata umana. Sono precisamente le prove assegnate ai task, a partire dal primo. Nessun successo del pilot autorizza a dichiararle già funzionanti.

## Primo task consigliato

**Prossimo task: Task 7, ingresso e conclusione della situazione.** I Task 1–6 hanno dimostrato il loop nella Sim, l'ingresso, la resa, il prelievo mirato e la rimessa attraversabile. Mancano istruzioni e conclusione comprensibili al giocatore; nessun criterio umano è certificato.

## Esito del Task 1 — 23 ottobre 2026

Completato senza regole Sim nuove: nessun controllo sugli ID Blackmarsh nel motore, nessuna scrittura di `Assignment` nei test. Dettagli, pianta e orari in `docs/BLACKMARSH_WATCHHOUSE.md`.

- **Contenuto:** `blackmarsh-watchhouse.json` (18×14, unica fonte della pianta), `blackmarsh/items.json` (sola chiave), manifest, `CREDITS.md` con l'attribuzione CC BY 4.0 di Blackmarsh v11. Tutti gli ID `bm_watch_*`, protagonista compreso; riusati solo i profili `paladin` e `raider`.
- **Factory:** `BlackmarshWatchhouse.CreateSession()`, apertura alle **19:57**. Ho anticipato di un minuto le 19:58 candidate: con le 19:58 il margine misurato era di circa 1:50.
- **Razzia autonoma:** consumo dei Vasan alle 19:59 (4 → 0), ordine della politica di fazione alle 20:00. Con la porta serrata il predone rompe il vetro della finestra bloccata (nessuna prova, deterministico), scavalca e si taglia, è alla cassa alle 20:00:16, ruba alle 20:03:16 e deposita all'approdo alle 20:05:28.
- **Recupero:** partendo subito il prelievo finisce alle 20:00:10, si torna al sentiero alle 20:00:20 e il predone rientra a mani vuote. Il recupero riesce anche partendo alle 19:59:45, quasi tre minuti dopo l'apertura.
- **Aperture:** con la porta lasciata aperta il predone entra dalla porta, il vetro resta intatto e arriva alla cassa 11 secondi più tardi (20:00:27; 20:00:29 con il tavolo del Task 2).
- **Checkpoint comune alle 20:30:**
  - recupero: protagonista 14, rimessa 0, provviste dei Vasan 0;
  - rinuncia: protagonista 10, rimessa 0, provviste dei Vasan 4.
  
  Conservazione verificata includendo il consumo. Il protagonista sul sentiero non acquisisce osservazioni.
- **Verifiche:** 5 test in `BlackmarshWatchhouseTests`, suite Sim **661/661**. Mutazione: con la porta non serrata nel JSON falliscono i due test sull'ingresso.
- **Limiti:**
  - Un NPC in cammino può ancora sovrapporsi allo spazio del protagonista (limite noto); il protagonista invece è respinto da un accesso occupato.
  - Gli attrezzi da scasso del predone non entrano in gioco con questa pianta.
  - Il margine umano resta da misurare nel Task 10.

## Esito del Task 2 — 23 ottobre 2026

- **Loader:** `ScenarioFile` legge l'elenco opzionale `obstacles` di una mappa (caselle `[x, y]`) e lo passa a `AddObstacle`, senza nuovi tipi. Il villaggio senza `obstacles` resta identico a quello costruito in codice (test esistente).
- **Validazione:** il builder ora rifiuta un ostacolo ripetuto o sulla casella di un deposito. Le regole esistenti rifiutavano già un ostacolo nel muro, su una porta, su un accesso o sotto un attore; un test copre ciascun caso.
- **Contenuto:** un solo tavolo a (10,6), nella stanza della cassa, sotto la lampada. Non blocca la vista, le porte o l'accesso (11,5).
- **Risultati verificati:**
  - posizione sul tavolo rifiutata; il percorso dalla porta interna alla cassa lo aggira;
  - dopo save/load ingombro e percorso sono identici;
  - test del Task 1 verdi. L'unico orario cambiato è l'arrivo del predone dalla porta aperta (20:00:29, furto alle 20:03:29); il ramo della finestra e il recupero non cambiano.
- **Verifiche:** suite Sim **670/670**. Mutazione: senza la lettura di `obstacles` nel loader falliscono 8 test.

## Esito del Task 3 — 23 ottobre 2026

- Scena `game/Scenes/BlackmarshWatchhouse.tscn` collegata direttamente alla factory `BlackmarshWatchhouse.CreateSession()`. Ingresso alle 19:57 in pausa, protagonista a (1,6), 10 razioni, rimessa con 4. Nessun salvataggio o scena Village necessario per avviarla; `Village.tscn` resta la scena principale.
- Un solo enum esportato `Entry` seleziona i cinque ingressi (Village, prototipo, pilot, prima slice, Blackmarsh). I parametri d'ingresso — orientamento, preroll, pausa e percorso di salvataggio — sono separati nella configurazione del client. Il flag storico `--interior-prototype` resta compatibile soltanto sull'ingresso Village. Le proprietà derivate per la grafica legacy restano fino al Task 4, senza aggiungere booleani indipendenti alle scene.
- Blackmarsh usa `user://blackmarsh-watchhouse.json`; «Riprova» nel menu ricarica la scena corrente. Lo smoke dedicato confronta il salvataggio completo dell'ingresso con due reload reali, verifica che otto frame in pausa non lo alterino, che il tempo riprenda, che il salvataggio Blackmarsh non tocchi quelli Village/slice e ripristina l'eventuale save Blackmarsh precedente.
- Review Task 1–2: test mirati 23/23 e suite Sim completa 670/670; nessun difetto bloccante trovato. Build soluzione 0 errori/0 avvisi. Smoke OpenGL a 1280×720: BLACKMARSH ENTRY, VILLAGE, SLICE ENTRY, INTERIOR ART PILOT e INTERIOR PROTOTYPE tutti verdi. La cattura `entry-paused.png` è stata ispezionata: pausa e stato sono leggibili; figure/inquadratura/arredo di Blackmarsh sono ancora provvisori, da trattare nei Task 4 e 6. Nessun criterio umano certificato e nessun Task 4 avviato.

## Esito del Task 4 — 23 ottobre 2026

- Nuovo profilo client di presentazione per ingresso: materiali/colori dei luoghi e stili degli attori sono mappati localmente. Blackmarsh assegna pietra alla rimessa, terreno al sentiero, strada all'approdo e stile Raider al Vasan. Gli ID del villaggio usati per la resa sono confinati a questa configurazione; per ogni scena il protagonista è identificato tramite `_sim.Player`.
- Pareti, tetto, aperture, illuminazione e risposta del tetto agli interni usano il profilo grafico condiviso `StyledInterior`, che vale per pilot e Blackmarsh. Le decorazioni e la camera specifiche del pilot rimangono specifiche del pilot; tavolo, props e composizione finale dell'inquadratura sono ancora Task 6. Nessuna collisione grafica aggiunta o regola Sim cambiata.
- Smoke Blackmarsh ampliato: controlla il vero tile con texture di pietra, il paladino live dopo rotazione e movimento, le alternative atlante/sprite, il Vasan visibile col modello 3D Raider e le due ripartenze. Catture `entry-paused.png` e `vasan-visible.png` ispezionate; il tetto e la figura sono leggibili, ma la scena non è ancora arredata e l'inquadratura ravvicinata al predone mostra sovrapposizioni di etichette, da affrontare nella composizione del Task 6.
- Verifiche: build soluzione 0 errori/0 avvisi, Sim 670/670, smoke OpenGL BLACKMARSH ENTRY, INTERIOR ART PILOT, SLICE ENTRY e VILLAGE completi. Catture del pilot e Village ispezionate senza regressioni evidenti. Nessun Task 5/6 avviato né collaudo umano dichiarato.

## Esito del follow-up Task 4 e del Task 5 — 23 ottobre 2026

- Follow-up review Claude: ripristinata tramite `FloorLook.WarmTint` la tinta calda anche sulle caselle in legno del pilot; gli ID e la coordinata del pilot restano nella configurazione di presentazione. Smoke e cattura del pilot verificati. La dicitura HUD «Sulla strada» vicino all'approdo e l'inquadratura/etichette restano al Task 6, come suggerito nella review.
- La barra depositi è ora un pannello visibile separato dalla vecchia barra esplorazione nascosta dalla quickbar. Elenca i soli `VisibleStores` della mappa, mostra il nome del deposito scelto e il comando «Prendi 4 razioni» all'ingresso Blackmarsh. Il bersaglio `StoreId` passa esplicitamente ai comandi `TakeFoodCommand`/`DepositFoodCommand`; non esiste un fallback verso il granaio. Portata, quantità, tempi e proprietà rimangono alla Sim; nessun avvicinamento automatico nuovo.
- Quando il deposito scelto esce dalla vista, la selezione si cancella. Dopo il follow-up del Task 6, se al ritorno ne è visibile uno solo viene preselezionato; la soppressione resta solo dopo la scelta esplicita «Seleziona deposito». Dopo un load la UI riparte dalla nuova vista: con un solo deposito visibile può selezionarlo, con due visibili richiede una scelta esplicita e disabilita i pulsanti. Un comando senza scelta viene rifiutato dal client; il pulsante fuori portata riceve il rifiuto della Sim senza trasferimento o movimento.
- Smoke OpenGL `--blackmarsh-store-smoke`: porta esterna con chiave, porta interna, cassa vista da una casella fuori accesso, rifiuto, perdita della vista, preselezione dell'unico deposito al ritorno e prelievo dall'accesso tramite il vero pulsante. Risultato player 10→14, rimessa 4→0, nessun deposito Vasan usato dal comando; il suo cibo può scendere autonomamente alle 19:59 per consumo di fazione. Save/load all'accesso con due depositi visibili richiede la scelta; load sul sentiero cancella la selezione. Catture `too-far.png`, `taken-four.png`, `choose-store-after-load.png` ispezionate.
- Regressioni: smoke `SLICE GUIDE` (consegna 3 razioni), `VILLAGE` completo e `INTERIOR ART PILOT` verdi a 1280×720; build soluzione 0 errori/0 avvisi, Sim 670/670. Nessun Task 6 avviato e nessun collaudo umano dichiarato.

## Esito del Task 6 e follow-up review Task 5 — 23 ottobre 2026

- Arredo ora definito come lista nel profilo di presentazione: i cinque props del pilot restano invariati; Blackmarsh usa solo il tavolo a (10,6), esattamente sull'ostacolo della Sim. La cassa esistente dello Store non è duplicata. Nessuna collisione o regola Sim introdotta nel client e nessun JSON cambiato.
- Camera Blackmarsh spostata per mostrare la rimessa completa all'ingresso a 1280×720, senza coprire porta e finestra con la hotbar. Il pannello depositi è più compatto, allineato al registro e sopra la hotbar. Presso l'approdo l'HUD usa «Presso l'approdo» anziché «Sulla strada»; la didascalia del Vasan è rialzata per separarla dalla figura vicina.
- Follow-up Claude: perdere un deposito dalla vista cancella la scelta senza disabilitare permanentemente la preselezione dell'unico deposito; «Seleziona deposito» scelto manualmente continua a lasciare i pulsanti disabilitati. `PreviewCombatForSmoke` nasconde il pannello intero. Smoke depositi adeguato e verde.
- Nuovo smoke OpenGL `--blackmarsh-building-smoke` a 1280×720: esterno senza tavolo o NPC nascosti, porta con chiave e anta visibile, tetto rimosso entrando, stanza chiusa ancora nascosta, tavolo e cassa leggibili dopo l'apertura interna, tentativo di entrare nella casella del tavolo rifiutato dalla Sim, picking del telaio finestra e Stop che cancella l'avvicinamento, tetto ripristinato uscendo. Catture `outside.png`, `door-open.png`, `inside.png`, `outside-after-visit.png` ispezionate. I test Task 1 confermano i due ingressi del Vasan e ora asseriscono che entrambi i suoi percorsi evitano il tavolo.
- Verifiche effettive: Sim **670/670**, build soluzione **0 avvisi/0 errori**, smoke OpenGL BLACKMARSH BUILDING/STORE/ENTRY, INTERIOR ART PILOT, SLICE GUIDE e VILLAGE completi; catture Blackmarsh e pilot esaminate, `git diff --check` pulito. Il Task 7 non è iniziato.
- Limiti estetici osservati, non bloccanti: parte esterna alla pianta ancora grigia/nera, prima stanza volutamente scarna; la didascalia rossa del Vasan resta poco contrastata sul terreno scuro. Il click manuale a coordinate schermo e la durata umana non sono certificati dallo smoke.
