# L'isola della vedetta: la regione

Prima piccola regione persistente (vedi `ASTRA_BLACKMARSH_REGION_ROADMAP.md`, Task 2): tre luoghi dell'isola 0912 in **un solo mondo**, con un solo orologio e un solo salvataggio. Questo documento conserva fonte, ID, scorte e orari dello scenario regionale. La situazione isolata della rimessa resta descritta in `docs/BLACKMARSH_WATCHHOUSE.md` e continua a caricarsi come prima.

## Fonte e adattamento

**Fonte:** *Blackmarsh*, versione 11, esagono **0912**. Autore Robert Conley (Bat in the Attic Games), copyright 2011. Licenza Creative Commons Attribution 4.0 (CC BY 4.0), https://creativecommons.org/licenses/by/4.0/. Sintesi del testo originale in `docs/BLACKMARSH_WATCHHOUSE.md`: un'isola di pescatori incaricati della vigilanza contro i razziatori, in particolare i vichinghi Vasan, con una torre di segnalazione a sud.

**Aggiunte originali di questo progetto**, non descritte nella fonte:
- i tre luoghi (nucleo dei pescatori, rimessa, caletta) e i sentieri che li uniscono;
- il custode, il pescatore e il singolo predone vasan;
- le tre scorte e tutte le piante.

Rappresentiamo un frammento dell'isola, non i suoi circa cento abitanti. La torre e i segnali di fuoco, Castle Blackmarsh e le altre località restano fuori dall'area percorribile e non hanno effetti nel gioco.

## Contenuto

- Scenario: `game/content/scenarios/blackmarsh-watch-island.json` (id `blackmarsh-watch-island`), unica fonte delle piante.
- Factory: `RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.CreateSession()`. Crea il mondo a mezzanotte del primo giorno e lo fa avanzare normalmente fino alle **19:35**: nessuno stato preparato a mano.
- Test: `tests/RpgSandbox.Sim.Tests/BlackmarshWatchIslandTests.cs`.

Tutti gli ID hanno il prefisso `bm_isle_`. Si riusano solo i profili (`paladin`, `village-guard`, `farmer`, `raider`), gli oggetti comuni e la chiave della rimessa `bm-watch-shed-key` (`game/content/blackmarsh/items.json`): è la stessa chiave dello stesso edificio.

## Geografia

Tre aree, ognuna con una mappa e un'uscita sul bordo, il **bivio**. Da ogni bivio partono due sentieri; si parte solo dalla casella d'uscita e si arriva sulla casella d'uscita dell'altra mappa (o, se occupata, su una libera accanto: regola del Task 1).

```
                  Nucleo dei pescatori
                  /                  \
         6 minuti/                    \12 minuti
                /                      \
          Rimessa ------- 8 minuti ----- Caletta
```

| Sentiero (bidirezionale) | Minuti a passo normale |
|---|---|
| Bivio del nucleo – Bivio della rimessa | 6 |
| Bivio della rimessa – Bivio della caletta | 8 |
| Bivio del nucleo – Bivio della caletta | 12 |

Ogni sentiero diretto costa meno del giro attraverso il terzo luogo. Sono durate di contenuto da tarare nel playtest, non una scala ufficiale dell'esagono.

### Nucleo dei pescatori (`bm_isle_hamlet`, 20×14)

```
x  01234567890123456789
y0 ####################
 1 #.................EE
 2 #.................EE
 3 #.CCCCCC..........EE     C  Casa comune (interno): x 2–7, y 3–8; porta in (7,6) verso est
 4 #.CCCCCC..WWWWW...EE        dispensa in (2,3), si usa da (3,3); focolare in (2,8)
 5 #.CCCCCC..WWWWW...EE        posto «casa» del pescatore (5,7); posto «guardia» (4,4)
 6 #.CCCCCC..WWWWW...EE     W  Piazzale del pozzo (acqua): pozzo con braciere in (12,6), ostacolo
 7 #.CCCCCC..WWWWW...EE        posto «casa» del custode (12,5); il protagonista parte da (11,8)
 8 #.CCCCCC..WWWWW...EE     E  Bivio del nucleo, uscita in (19,6)
 9 #.........WWWWW...EE
10 #.................EE
11 #.................EE
12 #.................EE
13 ####################
```

