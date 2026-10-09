# RPG Sandbox — piano della prima slice

Data: 9 ottobre 2026. Fonte della visione: [RPG_SANDBOX_CONTEXT.md](RPG_SANDBOX_CONTEXT.md).
Discussione e revisioni: [chat.txt](chat.txt). Decisioni architetturali condivise:
[docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md), redatto da Claude
e verificato da Codex. Quel documento è il riferimento per le scelte comuni;
questo piano dettaglia ordine di lavoro, verifiche e parametri sperimentali.

Stato: aggiornato dopo il Round 4 e le risposte dell'utente: turni come direzione
principale, protagonista con compagni reclutabili, isometrico e D&D 5e.
Non esiste ancora un'implementazione; la revisione SRD resta da scegliere.
Questo piano descrive un esperimento piccolo e modificabile, non un motore
universale. L'accordo tecnico non sostituisce le preferenze creative dell'utente.

## Esperienza da dimostrare

Un villaggio perde cibo per le azioni dei banditi. Chi vede un furto può
raccontarlo alla guardia; la guardia decide di presidiare il granaio. Il
giocatore può aspettare, consegnare cibo, osservare, oppure riferire una notizia
appresa. Le scorte, le conoscenze e le azioni cambiano anche senza il giocatore.

La memoria serve perché **una notizia cambia una decisione e quindi il mondo**.
Non basta mostrare un elenco di ricordi nei dialoghi.

Scenario iniziale: due aree (Villaggio, Bosco), quattro luoghi (Granaio,
Locanda, Fattoria, Campo banditi), sei NPC (contadino, locandiere, guardia,
mercante, capo bandito, razziatore), due fazioni e una risorsa, il cibo.
Sono parametri di scenario proposti per il primo esperimento, non contenuti
definitivi: nessuna classe speciale per ogni personaggio.

Un personaggio giocabile e rappresentazione isometrica con segnaposto. Interazioni
a pulsanti o menu e simboli leggibili bastano. Combattimento, party, asset
definitivi, prezzi e copertura estesa delle regole 5e vengono dopo la prova del loop.

## Decisioni applicative

| Tema | Scelta per la slice | Ragione / limite |
| --- | --- | --- |
| Stack | Godot 4 .NET + C# | Direzione già indicata nel contesto |
| Dominio | Class library C# indipendente da Godot | Testare il mondo senza scene |
| Struttura | Sim, Sim.Tests, client Godot | Nessun CLI aggiuntivo iniziale |
| Stato | Dati semplici, ID stabili, riferimenti tramite ID | Niente ECS o ereditarietà preventiva |
| Scrittura | Solo Simulation e handler di dominio | Il client invia comandi e legge viste |
| Tempo | Secondi interi `long`, scheduler a scadenze, avanzamento tramite azioni/attesa | Round 5e da 6 s; contratto round/clock deciso nel prototipo di combattimento |
| Spazio | Area → Luogo, grafo di collegamenti con durata | Le coordinate Godot non decidono le regole |
| Regole | Poche priorità per ruolo, motivo della decisione registrato | Utility AI solo se emerge un bisogno |
| Fazioni | Scorte, membri, politica a soglia e incarico concreto | Nessun planner universale |
| Memoria | Osservazioni strutturate autosufficienti e rapporti | Nessuna lettura onnisciente per l'AI |
| Eventi | Fatti immutabili registrati dopo mutazioni validate | Niente bus generico o replay obbligatorio |
| Salvataggi | Snapshot JSON versionato + storico limitato | Nessun event sourcing completo |
| Scenario | `ScenarioBuilder` C# distinto dallo stato mutabile | JSON contenuti e pack rimandati |
| LLM | Non necessario durante il gioco | Regole e stato restano espliciti |

### Autorità e tempo

Ogni comando identifica l'attore. Player e NPC seguono le stesse precondizioni
fisiche: presenza, disponibilità, quantità, durata. L'AI decide usando stato
proprio e informazioni note; l'handler valida contro il mondo reale.

`Travel` conserva origine, destinazione, partenza e arrivo previsto. Durante
il viaggio il personaggio non è presente in nessuno dei due luoghi. Anche un
passaggio Granaio → Locanda costa tempo; il movimento gratuito dei simboli
all'interno dello stesso luogo è soltanto presentazione.

Lettura dei pannelli e scelta di un comando non consumano tempo. Viaggio,
trasferimento, rapporto e attesa hanno durata positiva. Avanzare molte ore
processa tutti i passaggi intermedi. Senza nuovi comandi, `Advance(60)` equivale
a sessanta `Advance(1)`: le chiamate della UI non determinano le decisioni.
Nessun avanzamento offline alla riapertura. L'unità è il secondo (round 5e da
6 s); lo scheduler salta direttamente alla prossima scadenza esplicita. Consumi,
rivalutazioni e risvegli degli NPC inattivi devono avere scadenze proprie,
salvate nello stato. Le durate usano unità esplicite e conversioni nominate.
Il contratto
fra round, iniziativa e clock globale si decide nel prototipo di combattimento.

