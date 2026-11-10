# Blackmarsh — analisi delle prestazioni delle mappe grandi

10 novembre 2026. Base analizzata: `fdf32bf`. Analisi e misure, senza correzioni al gioco. Letti gli ultimi messaggi di `chat.txt`, il piano spaziale, il benchmark, il pilot 6B e i percorsi di rendering/percezione/movimento. I sorgenti, gli scenari, i launcher e le impostazioni del progetto non sono stati modificati. Le sonde diagnostiche e i risultati grezzi sono in `.tmp/performance-analysis-20261110/`, esclusi dal normale versionamento.

## Verdetto

**Non emerge un limite che obblighi il gioco a usare mappe minuscole. Emergono costi evitabili del renderer di prototipo e un problema di scalabilità nella costruzione della vista.** Il portatile influenza i risultati, ma attribuirgli il problema principale sarebbe prematuro: sullo stesso hardware, nella stessa scena, togliere dalla presentazione il solo pavimento porta circa 53 → 117 fps. La simulazione rimane presente.

Il 2D non rende economica qualunque implementazione: qui il pavimento è composto da molti oggetti disegnati separatamente, il client aggiorna frequentemente strutture estese alla mappa intera e la sfumatura della visione viene calcolata sulla CPU. Inoltre i personaggi sono modelli 3D renderizzati in texture, benché questa non sia risultata la causa dominante nel caso misurato.

**Raccomandazione:** mantenere l'obiettivo di aree esplorabili grandi. Prima di aumentare ulteriormente contenuti e dimensioni, dedicare un incremento circoscritto a terreno raggruppato, aggiornamenti della visione e costo della vista Sim. Conservare Sim autoritativa, mondo persistente e renderer locale sostituibile.

## Hardware e condizioni

Rilevati via Windows e log Godot:

- CPU AMD Ryzen 7 5800H, 8 core / 16 thread.
- GPU usata: AMD Radeon integrata. Non risulta una seconda GPU dall'inventario interrogato.
- RAM visibile: 16.542.109.696 byte, circa 15,4 GiB.
- Display: 2880×1800 a 90 Hz; prove in finestre 1920×1080 e 1280×720.
- Profilo energetico «ASUS Recommended»; batteria all'86%, `BatteryStatus=1` al rilevamento. Nessun profilo modificato.
- Godot 4.7.2 mono, OpenGL Compatibility. Build del client Debug come nei launcher correnti. Sim misurata separatamente anche in Release.

Le prove sono sequenziali, senza benchmark Sim concorrente a quelli grafici. Non sono stati misurati temperatura, frequenze sotto carico, potenza assorbita o GPU time mediante trace. Non c'è un confronto controllato alimentatore/batteria. Non possiamo stabilire perché la finestra del 9 novembre fosse molto più lenta, né escludere effetti di alimentazione, carico esterno, driver o presentazione delle finestre. Non serve acquistare hardware per la prossima verifica.

## Cosa dimostravano, e cosa non dimostravano, le prove precedenti

In chat: pilot 48×32 popolato circa 24 fps a 1080p; cambio della maschera visiva utile agli scatti, non al frame rate; culling del pavimento 8×8 e viewport aggiornati solo al cambio posa senza vantaggio significativo, poi rimossi. Questi esiti storici rimangono tali.

Il benchmark esistente in `VillageMap.RegionMapBenchmark.cs` ha limiti rilevanti:

