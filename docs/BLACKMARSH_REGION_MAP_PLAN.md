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

La prima matrice di terreno è in [BLACKMARSH_REGION_LAYOUT_48x32.json](BLACKMARSH_REGION_LAYOUT_48x32.json): tre mappe da 32 righe di 48 caratteri, con simboli di zona, blocchi esterni e ancore candidate. È una base tecnica per parlare di accessi e corridoi, non la pianta visiva definitiva. Tutte le ancore elencate sono raggiungibili dal rispettivo bivio nella griglia senza porte; risultano connesse 1294, 1290 e 1267 caselle attraversabili rispettivamente. Restano da specificare bordi degli edifici, porte, finestre, luci, ostacoli, oggetti e la resa degli scogli: oggi il client disegnerebbe i `#` esterni come muri alti, che qui non sarebbero credibili.

| Area | Struttura e snodi | Interazioni da realizzare con regole esistenti |
| --- | --- | --- |
| Nucleo dei pescatori | Casa comune a (9–15, 11–17); piazzale e pozzo a (21–28, 13–20); bivio a (47,16). Sentiero principale da ovest a est; anello a nord dietro la casa e passaggio a sud fra canneti e rive. Variazioni di quota rese da scogli e linea di costa, senza piano verticale simulato. | Dispensa e porta della casa; pozzo come fonte/ostacolo; focolare come luce; attrezzi o razioni a terra da raccogliere; punto riparato da cui vedere il custode e l'ingresso della casa. |
| Rimessa della vedetta | Bivio a (0,16); edificio a (22–29, 12–18), con porta esterna chiusa, due ambienti separati da una porta interna, cassa e finestra. Accesso principale sul sentiero centrale, seconda possibilità di arrivare alla finestra girando per il tratto nord. Scogli e sterpaglia dividono l'ampio spiazzo in più percorsi. | Porte/serratura e finestra già esistenti; cassa e lampada; oggetti da trovare vicino a reti e banco di lavoro; osservazione della rimessa da un punto esterno senza mostrare l'interno attraverso il tetto. |
| Caletta | Bivio a (0,16); approdo e provviste Vasan a (36–45, 19–29); spazio per il capanno del Task 7 a (34–42, 4–12), separato dall'approdo. Due vie fino alla caletta: sentiero scoperto a sud e varco fra gli scogli a nord, con punto d'osservazione a (25–29, 12–15). | Provviste con accessi liberi, barca e fuoco come elementi leggibili; tracce o razioni a terra da esaminare/raccogliere con le azioni esistenti; ostacoli che offrono copertura reale alla linea di vista Sim. Il capanno non va reso interattivo prima del Task 7. |

### Registro delle interazioni candidate

L'audit del motore chiarisce che `water` rifornisce automaticamente gli otri quando si entra nel luogo; `ground` è raccoglibile; una luce cambia la visibilità; porta, finestra, deposito, rapporto e difesa Vasan hanno già regole. Non esiste invece un'azione generica «esamina» per barche, reti o tracce. Questi elementi possono orientare visivamente, ma non vanno descritti come indizi interattivi senza un fatto o una regola esplicita. Le coordinate sono ancora quelle della pianta 48×32 candidata.

