# Review Codex — T5 ridotta e T4

10 ottobre 2026, commit esaminato `b49157c`. Letti gli aggiornamenti di `chat.txt`, inclusi il completamento del client durante l'assenza di Codex, la scelta del Paladino e le indicazioni sulla percezione e sulla guardia. Questa è una review: nessuna modifica al codice di produzione.

## Risultati verificati

- Suite della soluzione: **163/163 test passano**.
- Build della soluzione: zero errori e zero warning. Smoke con errore deliberato: **SMOKE FAIL, exit 1**, senza falso successo.
- Smoke originale T4/T5 eseguito in Godot: **SMOKE OK, exit 0**, inclusi dialogo, tiro del Paladino, caricamento a metà viaggio e pannelli. Evidenze in `tools/review/t4client`.
- Persistenza: **2.500 checkpoint** con comandi variati deterministicamente, confronto dello snapshot completo tra sessione continua e sessione ricaricata dopo Execute/Advance: nessuna divergenza, incluso RNG. Coperti furti, testimonianze, rapporti, vigilanza, presidi, confische e restituzioni.
- I fix precedenti per cancellazione, scadenze, PlayerView, ripresa del playback e scorrimento sono presenti. Il nuovo esito `Cancelled` viene gestito dal client come conclusione dell'attesa.
- Non trovati difetti numerici nel generatore, nei dadi, nei bonus o nelle schede predefinite. Questa verifica non equivale a una nuova revisione legale dell'attribuzione SRD.

Gli harness locali sono in `.review/t4-domain`, `.review/persistence_t4` e `tools/review/rules-t4`. Non sono parte della suite versionata.

## Difetti riprodotti

### T4-R1 — P2: due testimoni fanno pagare due volte lo stesso furto

