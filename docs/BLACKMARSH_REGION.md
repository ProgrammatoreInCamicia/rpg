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

### Le tre piante 48×32 (Task 6B)

Dal Task 6B ogni area misura **48×32 caselle**: circa 5,5 volte le vecchie 20×14 e 18×14. Le righe vengono dalla prova di Codex (`docs/BLACKMARSH_REGION_LAYOUT_48x32.json`) e la geometria da `VillageMap.RegionMapPilot.cs`. La fonte resta lo scenario JSON. I `#` all'aperto sono scogli e canneti: bloccano passo e vista come un muro, e il client li disegna come terreno naturale. Da ogni bivio si raggiunge a piedi ogni casella percorribile della sua area, attraverso le porte e mai attraverso muri o finestre (test `On_each_large_map_every_walkable_square_is_reached_on_foot_from_the_fork`).

**Nucleo dei pescatori (`bm_isle_hamlet`).**

| Cosa | Dove |
|---|---|
| Bivio del nucleo (E) | x 45–47, y 14–18; uscita in (47,16), sul bordo est |
| Casa comune (C, interno) | x 9–15, y 11–17, muri sui confini; porta verso est in (15,14) |
| Dispensa | in (10,12), si usa da (11,12) |
| Focolare | in (9,17), lampada piccola 10/20 ft e ostacolo, nell'angolo opposto alla dispensa |
| Posti della casa comune | «casa» del pescatore (12,15); «guardia» (12,13), accanto all'accesso della dispensa |
| Piazzale del pozzo (W, acqua) | x 21–28, y 13–20; braciere 15/30 ft in (25,17), ostacolo |
| Posti del piazzale | «casa» del custode (24,15); il protagonista parte da (24,16) |
| Da trovare | un chiodo di ferro a terra in (13,25), a sud |
| Scogli | due gruppi a nord (x 20–25 e 28–34, y 7–9) e due a sud |

**Rimessa (`bm_isle_shed_area`).** Stessa disposizione della rimessa di `blackmarsh-watchhouse`, su caselle proprie e più spaziose.

| Cosa | Dove |
|---|---|
| Bivio della rimessa (E) | x 0–2, y 14–18; uscita in (0,16) |
| Rimessa (S, interno) | x 22–29, y 12–18 |
| Porta d'ingresso | in (22,16) verso ovest, chiusa a chiave con `bm-watch-shed-key`, forzabile CD 15, con lo stesso indizio |
| Stanza d'ingresso | x 22–25; posto «lavoro» del pescatore in (24,14) |
| Porta interna | parete fra x 25 e x 26, porta in (26,16) |
| Stanza interna | x 26–29 |
| Cassa | in (28,13), accesso da (27,13) |
| Lampada | 15/30 ft in (28,17), accanto alla cassa |
| Posto «guardia» | (28,14), accanto all'accesso della cassa |
| Finestra | fra (29,15) e (30,15), bloccata dalla salsedine, CD 10 |
| Da trovare | arnesi da scasso a terra in (35,9), a nord-est: una seconda via per la porta chiusa |

**Caletta (`bm_isle_cove`).**

| Cosa | Dove |
|---|---|
| Bivio della caletta (E) | x 0–2, y 14–18; uscita in (0,16) |
| Approdo dei Vasan (A) | x 36–45, y 19–29, chiuso a ovest da uno sperone di scogli (x 30–35, y 18–22) |
| Avvicinamento | (35,23) |
| Posto «casa» del predone | (39,23) |
| Provviste | in (43,26), con **due** accessi, (42,26) e (43,25): chi sta su uno non chiude ogni via |
| Fuoco spento | in (40,27): ostacolo, **senza luce**, perché di notte la caletta resta buia come prima |
| Da trovare | una torcia a terra in (38,28) |
| Scogli e canneti | altri gruppi a nord e a sud-ovest; il resto della spiaggia è libero |

Il capanno con l'arco corto e le frecce è il Task 7: nella caletta c'è spazio libero a nord.

**Tre scostamenti dalla prova, concordati in chat per non cambiare regole già verificate:**
- il focolare della casa comune;
- la lampada della rimessa nella stanza interna;
- il fuoco spento all'approdo.