| Area e punto | Casella/ambito candidato | Azione e risultato già supportati | Vincolo da verificare |
| --- | --- | --- | --- |
| Nucleo: pozzo | zona `bm_isle_well`, (21–28, 13–20) | Entrare nel luogo con acqua rifornisce gli otri e soddisfa il bisogno giornaliero. | Il pozzo visivo e l'acqua Sim devono coincidere; non chiedere un clic che non esiste. |
| Nucleo: casa e dispensa | edificio (9–15, 11–17), cassa presso (10,12) | Aprire porta, prendere/depositare razioni; il trasferimento cambia il bersaglio della razzia. | Accesso alla cassa libero anche con il custode di guardia. |
| Nucleo: custode | piazzale presso (24,16) | Riferire un furto conosciuto e osservare la sua risposta fisica. | La relazione deve emergere dal rapporto, non da un'icona onnisciente. |
| Nucleo: attrezzo smarrito | sentiero sud presso (13,25) | Raccogliere un `iron-spike` a terra. | Solo se ha un uso o una funzione leggibile; altrimenti è riempitivo e si elimina. |
| Rimessa: ingresso | lato ovest edificio, presso (22,16) | Chiave o forzatura della porta; la porta resta parte della Sim. | Il nuovo percorso non deve rendere inutile la finestra. |
| Rimessa: finestra e deposito | lato est presso (29,15), cassa presso (28,13) | Passare/aprire dalla finestra secondo le regole esistenti; prendere o proteggere le razioni. | Le due vie devono restare realmente percorribili e distinguibili. |
| Rimessa: attrezzi | sterpaglia a nord presso (35,9) | Raccogliere `thieves-tools` se confermato dal contenuto; modifica le possibilità di scasso. | L'oggetto non deve anticipare gratuitamente la soluzione della rimessa. |
| Caletta: punto d'osservazione | fuori da `bm_isle_landing`, presso (36,18) | Possibile punto d'avvicinamento prima di entrare nell'approdo. | Nel pilot il predone non è ancora leggibile dalla cattura; non chiamarlo punto di spionaggio finché vista e camera non lo dimostrano. |
| Caletta: approdo e provviste | `bm_isle_landing`, (36–45, 19–29), cassa presso (43,26) | Entrare può provocare la difesa del Task 6A; toccare le provviste cambia la disponibilità di cibo e la reazione. | Almeno due caselle d'accesso, così una minaccia non sigilla ogni interazione. |
| Caletta: oggetto del campo | presso (38,28) | Raccogliere una `torch` o una `ration`, se coerente con le scorte Vasan. | Non duplicare cibo senza aggiornare la simulazione delle scorte. |

Barca, reti, fuochi spenti e tracce sono **riferimenti visivi** finché non hanno una conseguenza Sim. Con tre depositi, quattro persone e poche azioni ambientali, questa prima lista non può sostenere da sola ore di gioco in una sola area: il Task 6B va giudicato con un playtest, e l'eventuale contenuto aggiuntivo va progettato dopo aver visto dove il giocatore si ferma o torna.

## Prestazioni e verifica prima della pianta definitiva

Oggi `BuildMap` crea almeno un `Polygon2D` per ogni casella. La maschera di visione in `VillageMap.Vision.cs` copre l'intera proiezione della mappa, la rigenera a ogni cambio di vista e carica una texture: a 48×32 saranno circa 365 mila pixel, a 80×60 circa 1,1 milioni, contro circa 66 mila a 20×14. `ShadeMap` aggiorna poi tutte le caselle e i muri quando cambia la chiave luce/visibilità. Questo indica rischi di CPU, memoria e caricamento; non dimostra ancora un rallentamento misurato.

Per le due fixture misurare: tempo di costruzione dell'area, tempo di aggiornamento visione/luci durante il cammino, frame time a 1280×720 e 1920×1080, memoria, durata di un giorno simulato e calcolo di percorsi NPC. Il culling visivo da solo non riduce il costo di costruzione né quello della maschera: decidere l'ottimizzazione dopo le misure. Catture nei tre punti focali di ciascuna area devono mostrare orientamento, densità e leggibilità degli oggetti, non solo estensione del terreno.

### Prima misura client, 9 novembre 2026

Fixture isolata `--region-map-benchmark=<dimensioni>`, una mappa aperta e un attore: stesso renderer e UI della regione, 60 frame misurati dopo 60 di riscaldamento. Una esecuzione per combinazione su Godot 4.7.2 Mono/OpenGL, GPU AMD Radeon integrata. I tempi includono il costo della finestra e il limite di sincronizzazione del test, quindi sono un confronto locale, non una promessa di frame rate per la mappa piena di edifici e oggetti.

| Risoluzione | Mappa | Nodi della mappa | Costruzione mappa | Quattro cambi di sguardo | 60 frame / frequenza media |
| --- | --- | ---: | ---: | ---: | ---: |
| 1280×720 | 20×14 | 355 | 78,0 ms | 5,5 ms | 1980 ms / 30,3 fps |
| 1280×720 | 48×32 | 1611 | 92,2 ms | 9,3 ms | 2065 ms / 29,1 fps |
| 1280×720 | 80×60 | 4875 | 116,1 ms | 22,1 ms | 2078 ms / 28,9 fps |
| 1920×1080 | 20×14 | 355 | 78,4 ms | 5,5 ms | 1978 ms / 30,3 fps |
| 1920×1080 | 48×32 | 1611 | 90,5 ms | 11,9 ms | 2210 ms / 27,2 fps |
| 1920×1080 | 80×60 | 4875 | 113,8 ms | 20,8 ms | 2318 ms / 25,9 fps |

