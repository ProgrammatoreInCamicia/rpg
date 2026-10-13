# Prova interni

Aprire `game/Scenes/InteriorPrototype.tscn` in Godot e avviare la scena. Il villaggio principale resta la scena predefinita del progetto.

La casa dimostrativa ha due stanze su una griglia di 5 piedi: il perimetro e la parete interna sono confini sottili tra caselle; la finestra è sul lato nord e la porta è sul lato est. Il cursore mostra il percorso e il costo fino al punto preciso del pavimento; cliccare per andarci, cliccare sulla porta per aprirla/chiuderla, `T` per accendere la torcia. La porta si apre anche automaticamente quando il percorso la attraversa. Il movimento libero è per ora disponibile solo fuori dal combattimento in questa scena. La prova parte alle 13:00 per rendere subito visibile l'effetto della finestra; lo smoke visuale mostra anche lo stato notturno.

Smoke visuale (da root del repository, dopo la build):

```powershell
& 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --path game res://Scenes/InteriorPrototype.tscn -- --interior-smoke=C:/Progetti/rpg/game/.review/interior
```

Lo smoke salva gli stati giorno/esterno, giorno/finestra, giorno/interno, arresto su un punto libero, porta richiusa, notte/torcia e scheda della creatura. La Sim calcola vista, passaggio, luce e memoria delle caselle esplorate; il client disegna i confini e oscura le caselle sconosciute. Il salvataggio usa schema 23 e legge anche i salvataggi precedenti. Questa è una mappa separata per provare le regole senza cambiare le routine del villaggio. Il modello non gestisce ancora piani sovrapposti, tetti o luce riflessa; `MapView.Rows` contiene ancora la pianta completa per il renderer, perciò la maschera di esplorazione non è ancora una barriera contro client modificati.
