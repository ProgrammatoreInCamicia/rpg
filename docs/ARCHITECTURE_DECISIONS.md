# Decisioni architetturali — RPG Sandbox

> Esito della discussione Claude Code ↔ Codex del 2026-10-09 (vedi `chat.txt`).
> Contesto di riferimento: `RPG_SANDBOX_CONTEXT.md`.
> Sono decisioni **per la vertical slice**, reversibili. Si rivedono quando il prototipo mostra un problema concreto (§13).
> Le domande riservate all'utente sono in fondo e non bloccano gli incrementi 0–1.

## 1. Struttura del progetto

```text
RpgSandbox.sln
src/RpgSandbox.Sim/          # dominio e simulazione, ZERO dipendenze Godot
  Simulation.cs  WorldState.cs  GameTime.cs
  Commands/  Scenarios/ScenarioBuilder.cs
game/                        # progetto Godot 4 .NET: input, presentazione, UI, debug
  project.godot  RpgSandbox.Game.csproj  Scenes/Main.tscn  Scripts/Main.cs
tests/RpgSandbox.Sim.Tests/  # xUnit: scenari e invarianti
```

- Namespace: `RpgSandbox.Sim`, `.Sim.Commands`, `.Sim.Scenarios`, `RpgSandbox.Game`, `RpgSandbox.Sim.Tests`.
- Game e Tests referenziano Sim. **Nessun altro progetto** (niente CLI, niente librerie di infrastruttura o contenuti).
- Le versioni di Godot e .NET si verificano e si fissano all'avvio dell'incremento 0. Sulla macchina ci sono gli SDK 8.0 e 9.0.

## 2. Stato e mutazioni

- `WorldState` è fatto di classi dati semplici. Le entità si riferiscono tra loro **solo tramite ID tipizzati**.
- Niente ECS e niente gerarchie di ereditarietà preventive. Si creano solo i tipi richiesti dal loop: per esempio il cibo è una quantità in un deposito, senza un sistema generico di inventari o oggetti.
- **Lo stato ha un solo autore: Sim.** Godot legge tramite query e viste in sola lettura, e scrive solo inviando comandi. Fuori da Sim non si espongono collezioni mutabili.
- Flusso: **comando → handler → fatto registrato**.
  - L'handler valida tutte le precondizioni **prima** di mutare. Se una fallisce, il comando fallisce in modo esplicito e senza effetti parziali.
  - Comando, fatto strutturato e voce leggibile della timeline sono tre cose distinte.
- **Niente event sourcing e niente event bus generico.** Le reazioni avvengono in fasi esplicite del ciclo (§3).
- Invarianti: quantità mai negative, trasferimenti senza duplicazioni, al massimo un incarico attivo per NPC, nessuna azione eseguita da un morto.
  - Gli handler le impongono a runtime.
  - I test le verificano.

## 3. Tempo e ciclo di simulazione

- Il tempo logico è un intero in **secondi di gioco** (`long`) ed è separato dal frame rate. I secondi sono stati scelti perché i round di 5e durano 6 secondi.
- Lo scheduler **salta all'istante della prossima scadenza esplicita** invece di avanzare un secondo alla volta. Scadenze esplicite sono completamenti, consumi, rivalutazioni e risvegli degli NPC inattivi. Basta una collezione ordinata, senza framework.
- `Advance(n)` elabora in ordine ogni scadenza intermedia. **`Advance(60)` equivale a 60 × `Advance(1)`**: il modo in cui la UI chiama l'API non deve influenzare la simulazione.
- Nella slice il tempo avanza solo con azioni esplicite: viaggio, attesa, lavoro, dialogo. Non c'è un accumulatore real-time.
- Ordine delle fasi per ogni istante elaborato:
  1. **completamenti** delle azioni in scadenza, con precondizioni ricontrollate e fallimento esplicito;
  2. **percezione**: i testimoni vengono determinati **nel momento in cui il fatto accade**;
  3. **politiche di fazione** in scadenza;
  4. **decisioni** degli NPC disponibili.
- Ogni nuova azione si completa in un istante futuro, quindi non possono nascere catene infinite nello stesso istante. Le parità si risolvono con un ordine stabile.
- Le conversazioni sono azioni con durata, non un sistema a parte.
- Il rapporto tra round, iniziativa e orologio globale si decide nell'esperimento di combattimento. Per esempio: quando agisce un NPC che arriva a scontro iniziato? L'orologio non deve avanzare una volta per ogni combattente.

## 4. Spazio

- **Luogo** (Granaio, Locanda, Fattoria, Campo banditi…) è il nodo autorevole: decide presenza, collegamenti e interazioni.
- **Area** (Villaggio, Bosco) serve solo a raggruppare i Luoghi.
- **Presentazione isometrica con segnaposto già dall'incremento 0**, per scoprire subito i rischi tecnici di ordinamento, selezione col mouse e camera. Le API (per esempio `TileMapLayer`) vanno verificate sulla versione di Godot adottata. La proiezione è solo presentazione: non si aggiungono navigazione, terreno su più livelli o grafica definitiva.
- **Passare da un Luogo a un altro è sempre un viaggio simulato**, con origine, destinazione e arrivo previsto, anche dentro la stessa Area. Camminare dentro un Luogo in Godot è solo presentazione e non consuma tempo.
- La sim è l'unica verità. Godot mostra gli NPC presenti nei Luoghi dell'Area corrente: chi parte esce dalla scena e chi arriva entra. NavigationAgent2D è rimandato oppure resta un abbellimento.
- Quando distanza e ostacoli entreranno nelle regole (per esempio inseguimenti o combattimento locale), servirà uno stato spaziale autorevole più fine.

