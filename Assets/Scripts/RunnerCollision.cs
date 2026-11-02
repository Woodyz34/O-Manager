using System;
using UnityEngine;

public class RunnerCollision : MonoBehaviour
{
    public RunnerMapCollision RunnerMapCollision;
    public int numberOfSmallHills = 0;
    public int numberOfStones = 0;
    public int numberOfBigStones = 0;
    public int numberOfCliffs = 0;
    public int numberOfPunktHöjd = 0;
    public int numberOfStig = 0;
    public int numberOfBäck = 0;
    public int numberOfBeståndsgräns = 0;

    // Timer-fält
    [Header("Timer")]
    private float timer;               // nuvarande tid
    private bool timerActive = false;  // om timern ska räkna ner

    private float stigTimer;
    private bool stigTimerActive = false;

    private float bäckTimer;
    private bool bäckTimerActive = false;

    private float beståndsgränsTimer;
    private bool beståndsgränsTimerActive = false;

    private float safetyTime  = 0.2f;
    private float CantMissTimer = 0.2f;

    public bool cantStigMiss = false;
    public bool cantBeståndsgränsMiss = false;
    public bool cantBäckMiss = false;
    public bool shouldMiss = false;

    

    private void Start()
    {
    }

    private void Update()
    {
        if (!RunnerMapCollision.player.HasInputAuthority)
        {
            return;
        }
        // Räkna ner timern om den är aktiv
        if (timerActive)
        {
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                timer = 0f;
                timerActive = false;
                OnTimerFinished();
            }
        }
        // Räkna ner stigTimern om den är aktiv
        if (stigTimerActive)
        {
            stigTimer -= Time.deltaTime;

            if (stigTimer <= 0f)
            {
                stigTimer = 0f;
                stigTimerActive = false;
                OnStigTimerFinished();
            }
        }
        if (bäckTimerActive)
        {
            bäckTimer -= Time.deltaTime;

            if (bäckTimer <= 0f)
            {
                bäckTimer = 0f;
                bäckTimerActive = false;
                OnBäckTimerFinished();
            }
        }
        if (beståndsgränsTimerActive)
        {
            beståndsgränsTimer -= Time.deltaTime;

            if (beståndsgränsTimer <= 0f)
            {
                beståndsgränsTimer = 0f;
                beståndsgränsTimerActive = false;
                OnBeståndsgränsTimerFinished();
            }
        }
    }

    private void OnBeståndsgränsTimerFinished()
    {
        cantBeståndsgränsMiss = true;
    }

    private void OnBäckTimerFinished()
    {
        cantBäckMiss = true;
    }

    private void OnStigTimerFinished()
    {
        Debug.Log("cantStigMiss");
        cantStigMiss = true;
    }
    /// <summary>
    /// Körs när timern tar slut.
    /// </summary>
    private void OnTimerFinished()
    {
        shouldMiss = true;
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("MapFeature"))
        {
            MapFeature mf = other.GetComponent<MapFeature>();
            if (mf != null)
            {
                if (CheckFeatureName("höjdkurva", other) && mf.length < 100)
                {
                    numberOfSmallHills++;
                    if (RunnerMapCollision.numberOfSmallHills < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }

                else if(CheckFeatureName("sten", other))
                {
                    numberOfStones++;
                    if (RunnerMapCollision.numberOfStones < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }

                else if (CheckFeatureName("stor sten", other))
                {
                    numberOfBigStones++;
                    if (RunnerMapCollision.numberOfBigStones < 6)
                    {
                        if (GetTime() < safetyTime*2)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }

                else if (CheckFeatureName("grupp av stenar", other))
                {
                    numberOfStones++;
                    if (RunnerMapCollision.numberOfStones < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("Opasserbar brant(valfri tagg)", other))
                {
                    numberOfCliffs++;
                    if (RunnerMapCollision.numberOfCliffs < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("Passerbar brant/Skrent", other))
                {
                    numberOfCliffs++;
                    if (RunnerMapCollision.numberOfCliffs < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("Opasserbar brant", other))
                {
                    numberOfCliffs++;
                    if (RunnerMapCollision.numberOfCliffs < 6)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("Avlång punkthöjd", other))
                {
                    numberOfPunktHöjd++;
                    if (RunnerMapCollision.numberOfPunktHöjd < 4)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("punkthöjd", other))
                {
                    numberOfPunktHöjd++;
                    if (RunnerMapCollision.numberOfPunktHöjd < 4)
                    {
                        if (GetTime() < safetyTime)
                        {
                            SetTime(safetyTime);
                        }
                    }
                }
                else if (CheckFeatureName("Stor stig", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime*2)
                    {
                        SetTime(safetyTime*2);
                    }
                }
                else if (CheckFeatureName("Liten stig", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Passerbart litet vattendrag", other))
                {
                    if (numberOfBäck == 0)
                    {
                        bäckTimer = CantMissTimer * 2;
                        bäckTimerActive = true;
                    }
                    numberOfBäck++;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
                else if (CheckFeatureName("Mindre dike", other))
                {
                    if (numberOfBäck == 0)
                    {
                        bäckTimer = CantMissTimer * 2;
                        bäckTimerActive = true;
                    }
                    numberOfBäck++;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
                else if (CheckFeatureName("Större bäck", other))
                {
                    if (numberOfBäck == 0)
                    {
                        bäckTimer = CantMissTimer*2;
                        bäckTimerActive = true;
                    }
                    numberOfBäck++;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
                else if (CheckFeatureName("beståndsgräns", other))
                {
                    if (numberOfBeståndsgräns == 0)
                    {
                        beståndsgränsTimer = CantMissTimer*2;
                        beståndsgränsTimerActive = true;
                    }
                    numberOfBeståndsgräns++;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
                else if (CheckFeatureName("Smal väg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Mindre väg 0.30", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Liten otydlig stig", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Körväg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Större väg 0.5", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Cykelväg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Väg: sidlinje", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Motorväg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Rågång/Drivningsväg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Cykelväg, sidlinje", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
                else if (CheckFeatureName("Järnväg", other))
                {
                    if (numberOfStig == 0)
                    {
                        stigTimer = CantMissTimer;
                        stigTimerActive = true;
                    }
                    numberOfStig++;
                    if (GetTime() < safetyTime * 2)
                    {
                        SetTime(safetyTime * 2);
                    }
                }
            }
            else
            {
                Debug.Log("Kolliderade med MapFeature, men MapFeature-komponenten saknas!");
            }
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("MapFeature"))
        {
            MapFeature mf = other.GetComponent<MapFeature>();
            if(CheckFeatureName("höjdkurva", other) && mf.length < 100)
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
            else if (CheckFeatureName("punkthöjd", other))
            {
                numberOfPunktHöjd--;
                
            }
            else if (CheckFeatureName("avlång punkthöjd", other))
            {
                numberOfPunktHöjd--;
                
            }
            else if (CheckFeatureName("Stor stig", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Liten stig", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Smal väg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Större bäck", other))
            {
                numberOfBäck--;
                if (numberOfBäck == 0)
                {
                    cantBäckMiss = false;
                    bäckTimer = 0;
                    bäckTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Passerbart litet vattendrag", other))
            {
                numberOfBäck--;
                if (numberOfBäck == 0)
                {
                    cantBäckMiss = false;
                    bäckTimer = 0;
                    bäckTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Mindre dike", other))
            {
                numberOfBäck--;
                if (numberOfBäck == 0)
                {
                    cantBäckMiss = false;
                    bäckTimer = 0;
                    bäckTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("beståndsgräns", other))
            {
                numberOfBeståndsgräns--;
                if (numberOfBeståndsgräns == 0)
                {
                    cantBeståndsgränsMiss = false;
                    beståndsgränsTimer = 0;
                    beståndsgränsTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Mindre väg 0.30", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Liten otydlig stig", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Körväg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Större väg 0.5", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Cykelväg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Väg: sidlinje", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Motorväg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Rågång/Drivningsväg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Cykelväg, sidlinje", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
            else if (CheckFeatureName("Järnväg", other))
            {
                numberOfStig--;
                if(numberOfStig == 0)
                {
                    cantStigMiss = false;
                    stigTimer = 0;
                    stigTimerActive = false;
                    if (GetTime() < safetyTime)
                    {
                        SetTime(safetyTime);
                    }
                }
            }
        }
    }
    /// <summary>
    /// Startar eller fyller på timern med ny tid.
    /// </summary>
    public void StartTimer(float duration)
    {
        timer = duration;
        timerActive = true;
    }

    /// <summary>
    /// Lägg till tid på timern om den redan körs.
    /// </summary>
    public void AddTime(float extraTime)
    {
        timer += extraTime;
        timerActive = true;
    }

    public void SetTime(float time)
    {
        timer = time;
        timerActive = true;
    }
    public float GetTime()
    {
        return timer;
    }

    public bool CheckTimerActive()
    {
        return timerActive;
    }


    /// <summary>
    /// Jämför om ett kolliderat MapFeature har samma namn som input-stringen,
    /// efter att ha tagit bort de 6 första tecknen och mellanslag.
    /// </summary>
    public bool CheckFeatureName(string objectName, Collider2D other)
    {
        MapFeature mf = GetFeature(other);
        if (mf == null) return false;

        // Namnet utan allt fram till första mellanslaget och utan mellanslag, jämfört skiftlägesokänsligt
        return mf.NameAfterFirstSpace.Equals(MapFeature.WithoutSpaces(objectName), System.StringComparison.OrdinalIgnoreCase);
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
