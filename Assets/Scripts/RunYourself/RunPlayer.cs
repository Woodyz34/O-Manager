using System;
using System.Collections.Generic;
using UnityEngine;

// The runner the player is, seen through their own eyes (run-yourself game mode, RunSession).
// W A S D run (always flat out; slower with the map up), the mouse turns the head,
// Space jumps (stone walls, fences and fallen trunks: run into one without jumping and you fall).
// The speed follows the ground (RunTerrain, with the map's own terrain sliders) and the slope (the game's slope model,
// TerrainSpeedModel), and the whole game runs RunSettings.GameSpeed times quicker than real life.
// The map in the hand (RunMap) shakes with Shake: more the quicker the runner goes, downhill and on rough ground,
// less uphill and on a road. A tired runner (later in the race) stumbles more easily, above all fast downhill in
// rough ground or a thicket, and sometimes falls.
public class RunPlayer : MonoBehaviour
{
    // Real-life running speed (m/s) in white forest at the middle of every terrain slider: 3:58 min/km
    // (2026-11-19: was 3.4, then 3.8; Anders's courses still felt slow, he expects about 6 min/km over a whole course)
    public const float ForestSpeed = 4.2f;
    const float Reach = 0.3f;            // half the runner's width
    const float EyeHeight = 1.68f;

    public RunWorld world;
    public RunTerrain terrain;
    public RunSounds sounds;
    public Camera cam;

    public double e, n;                  // where the runner is, on the map
    public float yaw;                    // degrees; 0 = north, 90 = east
    public float pitch;                  // degrees; positive = looking down
    public float speed;                  // real-life m/s; the runner moves GameSpeed times this
    public float gameSpeed = 3f;
    public bool controlsOn;              // off before the start and after the finish
    public bool mapUp;                   // RunMap: the map is held up (a little slower)
    public bool lookHeld;
    public bool inputOff;                // a menu or window is open: no keys                // RunMap: the mouse moves the map, not the head
    public float raceSeconds;            // the race clock (real-life seconds), kept by RunSession
    public float distance;               // metres run

    public RunTerrain.Ground ground;
    public float gradient;               // rise per metre in the way the runner moves
    public float fatigue;                // 0 fresh .. 1 tired (late in the race)
    public float Shake { get; private set; }
    public float StepPhase { get; private set; }
    public float Jolt;                   // a knock to the map hand (landing, stumbling); RunMap lets it die away
    public float Effort { get; private set; }
    public bool Fallen => fallTimer > 0f;
    public bool Airborne => airborne;
    // Watching another runner after the finish (RunSpectate): the place and head are set from outside, nothing moves
    // by itself; the eyes bob with the other runner's speed
    public bool puppet;
    public float puppetSpeed;
    public bool puppetFallen;

    // Mass start: where the other runners are (map metres), so nobody runs through anyone
    public readonly List<Vector2> others = new List<Vector2>();
    const float BodyReach = 0.32f;

    // Called for every bit of the way the runner moves, so the punching units can be checked along it
    public Action<double, double, double, double> Moved;

    float moveDirX, moveDirZ = 1f;
    float jumpY, jumpV;
    bool airborne, manualJump;
    float fallTimer, fallLength = 1f;
    float stumbleTimer;
    float lastStep;
    System.Random random;

    public static RunPlayer Create(RunWorld world, RunTerrain terrain, RunSounds sounds, double e, double n, float yaw, int seed)
    {
        RunPlayer p = new GameObject("Run player").AddComponent<RunPlayer>();
        p.world = world;
        p.terrain = terrain;
        p.sounds = sounds;
        p.e = e;
        p.n = n;
        p.yaw = yaw;
        p.random = new System.Random(seed);
        p.gameSpeed = RunSettings.GameSpeed;

        p.cam = new GameObject("Run camera").AddComponent<Camera>();
        p.cam.transform.SetParent(p.transform, false);
        p.cam.clearFlags = CameraClearFlags.SolidColor;
        p.cam.backgroundColor = RunWorld.SkyColor;
        p.cam.nearClipPlane = 0.12f;
        p.cam.farClipPlane = world.ViewDistance + 20f;
        p.cam.fieldOfView = 62f;
        p.cam.depth = 5;
        p.cam.allowHDR = false;
        p.cam.allowMSAA = world.Msaa > 1;
        p.cam.renderingPath = RenderingPath.Forward;
        p.cam.useOcclusionCulling = false;
        p.PlaceCamera(0f);
        return p;
    }

