# Banbyggare: designförslag

## Så fungerar det i dag

- **Karta = scen.** Lobbyn listar bilderna i `Resources/Maps` (`MapHandler.cs`). Namnet på bilden som väljs är namnet på scenen som laddas (`ReadyUIHandler.cs:85`, `Runner.LoadScene(mapHandler.map)`). De fyra "IllernKartan Träning 1–4" är fyra scener med fyra 45 MB-bilder av samma karta.
- **Banan är inbränd i bilden** (`Resources/KartBild/*`), och kontrollerna är Transforms som placerats för hand i varje scen (`PathfindingManager.controlPoints`). Kartbilden passas in mot kontrollerna med `SpriteAlignerAuto`.
- **Griddet** (`Resources/2d-gridJson/output_grid*.json/.bytes`) är ett 10 m-rutnät i SWEREF 99 TM med löpfart per ruta plus lutningsfaktorer i 8 riktningar. Det finns bara ett, för Illern. Fart ≤ 0,01 räknas som ofarbart i A* (`AStarPathfinder.cs`), och A* returnerar `null` om målet inte går att nå.
- **Källdata:** OCAD → DXF → QGIS → GeoJSON med ISOM-lager (`Resources/Illernkartan/*.json`). Skriptet som gjorde griddet av GeoJSON + höjddata ligger inte i projektet.
- **Ingen molnlagring finns.** Photon Fusion används bara för realtid; värdens val synkas via `NetworkPlayer.lobbyMap` (max 64 tecken).

## Förslag

### 1. Karta och bana skiljs åt
- **Karta** = ren kartbild utan bana + grid + georeferens (hörnkoordinater i SWEREF 99 TM). Beskrivs i en liten `MapDefinition` (id, namn, bild, grid, bounds). Kartbilden placeras automatiskt utifrån bounds, så handjusteringen med SpriteAligner försvinner.
- **Bana** = data, inte scen:
  ```json
  { "id": "...", "name": "Kvällsträning", "author": "Anders", "mapId": "illern",
    "start": [499452.4, 6429169.1], "controls": [[x,y], ...], "finish": [x,y],
    "createdAt": "...", "version": 1 }
  ```
  Koordinater i meter (SWEREF), inte rutor, så banan överlever om griddet görs om.
- **En enda spelscen** läser in vald karta + bana och ritar start (triangel), kontroller (cirklar med nummer) och mål (dubbelcirkel) med linjer emellan, i lila som på riktiga kartor, ovanpå kartbilden. Befintlig kod som läser `controlPoints` fortsätter fungera eftersom vi skapar samma Transforms vid start.
- De fyra Träning-banorna konverteras till banfiler, och de fyra scenerna och dubblettbilderna kan tas bort.

### 2. Banbyggaren
- Ny sida från huvudmenyn/lobbyn: välj karta, klicka ut start, kontroller och mål. Dra för att flytta, högerklick/knapp för att ta bort, ändra ordning i en lista.
- **Nåbarhetskontroll:** för varje sträcka körs befintlig A*. Saknas väg markeras den kontrollen rött med texten "Kontroll 4 går inte att nå från kontroll 3" och sparaknappen spärras. Även punkter på ofarbar mark (sjö, förbjudet område) stoppas direkt vid klicket. Bonus: sträcklängd och beräknad vinnartid visas per sträcka.

### 3. Lagring i molnet och lobbyn
- Banan sparas i molnet som ett publikt objekt med namn, författare, karta och datum.
- Lobbyns kart-sida blir en **ban-lista**: kartbild som miniatyr, banans namn, "av Anders", antal kontroller och längd. Filter per karta, och "Mina banor".
- I multiplayer skickar värden bara ban-id via Fusion; övriga spelare hämtar banan från molnet (eller får hela banans JSON via en RPC, den är bara några hundra byte).

## Bättre sätt att få kartdata

1. **Grid från kartbildens färger.** ISOM-kartor har fasta färger (vit skog = full fart, ljus/mellan/mörkgrön = långsammare, gult = öppet, blått = vatten, svart/oliv = ofarbart/förbjudet). En klassning per pixel ger farts-griddet direkt från en PNG/PDF-export, utan OCAD-fil och QGIS. Banlinjer och text tas bort eller behandlas som "okänd" och fylls från grannrutor. Det här gör att vilken klubbkarta som helst kan importeras om man har en ren bild.
2. **Lantmäteriets öppna höjddata för lutning.** Markhöjdmodell grid 1+ (1 m-rutnät) är öppna data under CC0 sedan februari 2025 och hämtas via Geotorget. Kräver bara att kartbilden georefereras (två–tre kända punkter, eller hörnkoordinaterna från OCAD/OOM).
3. **Helt nya områden utan klubbkarta:** Topografi 10 (vektor, öppen sedan 2025) ger vatten, vägar, byggnader och 5 m-kurvor, och Lantmäteriets laserdata kan köras genom Karttapullautin, som gör en orienteringslik karta automatiskt (gratis för orienteringsbruk). Kvaliteten räcker för träning men inte för tävling; detaljer som stenar och gropar saknas.
4. OpenStreetMap ger vägar och stigar men för lite terrängdata för att vara huvudkälla; mest användbart som komplement.

Rekommendation: behåll OCAD-vägen när en klubb kan ge filen (bäst kvalitet), och bygg färgklassning + Lantmäteriet-höjd som standardvägen för nya kartor.

## Källor
- [Lantmäteriet: Markhöjdmodell Nedladdning, grid 1+](https://lantmateriet.se/en/geodata/our-products/product-list/terrain-model-download-grid-1)
- [Lantmäteriet: Öppna data (CC0)](https://www.lantmateriet.se/oppnadata)
- [Lantmäteriet: Produktnyheter (avgiftsfria produkter från 3 feb 2025)](https://www.lantmateriet.se/sv/geodata/vara-produkter/Produktnyheter/)
- [Lantmäteriet: Topografi 10 Nedladdning, vektor (produktbeskrivning)](https://www2.lantmateriet.se/globalassets/geodata/geodataprodukter/e-pb-topografi-10-nedladdning-vektor.pdf)
- [Gokartor: Laserkartan, Topografi 10 som öppna data](https://gokartor.se/laserkartan-uppdaterad-version/)
- [rusty-pullauta (Karttapullautin)](https://github.com/rphlo/rusty-pullauta/)
- [World of O: MapAnt, hela Finland automatiskt kartlagt](https://news.worldofo.com/2016/12/01/mapant-gigantic-orienteering-map-covering-finland)
