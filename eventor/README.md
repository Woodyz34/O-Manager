# Eventor-data för O-Manager

Skript som hämtar resultat med sträcktider från svenska Eventor till löparpoängen i spelet.
API-nyckeln ligger som repo-hemligheten `EVENTOR_API_KEY` och skrivs aldrig ut eller sparas.

| Fil | Vad den gör |
| --- | --- |
| `step0_count_events.py` | Ett anrop: räknar hur många tävlingar ett år innehåller. |
| `fetch_results.py` | Hämtar resultat. `--mode once` = senaste året, nivå 1 utan DM, Swedish League/WRE och nivå 2-tävlingarna i `extra_events.txt`. `--mode week` = senaste veckan, nivå 1, 2 och internationellt. |
| `import_winsplits.py` | Gör om WinSplits-filer i `winsplits/` (VM, EM, världscup) till samma format. Körs av sig själv när du laddar upp en fil, se `winsplits/README.md`. |
| `compute_points.py` | Räknar fysik och teknik för alla löpare från de sparade loppen (inga anrop). Körs efter varje hämtning och import. Skriver `data/runners.json`, `data/points_report.md` och spelets kortfil `Assets/Resources/Runners/runners.json` (synliga löpare i paketen, frysta kort behåller sina sista siffror). |
| `extra_events.txt` | Nivå 2-tävlingar som engångsjobbet också hämtar, en per rad (del av namnet räcker). |
| `klubbar.txt` | Klubb för löpare som saknar klubb (VM, EM och världscupen ger bara land), en per rad: `Namn = Klubb`. Skrivs om när poängen räknas, med alla som fortfarande saknar klubb. |
| `nationalitet.txt` | Land för löpare där landet bara är gissat från klubben (Eventor ger klubbens land, inte löparens). En per rad: `Namn = Land` (t.ex. `Storbritannien` eller `GBR`). Listar de som gissats till ett land utanför Sverige. Gissade länder visas som "guessed from club" i My Runners. VM, EM och världscupen ger säkert land. |
| `data/races/<tävling>_<etapp>.json` | Sparade resultat, bara klasser med 21, 20 eller 18 (och Men/Women). |
| `data/unknown_classes.txt` | Klassnamn som varken var spelklasser eller vanliga åldersklasser. Gå igenom för hand. |

Eventor-hämtningen tar bara fot-O, individuella tävlingar, lång, medel och sprint (sprint sedan 2026-11-14). Inställda tävlingar, sprint, ultralång och stafetter hoppas över.

**Sprint** kommer från Eventor och från WinSplits-filer (`_sprint_` i namnet). Finns samma lopp i båda (samma datum och samma tre snabbaste med samma tider) används Eventors och WinSplits-filen hoppas över. Den ger kortens *Speed* (sprintfart) på en egen skala där VM- och världscupssprintens snabbaste rena tid är 100. Skogsloppen ger *Physics* (fysik, hette speed förut) och teknik, och bestämmer vilka som är med i spelet (5 skogslopp, eller VM/EM/VC i skog). Löpare utan sprint får `-` och springer med fysiken även på asfalt. Sprintteknik sparas (`sprintTechnique`) men visas inte.
Etapper som redan finns sparade hämtas inte igen, och det är 20 sekunders paus mellan anropen.

Köra lokalt: `EVENTOR_API_KEY=... python eventor/fetch_results.py --mode once`
