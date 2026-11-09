# Blackmarsh: bozza spaziale per il Task 6B

9 novembre 2026. Documento di lavoro per concordare le piante prima di modificare lo scenario. Le coordinate qui sotto descrivono il prototipo **48×32**; non sono ancora dati di gioco.

## Scala e criterio di scelta

Le mappe correnti misurano 20×14 (nucleo, caletta) e 18×14 (rimessa). 48×32 equivale a 5,5–6,1 volte la loro superficie; 80×60 equivale a 17–19 volte. Con la velocità attuale di 30 piedi per round, un tratto diritto di 48 caselle richiede circa 48 secondi simulati; uno di 80 circa 80. La dimensione da sola non produce ore di esplorazione: servono percorsi alternativi, punti da osservare, decisioni e conseguenze.

Si prova prima 48×32, poi si profila una fixture 80×60. Scegliere la misura dopo aver visto tempi reali di traversata, densità percepita, costo Sim e fluidità del client. Se 48×32 risulta ancora un corridoio, ampliare la pianta e il contenuto insieme. Non allungare artificialmente la velocità del personaggio.

## Vincoli comuni

- I tre bivi restano gli unici collegamenti fra aree, sul bordo delle rispettive mappe. Ogni area conserva due destinazioni e le durate di viaggio dichiarate fra bivi; quelle durate non sono il tempo per camminare dentro la mappa.
- Conservare gli ID di area, luogo, attore, deposito e fazione. Spostare coordinate e aumentare le zone senza cambiare il significato delle tre scene.
- Il luogo `bm_isle_landing` resta il territorio Vasan per il Task 6A. Il percorso che passa sul suo bordo deve rendere comprensibile il rischio, mentre un approccio coperto offre un modo di osservarlo prima di entrare. Non sostituire la regola di vista della Sim con un segnale grafico onnisciente.
- I posti dei PNG e gli accessi ai depositi devono avere spazio per un personaggio e una via praticabile anche quando un altro è di guardia. Nessun oggetto decorativo deve alterare il percorso solo nel client.
- Il simbolo `.` è terreno libero senza luogo; un simbolo di zona collega la casella a un luogo esistente. `#`, ostacoli, porte e finestre hanno invece effetti Sim. Verificare che nuovi elementi visivi o nomi non promettano azioni che il motore non offre.

## Pianta candidata 48×32

Coordinate inclusive, origine in alto a sinistra. Le fasce descrivono la composizione, non sono ancora una matrice completa di caselle.

| Area | Struttura e snodi | Interazioni da realizzare con regole esistenti |
| --- | --- | --- |
| Nucleo dei pescatori | Casa comune a (7–16, 9–19); piazzale e pozzo a (21–28, 13–20); bivio a (47,16). Sentiero principale da ovest a est; anello a nord dietro la casa e passaggio a sud fra canneti e rive. Variazioni di quota rese da scogli e linea di costa, senza piano verticale simulato. | Dispensa e porta della casa; pozzo come fonte/ostacolo; focolare come luce; attrezzi o razioni a terra da raccogliere; punto riparato da cui vedere il custode e l'ingresso della casa. |
| Rimessa della vedetta | Bivio a (0,16); edificio a (19–30, 10–21), con la stessa sequenza funzionale di porta chiusa, stanza, seconda porta, cassa e finestra. Accesso principale sul sentiero centrale, seconda possibilità di arrivare alla finestra girando per il tratto nord. Scogli e sterpaglia dividono l'ampio spiazzo in più percorsi. | Porta/serratura e finestra già esistenti; cassa e lampada; oggetti da trovare vicino a reti e banco di lavoro; osservazione della rimessa da un punto esterno senza mostrare l'interno attraverso il tetto. |
| Caletta | Bivio a (0,16); approdo e provviste Vasan a (36–45, 19–29); spazio per il capanno del Task 7 a (34–42, 4–12), separato dall'approdo. Due vie fino alla caletta: sentiero scoperto a sud e varco fra gli scogli a nord, con punto d'osservazione a (25–29, 12–15). | Provviste con accessi liberi, barca e fuoco come elementi leggibili; tracce o razioni a terra da esaminare/raccogliere con le azioni esistenti; ostacoli che offrono copertura reale alla linea di vista Sim. Il capanno non va reso interattivo prima del Task 7. |

Questa bozza contiene alcuni punti attivi e molti riferimenti visivi. Prima dell'integrazione, ogni punto attivo va elencato con ID, casella, azione disponibile e risultato osservabile; barca, reti e tracce puramente decorative vanno indicate come tali oppure legate a oggetti/fatti già supportati. Una mappa con sole decorazioni non soddisferebbe il criterio di esplorazione del Task 6B.

## Prestazioni e verifica prima della pianta definitiva

Oggi `BuildMap` crea almeno un `Polygon2D` per ogni casella. La maschera di visione in `VillageMap.Vision.cs` copre l'intera proiezione della mappa, la rigenera a ogni cambio di vista e carica una texture: a 48×32 saranno circa 365 mila pixel, a 80×60 circa 1,1 milioni, contro circa 66 mila a 20×14. `ShadeMap` aggiorna poi tutte le caselle e i muri quando cambia la chiave luce/visibilità. Questo indica rischi di CPU, memoria e caricamento; non dimostra ancora un rallentamento misurato.

Per le due fixture misurare: tempo di costruzione dell'area, tempo di aggiornamento visione/luci durante il cammino, frame time a 1280×720 e 1920×1080, memoria, durata di un giorno simulato e calcolo di percorsi NPC. Il culling visivo da solo non riduce il costo di costruzione né quello della maschera: decidere l'ottimizzazione dopo le misure. Catture nei tre punti focali di ciascuna area devono mostrare orientamento, densità e leggibilità degli oggetti, non solo estensione del terreno.

## Consegna a Claude

Quando scala e topologia sono approvate, Codex consegna matrice di terreno, zone, bordi/porte, uscite, ostacoli, luci e punti d'interesse con coordinate validate. Claude integra scenario/posti, riesegue percorsi e orari del pescatore/predone e i test Sim. Codex aggiorna presentazione e prestazioni client sulle coordinate definitive; entrambi verificano che gli accessi restino raggiungibili e che la difesa Vasan del Task 6A operi nella nuova caletta.
