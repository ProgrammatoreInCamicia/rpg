@echo off
rem Compila e avvia la regione Blackmarsh, senza editor.
dotnet build "%~dp0game\RpgSandbox.Game.csproj" -nologo -v q
if errorlevel 1 (
	echo Compilazione non riuscita: il gioco non viene avviato.
	pause
	exit /b 1
)
"%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game"
