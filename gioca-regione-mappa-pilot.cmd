@echo off
rem Prova esplorabile delle tre mappe grandi 48x32; slot separato dalla regione principale.
dotnet build "%~dp0game\RpgSandbox.Game.csproj" -nologo -v q
if errorlevel 1 (
	echo Compilazione non riuscita: la prova non viene avviata.
	pause
	exit /b 1
)
"%~dp0tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game" res://Scenes/BlackmarshRegion.tscn -- --region-map-pilot