**Una correzione:** i posti «guardia» della prova erano lontani dall'accesso dei depositi. Il motore considera sorvegliato un deposito solo se la guardia sta accanto a una casella da cui lo si usa. Con quei posti il custode di presidio non dissuadeva più il predone. Ora un test lo controlla per ogni deposito.

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
| Vasan (`bm_isle_vasan`) | Provviste dei Vasan | Consumo 4 alle 19:59; razzia di 4 sotto 1 razione; valutazione oraria. Nemici dei pescatori. **Difendono l'Approdo dei Vasan** (`"defends"`, Task 6A). |

| Deposito | Luogo | Razioni |
|---|---|---|
| Dispensa della casa comune (`bm_isle_pantry`) | Casa comune | 12 |
| Cassa delle razioni d'emergenza (`bm_isle_shed_store`) | Rimessa | 4 |
| Provviste dei Vasan (`bm_isle_vasan_cache`) | Approdo dei Vasan | 4 |

## La prima sera senza il giocatore (misurata dai test, seme dello scenario)

| Ora | Cosa succede |
|---|---|
| 16:00 | Il pescatore esce dalla casa comune e parte dal bivio alle 16:00:35; alle 16:06 arriva alla rimessa, apre con la chiave e richiude. |
| 19:35 | **Apertura della partita.** Pescatore al lavoro nella rimessa, custode al pozzo, predone all'approdo. |
| 19:45 | Il pescatore chiude la rimessa a chiave e riparte dal bivio alle 19:45:24; alle 19:51 arriva al nucleo e rientra in casa. |
| 19:59 | I Vasan consumano le 4 razioni delle loro provviste. |
| 20:00 | Razzia: il predone attraversa la caletta e alle 20:01 parte verso la rimessa (8 minuti contro i 12 del nucleo). |
| 20:09–20:15 | Arriva, scassina la porta, entra e alle 20:14 ruba le 4 razioni. Nessuno lo vede: il pescatore è già a casa. |
| 20:26 | Rientra alla caletta e consegna il bottino: provviste di nuovo a 4. Torna a riposare al suo posto. |
| giorno 2, 19:59–20:00 | Nuovo consumo; la rimessa è vuota, quindi la razzia va alla dispensa del nucleo (12 minuti). |
| giorno 2, 20:17 | Ruba 4 razioni dalla dispensa (restano 8). Il pescatore, in casa, sente il furto ma non vede chi. |
| giorno 2, 20:22 | Il pescatore lo racconta al custode; il custode va a presidiare la dispensa dal posto di guardia (12,13). |

Questi orari sono il comportamento del motore sui valori di contenuto attuali, misurati dai test con il seme dello scenario. Con le piante 48×32 (Task 6B) la storia è la stessa delle vecchie piante: gli orari si spostano di uno o due minuti perché i tratti a piedi sono più lunghi. La taratura per il gioco resta al Task 10.

## Come l'ordine delle visite cambia la serata (Task 6)

Misurato in `tests/RpgSandbox.Sim.Tests/BlackmarshRegionTests.cs`, solo con comandi del giocatore e l'orario del mondo; nessun flag di ramo. Situazione la sera del giorno 2 alle 21:00:

| Cosa fa il giocatore | Cosa succede | Dispensa | Provviste Vasan | Custode |
|---|---|---|---|---|
| Nulla | Come nella tabella sopra. | 8 | 4 | presidia la dispensa |
| Va alla rimessa, assiste al furto dalla stanza interna, torna e lo racconta al custode | Il custode percorre il sentiero e presidia la rimessa (ormai vuota); il predone gli deve 4 razioni. La sera dopo la razzia va alla dispensa, rimasta senza guardia. | 8 | 4 | presidia la rimessa |
| Porta le 4 razioni della rimessa alla dispensa prima delle 20:00 | La razzia della prima sera va alla dispensa; il predone trova il paladino all'accesso, lo squadra e desiste. I Vasan restano senza cibo e riprovano la sera dopo. | 16 | 0 | al pozzo |
| Va prima alla caletta, aspetta sulla spiaggia e riprende le provviste al buio dopo la consegna, poi torna sulla spiaggia | Al buio il predone sente il furto ma non vede chi; la spiaggia non è il suo territorio, quindi nessuna difesa. I Vasan sono di nuovo sotto la soglia: alle 21:00 razziano la dispensa. Con le piante 48×32 quella prima notte la Furtività del predone (15) supera il pescatore e nessuno se ne accorge; la sera dopo torna, il pescatore lo sente e il custode presidia la dispensa; la sera del giorno 3 il predone vede la guardia e desiste. | 4 | 4 | presidia la dispensa |