### Rimessa (`bm_isle_shed_area`, 18×14)

La **stessa pianta** di `blackmarsh-watchhouse`: muri, porta d'ingresso chiusa a chiave in (6,6), porta interna in (9,6), finestra gonfia di salsedine fra (11,6) e (12,6), lampada e tavolo in (10,6), cassa in (11,4) con accesso da (11,5). Cambiano solo le zone:
- il vecchio sentiero diventa il **Bivio della rimessa**, esteso fino al bordo, con l'uscita in (0,6);
- il vecchio Approdo Vasan diventa terreno libero: l'approdo ora è alla caletta.

Posto «lavoro» del pescatore: (7,4), nella stanza d'ingresso, fuori dal passaggio fra le due porte. Posto «guardia»: (10,8), nell'angolo della stanza interna, in vista della cassa e fuori dal suo accesso.

### Caletta (`bm_isle_cove`, 20×14)

```
x  01234567890123456789
y0 ####################
 1 EEE................#
 2 EEE................#     E  Bivio della caletta, uscita in (0,6)
 3 EEE................#     A  Approdo dei Vasan: provviste in (17,11), accesso da (16,11);
 4 EEE................#        posto «casa» del predone (15,9), dove parte e torna a riposare
 5 EEE................#     x 11–16, y 1–5: spazio libero riservato al capanno del Task 7
 6 EEE................#
 7 EEE................#
 8 EEE..........AAAAAA#
 9 EEE..........AAAAAA#
10 EEE..........AAAAAA#
11 EEE..........AAAAAA#
12 EEE..........AAAAAA#
13 ####################
```

Il capanno con l'arco corto e le frecce è il Task 7: qui c'è solo lo spazio.

## Persone, fazioni, scorte

| Attore | Profilo | Casa | Note |
|---|---|---|---|
| Protagonista (`bm_isle_player`) | paladino | — | Pescatori della vedetta; parte nel piazzale; corredo come nella rimessa (cotta, scudo, spada lunga, 6 giavellotti, otre, 3 torce, chiave), 10 razioni. |
| Custode della vedetta (`bm_isle_keeper`) | village-guard | Piazzale del pozzo | Autorità dei pescatori: è da lui che si va a raccontare. Di guardia al pozzo. |
| Pescatore (`bm_isle_fisher`) | farmer | Casa comune | Lavora alla rimessa **16:00–19:45** e ha la chiave: percorre davvero il sentiero, apre e richiude a chiave. |
| Predone vasan (`bm_isle_raider`) | raider | Approdo dei Vasan | Vasan. |

Il motore ha **un solo posto per tipo per luogo**: custode e pescatore nella stessa casa starebbero sulla stessa casella. Per questo il custode ha il posto al pozzo, coerente con la sorveglianza della fonte. La casa di un personaggio è il luogo da cui parte.

| Fazione | Deposito di casa | Politica |
|---|---|---|
| Pescatori della vedetta (`bm_isle_islanders`) | Dispensa della casa comune | Nessuna iniziativa; autorità: il custode. |
| Vasan (`bm_isle_vasan`) | Provviste dei Vasan | Consumo 4 alle 19:59; razzia di 4 sotto 1 razione; valutazione oraria. Nemici dei pescatori. |

| Deposito | Luogo | Razioni |
|---|---|---|
| Dispensa della casa comune (`bm_isle_pantry`) | Casa comune | 12 |
| Cassa delle razioni d'emergenza (`bm_isle_shed_store`) | Rimessa | 4 |
| Provviste dei Vasan (`bm_isle_vasan_cache`) | Approdo dei Vasan | 4 |