1. La fixture vuota **non dichiara interni**. `GridMap.UsesExploration` dipende dalla presenza di zone interne: quella fixture non percorre il ramo di esplorazione del pilot. Parte anche a mezzanotte senza lampade. Nelle nuove misure stampa `sight-cells=0` e nessuna texture di maschera. Non è una prova equivalente di percezione su una mappa grande.
2. I frame vengono misurati **in pausa**, dopo il movimento e i cambi di sguardo. Non misurano la fluidità mentre si cammina, ruota lo sguardo o accelera il tempo.
3. Quattro rotazioni e 60 frame sono una finestra breve. JIT, compilazioni iniziali e oscillazioni della macchina possono pesare; manca una distribuzione dei tempi dei frame.
4. `simFacingMs` misura `FacePlayer`, mentre `clientFacingMs` include `Refresh`, che richiama `GetPlayerView`. Quindi «tempo refresh» non significa esclusivamente rendering o codice client: contiene anche costruzione della vista nella Sim.
5. `GC.GetTotalMemory(false)` prima/dopo misura una differenza di heap vivo, non le allocazioni cumulative. Può risultare negativa dopo una raccolta. Non misura memoria GPU o tutte le risorse native.
6. Conteggio nodi e fps non identificano da soli il collo di bottiglia. Un test funzionale verde non certifica prestazioni sufficienti.

Il fallimento del vecchio culling non confuta il batching: **nascondere blocchi fuori camera e raggruppare le chiamate di disegno delle celle visibili sono due interventi diversi**.

## Nuove misure grafiche

### Benchmark esistente, senza cambi al sorgente

Una esecuzione per combinazione a 1080p; i numeri non sono una promessa prestazionale:

| Scena | Sincronizzazione normale | VSync disabilitato, nessun limite fps |
|---|---:|---:|
| Fixture vuota 20×14 | 90,0 fps | 230,1 fps |
| Fixture vuota 80×60 | 69,7 fps | 76,4 fps |
| Pilot popolato 48×32 | 49,0 fps | 53,0 fps |

Il vecchio dato di 24 fps non è riprodotto in queste condizioni. Il VSync limita chiaramente il caso leggero; disabilitarlo non risolve da solo il pilot.

Nel pilot, quattro rotazioni: circa **189–194 ms**, con `FacePlayer` 2,6–2,9 ms complessivi, `Refresh` 186–191 ms e maschera visiva 105–107 ms. Una sonda successiva con 24 rotazioni registra **46,5 ms medi per cambio sguardo**, p95 60,9 ms. Questi tempi sincroni possono produrre scatti anche quando gli fps da fermo sembrano accettabili.

### Isolamento dei componenti

Sonda GDScript esterna: carica la vera scena/pilot, lascia riscaldare, misura circa cinque secondi; disattiva un solo componente a runtime per processo. Nessuna modifica allo stato Sim per fabbricare un risultato. VSync disabilitato. La baseline ripetuta e la prova senza pavimento hanno identiche camera `(56,640)` e zoom `1.08`.

| Caso 1080p | fps medi | Frame p95 | Draw call medie |
|---|---:|---:|---:|
| Pilot in pausa, baseline ripetuta | 53,3 | 23,2 ms | 2.188 |
| Solo pavimento nascosto | **116,7** | **11,3 ms** | **872** |
| Intera presentazione locale nascosta | 383,1 | 3,8 ms | 123 |
| SubViewport 3D disabilitati | 50,8 | 26,4 ms | 2.134 |
| HUD nascosto | 54,1 | 23,3 ms | 2.119 |
| `_Process` del client disabilitato | 56,7 | 21,7 ms | 2.188 |
| Mondo attivo, giocatore fermo | 53,1 | 24,3 ms | 2.188 |
| Camminata, camera segue il giocatore | 45,7 | **53,1 ms** | circa 1.505 |

La prima baseline aveva misurato 65,7 fps e meno draw call; non è stata scelta come confronto principale. Le differenze piccole fra HUD/modelli/processo e baseline rientrano nel rumore di prove brevi e non provano un peggioramento causato dalla loro disattivazione. La differenza del pavimento è invece ampia e accompagnata dal calo delle draw call. **Non significa che una futura ottimizzazione restituisca automaticamente gli stessi fps del pavimento assente.**

La sonda della camminata conferma che il personaggio si è spostato: posizione proiettata da circa `(370,764)` a `(512,1024)` durante la misura. Include l'arrivo e cambia inquadratura: non è un confronto isolato del solo costo del movimento. Mostra però frame lenti, con massimo circa 71 ms, che una media di 46 fps nasconde.

