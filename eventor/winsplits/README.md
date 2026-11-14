# WinSplits-filer

Lägg resultat från VM, EM, världscupen (och nivå 2-tävlingar du vill ha med) här som txt-filer exporterade från WinSplits. En fil per klass.

Döp filen så här:

```
ÅÅÅÅ-MM-DD_<VM|EM|VC|niva2>[-quali]_<lang|medel|sprint>_<klass>[_kval][_loppets namn].txt
```

`nivå2` går lika bra som `niva2`.

Exempel:

- `2026-07-14_VM_medel_H.txt`
- `2026-05-30_VC_lang_D_kval.txt`
- `2026-08-23_EM_medel_D_final_A.txt`
- `2026-07-19_niva2_medel_H_O-Ringen_E1.txt`
- `2026-07-19_niva2_medel_D20_O-Ringen_E1.txt`
- `2026-07-19_niva2_medel_H21Kort_O-Ringen_E1.txt`
- `2026-07-07_VM_sprint_H.txt` (VM-sprintfinalen, ankare = 100)
- `2026-07-07_VM-quali2_sprint_D.txt` (VM-sprintkval heat 2, bara för att länka ihop)
- `2026-05-16_nivå2_sprint_H21A_SM, sprint (Swedish League, #4), kval.txt` (ordet "kval" i namnet = kval)

**Sprint** ger kortens Speed (sprintfart), skogsloppen ger Physics och teknik. Sprintlopp gör ingen synlig i spelet, det krävs skogslopp.

Klassen är `H` eller `D` (betyder H21/D21) eller hela klassnamnet, t.ex. `D20`, `H18` eller `H21Kort`. Två klasser i samma lopp får olika filnamn tack vare klassen.

I spelet får loppet ett engelskt namn: "World Championships Middle", "World Cup Long Qualification". Texten sist i filnamnet (med _ som mellanslag) läggs till inom parentes, "European Championships Middle (final A)". För niva2 är texten loppets eget namn: "O-Ringen E1, Middle".

**Kval och B-finaler** (`_kval` i namnet, eller fritext som `final_B`, `B-final`, `semi`, `heat`): alla som springer dem från VM, EM och världscupen kommer med i spelet direkt, men vinnaren får inte automatiskt 100. Loppet räknas i stället om mot löpare som redan har betyg, precis som svenska lopp. Bara A-finaler (och lopp utan sådan text) är ankare där snabbaste rena tid = 100. Kval på niva2-lopp (t.ex. SM) ger inte direktplats i spelet.

Ladda upp på GitHub (Add file → Upload files i den här mappen) eller med GitHub Desktop. Jobbet "WinSplits-import" startar då av sig självt och sparar resultaten i `eventor/data/races/`. Om ett filnamn är fel blir körningen röd och säger vilken fil det gäller.
