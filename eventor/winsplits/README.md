# WinSplits-filer

Lägg resultat från VM, EM, världscupen (och nivå 2-tävlingar du vill ha med) här som txt-filer exporterade från WinSplits. En fil per klass.

Döp filen så här:

```
ÅÅÅÅ-MM-DD_<VM|EM|VC|niva2>_<lang|medel>_<H|D>[_kval][_valfri text].txt
```

Exempel:

- `2026-07-14_VM_medel_H.txt`
- `2026-05-30_VC_lang_D_kval.txt`
- `2026-08-23_EM_medel_D_finalA.txt`

Ladda upp på GitHub (Add file → Upload files i den här mappen) eller med GitHub Desktop. Jobbet "WinSplits-import" startar då av sig självt och sparar resultaten i `eventor/data/races/`. Om ett filnamn är fel blir körningen röd och säger vilken fil det gäller.