A 720p, baseline: 77,3 fps, p95 16,3 ms, 1.497 draw call. A zoom invariato la finestra più piccola inquadra meno oggetti: il miglioramento non quantifica separatamente fill rate della GPU e numero di primitive visibili.

Nel nucleo della sonda sono presenti **due SubViewport**, non quattro modelli necessariamente attivi perché il mondo contiene quattro attori. Il conteggio totale è 3.207 nodi, mentre 2.777 è il solo albero locale riportato dal benchmark esistente.

## Diagnosi del renderer

**Collo di bottiglia confermato: presentazione della mappa, in particolare il terreno.**

`VillageMap.cs:BuildMap` crea un `Polygon2D` per ogni cella. `VillageMap.Visuals.cs:ApplyGroundTexture` assegna texture e UV alla singola cella. Nel confronto il pavimento vale circa **1.316 draw call**: il numero dei nodi non è l'unico problema, il costo si ripete nella sottomissione del disegno ogni frame. Nascondere il pavimento non elimina i suoi nodi, eppure il frame rate più che raddoppia.

Questo localizza il costo nel percorso grafico, ma non separa definitivamente CPU del renderer, driver OpenGL, trasferimenti e GPU. La documentazione Godot distingue proprio questi costi e raccomanda il raggruppamento del disegno 2D. [Godot: GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html).

**Secondo costo confermato: maschera visiva e aggiornamento completo.** `VillageMap.Vision.cs:RefreshVisionFeather` costruisce un buffer RGBA, esegue interpolazioni e calcoli per pixel sulla CPU e crea/aggiorna la texture. La chiave include posizione precisa e sguardo. Il ritaglio già implementato è utile, ma resta molto lavoro a ogni cambiamento. `ShadeMap` alloca una griglia, scandisce celle e vicini, poi aggiorna molti nodi; nel ramo con esplorazione alcuni calcoli dei vicini non contribuiscono al valore finale. Non è il primo costo statico, ma va evitato come lavoro ripetuto durante il cammino.

`_Process` avanza la Sim e chiama `Refresh` quando passano tick. Il passo normale è 3 secondi simulati per secondo reale e la Sim ha 60 tick/s: durante il gioco la ricostruzione della vista può quindi avvenire a ogni frame. Interpolare i token, aggiornare il timer e ricostruire visibilità/topologia/UI non devono necessariamente avere la stessa frequenza o invalidazione.

## Sim senza Godot: scala e allocazioni

Sonda console separata. La factory del pilot è copiata dal file corrente, con la sola risoluzione del percorso JSON sostituita da un percorso assoluto. Il mondo viene creato normalmente e avanzato alle 19:35. Nessun accesso interno o mutazione di snapshot. Per la scala sono create fixture aperte con **esplorazione attiva**, una piccola zona interna e luce diurna alle 12:00. Servono a misurare la vista; non rappresentano città dense o popolazioni numerose.

Ogni operazione ha quattro chiamate preliminari e 12–24 campioni, otto per i percorsi. I tempi sono sensibili a tiered JIT, GC e ordine dei casi: le differenze Debug/Release non sono un fattore universale di accelerazione.

| Operazione `GetPlayerView` | Media Sim Debug | Media Sim Release | Allocazioni circa per chiamata |
|---|---:|---:|---:|
| Pilot reale, vista iniziale | 9,35 ms | 9,42 ms | 1,10 MB |
| Fixture esplorabile 20×14 | 1,94 ms | 1,63 ms | 0,16 MB |
| Fixture esplorabile 48×32 | 7,21 ms | 7,63 ms | 0,75 MB |
| Fixture esplorabile 80×60 | 11,71 ms | 11,90 ms | 2,20–2,25 MB |
| Fixture esplorabile 160×120 | **40,07 ms** | **13,86 ms** | **8,71 MB** |

MB decimali; le allocazioni usano `GC.GetAllocatedBytesForCurrentThread`, non la differenza di heap. Il runner è Release in entrambi i casi, la DLL Sim referenziata cambia fra Debug e Release. Non è una misura di un export Release del client Godot.