    // ---- Every frame (RunSession calls it) ----

    public void Tick(float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        ground = terrain.At(e, n);
        fatigue = Mathf.Clamp01((raceSeconds / 60f - 6f) / 45f);
        if (puppet)
        {
            speed = puppetSpeed;
            airborne = false;
            jumpY = 0f;
            ShakeAndBreath(dt);
            PlaceCamera(dt);
            world.SetPlayer(e, n);
            return;
        }

        Look();
        Move(dt);
        Vertical(dt);
        Stumbling(dt);
        ShakeAndBreath(dt);
        PlaceCamera(dt);
        world.SetPlayer(e, n);
    }

    void Look()
    {
        if (Cursor.lockState != CursorLockMode.Locked || lookHeld || Fallen) return;
        float s = RunSettings.MouseSpeed;
        yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * s, 360f);
        pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * s, -75f, 80f);
    }

    void Move(float dt)
    {
        int fwd = 0, side = 0;
        bool keys = controlsOn && !Fallen && !inputOff;
        if (keys)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) fwd++;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) fwd--;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) side++;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) side--;
        }
        bool wish = fwd != 0 || side != 0;
        if (wish)
        {
            float a = (yaw + Mathf.Atan2(side, fwd) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            moveDirX = Mathf.Sin(a);
            moveDirZ = Mathf.Cos(a);
        }

        // The slope the way the runner goes, over 6 m so bumps and the steps of cliffs don't count
        float y1 = world.GroundY(e + moveDirX * 3.0, n + moveDirZ * 3.0), y0 = world.GroundY(e - moveDirX * 3.0, n - moveDirZ * 3.0);
        gradient = Mathf.Lerp(gradient, Mathf.Clamp((y1 - y0) / 6f / RunSettings.Hills, -1.2f, 1.2f), 0.25f);   // the real slope: the hills are drawn higher (RunSettings.Hills) to be seen

        float target = 0f;
        if (wish)
        {
            float dirFactor = fwd > 0 ? (side != 0 ? 0.95f : 1f) : fwd < 0 ? 0.45f : 0.6f;
            target = ForestSpeed * GroundSpeed(ground, gradient) * dirFactor
                     * (mapUp ? 0.6f : 1f);   // reading the map: you can't run fast and read (Anders, 2026-11-19)
            if (stumbleTimer > 0f) target *= 0.4f;
        }
        float rate = target > speed ? 6f : 11f;     // real-life m/s gained or lost per (real) second
        speed = Mathf.MoveTowards(speed, target, rate * dt);
        if (Fallen) speed = 0f;
        if (speed < 0.01f) return;

        // The way in small steps, so nothing thin is jumped over between two frames
        float total = speed * gameSpeed * dt;
        int parts = Mathf.Max(1, Mathf.CeilToInt(total / 0.35f));
        float step = total / parts;
        for (int i = 0; i < parts && !Fallen; i++)
        {
            double ne = e + moveDirX * step, nn = n + moveDirZ * step;
            if (terrain.Crosses(e, n, ne, nn, out float hurdle, out bool wall, out bool smallCliff))
            {
                if (smallCliff)
                {
                    // A passable cliff: down it is a jump and a hard landing (fast, you may fall), up it is a slow
                    // climb, safer than going down (2026-11-19, Anders)
                    float drop = world.GroundY(e, n) - world.GroundY(ne + moveDirX * 0.8, nn + moveDirZ * 0.8);
                    if (drop > 0.6f)
                    {
                        Jolt += 1.4f;
                        sounds.Land(ground.surface);
                        if (speed > ForestSpeed * 0.7f && random.NextDouble() < 0.2 + 0.3 * fatigue)
                        {
                            Fall(0.9f);
                            break;
                        }
                        speed *= 0.6f;
                    }
                    else if (drop < -0.6f)
                    {
                        Jolt += 0.5f;
                        speed = Mathf.Min(speed, ForestSpeed * 0.2f);
                    }
                }
                if (wall)
                {
                    speed *= 0.3f;
                    break;
                }
                if (jumpY < hurdle * 0.55f)
                {
                    if (speed > ForestSpeed * 0.35f)
                    {
                        // Without Space the runner jumps by itself, but may catch a foot and fall; Space is always
                        // clean (2026-11-19, Anders: at 4 x speed it was too hard to time)
                        if (!manualJump && random.NextDouble() < 0.1 + 0.15 * fatigue + 0.1 * Mathf.Clamp01(hurdle - 0.6f))
                        {
                            Fall(0.9f);
                            break;
                        }
                        if (!airborne) speed *= 0.9f;
                        Hop(hurdle);
                    }
                    else
                    {
                        // Slowly: climbs over
                        Jolt += 0.3f;
                        speed = Mathf.Min(speed, ForestSpeed * 0.25f);
                    }
                }
            }
            if (terrain.Blocked(ne, nn, Reach))
            {
                // Slide along whatever is in the way
                if (!terrain.Blocked(ne, n, Reach)) nn = n;
                else if (!terrain.Blocked(e, nn, Reach)) ne = e;
                else
                {
                    speed *= 0.3f;
                    break;
                }
            }
            world.PushOut(ref ne, ref nn, Reach);
            foreach (Vector2 o in others)
            {
                double ox = ne - o.x, oz = nn - o.y, d2 = ox * ox + oz * oz, r = BodyReach * 2f;
                if (d2 >= r * r) continue;
                double d = Math.Sqrt(d2);
                if (d < 1e-4) { ox = moveDirZ; oz = -moveDirX; d = 1; }
                ne = o.x + ox / d * r;
                nn = o.y + oz / d * r;
            }
            if (terrain.Blocked(ne, nn, Reach)) break;
            double de = ne - e, dn = nn - n;
            double pe = e, pn = n;
            e = ne;
            n = nn;
            distance += (float)Math.Sqrt(de * de + dn * dn);
            Moved?.Invoke(pe, pn, e, n);
        }
    }

    // Speed compared with flat ground on a slope (positive = uphill). Uphill from the energy it costs (Minetti); downhill
    // a little quicker on a gentle slope, then slower the steeper it is, more so in rough ground, but never a crawl:
    // a steep clear-cut slope downhill is still run at more than half speed (2026-11-19, Anders' test).
    // The ground's speed with the slope: on stairs as quick as a road going up, but slow going down
    // (SymbolRules.StairsDownSpeed, whatever the slope; the same rule as the 2D grid's StairsSlopes)
    public static float GroundSpeed(RunTerrain.Ground g, float gradient)
    {
        if (g.stairs && gradient < -0.008f) return g.speed * (MapImport.SymbolRules.StairsDownSpeed / MapImport.SymbolRules.StairsSpeed);
        return g.speed * SlopeFactor(gradient, g.rough);
    }

    public static float SlopeFactor(float gradient, float rough)
    {
        if (gradient >= 0f) return Mathf.Max(0.3f, TerrainSpeedModel.EnergyCost(0f) / TerrainSpeedModel.EnergyCost(Mathf.Min(gradient, 0.45f)));
        float down = -gradient;
        if (down <= 0.08f) return 1f + down;                              // up to 8 % quicker
        if (down <= 0.18f) return 1.08f - (down - 0.08f) * 0.8f;          // back to flat speed at 18 %
        float steep = (down - 0.18f) * (1.1f + 0.5f * Mathf.Max(0f, rough - 1f));
        return Mathf.Max(0.45f, 1f - steep);
    }

    // A jump by itself over something in the way: just high enough
    void Hop(float hurdle)
    {
        if (airborne) return;
        airborne = true;
        jumpV = Mathf.Sqrt(2f * 9.81f * Mathf.Max(0.3f, hurdle * 0.6f));
        jumpY = 0.01f;
    }

    void Vertical(float dt)
    {
        if (controlsOn && !Fallen && !airborne && !inputOff && Input.GetKeyDown(KeyCode.Space))
        {
            airborne = true;
            manualJump = true;
            jumpV = 3.7f;
        }
        if (!airborne) return;
        jumpV -= 9.81f * dt;
        jumpY += jumpV * dt;
        if (jumpY <= 0f)
        {
            jumpY = 0f;
            airborne = false;
            manualJump = false;
            Jolt += 0.7f;
            sounds.Land(ground.surface);
        }
    }

    public void Fall(float seconds)
    {
        if (Fallen) return;
        fallTimer = fallLength = seconds;
        speed = 0f;
        airborne = false;
        jumpY = 0f;
        Jolt += 2.5f;
        sounds.Groan();
    }

    // Stumbling: almost never early in the race; tired, fast, downhill and in rough ground far more often
    void Stumbling(float dt)
    {
        if (fallTimer > 0f) fallTimer -= dt;
        if (stumbleTimer > 0f) stumbleTimer -= dt;
        if (!controlsOn || Fallen || airborne || stumbleTimer > 0f) return;
        float tired = 0.03f + fatigue;
        float down = 1f + Mathf.Clamp(-gradient, 0f, 0.5f) * 10f;
        float rough = ground.rough * ground.rough;
        float fast = Mathf.Pow(Mathf.Clamp01(speed / (ForestSpeed * 1.1f)), 3f);
        float perSecond = 0.02f * tired * down * rough * fast;
        if (random.NextDouble() >= perSecond * dt) return;
        float fallChance = 0.2f + 0.45f * Mathf.Clamp01(-gradient * 4f) * fatigue + 0.15f * Mathf.Clamp01(ground.rough - 1f);
        if (random.NextDouble() < fallChance)
        {
            Fall(0.8f + (float)random.NextDouble() * 0.3f);   // quick (2026-11-19, Anders: the fall took too long)
            return;
        }
        stumbleTimer = 0.5f;
        speed *= 0.45f;
        Jolt += 1.6f;
        sounds.Grunt();
    }

    void ShakeAndBreath(float dt)
    {
        float sf = Mathf.Clamp(speed / (ForestSpeed * 1.25f), 0f, 1.3f);
        float down = 1f + Mathf.Clamp(-gradient, 0f, 0.45f) * 4.5f;
        float up = 1f - Mathf.Clamp(gradient, 0f, 0.3f) * 1.6f;
        float target = speed > 0.2f ? (0.08f + Mathf.Pow(sf, 1.4f)) * down * up * ground.rough * (1f + 0.35f * fatigue) : 0f;   // standing: still hands
        if (airborne) target *= 0.6f;
        Shake = Mathf.Lerp(Shake, target, 1f - Mathf.Exp(-dt * 6f));
        Jolt = Mathf.Max(0f, Jolt - dt * 3f);

        // Steps: quicker the faster the runner goes
        if (!airborne && speed > 0.3f && !Fallen)
        {
            float cadence = (2.3f + 1.2f * Mathf.Clamp01(sf)) * (0.7f + 0.1f * gameSpeed);
            StepPhase += cadence * dt;
            float steps = Mathf.Floor(StepPhase * 2f);
            if (steps != lastStep)
            {
                lastStep = steps;
                sounds.Footstep(ground.surface, sf);
            }
        }

        float moving = speed > 0.3f ? 1f : 0f;
        float effortTarget = 0.18f + moving * (0.3f + 0.45f * sf + 2.5f * Mathf.Max(0f, gradient)) + 0.3f * fatigue;
        Effort = Mathf.MoveTowards(Effort, Mathf.Clamp01(effortTarget), dt * (effortTarget > Effort ? 0.25f : 0.08f));
        // No breathing sounds (2026-11-19, Anders: the breathing sounded like groaning). Effort is still kept.
    }

    // ---- The eyes ----

    void PlaceCamera(float dt)
    {
        float groundY = world.GroundY(e, n);
        float sf = Mathf.Clamp01(speed / (ForestSpeed * 1.25f));
        float bob = airborne ? 0f : (Mathf.Abs(Mathf.Sin(Mathf.PI * StepPhase * 2f)) - 0.5f) * 0.055f * sf;
        float sway = Mathf.Sin(Mathf.PI * StepPhase * 2f) * 0.6f * sf;
        float height = EyeHeight + jumpY + bob, extraPitch = 0f, roll = sway;
        if (Fallen)
        {
            // Down on the ground, then up again over the last 0.35 s
            float t = 1f - fallTimer / fallLength;
            float down = t < 0.15f ? t / 0.15f : fallTimer < 0.35f ? fallTimer / 0.35f : 1f;
            down = Mathf.SmoothStep(0f, 1f, down);
            height = Mathf.Lerp(EyeHeight, 0.5f, down);
            extraPitch = 50f * down;
            roll = 22f * down;
        }
        else if (puppet && puppetFallen)
        {
            height = 0.5f;
            extraPitch = 50f;
            roll = 22f;
        }
        else if (stumbleTimer > 0f)
        {
            float k = Mathf.Sin(Mathf.PI * (1f - stumbleTimer / 0.5f));
            height -= 0.18f * k;
            extraPitch = 14f * k;
            roll = 6f * k;
        }
        transform.position = world.ToScene(e, n, groundY + height);
        cam.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch + extraPitch, -80f, 89f), yaw, -roll);
    }
}
