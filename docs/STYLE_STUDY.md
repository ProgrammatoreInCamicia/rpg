# Campione di stile — interni e figure

Questa è una **prova visiva**, separata dalla demo. La casa generata non è una mappa giocabile e la tavola dei personaggi non contiene animazioni o direzioni coerenti. Serve a decidere materiali, palette, silhouette e leggibilità prima di costruire un set modulare. La scena reale continua a usare tetti, vista, luce e personaggi implementati in Godot.

## Come guardarlo

Aprire `game/Scenes/StyleStudyPreview.tscn` in Godot, oppure dalla root del repository:

```powershell
& 'tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe' --path game --rendering-method forward_plus --rendering-driver vulkan res://Scenes/StyleStudyPreview.tscn
```

`Tab` alterna **cattura attuale → proposta di interno → figure**; `Esc` esce. La scena usa il viewport del gioco (1280×720). Nella tavola delle figure, la riga superiore le mostra alte circa 70 pixel, mentre quella inferiore permette di esaminarle ingrandite.

File: [interno attuale](../game/Assets/StyleStudy/interior-current.png), [proposta di interno](../game/Assets/StyleStudy/interior-concept.png), [quattro figure](../game/Assets/StyleStudy/character-lineup.png).

## Direzione proposta

- Forme leggibili da lontano: perimetro e divisione delle stanze, porta e finestra riconoscibili senza etichette.
- Pietra, legno e intonaco dipinti ma sobri; palette ardesia, oliva, terra e blu polveroso con piccoli accenti caldi.
- Tetto interamente nascosto quando il giocatore è dentro. Il volume delle pareti rimane sottile e la stanza scura conserva una sagoma leggibile; le regole della Sim continuano a decidere che cosa è davvero visibile.
- Paladino blu, Guardia oliva, Contadino ocra e Razziatore scuro/ruggine: il ruolo deve leggersi alla scala della figura nel mondo, non dai dettagli del volto.

La proposta è più chiara e luminosa dell'interno attuale. Ha però un limite importante: il generatore ha reinventato arredi e parte della geometria, quindi l'immagine **non** è una specifica di level design o un asset da mettere come sfondo del gioco. Anche la barra è stata ridisegnata incidentalmente: per la UX vale la schermata reale, non quella generata. Le figure sono concept di silhouette; per animarle con movimento naturale serve una sorgente coerente (il rig 3D già sperimentato è la strada più promettente), non quattro ritagli statici.

## Criteri di valutazione

1. A 1280×720 si distinguono subito porta, finestra, divisione tra stanze e superfici percorribili?
2. Le quattro figure a circa 70 px sono distinguibili senza leggere i nomi?
3. Materiali e dettagli sono abbastanza leggibili quando luce e nebbia di guerra del gioco li scuriscono?
4. La resa è adatta a Blackmarsh oltre che alla casa di prova?

Se la direzione convince, il passo successivo è **un solo kit reale**: pavimento, pareti sottili, porta, finestra e tetto modulari nella scena interni, più una figura riggata nelle stesse proporzioni e palette. Va confrontato in Godot di giorno e di notte prima di estenderlo al villaggio o a Blackmarsh.

## Origine

Le due proposte raster sono state create con lo strumento imagegen integrato di Codex il 20 ottobre 2026, come immagini originali di studio. `interior-current.png` è una cattura del client esistente.

Prompt interno: modifica della cattura del prototipo a 1280×720, mantenendo camera isometrica, ingombro della casa, pareti sottili e barra; casa a due stanze senza tetto, pavimenti in legno e pietra, intonaco e travi, luce diurna dalla finestra, Paladino e Contadino piccoli; pittorico 3D con texture dipinte, palette smorzata, priorità alla leggibilità di gioco, senza copiare asset di altri giochi.

Prompt figure: quattro figure originali allineate e isolate su trasparenza — Paladino blu con spada e scudo, Guardia oliva con lancia, Contadino in abiti da lavoro e Razziatore scuro con accenti ruggine — stessa camera isometrica, scala, illuminazione e linguaggio materico; silhouette leggibili a 40–70 px, nessun testo o sfondo.