## 5. Determinismo e casualità

- Ordine di iterazione sempre stabile. Nel dominio non si usano mai l'orologio reale né l'ordine di enumerazione dei Dictionary.
- **RNG proprio e serializzabile**, basato su un algoritmo consolidato come SplitMix64 o xorshift64\*.
  - Motivo: `System.Random` non espone il proprio stato, quindi dopo un caricamento non si può riprendere la stessa sequenza.
  - Va introdotto con la prima meccanica casuale, non prima.
  - Va testato su sequenze note e sul ripristino dello stato.
- Niente fixed-point: si usano interi dove sono naturali.
- **Test decisivo:** una simulazione continua e una con salvataggio e caricamento a metà (durante viaggi e incarichi) devono arrivare allo stesso stato significativo.

## 6. Persistenza

- Snapshot JSON (`System.Text.Json`) **versionato**, indipendente da Godot. Le Resource `.tres` non si usano per lo stato di gioco.
- Lo snapshot contiene:
  - ora corrente;
  - entità;
  - azioni in corso e scadenze;
  - prossima valutazione di ogni fazione;
  - conoscenze degli NPC;
  - relazioni;
  - contatori degli ID;
  - stato dell'RNG.
- Si salva solo al termine di un passo completo.
- Lo storico dei fatti è limitato e si può potare. Le memorie restano valide perché ne contengono una copia (§7).
- Una versione incompatibile viene rifiutata con un messaggio chiaro. Per ora non c'è un framework di migrazione.

## 7. NPC: decisioni, conoscenza, memoria

- **Regole a priorità esplicite**, ognuna con un nome e un motivo. Utility AI e GOAP si valutano solo quando più alternative credibili competono davvero.
- Ogni decisione produce una traccia: regola scelta, dati letti ed eventuale motivo del fallimento. Si conserva solo l'ultima per NPC.
- Le azioni hanno una durata e precondizioni ricontrollate al completamento. Un sistema generale di interruzione può aspettare.
- **L'NPC pianifica su ciò che sa; l'esecuzione viene validata sulla realtà.**
- La memoria contiene **solo ciò che è stato percepito**: chi, cosa, dove e quando, con l'autore eventualmente ignoto. Si aggiungono la fonte (testimonianza diretta o racconto di X), il momento e un `EventId` facoltativo come riferimento.
- Testimonianza, regola provvisoria e **deterministica** nella slice: essere nello stesso Luogo e prestare attenzione, per esempio non dormire. **Accorgersi del furto e identificare l'autore sono due esiti distinti**: riconoscere il colpevole è un dato esplicito, non automatico. Prove 5e come Furtività contro Percezione arrivano dopo la slice (§9).
- Primo e unico fatto conoscibile nella slice: **il furto di cibo**. Pettegolezzi, valenze e confidenze numeriche arrivano solo quando cambiano una decisione giocabile.

## 8. Fazioni

- Una **politica concreta** al posto di un'AI generica: per esempio, se le scorte scendono sotto una soglia, i banditi organizzano una razzia.
- Le fazioni non creano risorse dal nulla.
- Ogni incarico viene assegnato a membri disponibili, richiede tempo e può fallire.
- Un incarico di fazione ha la precedenza sulla routine personale. In futuro non l'avrà su fame o fuga, che però si introducono solo quando serviranno.
- Un incarico si cancella **subito** se un partecipante diventa indisponibile, senza aspettare la valutazione successiva.
- La cadenza delle valutazioni è un **parametro sperimentale** salvato nello stato. Il giocatore deve avere davvero la possibilità di intervenire, e questo dipende anche dalla durata degli incarichi e dalle informazioni che riceve.

## 9. Contenuti, dadi, LLM

