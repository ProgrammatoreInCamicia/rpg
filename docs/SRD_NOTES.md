# Note sullo SRD 5.2.1 (regole 2024)

Fonte verificata il 2026-10-09: PDF ufficiale `SRD_CC_v5.2.1.pdf` (https://www.dndbeyond.com/srd), 364 pagine.
Le citazioni in inglese sono copiate dal PDF. Il resto è un riassunto per lo sviluppo; in caso di dubbio fa fede il PDF.

## 1. Licenza e attribuzione

Lo SRD 5.2.1 è rilasciato con **CC-BY-4.0**. Quando il gioco includerà materiale dello SRD, cioè dalla tappa 4, l'attribuzione va inserita **esattamente** così (pagina 1 del PDF):

> This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

Vincoli dalla stessa pagina:
- *"Please do not include any other attribution to Wizards or its parent or affiliates other than that provided above."* Quindi niente altri riferimenti a Wizards, né loghi o marchi.
- Si può dichiarare che il gioco è *"compatible with fifth edition"* o *"5E compatible"*.
- La sezione 5 di CC-BY-4.0 contiene l'esclusione di garanzia.

Dove metterla: un file `CREDITS.md` nel repository e una schermata dei crediti nel gioco.

## 2. Regole che servono alla tappa 4

**Modificatore di caratteristica** (p. 6): punteggio 10–11 → +0, 12–13 → +1, 14–15 → +2, 16–17 → +3, 18–19 → +4. In formula: `floor((punteggio - 10) / 2)`, che riproduce la tabella da 1 a 30.

**Prova d20** (p. 6):
1. Si tira 1d20; con Vantaggio o Svantaggio se ne tirano due e si tiene il più alto o il più basso.
2. Si sommano il modificatore di caratteristica, il bonus di competenza se si è competenti, ed eventuali bonus o malus.
3. La prova **riesce se il totale è uguale o maggiore** del numero bersaglio (CD).

**CD tipiche** (p. 6): molto facile 5, facile 10, media 15, difficile 20, molto difficile 25, quasi impossibile 30.

**Vantaggio/Svantaggio** (p. 8): non si cumulano. Se sono presenti entrambi, si annullano e si tira un solo d20, anche quando le fonti dell'uno sono più numerose di quelle dell'altro.

**Bonus di competenza** (tabella di avanzamento): +2 ai livelli 1–4, +3 ai livelli 5–8.

**Abilità rilevanti** (p. 9):
- Furtività (Destrezza): *"Escape notice by moving quietly and hiding behind things."*
- Rapidità di mano (Destrezza): *"Pick a pocket, conceal a handheld object…"*
- Percezione (Saggezza): *"notice something that's easy to miss."*
- Intuizione (Saggezza): *"Discern a person's mood and intentions."* Sarà utile nella tappa 5.

**Percezione passiva** (sezione "Character Creation"): `10 + modificatore delle prove di Saggezza (Percezione)`. Si usa quando *"your GM will determine whether your character notices something without asking you to make a Wisdom (Perception) check"*.

**Nascondersi, azione Hide** (glossario):
- Prova di Destrezza (Furtività) con **CD 15**.
- Bisogna essere **Pesantemente oscurati** oppure dietro copertura di tre quarti o totale, e **fuori dalla linea di vista** di qualunque nemico.
- Se la prova riesce si ottiene la condizione Invisibile, e il totale della prova diventa la CD che gli altri devono battere per trovarti con una prova di Saggezza (Percezione).
- Si smette di essere nascosti facendo un rumore più forte di un sussurro, venendo trovati, attaccando o lanciando un incantesimo con componente verbale.
- Inoltre (p. 11): *"The Game Master decides when circumstances are appropriate for hiding."*

**Ricerca, azione Search** (glossario): prova di Saggezza; per una creatura o un oggetto nascosto si usa Percezione.

**Luce e visibilità** (p. 11):
- Luce fioca, come il crepuscolo o la luna piena, rende un'area **Leggermente oscurata**: Svantaggio alle prove di Percezione basate sulla vista.
- L'oscurità, anche all'aperto nella maggior parte delle notti di luna, rende un'area **Pesantemente oscurata**: chi guarda è considerato Accecato.

**Influenzare, azione Influence** (glossario), per la tappa 5. Il GM stabilisce se la creatura è disponibile, contraria o esitante. Solo se è esitante si fa una prova, con CD pari a 15 o all'Intelligenza della creatura (il valore più alto). Se fallisce, si aspettano 24 ore prima di ritentare nello stesso modo.

## 3. Cosa implica per la nostra simulazione (da discutere con Codex)

- **Rubare sotto gli occhi di qualcuno non è "Nascondersi".** Secondo le regole, nascondersi richiede oscurità o copertura e nessuno che ti veda. Un furto al granaio di giorno con il Contadino presente **non permette di nascondersi**: si viene visti. Va bene così ed è un'informazione utile per il gameplay.
- Quando nessuno ti vede in modo diretto, ad esempio chi arriva dopo o chi è nella stanza ma distratto, si può usare il caso previsto dallo SRD: una prova di Furtività contro la **Percezione passiva** di chi è presente. Proposta di adattamento per un'azione che dura minuti: **una sola prova all'inizio**, il cui totale vale per tutta la durata contro chi è presente o arriva. Va marcato come ADATTAMENTO.
- **Il ciclo giorno/notte diventa gameplay.** Di notte il granaio è Pesantemente oscurato: si può davvero usare Nascondersi, e chi guarda senza luce è come Accecato. Di giorno no. Finora il giorno/notte era solo una semplificazione dichiarata; con queste regole diventa la variabile principale della furtività.
- Rapidità di mano può servire per piccoli furti, come borse o tasche. Per le razioni del granaio basta la Furtività.

## Combattimento (C1)

Regole lette e applicate dallo SRD 5.2.1:
- tabella **Weapons**, con proprietà e padronanze;
- **Rolling 20 or 1** e **Critical Hits**;
- **Dropping to 0 Hit Points** e **Instant Death**: Monster Death e Massive Damage;
- **Death Saving Throws**;
- **Knocking Out a Creature**;
- condizione **Unconscious**: gli attacchi da entro 5 piedi sono critici automatici;
- **Unarmed Strike**;
- **Level 1 Hit Points by Class**: Paladino 10 + Costituzione;
- privilegi del Paladino di livello 1: Weapon Mastery;
- schede **Bandit**, **Guard** e **Commoner**;
- **Initiative**: sorpresa e pareggi (i pareggi sono un adattamento).