Il ciclo condiviso (completamenti → percezione → politiche → decisioni),
esplicitato con il confine del salvataggio, è uguale con o senza Godot:

1. Completa azioni in ordine `(dueTick, sequenceId)`; ricontrolla le
   precondizioni. Applica i delta validati e registra i fatti e ciò che ogni
   testimone poteva percepire in quel preciso momento.
2. Applica le osservazioni e i rapporti completati in ordine stabile.
   Un rapporto trasmette il contenuto selezionato all'inizio dell'azione.
3. Aggiorna consumo e politiche di fazione alle rispettive scadenze; bisogni
   ulteriori entrano solo quando esiste una meccanica che li usa.
4. Gli NPC liberi scelgono la prossima azione; registra regola e motivazione.
5. Chiudi il passo e rendi disponibile il salvataggio. Ogni nuova azione o
   reazione sandbox termina dopo il secondo corrente: niente cascata ricorsiva
   istantanea. Il contratto per azioni e reazioni nei turni sarà definito separatamente.

Il primo scheduler può essere una semplice collezione ordinata. Non servono
thread di simulazione, code distribuite o un framework transazionale.
Due richieste sullo stesso cibo si risolvono sequenzialmente; la seconda può
fallire senza modificare nulla. Un'azione annullata libera attore e incarico.

### Memoria, percezione e informazione

Tenere distinti fatto globale, osservazione personale e informazione riferita.
L'evento globale può contenere l'autore del furto; l'osservazione lo contiene
solo se il testimone lo ha identificato. Scoprire scorte mancanti non identifica
automaticamente un ladro. Essere nella stessa area non basta per vedere.

Un'osservazione contiene almeno tipo di informazione, contenuto filtrato,
luogo, istante osservato e istante appreso (in secondi), fonte e identificatore dell'osservazione
originaria. Un `EventId` è un collegamento opzionale di debug, non la memoria.
Potare la timeline non cancella ciò che l'NPC sa.

Per la slice bastano il furto osservato e il rapporto che ne trasmette il
contenuto. La scoperta successiva di un ammanco è un'estensione possibile,
non un prerequisito; quando introdotta non rivela retroattivamente l'autore.
Niente motore generale di credenze o confidenze numeriche.
Il contenuto descrive ciò che è stato osservato allora, non una lettura dello
stato corrente. Ignoto e notizia non aggiornata sono stati leciti.

I rapporti richiedono un incontro nello stesso luogo e tempo di gioco. Deduplicare
per destinatario, origine e informazione; una voce che ritorna non diventa una
seconda prova. Non copiare automaticamente tutte le memorie fra tutti gli NPC.
L'AI riceve una `KnowledgeView`, mai accesso libero al log globale. Anche un
fallimento deve evitare informazioni segrete: «non è qui» non significa
automaticamente «è morto».

### Agency e conflitto minimo

Le regole iniziali hanno priorità esplicite: incarico valido, poi routine.
Una futura necessità urgente potrà prevalere quando esisterà la relativa
meccanica. Un attore ha al massimo un'azione e un incarico attivi. Un incarico
non crea né teletrasporta cibo; richiede viaggio e trasferimento reale.

I banditi con scorte basse assegnano la razzia a un membro disponibile. La
guardia che riceve un rapporto pertinente va al granaio e lo presidia per una
durata configurata. La presenza della guardia rende il furto non praticabile
secondo una regola generale; il bandito aspetta o rivaluta. Nessuna scena o
quest deve forzare questi passaggi a orari prestabiliti.

Scorte iniziali e consumo finiti sono sufficienti per un esperimento di alcuni
giorni. Non promettere un'economia sostenibile: produzione e commercio entrano
quando servono a prolungare scelte interessanti. Nel primo incremento il cibo
è una quantità, non un sistema universale di oggetti e inventari.

### Persistenza e determinismo

Salvare a fine passo: versione schema e scenario, tempo, entità e scorte,
posizioni/viaggi, azioni in corso, incarichi, prossime scadenze, conoscenze,
deduplicazione delle notizie, relazioni se introdotte, contatori ID e storico
limitato. Se esiste casualità, includere algoritmo/versione e stato RNG corrente.

La slice 0–3 è deterministica senza tiri. Quando una successiva interazione
giocabile introduce prove 5e e una
risoluzione richiede casualità, usare un piccolo generatore noto con stato
serializzabile e vettori di verifica. Il seed iniziale da solo non basta.
Non promettere replay fra versioni diverse di codice o contenuti.

Caricare ripristina lo stato e non ripubblica gli eventi storici. Il caricamento
valida versione e riferimenti prima di sostituire la partita corrente. Scrittura
su file temporaneo nello stesso percorso, sostituzione atomica dove supportata
e copia di recupero; errori leggibili e nessun reset silenzioso. Il client usa
la cartella utente di Godot per i salvataggi, il core lavora su DTO/stream.

### Debug minimo

