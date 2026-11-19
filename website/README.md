# O-Manager hemsida

En vanlig statisk hemsida: `index.html`, `styles.css`, `main.js`. Inget att bygga, inga paket.
Sidan finns på svenska och engelska (knappen SV/EN uppe till höger).

## Lägga till en nyhet
Öppna `news.json` och lägg en ny rad **överst**:

```json
{
  "date": "2026-12-01",
  "tag": "new",
  "title": { "sv": "Rubrik på svenska", "en": "Headline in English" },
  "text":  { "sv": "En eller två meningar.", "en": "One or two sentences." }
},
```

`tag` kan vara `new` (Nytt), `update` (Uppdatering) eller `fix`. Den översta nyheten visas stort.
När ändringen är på `main` uppdateras hemsidan av sig själv efter någon minut.

## Släppa en ny version av spelet
Spelet laddas ner som `O-Manager-Setup.exe` från GitHub Releases i det **publika** repot som står i `config.js`
(`Woodyz34/O-Manager-Download`). Själva spelkoden ligger kvar privat. Hela gången steg för steg finns i
[`installer/README.md`](../installer/README.md): kortversionen är **Tools > O-Manager > Build release** i Unity,
dra in `O-Manager-Setup.exe` på GitHub-sidan som öppnas och tryck **Publish release**.

Ladda ner-knappen pekar alltid på den senaste versionen och visar versionsnummer, storlek och datum.
Spelen som redan är installerade uppdaterar sig själva nästa gång de startas.

## Publicera hemsidan (Cloudflare Pages, gratis)
1. Skapa ett gratis konto på https://dash.cloudflare.com/sign-up.
2. Workers & Pages > Create > Pages > **Connect to Git** > välj GitHub och repot `Woodyz34/O-Manager`.
3. Production branch: `main`. Framework preset: **None**. Build command: lämna tomt.
   Build output directory: `website`.
4. Under Build watch paths, ta med bara `website/*` så att sidan bara byggs om när hemsidan ändras.
5. Save and Deploy. Sidan hamnar på `https://<namn>.pages.dev`.

## Koppla o-manager.se (köpt hos Loopia)
Domänen stannar hos Loopia (där betalar du den varje år), men Cloudflare får sköta DNS:en.
Du behöver inget webbhotell hos Loopia.

1. Cloudflare: **Add a domain** > skriv `o-manager.se` > välj **Free**. Cloudflare visar två namnservrar,
   t.ex. `anna.ns.cloudflare.com` och `bob.ns.cloudflare.com`.
2. Loopia Kundzon: klicka på `o-manager.se` > **Namnservrar / DNS-servrar** > byt Loopias namnservrar mot
   Cloudflares två. Det kan ta från några minuter upp till ett dygn innan det slår igenom.
3. Cloudflare: Workers & Pages > ditt Pages-projekt > **Custom domains** > lägg till `o-manager.se`
   och sedan `www.o-manager.se`. Cloudflare skapar DNS-posterna och HTTPS-certifikatet själv.
4. (Valfritt) Gör samma steg 1–2 för `omanager.se` och lägg en Redirect Rule som skickar allt till
   `https://o-manager.se`, så hamnar folk rätt även om de skriver utan bindestreck.

Adressen `https://<namn>.pages.dev` fortsätter fungera. Domänen går att byta eller lägga till när som helst
utan att något i hemsidan ändras.

## Bilder och video
`Docs/website_assets.py` gör om skärmdumparna till små AVIF/WebP-bilder och klipper introfilmen
till bakgrundsvideon. `Docs/website_contours.py` gör höjdkurvorna i bakgrunden.

Webbläsare sparar bilderna i 7 dagar. Byter du en bild men behåller filnamnet, öka siffran i `?v=2`
efter bildadresserna i `index.html` (sök och ersätt `?v=2` med `?v=3`), annars ser besökare den gamla bilden.

## Statistik: besök, klick, nedladdningar och spelare
Tre ställen att titta på, alla gratis och utan kakor på hemsidan.

**1. Besök och klick på hemsidan (GoatCounter)**
1. Gå till https://www.goatcounter.com/signup, välj ett kodnamn (t.ex. `o-manager`) och skapa kontot.
2. Öppna `website/config.js` och skriv koden mellan citattecknen på raden `goatcounter: ""`. Spara och pusha. Sidan bygger om sig själv.
3. Logga in på `https://<kod>.goatcounter.com`. Där ser du besökare per dag och timme, länder, var de kom ifrån
   och "Pages" > klick på Ladda ner-knappen (`download-win`, `download-mac`, `download-linux`).
   Siffran "just nu" visar hur många som är på sidan.

**2. Nedladdningar (GitHub)**
Öppna `https://o-manager.se/stats.html` (finns inte i Google). Den hämtar antalet nedladdningar per version och fil
direkt från GitHub. Spelets egna uppdateringar räknas också.

**3. Spelarna (Unity Analytics)**
1. Unity: Window > Package Manager > Unity Registry > **Analytics** > Install (om den inte redan står i listan).
2. https://cloud.unity.com > ditt projekt > **Analytics** > Get started / Enable (gratis).
3. Därefter visar **Analytics > Dashboards** nya spelare, aktiva spelare per dag, sessionslängd och när på dygnet de spelar.
   Egna händelser (Analytics > Event Explorer): `game_opened`, `lobby_created`, `lobby_joined`, `race_started`,
   `race_finished`, `single_player_started`, `pack_opened`.
4. Hur många som är online samtidigt står i Photons dashboard: https://dashboard.photonengine.com > ditt Fusion-program.

Spelet frågar inte, men spelaren kan stänga av statistiken i Esc-menyn (**Usage statistics**). Inga namn eller mejl skickas.