Nel pilot, `FacePlayer` da solo richiede circa 0,48 ms medio; rotazione più vista circa 5,6 ms in Release. La diversa direzione cambia quante celle risultano visibili, perciò non si deve sottrarre quest'ultimo numero dai 9,42 ms della vista iniziale.

Tre preview di percorso del pilot: circa 10,5 / 9,9 / 27,0 ms medi in Release; il caso più lento ha p95 circa 31 ms. Un'ora Sim dal mondo iniziale, senza intervento del giocatore, richiede circa 0,58 s in Debug e 0,66 s in Release in queste singole prove. **Il motore degli eventi con quattro attori non è dimostrato essere il limite primario del frame rate statico.** Questa prova non certifica decine/centinaia di NPC né una vista aggiornata a ogni passo dell'ora.

Cause leggibili nel codice:

- `SimulationSession.cs:GetPlayerView` costruisce visibilità, illuminazione e altre collezioni; `MapOf` ricopia righe, zone, edge e uscite anche se la geometria non cambia.
- `Simulation.Senses.cs:SquaresSeen` percorre tutta la griglia. La visione non ha un limite di distanza generale: di giorno il numero di celle candidate può crescere con tutta l'area.
- `LightOn` passa da `CarriedLights`, che ricostruisce una lista interrogando gli attori; ripeterlo per molte celle amplifica scansioni e allocazioni.
- `GridMap.HasLineOfSight` **ha già una cache**. Non occorre inventarne una duplicata. Tuttavia la cache è per coppie di celle e, senza una politica di dimensionamento, può crescere con le diverse origini visitate fra invalidazioni. Il rischio di memoria sul lungo periodo va misurato, non dichiarato già riprodotto.
- `SpotPlanner` ricostruisce geometria candidata e un grafo di visibilità; la ricerca prova collegamenti fra molti vertici e controlla collisioni. La densità di ostacoli conta oltre alle dimensioni.
- `Defense.KeepAway` ordina candidati dall'intera mappa e prova raggiungibilità per alcuni di essi. È un rischio specifico da misurare con ritirate e più attori, non una spiegazione dei fps bassi in pausa.

## Dimensione del mondo e obiettivo di gioco

Il confronto con BG1 è sensato come esigenza: un'area deve offrire spazio, percorsi e più attività. Non occorre fissare una corrispondenza fra le sue mappe e le nostre celle per stabilirlo. Non è giustificato scegliere adesso la dimensione definitiva soltanto in base al massimo che il renderer attuale riesce a disegnare.

Separare tre budget:

1. **Mondo persistente:** luoghi, attori, scorte, conoscenze e scheduler. Può contenere più aree di quella visibile.
2. **Sim locale e interrogazioni:** linee di vista, percorsi, informazioni necessarie al giocatore. Il lavoro deve rispondere ai cambiamenti rilevanti, evitando copie complete superflue.
3. **Presentazione:** geometria statica raggruppata, porzione inquadrata, attori visibili, piccole variazioni dinamiche. Una cella della simulazione non richiede un nodo grafico indipendente.

Le tre aree e il ciclo `MapPresentation` sono basi riutilizzabili. Per la prossima scala non è dimostrata la necessità di streaming del mondo, ECS, multithreading generalizzato o nuova AI. Non bisogna far dipendere percezione, razzie o routine dalla camera, né mettere in pausa gli NPC lontani per alleggerire il rendering.

## Percorso minimo proposto, senza implementazione in questa analisi

### P1 — Raggruppare il terreno statico del pilot

**Primo intervento consigliato.** Baseline ripetuta prima/dopo sullo stesso pilot, camera e luce. Sostituire la rappresentazione per cella del solo terreno con piccoli blocchi di geometria raggruppata per texture/materiale, riusando UV e asset esistenti. Un mesh per blocco è un candidato concreto; TileMapLayer è un'alternativa da scegliere soltanto se conserva la presentazione senza un nuovo sistema di authoring. Aggiungere un genitore a tanti Polygon2D non basta.

