# Piano UX del client: comandi, anteprime e feedback

21 ottobre 2026. Analisi della demo `VillageMap` a 1280×720, del codice corrente e delle reference `docs/bg3.png`, `docs/hotbar.webp` e `docs/hotbar_dimension.webp`. Questo documento definisce la direzione; non cambia le regole della Sim.

## Cosa insegnano i riferimenti

| Gioco | Scelta osservabile | Applicazione alla nostra demo |
| --- | --- | --- |
| Baldur's Gate I/II | Comandi rapidi, pausa e cursore contestuale; il manuale distingue visivamente movimento, interazione, porta, attacco e incantesimo. | Conservare la pausa rapida e dare al puntatore un'intenzione riconoscibile prima del clic. Una finestra deve promettere «vai e apri», non sembrare terreno. [Manuale ufficiale BGII:EE](https://files.beamdog.com/files/BGIIEE-AmnSurvivalGuide_en.pdf), sezione *Interface Overview*. |
| Baldur's Gate 3 | Barra con risorse, icone e tooltip; le patch ufficiali migliorano messaggi di indisponibilità nella barra, leggibilità delle risorse, log con causa della perdita di furtività e avviso quando il movimento per lanciare porta su terreno pericoloso. | La barra esistente è una base valida. Servono costi e stato leggibili, motivi contestuali e un'anteprima del risultato/rischio nel mondo. [Patch 1](https://baldursgate3.game/news/patch-1-now-live_87), [Patch 5](https://baldursgate3.game/news/patch-5-now-live_99), [Patch 6](https://baldursgate3.game/news/patch-6-now-live_108). |
| Pathfinder: Kingmaker/Wrath | Owlcat riconosce che tradurre la scheda cartacea direttamente sullo schermo è difficile e che molte capacità non si capiscono dalla sola icona. Per il turno, ha progettato percorso esatto e attacchi di opportunità nell'anteprima. | Nome e spiegazione restano disponibili oltre al pittogramma; in scontro il percorso e i rischi devono precedere la conferma. [Retrospettiva UI](https://owlcat.games/news/60), [turni e percorso](https://owlcat.games/news/42). |
| Solasta | Gli sviluppatori hanno trovato giocatori che non vedevano il pulsante Riposo e hanno introdotto insegnamento graduale di azione, bonus e reazione. Le patch citano avvisi sulla concentrazione e maggiore leggibilità di PF/ordine di iniziativa. | Non basta esporre un comando: va insegnato al primo uso, e il suo costo o rischio deve essere chiaro. [Dev Update #1](https://www.solasta-game.com/solasta-crown-of-the-magister/news/46-dev-update-1-goodbye-telema), [Spring Update](https://www.solasta-game.com/solasta-crown-of-the-magister/news/121-solasta-spring-update-patch-notes). |

I principi che seguono sono proposte nostre dedotte dal confronto, non funzioni da copiare alla lettera da quei giochi.

## Stato reale del client

La barra ha già ritratto/PF, mani, torcia, risorse Azione/Bonus, movimento, concentrazione, sezioni Comuni/Paladino/Oggetti, icone con tooltip e Fine turno/Pausa separato. Il registro è compatto, traslucido, opaco su hover e apribile dall'icona. Inventario, libro, stato espanso e menu Esc esistono. L'attacco e gli incantesimi hanno una modalità di scelta del bersaglio; la Sim offre già `PreviewAttack`, `PreviewMove` e `PreviewMoveToSpot`. Il clic distante su porte e finestre è stato collegato a cammino e interazione. Questi elementi vanno mantenuti.

| Problema osservato | Evidenza attuale | Effetto sul giocatore |
| --- | --- | --- |
| Il significato delle icone richiede apprendimento per tentativi | Nella barra 42×42 px si vedono solo simboli, spesso astratti; nome e costo completo stanno nel tooltip. «Disimpegno» e «Risali» riusano `escape`; alcune magie hanno un pittogramma simile. | Il giocatore scruta o prova i pulsanti invece di riconoscerli. |
| Lo stato dell'azione è poco esplicito | Disponibile, selezionata, fuori filtro, senza risorse e non valida sono espressi soprattutto con attenuazione, bordo o tooltip; Azione/Bonus attenuano i comandi, non li nascondono. | «È spenta perché?», «Sto ancora mirando?» e «Cliccando cosa succede?» non hanno risposta immediata. |
| Il feedback importante si perde nel registro | `RefreshHud()` concatena fatti, log di combattimento e ultimo `ShowMessage` come righe di testo; il registro chiuso usa opacità 0,42 e non differenzia errore, rischio, successo e danno. | Colpo mancato, porta impossibile e costo speso competono nello stesso canale. |
| L'anteprima nel mondo è disomogenea | Esistono preview Sim e testo/indicatori in alcune modalità; il clic di esplorazione, l'interazione e il targeting non condividono ancora un linguaggio unico di cursore, contorno, costo e motivo. | La stessa intenzione può sembrare diversa in esplorazione e in scontro. |
| Il combattimento ha informazioni sparse | Barra sotto, scheda luogo/ora a sinistra, registro a destra e riepilogo iniziativa in alto occupano zone separate. La cattura `ux-combat-bar.png` mostra testo piccolo e una fascia di informazioni piuttosto densa. | È difficile capire in un colpo d'occhio turno, bersaglio, risorse rimaste ed esito. |
| Poche spiegazioni al primo uso | Tooltip e messaggi esistono, ma la demo non guida in sequenza attraverso interazione, barra, bersaglio, costo e registro. | Anche un pulsante presente può rimanere invisibile, come nell'esempio Solasta. |

Prima di progettare nuovi pannelli, fare una prova manuale su Village e InteriorArtPilot: attacco riuscito/mancato, azione non disponibile, magia con bersagli, finestra distante, porta bloccata, perdita di concentrazione, fine turno. Registrare per ogni prova cosa il giocatore vede **prima**, **durante** e **dopo** il clic.

## Contratto di interazione proposto

Ogni comando segue `intenzione → anteprima → esecuzione → esito → memoria`.

1. **Intenzione.** Hover su oggetto/persona dà nome, verbo e contorno coerente: «Apri finestra», «Parla», «Attacca», «Vai». Clic su un'abilità che richiede bersaglio mantiene un indicatore persistente accanto alla barra: «Cura ferite · scegli 1 bersaglio · Esc annulla». Un clic sul terreno non deve far perdere silenziosamente l'intenzione.
2. **Anteprima.** In scontro mostrare costo (azione/bonus/movimento/slot), bersaglio valido, gittata/linea di vista, percorso e rischi calcolabili dalla Sim; se non disponibile, il motivo sta vicino al cursore o all'abilità. Niente probabilità inventate dove la Sim non le fornisce. In esplorazione il percorso blu e la griglia sul terreno restano nascosti, come già scelto; resta solo un segnale discreto sull'interagibile o sulla destinazione quando serve.
3. **Esecuzione.** Dare conferma nello spazio di gioco: movimento verso il bersaglio, gesto, arco/proiettile, apertura dell'anta, breve effetto di impatto. I feedback visivi non sostituiscono l'esito meccanico della Sim.
4. **Esito.** Vicino al bersaglio: «Mancato», «8 taglienti», «Porta chiusa», «Concentrazione interrotta». La barra aggiorna immediatamente risorse e stato. I rifiuti mostrano causa e, quando possibile, un rimedio concreto: «Fuori portata: avvicinati»; «Nessun percorso» solo quando il cammino fallisce.
5. **Memoria.** Nel registro espanso: chi agisce, contro chi, tiro/bonus quando noti, difesa, danni/tipo, effetti e cause. Nel registro chiuso: poche righe leggibili e filtro dei messaggi ripetuti; eventi critici rimangono richiamabili. Non serve trasformare l'HUD in una chat permanente.

Il puntatore e gli evidenziatori devono distinguere almeno: interagibile, attacco valido, attacco non valido, movimento, bersaglio di cura, porta/finestra, nemico visibile, destinazione irraggiungibile. Colore **più forma o simbolo**, mai solo colore.

## Ordine di lavoro

| Fase | Lavoro | Criterio di riuscita |
| --- | --- | --- |
| UX0 — mappa e prototipo | Inventario delle azioni/costi/stati, 8 schermate «prima/dopo», wireframe a 1280×720 per esplorazione, targeting, turno e registro aperto. | Nessun comando senza nome, costo, stato e feedback previsti; le decisioni precedenti su hotbar, Esc e log rimangono. |
| UX1 — feedback e stati | Modello unico per notifiche: informazione, successo, errore, rischio, combattimento; messaggio breve vicino all'azione + dettaglio nel log; stato selezionato/disabilitato/speso/fuori filtro differenziato; turno e risorse più leggibili. | Un giocatore capisce senza aprire il log se l'azione è partita, fallita o attende un bersaglio; aprendo il log trova la causa. |
| UX2 — puntamento e interazioni | Stesso linguaggio di hover/contorno/cursore per creature, porte, finestre e terreno; preview dei costi e motivi ottenuti dalla Sim; click distante resta «vai e interagisci». | La prova finestra distante non finisce in un clic di cammino dietro l'oggetto; un attacco impossibile spiega prima del clic perché. |
| UX3 — hotbar e iconografia | Affinare gerarchia della barra esistente e creare un set coerente di icone. Tooltip con nome, costo, bersagli, range, effetto e motivo di blocco. Evitare slot vuoti in eccesso quando il personaggio ha poche capacità, senza rompere la struttura fissa approvata. | A 1280×720 i comandi frequenti si riconoscono, lo stato selezionato è inequivoco e il registro non si sovrappone. |
| UX4 — apprendimento e accessibilità | Suggerimenti al primo attacco, prima interazione distante, prima magia e primo turno; scorciatoie mostrate nei tooltip; opzioni di scala UI/opacità log da considerare; verifica 1280×720 e 1920×1080. | Un nuovo giocatore completa quei quattro flussi senza dover indovinare un'icona; testi italiani non vengono tagliati. |

UX1 e UX2 vengono prima dell'abbellimento delle icone: altrimenti rendiamo più belli comandi ancora ambigui. Per lo sviluppo, separare componenti del client (barra/log/targeting) da eventuali campi nuovi della Sim; richiedere API nuove solo per dati che il client non può conoscere correttamente, come rischio di reazione o ragione di invalidità. Non duplicare le regole nel client.

## Icone: primo lotto e produzione

**Primo lotto campione, 8 icone:** attacco con arma, Scatto, Disimpegno, Schivata, Interagisci/apri, Cura ferite, Punizione divina, Torcia. Verifica soprattutto che Scatto/Disimpegno/Schivata siano distinguibili a 42 px. Dopo l'approvazione dello stile, estendere a Cerca trappole, Furtivo, Fermati, Inventario, Diario, Libro incantesimi, Riposo, Percezione divina, Imposizione delle mani, Veleno, Arma sacra e incantesimi effettivamente disponibili. Slot mano e condizioni seguono lo stesso linguaggio ma hanno significati diversi dalle azioni.

Specifiche per un campione generato: PNG originale con trasparenza, un solo pittogramma centrato per file, tela 256×256 o 512×512, silhouette leggibile quando ridotta a 32–42 px, margine libero 12–15%, nessuna lettera, numero, cornice, pulsante o sfondo. Un solo stile per tutto il lotto: fantasia medievale sobria, forme nette e poche superfici, luce e contorno coerenti. Il colore di categoria, i segnalini Azione/Bonus, la selezione, il grigio e i numeri di slot li applica Godot: non vanno «dipinti» nell'icona. Confrontare il foglio di otto pittogrammi **alla scala reale nella barra** prima di chiedere decine di immagini. Conservare il prototipo vettoriale corrente come fallback.

Per iniziare **non serve comprare o procurare un pacchetto di icone**. Il campione originale è in `docs/ux-icon-study/`: l'utente ha approvato lo stile inciso con finitura pittorica sobria. Prima di integrarlo o ampliare il set, semplificare i simboli meno chiari a 42 px e verificare la leggibilità nella barra. Non usare asset ritagliati dai giochi citati.
