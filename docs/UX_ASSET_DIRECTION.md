# Direzione UX e asset della demo

Riferimenti: `docs/bg3.png` per la gerarchia dei comandi, *Project Zomboid* per leggibilità di interni, tetti e occlusione. Non copiare cornici o icone dei giochi di riferimento.

Le immagini `docs/hotbar.webp` e `docs/hotbar_dimension.webp` rendono esplicita la struttura della barra: ritratto e mani a sinistra, risorse e concentrazione sopra, sezioni Common/Class/Items/Passives separate da divisori, schede in basso, una zona fissa per gli effetti riattivabili e il pulsante del turno a destra. Le misure 4×19 e oltre descrivono una barra piena di un personaggio avanzato; alla scala attuale della demo lascerebbero soprattutto celle vuote.

## Proposta per la prossima barra

| Zona | Prima versione nel nostro gioco |
| --- | --- |
| Sinistra | Ritratto con PF e condizioni essenziali; due slot mano e torcia rimangono immediati. |
| Sopra la griglia | Azione, azione bonus e movimento disponibili; concentrazione attiva sempre visibile qui, non soltanto nella scheda di stato espansa. I filtri Azione/Bonus restano cliccabili. |
| Centro | Griglia compatta di icone: una riga ora, una seconda quando il numero di comandi la richiede. Schede **Comuni**, **Paladino**, **Oggetti**; ogni scheda mostra solo comandi realmente disponibili nella demo. Niente celle decorative vuote. |
| Destra | Una striscia fissa per modalità persistenti e azioni riattivabili, quando esistono; **Fine turno** isolato e ben distinto dai comandi di attacco. |
| Sotto | Schede leggibili e selezione evidente. La futura scheda **Personalizza** compare solo quando spostare e salvare scorciatoie sarà funzionante. |

Comuni: attacco, movimento tattico, ricerca, riposo e interazioni generiche. Paladino: imposizione delle mani, incantesimi, percezione divina e punizioni quando disponibili. Oggetti: torcia, consumabili e strumenti davvero utilizzabili. La distinzione per categoria è indipendente dal filtro del costo Azione/Bonus. In esplorazione si mantiene la stessa posizione della barra, evitando che i comandi saltino da un lato all'altro quando inizia lo scontro.

## Gerarchia sullo schermo

1. Il mondo occupa il centro. Segnali importanti (bersaglio, trappola nota, oggetto interagibile) devono rimanere distinguibili dallo sfondo e dai nomi dei PNG.
2. La barra in basso contiene ritratto, mani, risorse del turno e azioni a icona. Il tooltip spiega nome, costo e motivo di indisponibilità; le informazioni frequenti non richiedono di aprirlo.
3. La scheda in alto a sinistra resta compatta: luogo, ora, scorte e condizioni. Un clic espande stato, esperienza, effetti e osservatori. PF e azioni non vengono duplicati in modo permanente.
4. Il registro è discreto durante l'esplorazione; eventi di combattimento e rifiuti devono avere un testo chiaro e recuperabile dall'icona a destra.

## Primo lotto grafico utile

| Priorità | Asset | Criterio per approvarlo |
| --- | --- | --- |
| 1 | Terreno, pareti sottili, porte, finestre e tetti della stessa stanza | Materiali coerenti a scala 1280×720, differenza netta tra spazio percorribile e ostacolo, interni leggibili anche con luce bassa. |
| 2 | Silhouette e palette di paladino, guardia, contadino e razziatore | Riconoscibili a 40–70 px di altezza e nelle otto direzioni principali; equipaggiamento leggibile senza etichette permanenti. |
| 3 | Effetti brevi di colpo, ferita, freccia, torcia e veleno | La direzione e il bersaglio dell'azione si capiscono anche senza il registro. |
| 4 | Ritratti e piccoli oggetti d'inventario | Si introducono dopo aver fissato lo stile dei personaggi; non devono contraddire il modello che appare nel mondo. |

Le icone dei comandi restano per ora nel sistema vettoriale del client: condividono spessore del tratto, dimensione e colori funzionali. Un'immagine generata per ciascun pulsante creerebbe variazioni difficili da leggere. Gli asset raster servono prima al mondo e ai personaggi. Conviene produrre un piccolo campione di una stanza e quattro silhouette, provarlo nel gioco alla scala reale e solo dopo estendere il set.
