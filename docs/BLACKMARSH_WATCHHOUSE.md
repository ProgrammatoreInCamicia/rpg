# La rimessa della vedetta

Seconda situazione giocabile, la prima in Blackmarsh (vedi `ASTRA_BLACKMARSH_ROADMAP.md`). Questo documento conserva nel repository la fonte, la pianta e gli orari misurati, così si lavora anche senza l'archivio `docs/black_marsh.zip` (locale, ignorato da Git).

## Fonte e adattamento

**Fonte:** *Blackmarsh*, versione 11, esagono **0912**. Autore Robert Conley (Bat in the Attic Games), copyright 2011. Licenza Creative Commons Attribution 4.0 (CC BY 4.0), https://creativecommons.org/licenses/by/4.0/. Il testo è distribuito anche sotto OGL 1.0a; qui si usa la licenza CC BY 4.0.

Il testo originale di 0912, in sintesi: un'isola abitata da circa cento uomini che vivono di pesca. I signori di Castle Blackmarsh gliel'hanno concessa in cambio della vigilanza contro i razziatori, in particolare i vichinghi Vasan. A sud dell'isola c'è una torre di legno di 20 piedi, visibile dalla terraferma; al segno di pericolo vi si accende un fuoco, rilanciato da una catena di torri fino a Castle Blackmarsh.

**Aggiunte originali di questo progetto**, coerenti con 0912 ma non descritte nella fonte: la rimessa e la sua chiave, le quattro razioni d'emergenza, il singolo predone vasan, la sua razzia, l'Approdo Vasan sulla stessa isola e la pianta locale. Non sono un episodio canonico di Blackmarsh. La torre e i segnali di fuoco restano contesto fuori dall'area percorribile e non hanno alcun effetto nel gioco.

## Contenuto

- Scenario: `game/content/scenarios/blackmarsh-watchhouse.json`, unica fonte della pianta.
- Chiave: `game/content/blackmarsh/items.json` (`bm-watch-shed-key`).
- Factory: `RpgSandbox.Sim.Scenarios.BlackmarshWatchhouse.CreateSession()`. Carica lo scenario e avanza normalmente fino alle **19:57**: nessuno stato preparato a mano.
- Test: `tests/RpgSandbox.Sim.Tests/BlackmarshWatchhouseTests.cs`.

Tutti gli ID dello scenario hanno il prefisso `bm_watch_`, compreso il protagonista `bm_watch_player`. Si riusano solo i profili (`paladin`, `raider`) e gli oggetti comuni.

```
x  012345678901234567
y0 ##################
 1 #PP.............##
 2 #PP.............##
 3 #PP.............##     S  rimessa (interno): x 6–11, y 4–8
 4 #PP...SSSSSS....##        stanza d'ingresso x 6–8, stanza della cassa x 9–11
 5 #PP...SSSSSS....##     P  Sentiero; il protagonista parte da (1,6)
 6 #PP...SSSSSS....##     A  Approdo Vasan; il predone parte da (14,10)
 7 #PP...SSSSSS....##
 8 #PP...SSSSSS....##
 9 #PP...........AA##
10 #PP...........AA##
11 #PP...........AA##
12 #PP...........AA##
13 ##################
```

I muri della rimessa sono confini sottili fra le caselle, non caselle bloccate.

| Elemento | Dove | Stato iniziale |
|---|---|---|
| Porta esterna | confine (5,6)–(6,6), casella (6,6) | chiusa, serrata; chiave del protagonista |
| Porta interna | confine (8,6)–(9,6), casella (9,6) | chiusa, senza serratura |
| Finestra | confine (11,6)–(12,6), verso la riva | chiusa, telaio bloccato (salsedine) |
| Lampada | (10,6) | luce intensa 15 ft, fioca 30 ft |
| Cassa delle razioni d'emergenza | (11,4), accesso da (11,5) | 4 razioni, proprietà dei Pescatori della vedetta |
| Provviste dei Vasan | (15,12), accesso da (14,12) | 4 razioni |

**Fazioni.** I Pescatori della vedetta possiedono la cassa. Il protagonista ne fa parte, quindi il suo prelievo non è un furto. I Vasan razziano: consumo di 4 razioni alle 19:59, valutazione ogni ora, razzia sotto la soglia di 1, bottino 4. Le due fazioni sono nemiche.

## Cronologia misurata

Seed e factory sono quelli del file. Orari della Sim (giorno 1).

| Momento | Senza intervento | Con la porta lasciata aperta |
|---|---|---|
| Apertura (in pausa nel client) | 19:57:00 | 19:57:00 |
| Consumo dei Vasan: provviste 4 → 0 | 19:59:00 | 19:59:00 |
| La politica di fazione ordina la razzia | 20:00:00 | 20:00:00 |
| Ingresso | vetro rotto alle 20:00:06, scavalca alle 20:00:14 (si taglia) | dalla porta alle 20:00:20 |
| All'accesso della cassa | 20:00:16 | 20:00:27 |
| Furto delle 4 razioni | 20:03:16 | 20:03:27 |
| Consegna all'approdo | 20:05:28, di nuovo dalla finestra | dalla porta |

**Recupero.** Partendo subito, il protagonista apre la porta con la chiave (si sblocca aprendola) e apre la porta interna. Comincia il prelievo alle 19:57:10, lo finisce alle 20:00:10 e torna al sentiero alle 20:00:20. Il predone, che ora trova la porta aperta, arriva alla cassa vuota e rientra a mani vuote.

**Margine.** Il recupero riesce anche partendo dal sentiero alle 19:59:45, quasi tre minuti dopo l'apertura. Partendo verso le 20:00 il predone è già alla cassa. Il protagonista non può più occupare l'accesso e il recupero fallisce, a meno di affrontarlo.

**Checkpoint comune:** 20:30.
- Recupero: protagonista 14 razioni, sul sentiero; rimessa 0; provviste dei Vasan 0.
- Rinuncia: protagonista 10; rimessa 0; provviste dei Vasan 4.

In tutti e due i casi le razioni si conservano: 18 all'inizio, uguale a 4 consumate più quelle rimaste su attori e depositi.

**Informazione.** Il protagonista rimasto sul sentiero non vede il predone e non sente il vetro: è a più di 6 caselle. A fine prova non ha alcuna osservazione.

## Regole usate

Nessuna regola nuova, nessun controllo sugli ID nella Sim, nessun incarico scritto dai test. La razzia nasce dalla politica di fazione (consumo, poi valutazione). Il resto viene dalle regole esistenti:
- il percorso di effrazione: `BreakInStep` sceglie la finestra perché costa meno della porta serrata;
- la finestra bloccata, il vetro rotto senza prova, lo scavalcamento con il taglio;
- il ritorno dalla stessa finestra;
- lo sblocco con la chiave all'apertura.

Il predone ha gli attrezzi da scasso del suo profilo, ma nella pianta attuale non li usa: la finestra è la via più corta. Il ramo di base quindi non dipende da tiri di scasso.

## Limiti noti

- **Sovrapposizione dei corpi.** Un NPC in cammino può entrare nello spazio del protagonista (limite noto della prima slice). Se il protagonista resta sull'accesso, il predone può cominciare il proprio prelievo sovrapposto. Il protagonista invece viene respinto da un accesso occupato.
- **Ritmo umano non misurato.** Gli orari qui sopra sono tempi della Sim; la durata per una persona e la leggibilità del margine si misurano nel Task 10.
- **Niente arredo.** Il tavolo arriva con il Task 2 (ostacoli nel loader JSON).