File: `VillageMap.cs:BuildMap/ShadeMap`, `VillageMap.Visuals.cs:ApplyGroundTexture`, ciclo `MapPresentation`; eventuale partial dedicato al terreno. Dipendenza: questa baseline. Nessuna modifica delle regole Sim.

Completamento: calo misurabile delle draw call del pavimento e del frame time, mantenendo terreno visibile, fog/memoria e luce; controllo immagini giorno/notte, porte, ritorni e load; vecchie scene e smoke pertinenti verdi. Un primo obiettivo sperimentale è ridurre almeno dell'80% le draw call del terreno nella medesima inquadratura, senza peggiorare gli scatti. Non promettere il risultato prima del confronto.

### P2 — Ridurre il lavoro della maschera e degli aggiornamenti visivi

File: `VillageMap.Vision.cs`, `ShadeMap`, sequenza `Refresh`. Dipendenza: baseline stabile, indipendente dalla modifica delle regole.

Consumare i dati filtrati della Sim per produrre una maschera meno costosa; valutare una texture di stato per cella con sfumatura grafica o aggiornamenti delle sole regioni cambiate. La GPU può sfumare, **non decidere chi o cosa il giocatore conosce**. Evitare riallocazioni e passate inutili. Separare il movimento dei token dagli aggiornamenti di geometria/UI.

Completamento: rotazioni e cammino misurati con p50/p95, riduzione del costo rispetto ai 46,5 ms per rotazione; nessuna luce attraverso pareti, attori remoti o conoscenza prematura. Il valore statico degli fps non basta.

### P3 — Rendere meno costosa la vista Sim senza cambiarne il significato

File: `Api/SimulationSession.cs`, `Simulation.Senses.cs`, `GridMap.cs`. Dipendenza: fixture con esplorazione attiva, luce, porte e attori; nessuna dipendenza tecnica dal renderer nuovo.

Distinguere topologia stabile e stato corrente, riusare i risultati validi, raccogliere una volta le sorgenti luminose pertinenti, ridurre copie/allocazioni e scansioni ripetute. Definire esplicitamente invalidazioni per posizione/sguardo, porte/vetri, torce, giorno/notte, combattimento, load e cambio area. Misurare la cache LOS prima di sostituirla o limitarla.

Completamento: stesse viste e conseguenze prima/dopo, save/load deterministico, isolamento tra aree, forte riduzione delle allocazioni nelle fixture 80×60 e 160×120. Test comportamentali di furtività, furto, rapporto e difesa devono restare verdi. Nessun limite di vista introdotto soltanto per nascondere un costo.

### P4 — Prova popolata più grande e percorsi difficili

Dipendenza: P1–P3 misurati. Usare 48×32 come riferimento e una candidata 80×60 realmente popolata, poi una dimensione maggiore dettata dai contenuti. Non moltiplicare soltanto terreno vuoto. Includere percorsi attorno a edifici, ingressi occupati, più sorgenti di luce, difesa/ritirata, NPC lontani, viaggio, una notte accelerata e ritorno/load.

File: benchmark/fixture e test dedicati; `SpotPlanner`, movimento e `KeepAway` soltanto se una misura individua il costo. Separare costo della creazione del planner, ricerca e avanzamento. Non sostituire preventivamente il pathfinding con un framework nuovo.

Completamento: budget esplicito per area e densità, tempi delle azioni e frame p95/p99 registrati; nessuna alterazione di causalità, RNG o comportamento fuori schermo. Su questo portatile propongo come obiettivo iniziale 60 fps a 1080p con frame p95 vicino a 20 ms nel cammino ordinario, da confermare su export Release e alimentazione documentata. È un obiettivo di prodotto, non un risultato già raggiunto.

## Cosa non fare adesso

