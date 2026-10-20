@echo off
rem Avvia il gioco con Vulkan (Forward+) invece di OpenGL, per quando i driver OpenGL non vanno.
"%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" --rendering-method forward_plus --rendering-driver vulkan
