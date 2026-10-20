# Prova giocabile degli interni

Aprire `game/Scenes/InteriorArtPilot.tscn` in Godot e premere **F6**. Con F5 si avvia ancora Village. La casa ha due ambienti percorribili, ingresso, porta interna, due finestre e tetto che si nasconde entrando. La stanza a destra contiene una lampada della Sim. Movimento, linea di vista e luce restano governati dalle regole del gioco.

## Revisione dopo il confronto con la reference

Il primo pilot aveva stanze quasi vuote e pareti piatte: **non riproduceva il dettaglio di `interior-concept.png`**. Questa revisione aggiunge letto, due tavoli, baule e due mobili con stoviglie, più intonaco e pietra sulle pareti e una finestra con davanzale e architrave. Gli arredi sono sprite originali generati per questo campione, ritagliati tramite alfa in Godot. Le loro caselle sono occupate nella Sim: il protagonista deve girarci intorno. La camera del pilot inquadra tutta la casa a una scala maggiore. Le nuove catture sono `.tmp/art-pilot/inside-bedroom.png` e `inside-stone-room.png`.

**Il risultato resta un campione, non la stessa resa della tavola.** La reference è un'illustrazione statica con vista onnisciente e illuminazione artistica; nel gioco la casa visibile cambia con sguardo, luce e ostacoli. Inoltre il muro modulare, il tappeto, la porta e parte del pavimento sono ancora provvisori. Gli arredi ora fanno percepire un interno abitato, ma serve un kit architettonico coerente e ulteriore lavoro di composizione e illuminazione prima di usare questa direzione nella demo ufficiale. Non valutare la qualità dal solo primo fotogramma: entra e cammina attorno ai mobili nelle due stanze.

Gli asset del pilot sono `game/Assets/StyleStudy/interior-{bed,table,chest,cabinet,plaster,flagstone,roof-slate}.png`. Letto, tavolo, baule, mobile e intonaco sono stati generati il 20 ottobre 2026 con imagegen integrato usando `interior-concept.png` come reference di stile; i due materiali del pavimento e tetto erano già stati generati per la prima prova. Le immagini degli arredi sono solo props statici, non modelli 3D o elementi interattivi. I personaggi continuano a usare il prototipo 3D esistente.

## Visibilità e volume della casa

La vista corrente della Sim continua a decidere quali NPC e nemici compaiono. Le caselle già esplorate restano invece leggibili, con una tinta attenuata rispetto alla luce corrente, anche quando il protagonista guarda altrove. Le stanze mai viste rimangono coperte; da fuori, il tetto continua a nascondere gli interni ricordati. Questo vale anche per il villaggio, non solo per il pilot.

Nel pilot i muri hanno una copertina di legno larga otto pixel di mondo e uno zoccolo di pietra più alto. Il tetto termina sul bordo superiore dei muri; timpani intonacati riempiono lo spazio sotto le falde e una fascia scura chiude la gronda. Sono elementi visivi: collisioni e linea di vista restano quelle dei confini sottili della Sim.
