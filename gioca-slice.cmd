@echo off
rem Avvia la prova "Una sera al granaio" (ASTRA_ROADMAP.md), senza editor.
"%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" res://Scenes/PlayableSlice.tscn
