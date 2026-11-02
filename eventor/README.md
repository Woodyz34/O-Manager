# Eventor-data för O-Manager

Skript som hämtar resultat med sträcktider från svenska Eventor till löparpoängen i spelet.
API-nyckeln ligger som repo-hemligheten `EVENTOR_API_KEY` och skrivs aldrig ut eller sparas.

| Fil | Vad den gör |
| --- | --- |
| `step0_count_events.py` | Ett anrop: räknar hur många tävlingar ett år innehåller. |
| `fetch_results.py` | Hämtar resultat. `--mode once` = senaste året, nivå 1 utan DM, Swedish League/WRE och nivå 2-tävlingarna i `extra_events.txt`. `--mode week` = senaste veckan, nivå 1, 2 och internationellt. |
| `extra_events.txt` | Nivå 2-tävlingar som engångsjobbet också hämtar, en per rad (del av namnet räcker). |
| `data/races/<tävling>_<etapp>.json` | Sparade resultat, bara klasser med 21, 20 eller 18 (och Men/Women). |
| `data/unknown_classes.txt` | Klassnamn som varken var spelklasser eller vanliga åldersklasser. Gå igenom för hand. |

Bara fot-O, individuella tävlingar, lång och medel. Inställda tävlingar, sprint, ultralång och stafetter hoppas över.
Etapper som redan finns sparade hämtas inte igen, och det är 20 sekunders paus mellan anropen.

Köra lokalt: `EVENTOR_API_KEY=... python eventor/fetch_results.py --mode once`
