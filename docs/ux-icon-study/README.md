# Campione di otto icone per la hotbar

Generato con la modalità integrata `image_gen` il 21 ottobre 2026. I primi otto PNG hanno sfondo trasparente; `preview.png` li mostra su pulsanti scuri a 42 px reali e a 84 px per esaminare il dettaglio. Gli export leggeri sono ora in `game/Assets/Ui/ActionIcons/` e sono usati dalla demo per le azioni corrispondenti; il sistema vettoriale in `VillageMap.ActionIcons.cs` resta come fallback per le altre azioni.

**Decisione utente:** la direzione visiva del campione è approvata. Usarla come riferimento per il resto del set; mantenere palette, tratto e silhouette. L'approvazione dello stile non elimina la verifica di leggibilità delle singole icone a 42 px.

## Secondo lotto ed export

Il secondo lotto aggiunge `search-traps.png`, `sneak.png`, `stop.png`, `inventory.png`, `journal.png`, `spellbook.png`, `rest.png` e `divine-sense.png`. La tavola `preview-v2.png` usa gli **export** a 42 px e 84 px. Le versioni `optimized-128/` sono copiate nel client; `interact.png` è predisposta ma non ancora associata a un pulsante generale di interazione.

Gli originali 1254×1254 px di tutti i 16 simboli occupano 19.347.137 byte. Gli export trasparenti 128×128 px in `optimized-128/` occupano in totale 289.643 byte (circa 283 KiB, riduzione del 98,5%); ciascuno pesa tra 8,7 e 26,8 KB. La dimensione in memoria e il costo GPU dipendono dall'importazione Godot, quindi questo confronto riguarda i file PNG su disco. Per la hotbar i 128 px danno margine più che sufficiente rispetto ai 42 px visualizzati.

Prompt comune usato per il secondo lotto, con modalità integrata `image_gen` e trasparenza abilitata:

> Use case: stylized-concept. Asset type: one isolated game UI action icon for a medieval fantasy CRPG hotbar. Match this approved visual direction: crisp hand-engraved fantasy pictogram with subtly painted metal and leather, bold simple silhouette, restrained warm ivory highlights, muted iron-steel fill, modest antique-gold accent, dark unified outline. The shape must read immediately at 42x42 pixels: ONE dominant object and at most ONE small secondary symbol, no filigree, no tiny texture, no scenery. Center within a square with 15% transparent padding. Genuinely transparent background with clean alpha; no text, letters, numbers, label, square, frame, button, watermark or logo. Original design, do not reproduce any existing game icon. Subject: [descrizione seguente].

| File | Descrizione soggetto del prompt |
| --- | --- |
| `search-traps.png` | Large magnifying glass revealing one sharp floor spike. |
| `sneak.png` | Dark hooded head in profile, finger held in a quiet gesture. |
| `stop.png` | Upright armored open palm facing the viewer, clear halt gesture. |
| `inventory.png` | Compact leather satchel with one metal clasp, slightly open. |
| `journal.png` | Open blank parchment journal with one quill across it. |
| `spellbook.png` | Closed dark leather grimoire, one luminous abstract rune on the cover. |
| `rest.png` | Rolled bedroll with one small crescent moon above it. |
| `divine-sense.png` | Alert eye with a radiant halo around the iris. |

Alla scala reale il secondo lotto è generalmente leggibile. La cattura della demo `.tmp/icons-game-final/ux-combat-bar.png` mostra che i simboli riempiono bene lo slot dopo aver ridotto il margine interno da 7 a 1 px e rimosso la tinta avorio applicata alle icone. `journal` e `spellbook` si distinguono grazie a libro aperto/chiuso e luminosità diversa. Alcuni comandi ancora vettoriali (per esempio opzioni e scheda del paladino) hanno adesso un contrasto più forte del nuovo set: da uniformare in un lotto successivo.

Direzione provata: pittogrammi originali fantasy medievali, aspetto inciso e lievemente dipinto, silhouette scura, ferro smorzato, avorio e oro antico; un simbolo isolato e centrato, bordo trasparente, nessun testo o cornice. Nel gioco i colori di categoria, i costi e gli stati vanno applicati dal client, non impressi nell'immagine.

Prompt comune usato per i sette simboli successivi al primo:

> Use case: stylized-concept. Asset type: one isolated game UI icon for a medieval fantasy CRPG action bar, exploratory style sample. Visual language: crisp hand-engraved fantasy pictogram, bold clean silhouette, restrained warm ivory highlights and muted iron-steel fill with a modest antique-gold accent, unified medium-width dark outline, very limited detail, subtle hand-painted surface only within the shape. Center one symbol filling about 72% of a square canvas with generous transparent padding; intended to sit inside a dark bronze Godot button. Genuinely transparent background and clean alpha edges. No text, letters, numbers, labels, frame, tile, button border, scenery, watermark or logo. Must remain recognizable at 42x42 pixels. Original design; do not imitate any existing game icon. Subject: [descrizione seguente].

| File | Descrizione soggetto del prompt |
| --- | --- |
| `attack.png` | Single diagonal longsword making a decisive downward strike, with a small motion notch. Primo prompt con lo stesso stile, senza l'aggiunta «antique-gold accent». |
| `dash.png` | One forward-leaning boot with two short horizontal speed strokes behind the heel. |
| `disengage.png` | One boot stepping sideways away from a small crossed-blades threat mark, with a curved retreat arrow. |
| `dodge.png` | An angled shield with a passing arrow glancing off its outside edge. |
| `interact.png` | A gloved hand grasping and turning a simple iron door ring on a fragment of wooden door. |
| `cure-wounds.png` | One open palm cupping a luminous four-point healing cross, with a tiny muted teal accent. |
| `divine-smite.png` | One sword struck by a vertical shaft of radiant light, with short rays at the impact point. |
| `torch.png` | One upright iron-bound wooden torch with a compact ivory-and-amber flame. |

Prima valutazione a 42 px: Attacco, Scatto, Schivata e Torcia si distinguono bene. Disimpegno è leggibile come movimento ma il significato «ritirarsi senza provocare» dipende ancora dal tooltip; Interagisci è troppo specifica per porte/finestre; Cura ferite e Punizione hanno luce e dettagli piccoli che potrebbero richiedere semplificazione. La prova serve a scegliere o correggere la direzione prima di estendere il set.
