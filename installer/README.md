# Installationsprogram och automatiska uppdateringar

Spelet finns för **Windows, Mac och Linux**. Varje release på GitHub har en fil per dator:

| Dator | Fil | Vad spelaren gör |
| --- | --- | --- |
| Windows | `O-Manager-Setup.exe` | Kör setup-filen |
| Mac (Intel och Apple silicon) | `O-Manager-Mac.zip` | Packar upp, drar O-Manager till Program |
| Linux (64-bitars) | `O-Manager-Linux.tar.gz` | Packar upp, startar `O-Manager.x86_64` |

Hemsidans *Ladda ner*-knapp väljer rätt fil själv efter besökarens dator. Alla tre uppdaterar sig själva.

Spelarna laddar ner **en fil**, `O-Manager-Setup.exe`, från hemsidan. Den installerar spelet, gör en genväg i
Start-menyn (och på skrivbordet om man vill) och lägger till O-Manager under *Inställningar > Appar*, så att
det går att avinstallera som vanligt.

Efter det behöver ingen ladda ner något igen. När spelet startar frågar det GitHub om det finns en nyare
version. Finns det, laddas den ner medan introt spelas, och på huvudmenyn visas *Updating to version …* med
en förloppsindikator. När den är klar installeras uppdateringen tyst och spelet startar om av sig självt.
Den som vill spela direkt trycker **Later** (då försöker spelet igen nästa gång). Sparade löpare och pengar
ligger kvar, de sparas inte i spelmappen.

## Engångsförberedelser (görs bara första gången)

0. **Lägg till Mac och Linux i Unity** (bara om du vill bygga dem): öppna **Unity Hub > Installs**, klicka på
   kugghjulet vid Unity 6000.3.25f1 > **Add modules**, kryssa i **Mac Build Support (Mono)** och
   **Linux Build Support (Mono)**, tryck **Install** och starta om Unity. Utan dem är rutorna Mac och Linux grå.
1. **Installera Inno Setup** (gratis): https://jrsoftware.org/isdl.php > `innosetup-6.x.x.exe` > installera
   med standardvalen. Unity hittar det själv.
2. **Skapa det publika nedladdningsrepot** på https://github.com/new
   - Repository name: `O-Manager-Download`
   - Välj **Public** (annars kan varken hemsidan eller spelen se versionerna).
   - Kryssa i *Add a README file* och tryck **Create repository**.
   Spelkoden läggs inte där, bara installationsfilerna.

## Släppa en ny version (varje gång)

1. Öppna projektet i Unity och välj **Tools > O-Manager > Build release…**
2. Kolla **New version**. Den föreslår nästa nummer själv (t.ex. `1.0.4`). Det måste vara högre än förra
   versionen, annars uppdaterar sig inte spelen.
3. Skriv vad som är nytt under **What's new**, t.ex. `Ny karta: Ängaryd` och `Nya löparvärden`. En sak per rad.
   Texten syns i spelet medan det uppdaterar och på GitHub.
4. Kolla rutorna under **Computers** (Windows, Mac, Linux) och tryck **Build release**. Unity bygger spelet en
   gång per dator och packar filerna (räkna med 5–15 minuter för alla tre). Unity växlar plattform under tiden
   och tillbaka till Windows på slutet.
5. När det är klart öppnas mappen `Builds\Release` och GitHub-sidan *New release* i webbläsaren, med
   versionsnummer, titel och text redan ifyllda.
6. Dra **alla** filerna (`O-Manager-Setup.exe`, `O-Manager-Mac.zip`, `O-Manager-Linux.tar.gz`) från mappen till
   rutan *Attach binaries* på GitHub-sidan och vänta tills de laddat upp.
7. Tryck **Publish release**.

Klart. Hemsidans *Ladda ner*-knapp ger nu den nya versionen, och alla som redan har spelet får uppdateringen
nästa gång de startar det. Vill du berätta om det på hemsidan också, lägg en rad i `website/news.json`.

Kom ihåg: filerna måste heta exakt som ovan och releasen får inte vara *pre-release* eller utkast. Saknas en fil
i en release får den datorn ingen uppdatering den gången (och hemsidan säger "kommer snart" för den).

## Bra att veta

- **"Windows skyddade datorn"**: spelet är inte kodsignerat (det kostar pengar varje år), så Windows varnar
  första gången man kör setup-filen. Man klickar **Mer information** och **Kör ändå**. Uppdateringarna som spelet
  laddar ner själv visar inte varningen.
- **Mac: "kan inte kontrolleras" / "är skadat"**: spelet är inte signerat och notariserat hos Apple (det kräver
  Apple Developer Program, 99 USD om året). Första gången går spelaren till *Systeminställningar > Integritet och
  säkerhet > Öppna ändå*. Säger Macen att appen är skadad hjälper `xattr -cr /Applications/O-Manager.app` i Terminal.
  Båda står på hemsidan. Uppdateringar som spelet laddar ner själv får ingen varning.
- **Mac: uppdatering kräver att spelet ligger i Program**: körs det direkt från Hämtade filer kör macOS en dold,
  skrivskyddad kopia. Då visar spelet bara "Version X is out" med en knapp till hemsidan. Samma sak om mappen inte
  går att skriva i, eller om en uppdatering misslyckats två gånger i rad.
- **Mac byggs från Windows**: Unity bygger Mac-spelet med Mono. Zip-filen skrivs av Unity-skriptet självt
  (`Assets/Editor/ReleaseArchives.cs`), eftersom Windows egna zip tappar "får köras"-flaggan som Macen behöver.
- **Linux-introt**: Linux kan inte spela mp4, så introfilmen finns också som `Resources/Intro/intro_linux.webm`.
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
| `Assets/Editor/ReleaseBuilder.cs` | Menyn *Build release*: version, bygge för varje dator, Inno Setup, GitHub-sidan |
| `Assets/Editor/ReleaseArchives.cs` | Packar Mac-zip och Linux-tar.gz med rätt "får köras"-flaggor |
| `Assets/Scripts/UI/GameUpdater.cs` | Uppdateringskollen i spelet och versionsnumret nere till höger på huvudmenyn |
| `website/config.js` | Vilket repo och vilka filnamn hemsidans *Ladda ner*-knapp använder |

## Varför bygget går snabbare nu (kartbilderna)

Kartbilderna ligger inte längre i `Assets/Resources`. Unity behövde annars importera dem (en minut för en stor
karta) och skrev in dem **okomprimerade** i varje bygge: 4,2 GB av spelets 4,7 GB, och Inno Setup tog ungefär
10 minuter på sig att packa dem. Nu:

- PNG-bilderna ligger i `MapPictures/<karta>/picture.png` (i git, utanför Assets, så Unity rör dem inte).
- **Build release** gör först färdiga, komprimerade bildfiler i `Assets/StreamingAssets/MapPictures/`, men bara
  för kartor vars PNG har ändrats sedan förra gången. Oförändrade kartor tar en sekund eller två, en ny eller
  ändrad stor karta ungefär en minut, en gång. Mappen ligger inte i git.
- Unity kopierar sedan bara in de färdiga filerna i spelet, utan att bearbeta dem.
- Dialogen när bygget är klart visar hur lång tid varje steg tog.

Du behöver inte hålla reda på vad som har ändrats: verktyget jämför själv innehållet i varje PNG. Koden och
resten byggs alltid om, det är den snabba delen. Vill du göra bildfilerna i förväg (t.ex. efter en ny karta)
finns **Tools > O-Manager > Developer > Prepare map pictures for the build**. Import av en ny karta gör det
också direkt.