- **Scenario costruito in C#** (`ScenarioBuilder`), separato dallo stato mutabile. JSON, content pack e behavior registry arriveranno solo con un'esigenza concreta.
- **Regolamento: D&D 5e, solo tramite SRD** (CC-BY-4.0, con l'attribuzione richiesta).
  - Riferimento provvisorio: **SRD 5.2.1**. L'utente deve ancora confermare se intende le regole 2014 (SRD 5.1) o quelle 2024 (SRD 5.2). Non si mescolano versioni.
  - Contenuti originali (ambientazione, creature, nomi) sono permessi. Contenuti D&D esterni allo SRD e il marchio come nome del prodotto no.
  - Il testo dell'attribuzione e le condizioni per gli adattamenti vanno verificati sul materiale effettivamente incorporato.
- Il modulo `Rules` in Sim nasce **solo quando serve la prima regola**: si prevede una meccanica di furtività giocabile dopo la slice. In quel momento entrano anche dadi, prove d20 e l'RNG serializzabile (§5). I riposi aspettano che esistano risorse da recuperare.
- Spirito OSR e 5e: restano il mondo autonomo, le risorse limitate e le conseguenze persistenti. Letalità e scarsità si regolano dopo averle osservate giocando, senza compensazioni preventive.
- **Nessuna interfaccia per i dadi per ora.** Una futura risoluzione manuale sarebbe asincrona e richiederebbe di sospendere l'azione, quindi l'interfaccia si definisce al primo caso d'uso reale.
- Nessun LLM nella logica di simulazione.

## 10. Debug

- Pannello minimo, separato dalla UI del giocatore perché il pannello mostra la verità globale e il dialogo no. Contiene:
  - ora corrente;
  - scorte;
  - per l'NPC selezionato: azione corrente, ultima decisione e motivo, cosa sa;
  - ultimi N fatti.
- Timeline navigabile, inspector completi e controlli di velocità si aggiungono quando servono.

## 11. Piano incrementale

| # | Contenuto | Criterio d'uscita |
|---|-----------|-------------------|
| 0 | Scheletro: solution, Sim, test, Godot che chiama Sim. Due Luoghi collegati. | In Godot il giocatore viaggia tra i Luoghi, il viaggio dura del tempo e ora e arrivo sono visibili. Un test verifica il viaggio. |
| 1 | Trasferimento di cibo tra possessori e depositi reali. | Le scorte si aggiornano. Una quantità insufficiente provoca un rifiuto senza effetti. I test coprono viaggio, conservazione del cibo e assenza di effetti parziali. |
| 2 | Furto autonomo di un bandito (deterministico, senza tiri), salvataggio/caricamento, debug della causa. | Il furto avviene senza il giocatore. Salvare e ricaricare conserva azioni in corso e scadenze. Il pannello spiega il perché. |
| 3 | Testimonianza e reazione della guardia. | La guardia reagisce a informazioni ricevute da un testimone **o** dal giocatore. |

**Criterio d'uscita della slice** (scenario §14 del context):

- a) Senza il giocatore, le scorte calano e la simulazione prosegue.
- b) Partendo dallo stesso stato e con lo stesso seed, *attendere*, *consegnare cibo* e *riferire il furto* portano a stati diversi.
- c) Ogni reazione si spiega dal pannello debug risalendo all'informazione ricevuta.
- d) Gli scenari con ladro identificato, ladro ignoto e nessun testimone si comportano correttamente.

## 12. Rischi principali

1. **Ordine temporale ambiguo.** Mitigazione: parità risolte con ordine stabile, test sull'avanzamento a passi diversi e sui completamenti simultanei.
2. **NPC onniscienti.** Mitigazione: osservazioni filtrate, test con ladro identificato, ignoto e senza testimoni.
3. **Ripresa incoerente dopo un caricamento.** Mitigazione: confronto tra simulazione continua e save/load durante viaggi e incarichi.
4. **Simulazione corretta ma poco giocabile.** Mitigazione: verificare che le conseguenze siano visibili e che ci siano opportunità concrete di intervento.

## 13. Direzione creativa (risposte dell'utente, 2026-10-09) e conseguenze

| Tema | Scelta dell'utente | Conseguenza concordata |
|------|--------------------|------------------------|
| Combattimento | A turni, con interesse per la "via di mezzo" di BG1/BG2 | Prima implementazione a turni 5e (iniziativa, round da 6 s), dentro Sim ed eseguibile anche fuori schermo. Il tempo reale con pausa **non** è considerato solo una modalità di presentazione: se servirà, si rivaluterà nel dominio. Si parte da un sottoinsieme esplicito delle regole, non dalla 5e completa. |
| Personaggio | Protagonista singolo con compagni reclutabili nel mondo | Un compagno è un NPC normale, con obiettivi e memoria propri. L'**appartenenza al gruppo è una relazione persistente**, distinta dal comportamento "seguire" e dall'azione `Travel`. Primo esperimento: reclutamento consensuale e viaggio insieme. Rifiuto, abbandono e controllo diretto non sono ancora requisiti. |
| Grafica | Isometrica | Segnaposto isometrici già dall'incremento 0 (§4). |
| Regole | D&D 5e | Solo SRD (§9). Versione 2014 o 2024 da confermare. |

**Candidati dopo la slice** (non sono milestone fissate):

1. Furtività giocabile: prime prove 5e e RNG.
2. Combattimento a turni con un'interazione del giocatore. La guardia contro un bandito fuori schermo verifica l'autonomia, ma da sola non dimostra che il gioco sia giocabile. Spazio tattico, tempo condiviso e salvataggio di uno scontro in corso si decidono dentro questo esperimento.
3. Reclutamento di un compagno.

**Domande ancora aperte per l'utente:**

- SRD 5.1 (regole 2014) o SRD 5.2.1 (regole 2024)?
- Combattimento: turni puri, oppure in futuro anche una modalità alla BG (turni risolti automaticamente con pausa, o tempo reale con pausa vero e proprio)?
