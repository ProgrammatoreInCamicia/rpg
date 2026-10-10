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

Nel gioco: clic su una casella per camminare, clic sul segnale «Strada per il bosco» per raggiungere il campo dei banditi, Spazio per mettere in pausa/riprendere, X per fermarsi, Maiusc+clic su una porta per entrarci, WASD/frecce per la camera, rotella per lo zoom. Dal campo, «Torna al villaggio» riporta all'uscita. La vecchia scena a Luoghi si avvia con `--path game res://Scenes/Main.tscn`.

La scena principale usa il villaggio ampliato: Locanda, granaio con accesso sul lato ovest, casa del contadino e strada verso il bosco. La vecchia mappa piccola rimane disponibile come scenario di regressione per i test T6a/T6b. La casella di uscita verso il campo dei banditi si trova all'estremità est della strada.

Lo smoke del villaggio include la razzia notturna: carica salvataggi riproducibili poco prima dell'arrivo del Razziatore, durante il suo cammino e dopo il furto, controlla che il giocatore lo veda al crepuscolo e salva gli screenshot `11-raider-enters.png`–`15-raid-returned.png` nella cartella scelta.