La misura non include strutture fitte, NPC, ciclo Sim di un giorno o un playtest con camera in movimento. Il tempo di quattro cambi di sguardo resta sotto 23 ms anche a 80×60, mentre il numero di nodi sale a quasi 4900 e il frame rate a 1080p cala di circa il 15% rispetto alla mappa piccola. Servono il profilo completo e la prova di densità prima di fissare 48×32 o 80×60; non giustificano ancora un'ottimizzazione specifica.

### Pilot esplorabile e secondo profilo

`gioca-regione-mappa-pilot.cmd` apre una sessione di prova con le tre piante 48×32, collegamenti, edifici, porte, depositi, quattro attori e cibo Vasan. Usa `user://blackmarsh-map-pilot.json`, separato dalla partita regionale. La geometria viene dal JSON candidato sotto `docs/`; lo scenario principale resta quello dei test di Claude. La costruzione della sessione parte dalla mezzanotte e arriva davvero alle 19:35, per mostrare il pescatore al lavoro e il predone alla caletta.

Lo smoke `--region-map-pilot --region-map-pilot-smoke=<cartella>` attraversa nucleo → rimessa → caletta → nucleo con comandi pubblici, apre le porte esterne e quella interna della rimessa, entra nei due edifici, entra nel territorio Vasan e cattura otto viste. Passa a 1280×720. La minaccia del predone compare sia negli eventi filtrati del giocatore sia in una frase breve sopra il PNG visibile; lo smoke verifica entrambe. Nelle catture controllate la casa iniziale era ancora sproporzionata: è stata ridotta da 10×11 a 7×7 caselle; la rimessa da 12×12 a 8×7. Il grigio fuori mappa è stato sostituito con uno sfondo scuro. Restano un pozzo segnaposto, scogli troppo simili a piastre di pietra e il campo Vasan poco leggibile dal punto d'approccio: sono difetti visivi osservati, non caratteristiche concluse.

Sul pilot popolato a 1920×1080, prima dell'ottimizzazione, quattro cambi di sguardo richiedevano circa 154 ms, di cui 114 ms per la maschera di visione. La maschera ora copre solo la regione proiettata delle caselle in vista e usa 4 pixel della texture per campione sulle mappe regionali larghe almeno 40 caselle; la stessa misura è scesa a circa 76 ms, con la maschera a circa 38 ms. La frequenza media della finestra nel pilot è rimasta circa 23 fps: questo cambio riduce lo scatto quando si gira, ma da solo non risolve il rendering per frame. Il prossimo profilo deve separare il costo dei nodi di terreno e della presentazione 3D; il risultato del pilot non è ancora la soglia di fluidità del Task 6B.

Con gli edifici ridotti alla misura attuale, il pilot conta 2777 nodi e misura circa 26 fps a 1280×720 e 24 fps a 1920×1080 nelle stesse condizioni; la maschera impiega circa 44 ms per quattro rotazioni. Ho provato anche a raggruppare il pavimento in blocchi 8×8 e nascondere quelli fuori camera: la misura a 1080p è rimasta circa 24 fps e quella a 720p circa 26. Ho rimosso il culling sperimentale; su questa pianta quasi tutta la proiezione cade comunque nell'inquadratura. Un'area 80×60 potrebbe comportarsi diversamente, ma richiede un benchmark popolato prima di adottare la stessa tecnica.

## Consegna a Claude

Quando scala e topologia sono approvate, Codex consegna matrice di terreno, zone, bordi/porte, uscite, ostacoli, luci e punti d'interesse con coordinate validate. Claude integra scenario/posti, riesegue percorsi e orari del pescatore/predone e i test Sim. Codex aggiorna presentazione e prestazioni client sulle coordinate definitive; entrambi verificano che gli accessi restino raggiungibili e che la difesa Vasan del Task 6A operi nella nuova caletta.