- Non ridurre le mappe a stanzette per compensare il costo del renderer.
- Non comprare hardware o cambiare motore prima delle correzioni misurabili.
- Non attribuire tutto al 3D: la prova con i viewport disabilitati non lo sostiene.
- Non far avanzare meno fedelmente il mondo soltanto perché il client disegna meno.
- Non cambiare contemporaneamente renderer, AI, percezione e contenuti: renderebbe il confronto poco interpretabile.
- Non considerare 48×32 o 80×60 un massimo architetturale, né promettere ora mappe arbitrariamente grandi.
- Non convertire subito tutta l'ambientazione: prima dimostrare che il costo per una medesima inquadratura e densità rimane ragionevole aumentando l'area fuori schermo.

## Evidenze, riproduzione e limiti residui

Directory locale: `.tmp/performance-analysis-20261110/`. File `empty-*-default/unlimited.txt` e `pilot-default/unlimited.txt`: benchmark originale. `probe-*.json/txt`: isolamento grafico. `isolation_probe.gd`: sonda esterna; `sim-probe/`: progetto console, factory estratta e misure; `sim-probe.txt` / `sim-probe-release.txt`: risultati Sim. La directory è ignorata da Git: i numeri principali sono conservati qui perché rimangano consultabili anche senza gli artefatti locali.

Esempi dalla radice:

```powershell
$godotExe = 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $godotExe --rendering-method gl_compatibility --disable-vsync --max-fps 0 --resolution 1920x1080 --path game res://Scenes/BlackmarshRegion.tscn -- --region-map-pilot --region-map-benchmark=48x32
& $godotExe --rendering-method gl_compatibility --disable-vsync --max-fps 0 --resolution 1920x1080 --path game --script C:/Progetti/rpg/.tmp/performance-analysis-20261110/isolation_probe.gd -- --region-map-pilot --region-analysis-smoke --analysis-mode=baseline-repeat
# Ripetere con --analysis-mode=nofloor, no3d, nomap, nohud, noprocess, active, walking, turning.
dotnet run --project .tmp/performance-analysis-20261110/sim-probe/Probe.csproj --configuration Release -p:SimConfiguration=Release --no-restore
```

La sonda grafica ha avuto un errore iniziale di tipizzazione e un errore di lettura di nodi UI già liberati nel campione di camminata. Corretti soltanto nello script temporaneo, con campione fallito scartato e camminata rieseguita. Nessun difetto del gioco è stato corretto per migliorare questi risultati. `Performance.TIME_PROCESS` è raccolto nei JSON ma non utilizzato come separazione affidabile dei costi CPU/GPU: per quella serve un profiling dedicato.

Build solution e build Sim Release: 0 warning, 0 errori. Suite Sim eseguita dopo la build: **724/724**, nessun fallimento o test ignorato. `git diff --check` senza errori; nessun file tracciato preesistente modificato. Nessuna correzione di prestazioni implementata. Non eseguiti export Release del gioco, trace GPU, test termico prolungato, confronto con altro PC o stress di popolazione. Le prove isolano costi concreti e orientano il prossimo intervento; non certificano ancora il budget finale della regione.

## P1 implementato sul pilot — 10 novembre 2026

Il terreno del solo pilot 48×32 usa ora `MeshInstance2D` per blocchi 8×8 e texture. Le singole celle conservano UV e tinta; la luce e la memoria visiva colorano i vertici e ricostruiscono soltanto i blocchi il cui colore cambia. Pareti, oggetti e scena regionale ufficiale mantengono il percorso precedente. Nella stessa cattura della rimessa e della caletta, la pavimentazione, le zone buie e gli interni risultano visivamente equivalenti al pilot prima del cambio.

Sonda di Astra, 1080p, VSync spento, camera `(56,640)`, zoom 1,08, pilot in pausa, cinque secondi dopo riscaldamento:

| Variante | Draw call medie | fps medi | Frame p50 | Frame p95 | Nodi totali |
| --- | ---: | ---: | ---: | ---: | ---: |
| Prima di P1, ripetizione locale | 2188 | 48,9 | 19,7 ms | 26,7 ms | 3207 |
| Dopo P1 | 931 | 114,5 | 8,3 ms | 11,5 ms | 1734 |