## La prima sera senza il giocatore (misurata dai test, seme dello scenario)

| Ora | Cosa succede |
|---|---|
| 16:00 | Il pescatore esce dalla casa comune e parte dal bivio; alle 16:06 arriva alla rimessa, apre con la chiave e richiude. |
| 19:35 | **Apertura della partita.** Pescatore al lavoro nella rimessa, custode al pozzo, predone all'approdo. |
| 19:45 | Il pescatore chiude la rimessa a chiave e riparte; alle 19:51 arriva al nucleo e rientra in casa. |
| 19:59 | I Vasan consumano le 4 razioni delle loro provviste. |
| 20:00 | Razzia: il predone parte verso la rimessa (8 minuti contro i 12 del nucleo). |
| 20:08–20:13 | Arriva, scassina la porta, entra e ruba le 4 razioni. Nessuno lo vede: il pescatore è già a casa. |
| 20:24 | Rientra alla caletta e consegna il bottino: provviste di nuovo a 4. Torna a riposare al suo posto. |
| giorno 2, 19:59–20:00 | Nuovo consumo; la rimessa è vuota, quindi la razzia va alla dispensa del nucleo (12 minuti). |
| giorno 2, 20:15 | Ruba 4 razioni dalla dispensa (restano 8). Il pescatore, in casa, sente il furto ma non vede chi. |
| giorno 2, 20:20 | Il pescatore lo racconta al custode; il custode va a presidiare la dispensa dal posto di guardia (4,4). |

Questi orari sono il comportamento del motore sui valori di contenuto attuali, misurati dai test con il seme dello scenario. La taratura per il gioco resta al Task 10.

## Come l'ordine delle visite cambia la serata (Task 6)

Misurato in `tests/RpgSandbox.Sim.Tests/BlackmarshRegionTests.cs`, solo con comandi del giocatore e l'orario del mondo; nessun flag di ramo. Situazione la sera del giorno 2 alle 21:00:

| Cosa fa il giocatore | Cosa succede | Dispensa | Provviste Vasan | Custode |
|---|---|---|---|---|
| Nulla | Come nella tabella sopra. | 8 | 4 | presidia la dispensa |
| Va alla rimessa, assiste al furto dalla stanza interna, torna e lo racconta al custode | Il custode percorre il sentiero e presidia la rimessa (ormai vuota); il predone gli deve 4 razioni. La sera dopo la razzia va alla dispensa, rimasta senza guardia. | 8 | 4 | presidia la rimessa |
| Porta le 4 razioni della rimessa alla dispensa prima delle 20:00 | La razzia della prima sera va alla dispensa; il predone trova il paladino all'accesso, lo squadra e desiste. I Vasan restano senza cibo e riprovano la sera dopo. | 16 | 0 | al pozzo |
| Va prima alla caletta e riprende le provviste dopo la consegna | I Vasan sono di nuovo sotto la soglia: alle 21:00 razziano la dispensa, il pescatore lo sente e il custode la presidia; la sera dopo il predone vede la guardia e desiste. | 8 | 0 | presidia la dispensa |

Il giocatore lontano non sa chi ha svuotato la cassa, neanche vedendola vuota: nessuna osservazione e nessun rapporto da offrire al custode.

**Correzioni di contenuto del Task 6:** il posto «casa» del predone, perché prima riposava sull'accesso delle provviste e il giocatore non poteva più usarle; i posti «guardia» di casa comune e rimessa, perché prima il custode di presidio si fermava sull'unico accesso della dispensa. Il presidio dissuade comunque, perché conta che il predone veda la guardia.

**Limiti noti, comportamento del motore:** il predone non reagisce a un intruso nel suo approdo, la valutazione scatta solo all'accesso del deposito. Gli NPC non richiudono una porta senza chiave: la porta della casa comune resta aperta dopo la prima uscita del pescatore. Il custode presidia il deposito del furto anche se è già vuoto.
