# Dev playground

Otvori **debug/DevPlayground.tscn** i pritisni **F6**. To je ulaz za razvoj i testiranje;
F5 i dalje pokrece glavnu igru. Potreban je Godot 4.7.2 .NET i uspjesan Build C#.

## Najbrzi pocetak

1. Pocetni izbornik ima **Laboratoriji** i **Alati**. Odaberi **Nastavi igrati** ili odaberi
   **Kretanje / Glavni poligon** pa **Pokreni odabrani laboratorij**. Oba ulaze u poligon bez
   ponovnog otvaranja izbornika. Ponovni odabir aktivnog laboratorija nastavlja trenutnu probu;
   za potpuno novo ucitavanje postoji **Ponovno pokreni ovaj lab**.
2. **F10** otvara/zatvara ovaj izbornik u svakom laboratoriju. Izbornik pauzira probu i cuva
   prethodnu pauzu druge stranice. Mis je slobodan dok je izbornik otvoren.
3. U **Alati** izaberi checkpoint pa **Idi na checkpoint**: nagibi, stepenice, poprecni nagib,
   kamenje i padovi na razlicite podloge. U proceduralnom svijetu postoje i stanice 1-3.
   Kod teleportiranja lik ceka da se ucita tlo ispod njega.
4. Odaberi predmet iz popisa pa ga dodaj u ruksak ili stvori na tlu za E pickup.
   Pun ruksak odbija dodavanje. Predmeti na tlu su namjerne nove testne kopije.
5. Napuni health/stamina/glad/zed, isprazni dio stamine, osteti lika ili pokreni odbacivanje
   za probu koluta. **Vrati lika na pocetak** ne zavrsava run; **Ponovno pokreni ovaj lab**
   ucitava cijelu scenu iznova.
6. Postavi dan/noc ili promijeni vrijeme. Za stvarni lov otvori **Sumu** ili **Cijelu igru**,
   klikni **New Run**, zatim u Alatima **Dovedi nocnu zvijer**. Izvan vatre/sklonista vrijede
   stvarna pravila lova. Pregled zvijeri samo prikazuje modele.

**WASD** kretanje, **Shift** trcanje, **C** kocenje, **Space** skok/kolut, **Ctrl** zaron,
**I** inventory, **Tab** 3D pakiranje u scenama koje ga imaju. **F1** slobodna kamera i
**F3** debug rade u laboratorijima koji imaju te postojece komponente. **F9** ukljucuje
snimanje svakih pet renderiranih frameova; snimanje moze smanjiti FPS.

## Laboratoriji

| Izbornik | Postojeca scena / sto provjeriti |
| --- | --- |
| Glavni poligon | playground/DevSandbox.tscn: prosireni TestPacking, pravi inventory/HUD, podloge, padovi, vrijeme |
| Odjeca i oprema | TestClothing: equip/unequip, modeli, stanje, boja i save/load tereta |
| Ruksak: polozaj i animacije | TestBackpackAttachment: odabir lika, jedan pack, pomak/rotacija/velicina i bone debug |
| Likovi, statovi i sposobnosti | TestCharacterAbilities: profili i H sposobnosti |
| Animacije i locomotion demo | TestSmoothLocomotion: demo i rucna proba |
| Cisti test kretanja i IK | TestLocomotion: poligon bez inventara |
| Balans i raspored tereta | TestBalance: L mijenja postojece presete |
| 3D pakiranje | TestPacking: pozicije, rotacija, teziste |
| Suma i teren | TestForestDescent: New Run, F7 checkpointi, streaming |
| Spasavanje pumpe i dostava | TestRecoveryExpedition: teret, stanje, dostava i nagrade |
| Ciljevi i istrazivanje | TestPurpose: ciljevi, nalazi uz put, poslovi i journal |
| NPC modeli i animacije | TestNpcModels: modeli putnika, sjedenje i razgovor |
| Predsjednik: dijalozi i glas | TestPresident: gore/dolje, Enter, V varijanta glasa |
| Nocne zvijeri: pregled | TestBeasts: pregled zamjenskih modela; lov je u igri |
| Zivotinje | TestWildlife: postojece zivotinje i reakcije |
| Cijela igra | TestTerrain: povezana igra, questovi, mapa, opasnosti, postavke |
| Jednostavna scena za ucenje | LearnGodot: obican pod i blokovi za vjezbu u editoru |

