// Cloud Code script "SharedMapFiles" for O-Manager (see Assets/Scripts/Courses/SharedMaps.cs).
//
// A map a player imports is saved in their own Cloud Save Player Files (map-<id>-source.ocd etc.), which only that
// player can read. This script lets every player download a shared map: it hands out short-lived download links to
// another player's map file and own map picture, and to nothing else (not the info file with contact details).
//
// Set up once: Unity Cloud dashboard > Cloud Code > Scripts > Create script, name it SharedMapFiles, paste this file,
// and publish it. The parameters below are read from the script itself.
//
// Parameters:
//   ownerId  the player who shared the map
//   keys     comma-separated file keys, e.g. "map-illern-2025-source.ocd,map-illern-2025-picture.png"
// Answer: { files: { "<key>": { signedUrl, httpMethod, requiredHeaders } } }; a key that is missing is left out.

const { FilesApi } = require("@unity-services/cloud-save-1.4");

const ALLOWED = /^map-[a-z0-9-]{1,48}-(source\.(ocd|omap|xmap)|picture\.png)$/;

module.exports = async ({ params, context, logger }) => {
  const ownerId = String(params.ownerId || "");
  const keys = String(params.keys || "").split(",").map(k => k.trim()).filter(k => k.length > 0);
  if (!/^[A-Za-z0-9_-]{1,64}$/.test(ownerId)) throw new Error("Bad ownerId");
  if (keys.length === 0 || keys.length > 4) throw new Error("Give 1 to 4 keys");

  // The service token may read other players' files; the calling player's own token may not
  const files = new FilesApi({ accessToken: context.serviceToken });
  const result = {};
  for (const key of keys) {
    if (!ALLOWED.test(key)) throw new Error("Not a shared map file: " + key);
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

module.exports.params = {
  ownerId: { type: "String", required: true },
  keys: { type: "String", required: true }
};
