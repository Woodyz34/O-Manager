using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RunnerMapCollision : MonoBehaviour
{
    public NetworkPlayer player;
    public int numberOfSmallHills = 0;
    public int numberOfStones = 0;
    public int numberOfBigStones = 0;
    public int numberOfCliffs = 0;
    public int numberOfPunktHöjd = 0;
    public int numberOfStig = 0;
    public int numberOfBäck = 0;
    public int numberOfBeståndsgräns = 0;

    private void Awake()
    {
        player = GetComponentInParent<NetworkPlayer>();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (ActiveScene.Name == "Lobby")
        {
            return;
        }
        if (!player.HasInputAuthority)
        {
            return;
        }
        if (other.CompareTag("MapFeature"))
        {
            MapFeature mf = other.GetComponent<MapFeature>();
            if (CheckFeatureName("höjdkurva", other) && mf.length < 100)
            {
                numberOfSmallHills++;
            }

            else if (CheckFeatureName("sten", other))
            {
                numberOfStones++;
            }

            else if (CheckFeatureName("stor sten", other))
            {
                numberOfBigStones++;

            }

            else if (CheckFeatureName("grupp av stenar", other))
            {
                numberOfStones++;

            }
            else if (CheckFeatureName("Opasserbar brant(valfri tagg)", other))
            {
                numberOfCliffs++;
            }
            else if (CheckFeatureName("Passerbar brant/Skrent", other))
            {
                numberOfCliffs++;
            }
            else if (CheckFeatureName("Opasserbar brant", other))
            {
                numberOfCliffs++;
            }
            else if (CheckFeatureName("Avlång punkthöjd", other))
            {
                numberOfPunktHöjd++;
            }
            else if (CheckFeatureName("punkthöjd", other))
            {
                numberOfPunktHöjd++;
            }
            else if (CheckFeatureName("Stor stig", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Liten stig", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Passerbart litet vattendrag", other))
            {
                numberOfBäck++;
            }
            else if (CheckFeatureName("Mindre dike", other))
            {
                numberOfBäck++;
            }
            else if (CheckFeatureName("Större bäck", other))
            {
                numberOfBäck++;
            }
            else if (CheckFeatureName("beståndsgräns", other))
            {
                numberOfBeståndsgräns++;
            }
            else if (CheckFeatureName("Smal väg", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Mindre väg 0.30", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Liten otydlig stig", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Körväg", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Större väg 0.5", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Cykelväg", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Väg: sidlinje", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Motorväg", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Rågång/Drivningsväg", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Cykelväg, sidlinje", other))
            {
                numberOfStig++;
            }
            else if (CheckFeatureName("Järnväg", other))
            {
                numberOfStig++;
            }
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("MapFeature"))
        {
            MapFeature mf = other.GetComponent<MapFeature>();
            if (CheckFeatureName("höjdkurva", other) && mf.length < 100)
            {
                numberOfSmallHills--;
            }
            else if (CheckFeatureName("sten", other))
            {
                numberOfStones--;

            }

            else if (CheckFeatureName("stor sten", other))
            {
                numberOfBigStones--;

            }

            else if (CheckFeatureName("grupp av stenar", other))
            {
                numberOfStones--;

            }
            else if (CheckFeatureName("Opasserbar brant(valfri tagg)", other))
            {
                numberOfCliffs--;

            }
            else if (CheckFeatureName("Passerbar brant/Skrent", other))
            {
                numberOfCliffs--;

            }
            else if (CheckFeatureName("Opasserbar brant", other))
            {
                numberOfCliffs--;

            }
            else if (CheckFeatureName("Avlång punkthöjd", other))
            {
                numberOfPunktHöjd--;

            }
            else if (CheckFeatureName("punkthöjd", other))
            {
                numberOfPunktHöjd--;

            }
            else if (CheckFeatureName("Stor stig", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Liten stig", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Smal väg", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Stor bäck", other))
            {
                numberOfBäck--;
            }
            else if (CheckFeatureName("Passerbart litet vattendrag", other))
            {
                numberOfBäck--;
            }
            else if (CheckFeatureName("Mindre dike", other))
            {
                numberOfBäck--;
            }
            else if (CheckFeatureName("beståndsgräns", other))
            {
                numberOfBeståndsgräns--;
            }
            else if (CheckFeatureName("Mindre väg 0.30", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Liten otydlig stig", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Körväg", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Större väg 0.5", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Cykelväg", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Väg: sidlinje", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Motorväg", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Rågång/Drivningsväg", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Cykelväg, sidlinje", other))
            {
                numberOfStig--;
            }
            else if (CheckFeatureName("Järnväg", other))
            {
                numberOfStig--;
            }
        }
    }

    public bool CheckFeatureName(string objectName, Collider2D other)
    {
        MapFeature mf = GetFeature(other);
        if (mf == null) return false;

        // Namnet utan de 6 första tecknen och utan mellanslag, jämfört skiftlägesokänsligt
        return mf.NameAfterPrefix.Equals(objectName, System.StringComparison.OrdinalIgnoreCase);
    }

    // Senaste collidern och dess MapFeature, så att kedjan av namn-kontroller bara gör ett GetComponent per kollision
    private Collider2D lastCheckedCollider;
    private MapFeature lastCheckedFeature;

    private MapFeature GetFeature(Collider2D other)
    {
        if (!ReferenceEquals(other, lastCheckedCollider))
        {
            lastCheckedCollider = other;
            lastCheckedFeature = other.GetComponent<MapFeature>();
        }
        return lastCheckedFeature;
    }
}