Ora del mondo, scorte, azione dell'NPC selezionato, ultima regola scelta con
motivazione, conoscenze e ultimi fatti. Conservare buffer limitati. L'inspector
può essere onnisciente; dialoghi e diario del giocatore devono essere filtrati.

## Struttura proposta

```text
RpgSandbox.sln
src/RpgSandbox.Sim/
  RpgSandbox.Sim.csproj
  WorldState.cs
  Simulation.cs
  Commands/
  Knowledge/
  Persistence/
  Scenarios/ScenarioBuilder.cs
tests/RpgSandbox.Sim.Tests/
game/
  project.godot
  RpgSandbox.Game.csproj
  Scenes/
  Scripts/
```

Le cartelle nascono quando contengono codice necessario. Dipendenze:
`Game → Sim`, `Sim.Tests → Sim`; Sim non dipende da Godot.

## Incrementi e criteri di uscita

| Incremento | Risultato giocabile | Verifica necessaria |
| --- | --- | --- |
| 0 — collegamento | Godot mostra due luoghi in isometrico con segnaposto; il giocatore viaggia usando Sim | Build/avvio, selezione e sovrapposizioni; arrivo e tempo in secondi verificati nel core |
| 1 — conseguenza | Consegna cibo e vede cambiare le scorte | Trasferimento conservativo; quantità non valida o insufficiente senza effetti parziali |
| 2 — autonomia e persistenza | Un NPC ruba senza input del giocatore; save/load e inspector | Prosecuzione continua equivalente a save/load anche durante un viaggio o incarico |
| 3 — informazione giocabile | Un testimone riferisce; la guardia cambia comportamento e limita i furti | Confronto con scenario senza testimone; nessuna conoscenza automatica |

Alla fine eseguire dalla stessa configurazione almeno tre partite: nessun
intervento, aiuto materiale, trasmissione di informazione. Devono produrre
differenze spiegabili nelle scorte, nelle conoscenze o nelle azioni, senza
condizioni speciali basate sul nome della partita.

Test mirati aggiuntivi: equivalenza fra avanzamento unico e frazionato;
completamenti simultanei con ordine stabile; competizione per l'ultima scorta; azione di attore
indisponibile/morto; notizia circolare; osservazione rimasta valida dopo potatura
log; arrivo di un NPC dopo il furto; salvataggio incompatibile o corrotto;
assenza di catene infinite e incarichi bloccati. I test non dimostrano che il
gioco sia divertente: serve una breve sessione osservando se l'utente comprende
le conseguenze e vede alternative sensate.

## Preferenze recepite e dettagli ancora aperti

Risposte dell'utente (9 ottobre 2026), dettagliate in
`docs/ARCHITECTURE_DECISIONS.md` §13 e nel Round 4 di `chat.txt`:

- Combattimento a turni 5e (con interesse per la "via di mezzo" BG); tempo
  reale con pausa eventualmente da rivalutare come scelta di dominio.
- Protagonista singolo con compagni reclutabili: appartenenza al gruppo come
  relazione persistente, primo esperimento dopo la slice.
- Presentazione isometrica, segnaposto dall'incremento 0.
- D&D 5e tramite SRD (CC-BY-4.0); prove, RNG e modulo `Rules` dopo la slice.

Ancora aperte: SRD 5.1 (2014) o 5.2.1 (2024); eventuale modalità futura
alla BG, senza rimettere in discussione i turni come prima direzione.
La [pagina ufficiale SRD](https://www.dndbeyond.com/srd) distingue le versioni;
la scelta precede la prima meccanica dipendente dall'edizione. Non servono due
ruleset paralleli. Le condizioni di percezione e furtività vanno definite
sulla versione scelta: una prova non identifica automaticamente il ladro,
e le semplificazioni del videogioco vanno dichiarate.
Nessuna di queste scelte blocca gli incrementi 0–3. Non sono previste stime di calendario
finché non è verificato il primo collegamento giocabile.

## Verifica tecnica dell'ambiente

Rilevati SDK .NET `8.0.412` e `9.0.302`. `godot`/`godot4` non risolti nel PATH:
questo non dimostra che l'editor non sia installato. Prima di creare la solution,
individuare l'editor **Godot .NET** e fissare versione editor, target framework
e SDK compatibili; non scegliere il target indipendentemente dall'editor.

La [documentazione ufficiale Godot su C#](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)
richiede SDK .NET ed editor con supporto .NET. Il primo target proposto è desktop
Windows, coerente con l'ambiente di lavoro; nessun cambio di stack.

## Rischi da osservare

Il rischio principale è costruire infrastruttura prima di arrivare al gioco.
Gli incrementi devono rimanere utilizzabili e piccoli. Gli altri rischi sono
informazioni onniscienti, movimento grafico incoerente con il dominio,
salvataggi incompleti e conflitto che si esaurisce senza possibilità di
intervento. Verificarli con scenari ripetibili e una partita breve; aggiungere
astrazioni e contenuti solo dopo aver osservato un limite concreto.
