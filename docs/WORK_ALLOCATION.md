# Divisione del lavoro — prima slice

Stato: divisione proposta da Claude nel Round 5 e accettata da Codex in
`chat.txt`. Supera la prima proposta con assegnazioni invertite.
Obiettivo complessivo: incrementi 0–3 del piano, fino a furto, informazione e
reazione della guardia. Primo punto di integrazione: incremento 0; poi 1.

## Responsabilità concordate

| Responsabile | File di competenza | Consegna |
| --- | --- | --- |
| Claude Code, sessione esterna attiva | `src/RpgSandbox.Sim/**` | Simulazione C# indipendente da Godot, viste e comandi |
| Claude Code | `tests/RpgSandbox.Sim.Tests/**` | Verifiche dominio, determinismo, conoscenza e persistenza |
| Codex, sessione utente | `game/**` | Client isometrico, input, visualizzazione, debug e gestione file save |
| Claude Code | Solution, SDK/configurazione root, `.gitignore`, README | Versioni compatibili e istruzioni di build/avvio |
| Claude, dopo accordo con Codex | `src/RpgSandbox.Sim/Api/**`, `docs/INTEGRATION_CONTRACT.md` | Facciata pubblica compilabile e descrizione aggiornata prima dei cambiamenti |
| Codex | `IMPLEMENTATION_PLAN.md`, `docs/WORK_ALLOCATION.md` | Piano, assegnazioni e verifiche di integrazione |
| Claude | `docs/ARCHITECTURE_DECISIONS.md` | Decisioni tecniche concordate |
| Entrambi, solo aggiunte in coda | `chat.txt` | Accordi, consegne, problemi e passaggi di responsabilità |

Nessuna modifica ai file dell'altro senza passaggio esplicito. Nessuna copia
delle regole in Godot e nessun core fittizio con nomi incompatibili. Se una
parte termina prima, verifica il contratto e segnala problemi all'autore.

## Consegne parallele e integrazione

| Passo | Claude — dominio | Codex — client | Criterio per passare oltre |
| --- | --- | --- | --- |
| Preparazione | Setup Sim/Tests e API compilabile; sostituisce la bozza del contratto | Verifica editor .NET e target; rivede API per UI | Contratto accettato e target comunicato |
| 0 | Tempo, due luoghi, viaggio e test | Isometrico, selezione luoghi, attesa e ora | Client reale collegato al core; viaggio/arrivo visibili |
| 1 | Trasferimenti/scorte e validazioni | UI trasferimento e feedback dei rifiuti | Cibo conservato, rifiuti senza modifiche parziali |
| 2 | Azioni autonome, snapshot JSON, prosecuzione verificata | Debug, save/load su percorso utente, messaggi d'errore | Furto autonomo; ripresa corretta durante azioni in corso |
| 3 | Osservazioni filtrate, rapporti, reazione guardia | Dialogo/rapporto e conseguenze leggibili | Ignora/dona/riferisce producono differenze spiegabili |

Ogni passo estende il contratto prima che il client ne dipenda. Il lavoro
procede in parallelo dentro il passo; non occorre aspettare l'intera simulazione
per collegare il client. I primi risultati devono già essere eseguibili.

## Verifiche e chiusura

Claude esegue build e test dei progetti Sim/Tests, Codex build del client e
smoke test Godot. Le prove del dominio non sostituiscono l'avvio del client.
Il responsabile comunica comando, risultato e limiti in `chat.txt`.

Per il passo 0 verificare viaggio a metà e completato, attore già impegnato,
destinazione non valida, equivalenza avanzamento unico/frazionato, viste che
non consentono di mutare lo stato. Nel client controllare selezione, ordine
visivo, camera, ora e posizione rappresentata rispetto allo stato del core.

Per la chiusura della slice servono build/test riusciti e una sessione reale
nei tre percorsi previsti. Un editor assente o non avviabile va riportato come
verifica mancante, non come risultato positivo. Codex verifica i tre percorsi
nel client; Claude li riproduce come test headless. Le review incrociate
controllano sufficienza API e assenza di regole duplicate nella UI.
Combattimento, reclutamento,
prove casuali e regole 5e estese restano successivi alla slice.

## Stato del contratto e coordinamento

Claude pubblica `SimulationSession` con Execute/comandi, viste immutabili,
avanzamento esplicito e `AdvanceUntilCompleted(actionId)` per attendere una
specifica azione. I nomi e i campi esatti saranno quelli dell'API reale.
La bozza iniziale `Simulation` in INTEGRATION_CONTRACT.md è superata: Claude
ne ha ricevuto la responsabilità per riallinearla prima dell'uso nel client.
Non compilare il client contro firme ancora proposte né introdurre stub duplicati.
Save/Load su stream e viste successive vengono aggiunti quando serve il passo.

I comandi programmano azioni senza avanzare implicitamente il tempo. Un helper
per attendere la fine di un'azione usa l'identificatore di quella specifica
azione, senza inseguire indefinitamente routine successive. Un caricamento
fallito conserva la partita corrente. Il filtraggio delle
informazioni del giocatore appartiene al core; la UI usa la vista appropriata.

Solution e file root hanno un autore alla volta. Le modifiche alla solution
sono comunicate a Claude; non eseguire commit concorrenti o staging globale.
Un commit include soltanto i percorsi di competenza e le consegne concordate.
Nessuna pubblicazione remota è prevista.

Nel passo 3 si verificano due confronti distinti: intervento del giocatore
(attesa/consegna/rapporto) e disponibilità dell'informazione (ladro identificato,
ignoto, nessun testimone).
