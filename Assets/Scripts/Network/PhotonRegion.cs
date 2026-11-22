using Fusion.Photon.Realtime;

// Every player uses the same Photon region, so everyone in the world sees every lobby.
// Before 2026-11-22 Fusion picked the "best" (lowest ping) region per player: a player in Croatia
// could land on another region than a friend in Sweden, and then they never saw each other's lobbies,
// because lobby lists and sessions only exist inside one region.
// "eu" (Amsterdam) is close for the Nordic and European players the game has today.
public static class PhotonRegion
{
    public const string Fixed = "eu";

    // A copy of the global Photon settings with the region locked. Passed to every StartGame and
    // JoinSessionLobby that goes online, so it holds even if the settings asset is changed by mistake.
    public static FusionAppSettings Settings()
    {
        FusionAppSettings settings = PhotonAppSettings.Global.AppSettings.GetCopy();
        settings.FixedRegion = Fixed;
        return settings;
    }
}
