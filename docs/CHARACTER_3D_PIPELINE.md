# Personaggi 3D: prova di produzione

Stato: ricerca del 20 ottobre 2026. Nessun modello generato con Meshy è stato ancora importato o provato nel gioco. La tavola `game/Assets/StyleStudy/character-lineup.png` definisce solo palette e silhouette: le quattro figure non sono viste ortogonali coerenti né un modello 3D.

## Vincoli del client

Il villaggio è una scena 2D isometrica. `VillageMap.Visuals.cs` mostra già Paladino e PNG come figure 3D animate in tempo reale, ciascuna renderizzata in una texture trasparente, poi composta sulla mappa. I modelli attuali sono costruiti con primitive in `Paladin3DPrototype.cs`. La Sim decide posizione, direzione e azioni; la grafica interpreta questi dati. Un GLB esterno può sostituire la figura procedurale senza cambiare le regole, ma servirà adattare scala, origine ai piedi, orientamento, clip e camera. Il costo di molte viewport contemporanee va misurato prima di estenderlo a una folla.

## Candidato Meshy

La [guida Image to 3D](https://docs.meshy.ai/en/webapp/image-to-3d) supporta una reference, più viste e controllo della posa; consiglia T-pose o A-pose per il rig. La [guida Animate](https://docs.meshy.ai/en/webapp/guides/animate) descrive auto-rig di umanoidi e clip pronte, con esportazione GLB/FBX. La [documentazione del rig](https://docs.meshy.ai/en/api/rigging) precisa che arti poco separati, corpo poco leggibile e geometria troppo densa possono impedire o rovinare il risultato; il rig API da task Meshy non supporta più di 300.000 facce. Questo rende mantello, armi e scudo i principali punti di prova, non dettagli da dare per risolti.

Secondo il [confronto ufficiale dei piani](https://help.meshy.ai/en/articles/12062933-which-meshy-plan-is-right-for-you-free-vs-pro-vs-premium-vs-ultra), il piano Free permette di esplorare il servizio ma non scaricare i modelli; Pro è indicato a 20 USD/mese con 1.000 crediti mensili e download. I [crediti mensili non si accumulano](https://help.meshy.ai/en/articles/9991981-how-do-meshy-credits-work). Prezzo e condizioni vanno ricontrollati al momento dell'acquisto. Non serve acquistare un piano più alto per la prima prova.

## Esperimento minimo, prima di una produzione in serie

1. Preparare una reference **originale** del Paladino, in piedi e con braccia separate dal tronco, coerente con la palette approvata. La tavola corrente è isometrica e può guidare lo stile, ma non va trattata come set multiview. Per le viste frontale, laterale e posteriore serve coerenza di abiti, proporzioni e accessori.
2. Generare **un solo** corpo umanoide in A-pose/T-pose. Esaminare il modello ruotandolo: silhouette da tutti i lati, retro, mani, piedi e contatto fra mantello, armatura e gambe. Se necessario ridurre/riordinare la topologia prima del rig.
3. Fare rig e ottenere almeno idle, camminata e attacco. Preferire un corpo senza arma fusa alla mano: spada e scudo dovrebbero essere oggetti separati da agganciare ai nodi/bone, così l'equipaggiamento potrà cambiare. Questa è una scelta progettuale da verificare sull'export concreto, non una funzione garantita dal generatore.
4. Esportare GLB. [Godot raccomanda glTF/GLB](https://docs.godotengine.org/en/4.6/tutorials/assets_pipeline/importing_3d_scenes/index.html); importare prima in una scena isolata, poi nel renderer dei personaggi del villaggio. Controllare materiali, skeleton, clip, orientamento e posa di riposo.
5. Provare in una stanza giocabile con luce di giorno/notte, rotazione continua, camminata diagonale, arresto, attacco e caduta. Confrontare a 1280×720 e 50–70 px d'altezza con i modelli attuali. Passa solo se non ci sono scatti, piedi che slittano, compenetrazioni vistose o perdita della silhouette.
6. Misurare la scena con più PNG attivi: il costo per personaggio conta perché oggi ogni figura ha una viewport 3D propria. Se il risultato funziona visivamente ma pesa troppo, ottimizzare il compositing prima di creare altri modelli.

Per un confronto senza comprare Meshy, [Mixamo](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) offre auto-rig e animazioni per umanoidi bipedi con Adobe ID gratuito; permette di provare l'integrazione delle clip, ma non sostituisce la creazione del modello originale. Blender rimane lo strumento di rifinitura più utile per correggere clipping, materiali, ossatura o punti di attacco quando il risultato automatico non basta. Non è necessario che l'utente lo impari prima della prova.

Decisione proposta: **non impegnarsi ancora in una libreria di personaggi**. Approvare la stanza come riferimento di ambiente, poi validare un GLB completo e animato nel client. Un mese di Meshy Pro ha senso solo quando siamo pronti a scaricare e testare quel primo personaggio.