Il giocatore lontano non sa chi ha svuotato la cassa, neanche vedendola vuota: nessuna osservazione e nessun rapporto da offrire al custode.

**Correzioni di contenuto del Task 6:** il posto «casa» del predone, perché prima riposava sull'accesso delle provviste e il giocatore non poteva più usarle; i posti «guardia» di casa comune e rimessa, perché prima il custode di presidio si fermava sull'unico accesso della dispensa. Il presidio dissuade solo se il predone vede la guardia e se questa copre il deposito, cioè sta accanto a una casella da cui lo si usa: il Task 6B ha dovuto spostare per questo i posti «guardia» della prova 48×32.

**Limiti noti, comportamento del motore:** gli NPC non richiudono una porta senza chiave: la porta della casa comune resta aperta dopo la prima uscita del pescatore. Il custode presidia il deposito del furto anche se è già vuoto.

## Il predone difende l'approdo (Task 6A)

Regola generale del motore, attivata dal contenuto: una fazione può dichiarare i luoghi che difende (`"defends"`). Un suo membro combattente che vede, su quella mappa, qualcuno che considera ostile in uno di quei luoghi lo squadra una volta, con la stessa valutazione della razzia: Intuizione CD 12, valore reale se riesce, solo l'aspetto (armatura, scudo, arma in mano, ferite) se fallisce. Poi:

| Esito della valutazione | Cosa fa |
|---|---|
| Si aspetta di vincere | Si scaglia contro l'intruso. |
| Incerto: lo abbattono in almeno il 40% dei round che servono a lui | Gli intima di andarsene e si para davanti alle provviste. Se l'intruso mette le mani sulle provviste attacca subito; se resta più di 2 minuti attacca comunque. |
| Si aspetta di perdere nettamente | Arretra tenendosi a distanza e guarda senza intervenire, anche se gli rubano le provviste. Se la banda lo manda a razziare, ci va. |

Niente percezione a distanza: un intruso che non vede (buio, alle spalle, fuori vista) non provoca nulla; fuori dai luoghi difesi valgono le regole di prima. Le frasi (`SizedUp`, `DefenseWarning`, `DefenseAttack`, `DefenseWithdrawal`, `DefenseHelpless`) hanno il giocatore fra i partecipanti: compaiono fra i suoi eventi. Lo stato della difesa entra nel salvataggio (schema 44).

Misurato sull'isola, seme dello scenario (`BlackmarshRegionTests.cs`), arrivando alla caletta alle 19:47 e camminando fino a (37,20), dentro l'approdo:

| Il protagonista | Cosa succede |
|---|---|
| Paladino armato entra nell'approdo e resta | Alle 19:48, appena lo vede entrare, il predone lo squadra (lo legge: lo abbatte in 5,7 round, ne servono 2,8 al paladino) e gli intima di andarsene; alle 19:50 perde la pazienza e attacca. |
| Paladino armato va dritto alle provviste e prende | Avvertimento, poi «vede Protagonista mettere le mani sulle provviste e attacca» prima che il prelievo finisca: le provviste restano 4. |
| Paladino con spada e scudo riposti | Il predone si aspetta di vincere e attacca subito. |
| Resta sulla spiaggia, o prende al buio | Nessuna reazione (luogo non difeso; al buio non lo vede). |

Il ritiro davanti a due avversari e il luogo non difeso sono coperti da `DefenseTests.cs` su una piccola mappa dedicata. Soglie in `Tuning` (`DefendHopelessRatio` 0,4, `DefendPatience` 2 minuti, `DefendWithdrawSteps` 6), da tarare nel playtest.

**Limiti noti:** chi si ritira non porta rinforzi e non cambia i piani della banda; il predone non insegue fuori dal territorio; la reazione al furto in corso arriva alla sua decisione successiva (entro un minuto, prima che i 3 minuti del prelievo finiscano).
