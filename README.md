# RPG Sandbox

Sandbox RPG con mondo persistente e simulato: Godot 4 .NET + C#.
Visione: `RPG_SANDBOX_CONTEXT.md`. Decisioni: `docs/ARCHITECTURE_DECISIONS.md`. Piano: `IMPLEMENTATION_PLAN.md`.
Contratto Sim ↔ Godot: `docs/INTEGRATION_CONTRACT.md`. Discussione tra assistenti: `chat.txt`. Crediti e licenza SRD: `CREDITS.md`.

## Struttura

```text
src/RpgSandbox.Sim/          simulazione, nessuna dipendenza da Godot
tests/RpgSandbox.Sim.Tests/  test xUnit della simulazione
game/                        client Godot (presentazione e input)
tools/godot/                 editor Godot portabile (non versionato)
```

## Requisiti

- .NET SDK 8.0.4xx (fissato in `global.json`).
- Godot **4.7.2 .NET** portabile, estratto in `tools/godot/Godot_v4.7.2-stable_mono_win64/`.
  `nuget.config` usa i pacchetti `Godot.NET.Sdk` inclusi nell'editor, così la build funziona anche offline.

## Una sera al granaio — prova giocabile

Questa prova breve usa il villaggio esistente: osservi un furto, puoi riferirlo alla guardia e vederne la reazione. Richiede Windows, il .NET SDK indicato in `global.json` e **Godot 4.7.2 .NET** estratto esattamente nella cartella `tools/godot/Godot_v4.7.2-stable_mono_win64/` (l'eseguibile deve essere `Godot_v4.7.2-stable_mono_win64.exe`). Da PowerShell, nella root del repository, esegui una volta `dotnet build game/RpgSandbox.Game.csproj`. Poi apri `gioca-slice.cmd` con doppio clic, oppure esegui `./gioca-slice.cmd` da PowerShell. Il launcher apre `PlayableSlice.tscn`; `gioca.cmd` e la scena principale restano il villaggio ordinario. Un salvataggio precedente non serve per iniziare.

La scena inizia **in pausa alle 19:27**: leggi l'introduzione e premi **Comincia**. Clicca il terreno per camminare verso il granaio lungo la strada a sud-est. **Spazio** alterna pausa e ripresa; **X** ferma il cammino. Quando sei vicino al deposito, guarda il razziatore e usa **Attendi fino a 5 minuti** se vuoi far passare il tempo: l'attesa si interrompe quando noti qualcosa. La torcia, se accesa dall'icona nella barra, può aiutarti a identificare chi vedi; se impugni lo scudo, il gioco chiede conferma per riporlo. Il **Diario** contiene ciò che il personaggio ha visto o sentito, non tutti gli eventi del mondo.

Puoi lasciare correre, oppure tornare dalla guardia presso la locanda, cliccarla e scegliere **Riferisci il furto**. Il rapporto richiede un breve tempo per concludersi; torna al granaio per osservare se la guardia prende servizio. Dal lato segnato dal rombo presso la cassa puoi anche usare **Consegna 3 razioni**. **Riepilogo** mette in pausa e mostra le informazioni note al personaggio; **Termina la prova** apre la conclusione e **Riprova** ricomincia dalla stessa situazione iniziale.

Dal menu **Esc** o **⋯** puoi **Salva**, **Carica** e **Riprova**. Riprova ricomincia subito e scarta il progresso non salvato. La prova usa `user://playable-slice.json`, separato dal salvataggio di Village; il caricamento vale per questa stessa mappa. Per un collaudo senza leggere il codice, segui la [scheda di prova](docs/PLAYABLE_SLICE_PLAYTEST.md) e annota soprattutto dove esiti o non capisci cosa è successo.

## Comandi

```sh
dotnet build RpgSandbox.sln          # Sim, Tests e client
dotnet test                          # test della simulazione

# editor
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game --editor
# avvio diretto del gioco (oppure doppio clic su gioca.cmd)
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game
# prova automatica: viaggio scriptato con screenshot in <dir>
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --village-smoke=<dir>
# prova automatica della vecchia scena a Luoghi (banditi, guardia, conversazioni)
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path game res://Scenes/Main.tscn -- --smoke=<dir>

# anteprima autonoma dell'interfaccia di combattimento (dati finti, nessuna Sim)
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game res://Scenes/CombatPreview.tscn
# smoke e screenshot dell'anteprima
tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path game res://Scenes/CombatPreview.tscn -- --combat-preview-smoke=<dir>
```

Nel gioco: clic sul punto preciso del terreno per camminarci, clic su una finestra vicina per aprirla o chiuderla, clic sul segnale «Strada per il bosco» per raggiungere il campo dei banditi, Spazio per mettere in pausa/riprendere, X per fermarsi, Maiusc+clic su una porta per entrarci, WASD/frecce per la camera, rotella per lo zoom. Il riquadro «Eventi» mostra con l'ora gli ultimi fatti che riguardano il giocatore. Dal campo, «Torna al villaggio» riporta all'uscita. La vecchia scena a Luoghi si avvia con `--path game res://Scenes/Main.tscn`.

Per attaccare fuori dallo scontro, clic destro su una persona visibile, scegli l'attacco e conferma. In esplorazione il pavimento non mostra caselle o percorsi al passaggio del mouse. Durante lo scontro, passa il mouse sul terreno per vedere percorso, distanza e attacchi di opportunità noti, poi clicca per muoverti. Clicca su un avversario per scegliere l'attacco; «Tramortisci» vale solo per la mischia. «Fine turno» fa agire gli altri combattenti.

Il pulsante «Diario» mostra i fatti che il personaggio ha visto, sentito o appreso: furti, scontri e uccisioni. «Parla con» distingue questi fatti nei racconti disponibili. Un personaggio che ti considera ostile è segnato in rosso e con la scritta «OSTILE»; il colore indica il suo atteggiamento, non il motivo.

Le porte sono mostrate secondo ciò che il personaggio sa: aspetto pieno se le vede adesso, attenuato se ne ricorda uno stato passato, contorno neutro se non le ha mai viste. Il clic diretto per aprire o chiudere usa solo lo stato visto adesso; camminare attraverso una porta segue le normali regole di movimento.

La scena principale usa il villaggio ampliato: Locanda e casa del contadino hanno pareti sottili, porte, finestre e luce interna; il granaio conserva il cortile aperto e l'accesso al deposito sul lato ovest. Cliccando sul terreno si cammina fino al punto scelto, senza fermarsi automaticamente al centro della casella. La vecchia mappa piccola rimane disponibile come scenario di regressione per i test T6a/T6b. La casella di uscita verso il campo dei banditi si trova all'estremità est della strada.

Lo smoke `--village-smoke=<dir>` include il furto scoperto di giorno: cattura il furto al granaio, l'arrivo della Guardia, la confisca, la restituzione delle razioni al deposito e il rapporto del Contadino. Salva nella cartella indicata screenshot e partite riproducibili per ciascun passaggio.
Include anche tre viste della porta di casa: mai vista dalla Locanda, vista chiusa da vicino e ricordata chiusa dopo che il Contadino l'ha aperta fuori vista.

Lo smoke del villaggio include la razzia notturna: carica salvataggi riproducibili poco prima dell'arrivo del Razziatore, durante il suo cammino e dopo il furto, controlla che il giocatore lo veda al crepuscolo e salva gli screenshot `11-raider-enters.png`–`15-raid-returned.png` nella cartella scelta.

Lo stesso smoke prova anche il duello notturno: apre uno scontro reale con il Razziatore, salva e ricarica durante il turno del giocatore, avanza i round fino alla fine e cattura `27-duel-start.png`–`30-duel-end.png`.
