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

Nel gioco: clic su una casella per camminare, Spazio per mettere in pausa/riprendere, X per fermarsi, Maiusc+clic su una porta per entrarci, WASD/frecce per la camera, rotella per lo zoom. La vecchia scena a Luoghi si avvia con `--path game res://Scenes/Main.tscn`.