## Sto se sprema

Ovo je developer ulaz u **stvarne sustave igre**. Koristi isti lokalni profil: questovi,
rekordi i postavke mogu se trajno spremati prema pravilima pojedine scene. Promjena laboratorija
resetira trenutni run na seed iz ulazne scene (zadano 1); nije rollback spremljenog profila.

**Spremi/Ucitaj testni teret** koristi zaseban `user://dev_playground_load.json` i postojeci
EquipmentPersistence. Sprema noseni teret/opremu; ne sprema cijeli svijet ili sve questove.

## Kako ga uredjivati

- Za svoje prepreke otvori **debug/playground/DevSandbox.tscn**. Dodaj StaticBody3D,
  MeshInstance3D i CollisionShape3D pod korijen scene. Poligon `Course` ima podesive nagibe,
  stepenice, podloge i visine padova u Inspectoru; generirani Course/Generated cvorovi se obnavljaju.
- Igraceve LocomotionSettings u toj sceni lokalne su toj sceni. Promjene trajnih resursa igre
  rade se u odgovarajucim resursima i testiraju u njihovim laboratorijima.
- Za novi laboratorij otvori **DevPlayground.tscn**, odaberi korijen i prosiri **Labs**.
  Dodaj DevPlaygroundEntry s jedinstvenim Id, naslovom, kategorijom, ScenePath, opisom i kontrolama.
  Dodaj rucnu scenu; automatizirane debug/tests scene same gase proces i nisu za taj izbornik.
- DevPlaygroundConsole ima izvozne vrijednosti za seed, stetu, visinu odbacivanja i testni save.
  Konzola postoji samo dok se ovaj developer ulaz izvrsava; nema novog projektnog autoloada.

## Provjera i datoteke

DevPlaygroundTests otvara svih 17 laboratorija u istom procesu i vraca ih na poligon. Provjerava
prijenos predmeta, uklanjanje starog inventoryja, cuvanje pauze, reset lika, nepoznatu scenu i
cekajuci teleport. Pri promjeni svijeta koristi se kratka prazna prijelazna scena i dovrsetak .NET
finalizatora prije ponovnog ucitavanja resursa; to je loading pauza, ne postupak tijekom hodanja.

Lokalna provjera 2026-11-09: build ima 0 upozorenja i 0 gresaka, a DevPlaygroundTests prolazi
svih 32 provjere, ukljucujuci Start, hodanje i predmet/prop ispod teleport cilja koji ne smije
glumiti spreman teren. Graficki su pregledani izbornik i Alati. Nakon optimizacije alokacija
terena puni paket prolazi **53 scene / 715 provjera**, uz isti prag od 16 ms. Raniji zastoji
ostaju dokumentirani u [terrain-streaming-performance.md](terrain-streaming-performance.md);
rezultat nije jamstvo FPS-a na svakom uredaju.

Naknadni ispravak gumba za glavni poligon: izbornik se otvara samo pri ulasku u playground,
ne pri svakom pokretanju poligona. Regresija provjerava pokretanje iz odabira i stvarno hodanje
(1.70 m u probi), a isti kratki scenarij prosao je i u grafickom rendereru. Katalog predmeta cuva
managed reference resursa dok ih trajni developer UI koristi.

Nove datoteke: debug/DevPlayground.cs i .tscn; debug/playground/DevPlaygroundEntry.cs,
DevPlaygroundConsole.cs, DevSandbox.tscn, DevTransition.tscn; debug/tests/DevPlaygroundTests.cs
i .tscn te njihovi generirani script UID-ovi. Dokumentacija: ova stranica i debug/README.md.

Playground olaksava testiranje onoga sto postoji. Zavrsni art, novi physics model skoka,
preuredeni inventory i ostale jos neimplementirane zelje ostaju zasebni zadaci.
