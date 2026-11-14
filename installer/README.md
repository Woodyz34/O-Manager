# Installationsprogram och automatiska uppdateringar

Spelarna laddar ner **en fil**, `O-Manager-Setup.exe`, från hemsidan. Den installerar spelet, gör en genväg i
Start-menyn (och på skrivbordet om man vill) och lägger till O-Manager under *Inställningar > Appar*, så att
det går att avinstallera som vanligt.

Efter det behöver ingen ladda ner något igen. När spelet startar frågar det GitHub om det finns en nyare
version. Finns det, laddas den ner medan introt spelas, och på huvudmenyn visas *Updating to version …* med
en förloppsindikator. När den är klar installeras uppdateringen tyst och spelet startar om av sig självt.
Den som vill spela direkt trycker **Later** (då försöker spelet igen nästa gång). Sparade löpare och pengar
ligger kvar, de sparas inte i spelmappen.

## Engångsförberedelser (görs bara första gången)

1. **Installera Inno Setup** (gratis): https://jrsoftware.org/isdl.php > `innosetup-6.x.x.exe` > installera
   med standardvalen. Unity hittar det själv.
2. **Skapa det publika nedladdningsrepot** på https://github.com/new
   - Repository name: `O-Manager-Download`
   - Välj **Public** (annars kan varken hemsidan eller spelen se versionerna).
   - Kryssa i *Add a README file* och tryck **Create repository**.
   Spelkoden läggs inte där, bara installationsfilerna.

## Släppa en ny version (varje gång)

1. Öppna projektet i Unity och välj **Tools > O-Manager > Build release (setup.exe)…**
2. Kolla **New version**. Den föreslår nästa nummer själv (t.ex. `1.0.4`). Det måste vara högre än förra
   versionen, annars uppdaterar sig inte spelen.
3. Skriv vad som är nytt under **What's new**, t.ex. `Ny karta: Ängaryd` och `Nya löparvärden`. En sak per rad.
   Texten syns i spelet medan det uppdaterar och på GitHub.
4. Tryck **Build game and setup.exe**. Unity bygger spelet och Inno Setup packar det (några minuter).
5. När det är klart öppnas mappen `Builds\Release` och GitHub-sidan *New release* i webbläsaren, med
   versionsnummer, titel och text redan ifyllda.
6. Dra `O-Manager-Setup.exe` från mappen till rutan *Attach binaries* på GitHub-sidan och vänta tills den laddat upp.
7. Tryck **Publish release**.

Klart. Hemsidans *Ladda ner*-knapp ger nu den nya versionen, och alla som redan har spelet får uppdateringen
nästa gång de startar det. Vill du berätta om det på hemsidan också, lägg en rad i `website/news.json`.

Kom ihåg: filen måste heta exakt `O-Manager-Setup.exe` och releasen får inte vara *pre-release* eller utkast.

## Bra att veta

- **"Windows skyddade datorn"**: spelet är inte kodsignerat (det kostar pengar varje år), så Windows varnar
  första gången man kör setup-filen. Man klickar **Mer information** och **Kör ändå**. Uppdateringarna som spelet
  laddar ner själv visar inte varningen.
- **Var spelet hamnar**: `%LOCALAPPDATA%\Programs\O-Manager` (samma sätt som Discord och VS Code installeras).
  Då behövs ingen administratörsruta, varken vid installation eller uppdatering.
- **Testa själv innan**: kör `Builds\Windows\O-Manager.exe` direkt, eller installera `Builds\Release\O-Manager-Setup.exe`
  innan du publicerar. Ett bygge som körs i Unity-editorn letar aldrig efter uppdateringar.
- **Gå tillbaka till en äldre version**: ta bort den trasiga releasen på GitHub och släpp den gamla igen med ett
  *högre* nummer (spelen hoppar bara framåt).
- **Bygga setup.exe för hand** (om spelet redan är byggt):
  `"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.4 /DBuildDir="C:\...\Builds\Windows" installer\O-Manager.iss`

## Filer

| Fil | Vad den gör |
| --- | --- |
| `installer/O-Manager.iss` | Inno Setup-skriptet (genvägar, avinstallation, väntar på att spelet stängs vid uppdatering) |
| `installer/O-Manager.ico` | Ikonen på setup-filen (från `Assets/Art/Icon/OManagerIcon.png`) |
| `Assets/Editor/ReleaseBuilder.cs` | Menyn *Build release*: version, bygge, Inno Setup, GitHub-sidan |
| `Assets/Scripts/UI/GameUpdater.cs` | Uppdateringskollen i spelet och versionsnumret nere till höger på huvudmenyn |
| `website/config.js` | Vilket repo och filnamn hemsidans *Ladda ner*-knapp använder |
