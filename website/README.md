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
Spelet laddas ner från GitHub Releases i det **publika** repot som står i `config.js`
(`Woodyz34/O-Manager-Download`). Själva spelkoden ligger kvar privat.

1. Bygg spelet i Unity (File > Build Profiles > Windows > Build).
2. Zippa hela byggmappen och döp zip-filen till exakt `O-Manager-Windows.zip`.
3. Gå till `https://github.com/Woodyz34/O-Manager-Download/releases/new`.
4. Skriv ett versionsnummer som tagg (t.ex. `v1.0.0`), en titel och vad som är nytt.
5. Dra in zip-filen och tryck **Publish release**.

Ladda ner-knappen pekar alltid på den senaste versionen och visar versionsnummer, storlek och datum.

## Publicera hemsidan (Cloudflare Pages, gratis)
1. Skapa ett gratis konto på https://dash.cloudflare.com/sign-up.
2. Workers & Pages > Create > Pages > **Connect to Git** > välj GitHub och repot `Woodyz34/O-Manager`.
3. Production branch: `main`. Framework preset: **None**. Build command: lämna tomt.
   Build output directory: `website`.
4. Under Build watch paths, ta med bara `website/*` så att sidan bara byggs om när hemsidan ändras.
5. Save and Deploy. Sidan hamnar på `https://<namn>.pages.dev`.

Egen domän (t.ex. `o-manager.se`) går att lägga till senare under Custom domains, utan att ändra något här.

## Bilder och video
`Docs/website_assets.py` gör om skärmdumparna till små AVIF/WebP-bilder och klipper introfilmen
till bakgrundsvideon. `Docs/website_contours.py` gör höjdkurvorna i bakgrunden.