Le draw call totali calano del 57%; rispetto alle circa 1316 attribuite da Astra al pavimento, il calo di 1257 equivale a circa il 95,5%. La misura a 720p dopo P1 è 769 draw call, 133,3 fps medi e p95 13,6 ms; manca una baseline locale 720p nello stesso istante, quindi non la uso come rapporto prima/dopo. Sono prove brevi su portatile: indicano un grande vantaggio del batching, non un budget finale garantito.

Nel campione di cammino dopo P1: 62,3 fps, p95 48,1 ms, massimo 64,1 ms, posizione del giocatore realmente cambiata. La precedente sonda di Astra aveva 45,7 fps e p95 53,1 ms, ma il percorso e l'hardware variabile non permettono di attribuire tutta la differenza a P1. Le 24 rotazioni dopo P1 richiedono 47,8 ms medi, p95 61,9 ms, praticamente come la baseline di Astra 46,5/60,9 ms. P1 risolve il costo statico del terreno, non gli scatti di vista e movimento: P2 resta necessario.

Build client 0 avvisi/0 errori; smoke pilot delle tre aree e smoke di presentazione regionale ufficiale/isola e fixture verdi. La scena ufficiale non usa ancora le mesh raggruppate. Catture `shed-inside.png` e `cove-overlook.png` confrontate con quelle pre-P1.

## P3 implementato nella Sim — 10 novembre 2026 (Claude)

La vista del giocatore (`GetPlayerView`) costa meno, con lo stesso significato. Sono cambiati solo `Api/SimulationSession.cs`, `GridMap.cs` e `Simulation.Senses.cs`. API pubblica, salvataggio e schema (44) non cambiano.

**Cosa è cambiato:**
- **Luce.** Le torce accese sulla mappa (`CarriedLights`) si raccolgono una volta per vista, non per ogni casella. `SourceLight` non alloca più una concatenazione per chiamata.
- **Una sola passata.** `SquaresSeenLit` dà insieme le caselle viste e la luce su ciascuna. `MapVisibility` e `MapLight` si scrivono direttamente in array di caratteri, senza LINQ per carattere.
- **Ordine dei controlli.** Prima il cono, che non chiede più `EncounterOf` per ogni casella, poi la luce, poi la linea di vista. La linea si traccia solo per una casella nel cono, illuminata o adiacente: al buio quasi nessuna.
- **Tracciamento della linea.** Due griglie: le caselle che bloccano la vista (ricostruita quando una porta cambia) e le caselle che toccano un confine. Un passo lontano da muri e porte su confine non consulta più il dizionario dei confini. Gli altri passi usano `StepOpen` come prima.
- **Memoria delle linee.** Lo sguardo del giocatore usa una griglia per l'ultima casella da cui guarda (`HasLineOfSightFrom`), non migliaia di voci nel dizionario delle coppie. Il dizionario delle coppie, usato da PNG, luci e luce del giorno negli interni, ha un tetto di 500.000 voci (circa 20 MB) e oltre si svuota: è solo memoria, e svuotarlo non cambia nessuna risposta.
- **Pianta della mappa.** Il `MapView` (righe, zone, uscite, percorsi, depositi) si costruisce una volta per mappa e la stessa istanza viene restituita a ogni vista. I confini si rileggono comunque a ogni vista, perché lo stato di una finestra (aperta, vetro, sbarre) è lì. Se cambia, il `MapView` viene ricostruito. **Per il client:** se `view.Map` è la stessa istanza della vista precedente, la pianta non è cambiata.
- Interni letti da una griglia invece che dalla zona di ogni casella.