Riferimento: [Simulation.Npcs.cs:143](../src/RpgSandbox.Sim/Simulation.Npcs.cs#L143).

Ogni testimone riceve un'osservazione con un proprio `Origin`. `OrganiseGuard` crea un debito per ciascun Origin, senza distinguere più testimonianze dello stesso fatto da furti diversi. Anche dopo il pagamento, una nuova testimonianza può ricreare il debito già estinto.

Riproduzione con scenario valido costruito tramite API: deposito con 40 razioni, giocatore con 10, guardia e un altro membro della fazione nello stesso luogo; furto di 3 razioni a mezzogiorno. Con la sola guardia testimone il giocatore torna a 10 e il deposito a 40. Aggiungendo un testimone, il giocatore finisce a **7**, il deposito a **43** e la cronaca contiene due confische di 3 per quell'unico furto.

È un caso multi-testimone: il percorso standard del furto del giocatore sotto gli occhi del solo contadino non lo copre. Non sono necessari salvataggi modificati o stati interni.

Correzione proposta: associare il debito al fatto di furto già identificato internamente da `Observation.Fact`, con memoria degli esiti anche dopo il pagamento. Conservare le diverse testimonianze come conoscenze, senza sommarne le quantità. L'identificatore non deve diventare informazione onnisciente esposta al giocatore. Test: due rapporti dello stesso furto, secondo rapporto dopo il pagamento, due furti realmente distinti.

### T4-R2 — P2: la restituzione usa anche cibo personale e perde il deposito di destinazione

Riferimento: [Simulation.Npcs.cs:187](../src/RpgSandbox.Sim/Simulation.Npcs.cs#L187).

`ReturnStep` si applica a qualsiasi NPC con cibo e fazione dotata di HomeStore. Non controlla che sia un'autorità o che trasporti razioni confiscate. La destinazione è sempre HomeStore, ignorando il deposito a cui si riferiva il debito.

Due riproduzioni con scenari validi:

- Un contadino che parte con 5 razioni proprie e nessun furto in memoria sceglie immediatamente «Consegna di 5 razioni» e resta a zero: il cibo personale è interpretato come bottino recuperato.
- Una fazione possiede due depositi da 40. Il giocatore ruba 3 dal deposito secondario; la guardia le confisca e le versa nel deposito principale. Risultato: **deposito derubato 37, principale 43**.

La slice attuale ha un solo deposito per fazione e gli abitanti partono senza cibo: questi limiti nascondono il difetto, ma non sono vincoli di ScenarioBuilder.

Correzione proposta: stato esplicito del carico da restituire, con quantità e deposito destinatario, separato dal cibo personale. Persistenza e regola di ritorno devono usare quel carico. Limitare la regola all'autorità da solo non risolve la destinazione né l'eventuale cibo personale della guardia.

### T4-R3 — P3: alcune schede accettate dal builder non si possono ricaricare

Riferimenti: [Invariants.cs:33](../src/RpgSandbox.Sim/Invariants.cs#L33), [SaveGame.cs:100](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L100), [SaveGame.cs:204](../src/RpgSandbox.Sim/Persistence/SaveGame.cs#L204).

`CharacterSheet.Commoner("")` e una scheda con `SkillProficiencies = new[] { (Skill)999 }` passano Build/Create/Save, ma TryLoad rifiuta il file appena prodotto. Il loader controlla titolo ed enum, mentre la validazione condivisa del builder non lo fa. Non riguarda il Paladino o le altre schede predefinite.

Correzione proposta: allineare le invarianti e rifiutare questi valori già durante la costruzione dello scenario, con test builder → save → load.

### T5-R4 — P2: Carica conserva il testo della conversazione precedente

Riferimenti: [Main.cs:501](../game/Scripts/Main.cs#L501), [Main.cs:351](../game/Scripts/Main.cs#L351).

La cache del pannello confronta gli ID e il gesto, non i contenuti, e il caricamento non invalida `_talkKey`. Riproduzione con due partite dello scenario normale: nella prima il giocatore ha assistito al furto di 8 razioni e apre la conversazione con il contadino; nella seconda ha prelevato 37 razioni prima della razzia, per cui assiste al furto delle sole 3 rimaste. Caricando la seconda, l'API restituisce correttamente l'argomento da 3, ma il pulsante continua a mostrare **8**. Premendolo si riferirebbe un contenuto diverso da quello mostrato.

Correzione proposta: chiudere o ricostruire la conversazione dopo il caricamento e invalidare la cache della sessione precedente. Prova: [screenshot](../tools/review/t4client/9-stale-talk-after-load.png) e `tools/review/t4client/runtime.log`.

### T4-R5 — P2: la mappa rivela l'identità che la percezione notturna nasconde

Riferimenti: [SimulationSession.cs:168](../src/RpgSandbox.Sim/Api/SimulationSession.cs#L168), [SimulationSession.cs:203](../src/RpgSandbox.Sim/Api/SimulationSession.cs#L203), [SimulationSession.cs:222](../src/RpgSandbox.Sim/Api/SimulationSession.cs#L222).

VisibleActors e PeopleHere non considerano la luce o la percezione; il client scurisce soltanto il disegno. In una variante di scenario con razzia alle 01:33, il giocatore al granaio sente il furto e riceve un'osservazione con `Thief == null`. Con debug spento, la mappa mostra però il personaggio rosso etichettato **Razziatore** proprio al deposito. Nome e gesto sono esposti anche durante il furto. Prova: [screenshot](../tools/review/t4client/10-night-hidden-thief-revealed.png).

La visibilità per area era dichiarata nella slice, ma ora aggira il nuovo sistema di identità notturna. Correzione proposta: rendere coerenti entità identificate, sagome/eventi percepiti e interlocutori disponibili. La mappa comune può restare nota; i dati live degli attori devono rispettare le condizioni che decidono se sono visti e riconosciuti. Se si vuole una mappa volutamente onnisciente, va presentata come tale e separata dalla vista del personaggio.

### T4-R6 — P3: il diario descrive come visto un furto soltanto sentito

Riferimento: [Main.cs:485](../game/Scripts/Main.cs#L485).

`Source == null` viene sempre reso come «l'hai visto tu». Il caso notturno precedente produce quella frase anche se il giocatore non ha visto nessuno. La modalità percettiva non è conservata nell'osservazione. Correzione minima: formulazione neutra «te ne sei accorto direttamente»; soluzione completa: persistere e mostrare la distinzione visto/sentito, conservandola nei rapporti.

## Risposte alle indicazioni lasciate da Claude

**T5, P1/P2.** Confermo la direzione della T5 ridotta: distinguere una segnalazione alla guardia dalla vigilanza del contadino produce conseguenze verificabili. I valori di confronto aggiornati sono quelli del giorno 4 alle 11:00: 32 / 24 / 16. La regola «gli NPC non portano voci alla guardia» è una politica provvisoria del villaggio, non una regola universale da estendere automaticamente a tutti i personaggi. La distinzione tra esperienza diretta e sentito dire va mantenuta.

**T4, percezione durante i tre minuti.** Una prova memorizzata è corretta per evitare nuovi tiri a ogni frame. L'implementazione attuale valuta però soltanto la luce e le presenze al completamento. Con seme 2, Paladino furtività 7 e Contadino percezione passiva 11: furto 18:55–18:58 riconosciuto; furto 18:58–19:01 ignorato, nonostante i primi due minuti in piena luce. È coerente con la precedente semplificazione del «momento del fatto», ma ora ha conseguenze di gameplay rilevanti. Propongo di registrare ciò che viene percepito durante il tentativo, senza ritirare dadi. Se si conserva la valutazione finale, occorre descrivere esplicitamente che l'atto di sottrarre avviene soltanto in quell'istante: non dichiararlo come osservazione continua dell'azione.

**T4, udito e luce fioca.** Il piano viene seguito alla lettera, ma produce un effetto da rivedere: con gli stessi valori, alle 19:05 il furto non è notato (solo vista penalizzata), alle 20:05 viene sentito (udito senza penalità). La luce fioca non dovrebbe disabilitare l'udito. Propongo di valutare separatamente udito e vista: il primo può far notare l'evento, soltanto la seconda consente di vedere l'attore. Questo è un problema dell'adattamento concordato, distinto da un'implementazione difforme dal piano.

**T4, guardia che non cerca.** È un limite dichiarato, non un bug nascosto. Accetto per questo incremento che non insegua né conosca a distanza la posizione del ladro. Non considererei invece definitiva la reazione solo alla fine di un turno fino a un'ora: un ladro può passare davanti alla guardia e andarsene prima del controllo. Proposta successiva: evento di arrivo/presenza che renda rivalutabile la confisca, senza teletrasporto e senza cancellare il presidio in modo improprio. La descrizione deve dire «interviene quando rivaluta e ti trova lì», finché questo è il comportamento reale.

**T4, identità come etichetta.** Confermo che resta una semplificazione nota: visto e riconosciuto non equivalgono a conoscere automaticamente il nome. Non richiedo un sistema completo di familiarità per chiudere questa slice. Evitare però di aggiungere nuove rivelazioni d'identità attraverso UI e dati che aggirino le regole notturne.

**T4, confisca e provenienza.** Il codice implementa un debito recuperabile sul cibo posseduto, non la tracciatura delle specifiche razioni rubate. Restituire volontariamente il cibo non estingue il debito e un acquisto successivo può essere confiscato: il test esistente conserva esplicitamente il debito. Questa politica deve essere dichiarata come tale. La verifica «non ha più quelle razioni» non può essere dedotta dal solo totale dell'inventario. T4-R1 e T4-R2 vanno comunque corretti, indipendentemente dalla politica scelta.

## Documentazione e prossima verifica

Il contratto ha una sezione T4 corretta in fondo, ma conserva più sopra la regola «chi è presente vede il furto» senza eccezioni e un elenco incompleto delle priorità NPC. «Oltre la slice» presenta ancora dadi/furtività come futuri. Consolidare le descrizioni per evitare due contratti diversi nello stesso file.

Prima di dichiarare chiusa T4, aggiungere le regressioni dei debiti e della restituzione e correggere la vista notturna; per T5 resta la conversazione dopo Carica. Verificare poi suite e smoke sul risultato. Le prove di roundtrip e la suite attuale sono positive, ma non coprono queste nuove interazioni multi-testimone e multi-deposito.

Ripartizione proposta: Codex sul pannello/cache e testo del diario (R4/R6); Claude su debiti, restituzioni, invarianti e filtro del PlayerView (R1/R2/R3/R5). La modalità percettiva completa richiede un accordo API prima dei cambiamenti al client. Nessuno di questi nuovi fix è stato iniziato durante la review.

## Aggiornamento dopo i fix concordati

Claude ha implementato D1–D10 in `cbd92e1` e messo a disposizione `ObservationView.Perceived` e `PlayerView.UnseenNearby` (schema 6). Codex ha completato R4 e R6 nel client:

- Carica chiude la conversazione della sessione precedente e invalida la cache dei pulsanti. La chiave tiene conto anche dei testi e dei nomi; aprendo il dialogo nella nuova sessione si vedono gli argomenti nuovi.
- Il diario distingue «l'hai visto tu» e «l'hai sentito tu». Nei racconti ricevuti specifica se il primo testimone ha visto o sentito il furto, senza attribuire l'esperienza diretta al messaggero.
- `UnseenNearby` compare come presenza non identificata nel pannello: «Senti qualcuno vicino, ma è troppo buio per capire chi».
- Lo smoke ora riproduce due salvataggi con ID uguali ma furti da 8 e 3 razioni, una testimonianza uditiva notturna e una persona vicina invisibile. Screenshot in `tools/review/client-r4-r6/`.

Verifica finale del 10 ottobre: build del client senza warning/errori, **176/176 test**, smoke Godot **SMOKE OK / exit 0**. L'harness originale di Codex conferma R1/R2 corretti: con uno, due o tre testimoni il giocatore torna a 10 razioni e il deposito a 40; il contadino conserva le sue 5 razioni personali; due depositi della stessa fazione tornano entrambi a 40 dopo restituzione. Le osservazioni originarie delle sezioni precedenti restano come storia della review, non come difetti ancora aperti.
