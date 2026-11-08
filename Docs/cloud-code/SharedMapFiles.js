// Cloud Code script "SharedMapFiles" for O-Manager (see Assets/Scripts/Courses/SharedMaps.cs and PrivateMaps.cs).
//
// A map a player imports is saved in their own Cloud Save Player Files, which only that player can read. This script
// hands out short-lived download links to another player's map file and own map picture, and to nothing else (not
// the info file with contact details):
//   map-<id>-source.ocd / map-<id>-picture.png          public maps: every player may download them
//   private-<id>-source.ocd / private-<id>-picture.png  private maps: only players the owner has let in, while the
//        owner hosts a lobby race on the map. The owner lists them in their Public player data key "privateMaps":
//        [{ "id": "<map id>", "guests": ["<player id>", ...], "until": <unix seconds>, ... }]
//        Only the owner can write their own player data, so nobody can let themselves in.
//
// Set up once: Unity Cloud dashboard > Cloud Code > Scripts > SharedMapFiles, paste this file over the old one, and
// publish it. The parameters below are read from the script itself.
//
// Parameters:
//   ownerId  the player whose map it is
//   keys     comma-separated file keys, e.g. "map-illern-2025-source.ocd,map-illern-2025-picture.png"
// Answer: { files: { "<key>": { signedUrl, httpMethod, requiredHeaders } } }; a key that is missing is left out.

const { FilesApi, DataApi } = require("@unity-services/cloud-save-1.4");

const SHARED = /^map-[a-z0-9-]{1,48}-(source\.(ocd|omap|xmap)|picture\.png)$/;
const PRIVATE = /^private-([a-z0-9-]{1,48})-(source\.(ocd|omap|xmap)|picture\.png)$/;
const PRIVATE_KEY = "privateMaps";

module.exports = async ({ params, context, logger }) => {
  const ownerId = String(params.ownerId || "");
  const keys = String(params.keys || "").split(",").map(k => k.trim()).filter(k => k.length > 0);
  if (!/^[A-Za-z0-9_-]{1,64}$/.test(ownerId)) throw new Error("Bad ownerId");
  if (keys.length === 0 || keys.length > 4) throw new Error("Give 1 to 4 keys");

  // Private map files: the calling player has to be on the owner's guest list for that map
  let privateEntries = null;
  for (const key of keys) {
    if (SHARED.test(key)) continue;
    const match = PRIVATE.exec(key);
    if (!match) throw new Error("Not a shared map file: " + key);
    if (context.playerId === ownerId) continue;
    if (privateEntries === null) privateEntries = await loadPrivateEntries(context, ownerId, logger);
    const now = Math.floor(Date.now() / 1000);
    const entry = privateEntries.find(e => e && e.id === match[1]);
    const guests = entry && Array.isArray(entry.guests) ? entry.guests : [];
    if (!entry || !(Number(entry.until) > now) || guests.indexOf(context.playerId) < 0) {
      throw new Error("Not let in to the private map " + match[1]);
    }
  }

  // The service token may read other players' files; the calling player's own token may not
  const files = new FilesApi({ accessToken: context.serviceToken });
  const result = {};
  for (const key of keys) {
    try {
      const response = await files.getDownloadUrl(context.projectId, ownerId, key);
      result[key] = response.data;
    } catch (err) {
      const status = err && err.response ? err.response.status : "?";
      logger.warning("No download link for " + ownerId + "/" + key + ": " + status);
    }
  }
  return { files: result };
};

// The owner's list of private maps they host races on (their Public player data)
async function loadPrivateEntries(context, ownerId, logger) {
  try {
    const data = new DataApi({ accessToken: context.serviceToken });
    const response = await data.getPublicItems(context.projectId, ownerId, [PRIVATE_KEY]);
    const results = response.data && response.data.results ? response.data.results : [];
    const item = results.find(r => r.key === PRIVATE_KEY);
    if (!item) return [];
    const value = typeof item.value === "string" ? JSON.parse(item.value) : item.value;
    return Array.isArray(value) ? value : [];
  } catch (err) {
    const status = err && err.response ? err.response.status : String(err);
    logger.warning("Could not read the private maps of " + ownerId + ": " + status);
    return [];
  }
}

module.exports.params = {
  ownerId: { type: "String", required: true },
  keys: { type: "String", required: true }
};