**Equivalenza.**
- **Viste di riferimento.** Le viste complete serializzate per riflessione, più il save al termine di ogni sequenza, sono identiche byte per byte prima e dopo. Sono 207 viste in quattro sequenze:
  - **pilot:** giorno, rotazioni, cammino, porta, interno di notte, torcia, viaggio alla caletta, approdo;
  - **isola ufficiale:** apertura, rotazioni, +40 min, +3 h 40 min;
  - **campi 48×32 e 80×60 con casa:** pareti, porta, finestra, lampade e un PNG con torcia. Ogni percorso tocca giorno, notte, finestra aperta, chiusa, vetro rotto e tolto, scavalcamento e torcia spenta.

  Il riferimento è stato generato dal codice di `b95025e` in un worktree pulito.
- **Nuovi test (`SightPerformanceTests`):**
  - le linee lette dalle griglie e dal campo coincidono con il tracciamento classico per tutte le coppie di 25 mappe casuali con muri, confini, finestre e porte, anche dopo aver invertito ogni porta;
  - la memoria delle linee resta sotto il tetto;
  - uno sguardo su un campo intero non riempie il dizionario;
  - la pianta è la stessa istanza mentre le porte cambiano, e un caricamento ne costruisce una propria.
- Una prima versione della cache della pianta ignorava lo stato delle finestre. I due test delle finestre l'hanno colta ed è stata corretta prima della consegna.

**Misure.** Sim Release, `GetPlayerView` da fermo, base `b95025e` ed esecuzioni alternate. Il portatile oscillava parecchio durante la misura: contano i rapporti più dei valori assoluti.

| Caso | Prima | Dopo | Allocazioni prima → dopo |
|---|---:|---:|---:|
| Pilot 48×32, vista | 7,7 ms | 1,9 ms | 1,10 → 0,08 MB |
| Pilot, rotazione + vista | 5,2 ms | 1,4 ms | 0,60 → 0,06 MB |
| Campo esplorabile 48×32 | 5,7 ms | 0,7 ms | 0,75 → 0,07 MB |
| Campo esplorabile 80×60 | 14,1 ms | 1,9 ms | 2,30 → 0,20 MB |
| Campo esplorabile 160×120 | 12,5 ms | 2,5 ms | 8,72 → 0,71 MB |
| Notte con due torce, 80×60 | 5–18 ms | 2,7–3,8 ms | 1,07 → 0,08 MB |

**Camminata**, il caso che il benchmark da fermo non vedeva. Il giocatore attraversa di giorno un campo esplorabile, una casella alla volta, con una vista per passo:

| Campo | Vista per passo, prima → dopo | Memoria delle linee a fine traversata, prima → dopo |
|---|---:|---:|
| 80×60 | 11,7 → 4–5 ms | 362.000 voci, +21,5 MB → 144 voci, +0,2 MB |
| 160×120 | 77,5 → 16–19 ms | 2.983.000 voci, +191,6 MB → 144 voci, +0,8 MB |

Prima la memoria cresceva senza limite camminando: ore di gioco su una mappa grande la portavano a centinaia di MB.

**Difesa (`KeepAway`).** Su un campo 80×60 il predone che si ritira davanti a paladino e guardia costa circa 1,2 ms per secondo simulato, in linea con 16×6 e 48×32. Non è un collo di bottiglia, quindi non è stato toccato.

**Restano:**
- da una casella nuova, su 160×120 di giorno la vista traccia ancora circa 10.000 linee (16–19 ms). Scendere sotto richiederebbe un algoritmo di visibilità diverso (ad esempio lo shadowcasting), che non dà esattamente le stesse linee: andrebbe concordato come cambio di regola, non come ottimizzazione;
- il comando di movimento (`SpotPlanner`) costa 2–5 ms per destinazione: è materia di P4;
- la vista resta una ricostruzione completa a ogni chiamata, a parte la pianta: il client farebbe bene a chiederla quando passano tick, non a ogni frame.

**Verifiche** in worktree pulito su `b95025e` più P3:
- suite **728/728** e build client 0/0;
- smoke regionali: viaggio, presentazione dell'isola e della fixture, persistenza seed/resume;
- smoke del pilot sulle tre mappe 48×32;
- le sei smoke Blackmarsh, slice e villaggio: tutte verdi.
