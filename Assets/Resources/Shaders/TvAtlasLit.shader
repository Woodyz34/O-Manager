// The trees, bushes, undergrowth, forest-floor litter and town walls of the 3D world with real pictures on them (2026-11-25):
// bark, needles, leaves, bilberry, grass, ferns, twigs (the forest atlas, TV/TvFlora.cs). Lit like TvVertexLit (its
// own sun, sky and ground light, ACES tone mapping, fog), so it sits in the same picture.
// Each corner carries u, v and the atlas tile in its first uv (x, y, z); tile -1 = no picture (the corner's colour only).
// Tiles 36-37 (slash, heather from above) are plants too, 38 churned soil is solid; 20-21 stone and cliff get their
// own colours and moss (StoneLife). Tiles 0-4 (and 35) are bark (a fraction on a bark tile number carries the tree's moss and lichen, TreeLife), 20-24 stone and wood, 25-31 walls, roofs and glass, 32 moss at a trunk's foot (lit as solid
// things, with relief); 5-19 and 33-35 are leaves and plants.
// u, v repeat inside the tile (bark round a trunk), so the picture is looked up with its own slopes (no seam lines).
// The leaves and plants are cut out by the picture's alpha and drawn from both sides; they are lit softly through
// (light wraps round a leaf), with the normal the builder gave them (out of the crown), so a crown is lit as a whole.
Shader "O-Manager/TvAtlasLit"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _BarkHD ("Sharp bark (3 x 2 tiles, High and Ultra)", 2D) = "white" {}
        _BarkHDOn ("Use the sharp bark", Float) = 0
        _Tiles ("Tiles per side", Float) = 5
        _Pad ("Padding share of a tile", Float) = 0.03125
        _Cutoff ("Alpha cut", Range(0,1)) = 0.5
        _Surface ("Surface grain strength", Range(0,1)) = 0.25
        _Wind ("Leaves moving in the wind", Range(0,1)) = 1
        _Bump ("Relief of bark, stone and walls (m)", Range(0,0.05)) = 0.012
        _BumpFar ("Relief out to (m); 0 = none (Low)", Float) = 30
        _FogStart ("Fog start (m)", Float) = 45
        _FogEnd ("Fog end (m)", Float) = 230
        _Fade ("Cross-fade (1 = shown)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Tv vertex:tvVert finalcolor:tvFinal addshadow alphatest:_Cutoff noambient novertexlights noforwardadd nofog nolightmap
        #pragma target 3.0
        // The run-yourself world draws its trees as many copies of a few (RunTrees, Graphics.RenderMeshInstanced)
        #pragma multi_compile_instancing

        #define TV_SUN half3(1.75, 1.54, 1.18)
        #define TV_SKY_LIGHT half3(0.592, 0.708, 0.812)
        #define TV_GROUND_LIGHT half3(0.078, 0.070, 0.038)
        #define TV_FOG half3(0.571, 0.672, 0.701)

        sampler2D _MainTex;
        sampler2D _BarkHD;
        float _BarkHDOn;
        float _Tiles;
        float _Pad;
        half _Surface;
        half _Wind;
        float _Bump;
        float _BumpFar;
        float _FogStart;
        float _FogEnd;
        float _Fade;
        // The trees' levels of detail (RunTrees, 2026-11-26): x where this level starts, y over how many metres
        // before that it comes in, z where it ends, w over how many metres before that it goes (0 = no such end);
        // _TvLodCenter.xy: the runner, from whom the distances are counted
        float4 _TvLod;
        float4 _TvLodCenter;
        // The undergrowth thinned out with distance (RunWorld, 2026-11-26, Anders: Ultra lagged): all of it up to x
        // metres from the runner, only the share z of it from y metres on; w > 0.5: on. Each tuft has its own number
        // 0..1 in the fourth uv (TvMesh.ranks; 0: never thinned) and shows while it is below the share there.
        float4 _TvThin;
        // The far trees thinned out (RunTrees, 2026-11-26): all up to x metres, the share z of them from y on; w > 0.5: on
        float4 _TvFarThin;

        // A tree's own number 0..1 from where it stands (the same sums as RunTrees.Rank)
        float TvRank (float2 p)
        {
            float3 q = frac(float3(p.x * 0.1031, p.y * 0.1031, p.x * 0.0973));
            float d = q.x * (q.y + 33.33) + q.y * (q.z + 33.33) + q.z * (q.x + 33.33);
            q += d;
            return frac((q.x + q.y) * q.z);
        }

        struct Input
        {
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            float tvDepth;
            float4 atlas;      // xyz: u, v, tile; w: the cross-fade of this corner's tree or piece (as _Fade)
        };

        struct SurfaceOutputTv
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;
            half Specular;
            fixed Gloss;
            fixed Alpha;
            half Foliage;
            half3 WNormal;     // the world normal with the picture's relief on it
        };

        void tvVert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.atlas = float4(v.texcoord.xyz, _Fade);
            // A tree near the change to its next level: on its share of the pixels only (no pops)
            if (_TvLod.x > 0.001 || _TvLod.z > 0.001)
            {
                float2 root = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xz;
                float d = distance(root, _TvLodCenter.xy);
                float going = _TvLod.z > 0.001 ? saturate((d - (_TvLod.z - _TvLod.w)) / max(_TvLod.w, 0.01)) : 0.0;
                float coming = _TvLod.x > 0.001 ? saturate((d - (_TvLod.x - _TvLod.y)) / max(_TvLod.y, 0.01)) : 1.0;
                o.atlas.w = going > 0.0 ? -1.0 - going : coming;
                if (_TvFarThin.w > 0.5 && d > _TvFarThin.x && going <= 0.0)
                {
                    float share = lerp(1.0, _TvFarThin.z, saturate((d - _TvFarThin.x) / max(_TvFarThin.y - _TvFarThin.x, 1.0)));
                    o.atlas.w *= saturate((share - TvRank(root)) / 0.1);
                }
            }
            // Thinned undergrowth: a tuft past its share fades out on its share of the pixels, and one well past it is
            // folded to a point, so the graphics card draws nothing for it
            // (a plant drawn as a copy, RunTufts, has -1 there: its number comes from where the copy stands)
            float tuftRank = v.texcoord.w < -0.5 ? max(TvRank(mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xz), 0.002) : v.texcoord.w;
            if (_TvThin.w > 0.5 && tuftRank > 0.0005)
            {
                float2 wp = mul(unity_ObjectToWorld, v.vertex).xz;
                float d = distance(wp, _TvLodCenter.xy);
                float share = lerp(1.0, _TvThin.z, saturate((d - _TvThin.x) / max(_TvThin.y - _TvThin.x, 0.01)));
                float room = share - tuftRank;
                if (room < -0.04)
                {
                    v.vertex = float4(0, -1000, 0, 1);
                }
                else
                {
                    float keep = saturate(room / 0.05);
                    o.atlas.w = o.atlas.w >= 0.0 ? o.atlas.w * keep : o.atlas.w;
                    if (keep <= 0.0) o.atlas.w = 0.0;
                }
            }
            // Leaves and needles stir a little in the wind: more at the tip of a card (v near 1) than at its base
            // (not the cards lying flat on the ground, litter and leaf tops: 2026-11-25, Anders: the litter moved)
            if (_Wind > 0.001 && abs(v.normal.y) < 0.85 && ((v.texcoord.z >= 4.5 && v.texcoord.z < 19.5) || (v.texcoord.z >= 32.5 && v.texcoord.z < 34.5) || (v.texcoord.z >= 36.5 && v.texcoord.z < 37.5) || (v.texcoord.z >= 39.5 && v.texcoord.z < 40.5)))
            {
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float tip = saturate(v.texcoord.y + 0.25);
                float t = _Time.y;
                float sway = sin(t * 1.7 + w.x * 0.35 + w.z * 0.27) * 0.6 + sin(t * 3.1 + w.x * 1.3 - w.z * 0.9) * 0.4;
                v.vertex.xyz += mul((float3x3)unity_WorldToObject, float3(sway, sway * 0.4, sway * 0.7)) * 0.018 * tip * _Wind;
            }
            o.tvDepth = -UnityObjectToViewPos(v.vertex).z;
        }

        half4 LightingTv (SurfaceOutputTv s, half3 lightDir, half atten)
        {
            half d = dot(s.WNormal, lightDir);
            // Leaves: light wraps round them and comes through them
            half nl = s.Foliage > 0.5 ? saturate((d + 0.45) / 1.45) : saturate(d);
            half4 c;
            c.rgb = s.Albedo * TV_SUN * (nl * atten);
            c.a = s.Alpha;
            return c;
        }

        float TvHash (float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float TvNoise (float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = TvHash(i), b = TvHash(i + float3(1, 0, 0)), c = TvHash(i + float3(0, 1, 0)), d = TvHash(i + float3(1, 1, 0));
            float e = TvHash(i + float3(0, 0, 1)), g = TvHash(i + float3(1, 0, 1)), h = TvHash(i + float3(0, 1, 1)), k = TvHash(i + float3(1, 1, 1));
            return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y), lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
        }

        // Fine noise that fades to its middle when its spots get smaller than about two pixels (fw = metres per
        // pixel), so it never sparkles into white and dark specks (2026-11-25, Anders: specks on the trunks)
        float TvNoiseAA (float3 p, float fw)
        {
            return lerp(0.5, TvNoise(p), saturate(2.0 - fw * 2.5));
        }

        half TvSurfaceGrain (float3 p, float depth)
        {
            float fine = saturate(1.0 - depth / 40.0), finest = saturate(1.0 - depth / 14.0);
            float big = TvNoise(p * 0.83) * 0.6 + TvNoise(p * 2.31 + 17.3) * 0.4;
            float small = (TvNoise(p * 6.17 + 41.9) - 0.5) * 0.55 * fine + (TvNoise(p * 15.3 + 7.7) - 0.5) * 0.45 * finest;
            return (big - 0.5) * 0.06 + small * 0.4 + 0.02;
        }

        // A tile's picture at u, v (repeating inside the tile). Bark on High and Ultra comes from the sharp bark atlas
        // (2026-11-25, Anders: the trees looked pixly up close): the same pictures at twice the size, 3 x 2 tiles in
        // the order 0-4, 35, the same padding share.
        half4 TvTile (float tile, float2 uv, float2 duvx, float2 duvy)
        {
            float inner = 1.0 - 2.0 * _Pad;
            float2 f = frac(uv);
            bool hd = _BarkHDOn > 0.5 && ((tile < 4.5 && tile > -0.5) || (tile > 34.5 && tile < 35.5));
            if (hd)
            {
                float k = tile > 4.5 ? 5.0 : tile;
                float2 cellH = float2(fmod(k, 3.0), floor(k / 3.0));
                float2 per = float2(3.0, 2.0);
                float2 topH = (cellH + _Pad + float2(f.x, 1.0 - f.y) * inner) / per;
                return tex2Dgrad(_BarkHD, float2(topH.x, 1.0 - topH.y), duvx * inner / per, duvy * inner / per);
            }
            float2 cell = float2(fmod(max(tile, 0.0), _Tiles), floor(max(tile, 0.0) / _Tiles));
            float2 fromTop = float2(cell.x + _Pad + f.x * inner, cell.y + _Pad + (1.0 - f.y) * inner) / _Tiles;
            return tex2Dgrad(_MainTex, float2(fromTop.x, 1.0 - fromTop.y), duvx * inner / _Tiles, duvy * inner / _Tiles);
        }

        void surf (Input IN, inout SurfaceOutputTv o)
        {
            // Cross-fade between two levels of detail of a piece (RunWorld, 2026-11-26): the coming one shows on a share
            // _Fade (0..1) of the pixels, the going one (_Fade -1..-2) on exactly the other pixels, so nothing pops
            float fade = IN.atlas.w;
            if (fade < 0.999)
            {
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(IN.worldPos, 1.0));
                float2 px = floor((clipPos.xy / max(abs(clipPos.w), 1e-5) * 0.5 + 0.5) * _ScreenParams.xy);
                float dither = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
                clip(fade >= 0 ? fade - dither : dither + fade + 1.0);
            }
            float3 c = IN.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            // The tile's picture: u, v repeat inside the tile's inner part (its padding keeps the neighbours out)
            float tile = floor(IN.atlas.z + 0.5);
            float2 duvx = ddx(IN.atlas.xy), duvy = ddy(IN.atlas.xy);
            float fw = length(fwidth(IN.worldPos));
            half4 tex = TvTile(tile, IN.atlas.xy, duvx, duvy);
            // Stones and cliffs never show the picture's repeat (2026-11-25, Anders: a pattern of black lines on the
            // cliffs; every part of every 3D object its own): the same picture at another scale and place is blended
            // in by a slow noise of the world position, so no two metres of rock look alike
            if (tile > 19.5 && tile < 21.5)
            {
                half4 tex2 = TvTile(tile, IN.atlas.xy * 0.41 + float2(0.37, 0.71), duvx * 0.41, duvy * 0.41);
                float w = smoothstep(0.3, 0.7, TvNoise(IN.worldPos * 0.23 + 3.0));
                tex.rgb = lerp(tex.rgb, tex2.rgb, w);
            }
            if (tile < -0.5) tex = half4(1, 1, 1, 1);
            c *= tex.rgb;
            // A tree's own life on its bark (TreeLife in TV/TvMesh.cs): damp streaks, moss up the foot on its damp
            // side, pale crust lichen in patches, all from the tree's own place in the world so no two look alike
            float code = IN.atlas.z - tile;
            bool bark = (tile < 4.5 && tile > -0.5) || (tile > 34.5 && tile < 35.5);
            if (bark)
            {
                float3 wp = IN.worldPos;
                c *= 0.84 + 0.32 * TvNoise(float3(wp.x * 5.0, wp.y * 0.45, wp.z * 5.0));
                if (code > 0.01)
                {
                    float a = (code - 0.02) / 0.47;
                    float li = floor(a * 4.0 + 0.02);
                    float moss = saturate((a - li * 0.25) / 0.24);
                    float n = TvNoise(wp * 2.3) * 0.5 + TvNoise(wp * 7.1 + 3.1) * 0.33 + TvNoiseAA(wp * 21.0 + 9.7, fw * 21.0) * 0.17;
                    float th = 1.02 - min(moss, 0.8) * 0.75;   // never the whole trunk green
                    float mm = moss > 0.02 ? smoothstep(th - 0.05, th + 0.05, n) : 0.0;
                    float3 mc = lerp(float3(0.40, 0.52, 0.14), float3(0.64, 0.74, 0.26), TvNoiseAA(wp * 37.0, fw * 37.0));
                    mc *= 0.7 + 0.45 * TvNoiseAA(wp * 90.0, fw * 90.0);
                    mc = mc * mc;
                    float ln = TvNoise(wp * 9.0 + 11.0) * 0.6 + TvNoiseAA(wp * 26.0 + 5.0, fw * 26.0) * 0.4;
                    float lm = li > 1.5 ? smoothstep(0.68 - li * 0.03, 0.72 - li * 0.03, ln) * (0.12 + 0.08 * li) : 0.0;
                    float3 lc = float3(0.6, 0.62, 0.56) * (0.8 + 0.3 * TvNoiseAA(wp * 50.0, fw * 50.0));
                    lc = lc * lc;
                    c = lerp(c, lc, lm * 0.85);
                    // A birch's foot: black, cracked up and down
                    if (tile > 2.5 && tile < 3.5)
                    {
                        float cr = TvNoise(float3(wp.x * 14.0, wp.y * 1.6, wp.z * 14.0)) * 0.7 + TvNoiseAA(wp * 40.0, fw * 40.0) * 0.3;
                        float bm = smoothstep(1.0 - moss * 0.9, 1.12 - moss * 0.9, n * 0.6 + cr * 0.4 + 0.05);
                        float3 bc = float3(0.05, 0.045, 0.04) * (0.5 + 1.5 * cr);
                        c = lerp(c, bc, bm);
                    }
                    else c = lerp(c, mc, mm);
                }
            }
            // Stones and cliffs alive (2026-11-25, Anders: layers, cracks, different colours, moss on the stones): big
            // patches of warmer, colder and darker rock from the world position, dark streaks running down a cliff,
            // and moss where the rock faces up (a stone's own amount rides in its tile fraction, StoneLife; a cliff
            // has some everywhere)
            if (tile > 19.5 && tile < 21.5)
            {
                float3 wp = IN.worldPos;
                float3 wn = normalize(IN.worldNormal);
                c *= 0.84 + 0.3 * TvNoise(wp * 0.35 + 5.0);
                c = lerp(c, c * float3(1.14, 0.98, 0.88), smoothstep(0.55, 0.75, TvNoise(wp * 0.6 + 17.0)));
                c = lerp(c, c * float3(0.88, 0.9, 0.93), smoothstep(0.6, 0.8, TvNoise(wp * 0.45 + 40.0)));
                if (tile > 20.5)
                {
                    c *= 0.94 + 0.1 * smoothstep(0.25, 0.7, TvNoise(float3(wp.x * 2.2, wp.y * 0.22, wp.z * 2.2)));
                    c = lerp(c, c * float3(1.2, 0.95, 0.75), smoothstep(0.74, 0.86, TvNoise(float3(wp.x * 1.3, wp.y * 0.3, wp.z * 1.3) + 9.0)) * 0.15);
                }
                float mossAmt = code > 0.01 ? saturate((code - 0.02) / 0.47) : (tile > 20.5 ? 0.45 : 0.0);
                float n3 = TvNoise(wp * 2.1) * 0.55 + TvNoise(wp * 6.3 + 2.0) * 0.3 + TvNoiseAA(wp * 19.0 + 7.0, fw * 19.0) * 0.15;
                float upf = saturate((wn.y - 0.1) * 1.4);
                float m = upf * (0.35 + mossAmt * 0.7) + (n3 - 0.5) * 0.8 + mossAmt * 0.25 - 0.32;
                float mm = mossAmt > 0.01 ? smoothstep(0.0, 0.08, m) : 0.0;
                float3 mc = lerp(float3(0.36, 0.48, 0.13), float3(0.62, 0.72, 0.24), TvNoiseAA(wp * 31.0, fw * 31.0));
                mc *= 0.7 + 0.45 * TvNoiseAA(wp * 80.0, fw * 80.0);
                mc = mc * mc;
                // pale crust lichen in big patches where there is no moss (2026-11-25, Anders' photos: white parts)
                float ln2 = TvNoise(wp * 1.3 + 21.0) * 0.65 + TvNoise(wp * 4.1 + 6.0) * 0.35;
                float lw = smoothstep(0.6, 0.66, ln2) * (1.0 - mm);
                c = lerp(c, float3(0.6, 0.61, 0.53) * (0.9 + 0.2 * TvNoise(wp * 9.0)), lw * 0.4);
                c = lerp(c, mc, mm);
            }
            if (_Surface > 0.001)
            {
                half v = TvSurfaceGrain(IN.worldPos, IN.tvDepth);
                c *= 1.0 + v * 0.75 * _Surface;
            }
            o.Albedo = c;
            o.Foliage = (tile >= 4.5 && tile < 19.5) || (tile >= 32.5 && tile < 34.5) || (tile >= 35.5 && tile < 37.5) || (tile >= 39.5 && tile < 40.5) ? 1.0 : 0.0;
            // Relief (2026-11-25, Anders: the bark looked flat, like a picture): the picture's light and dark read
            // as height, bent into the normal from the screen's own slopes (no tangents needed); fades out far away
            float3 n = normalize(IN.worldNormal);
            bool solid = tile > -0.5 && (tile < 4.5 || (tile > 19.5 && tile < 32.5) || (tile > 34.5 && tile < 35.5) || (tile > 37.5 && tile < 39.5) || tile > 40.5);
            float bump = solid && _BumpFar > 0.5 ? _Bump * saturate(1.0 - IN.tvDepth / _BumpFar) : 0.0;
            if (bump > 0.0)
            {
                // (2026-11-25, Anders: still specks on the trunks) the relief from the picture a little blurred (a mip
                // level or two up), so only real cracks and plates stand out, not each texel's grain
                float2 bx = duvx * 3.0, by = duvy * 3.0;
                float hgt = dot(TvTile(tile, IN.atlas.xy, bx, by).rgb, float3(0.3, 0.5, 0.2));
                float3 dpx = ddx(IN.worldPos), dpy = ddy(IN.worldPos);
                // the picture's slope from two more looks one pixel over, not ddx/ddy of the height (those are the
                // same for 2 x 2 pixels, which showed as square specks up close)
                float hx = dot(TvTile(tile, IN.atlas.xy + duvx * 2.0, bx, by).rgb, float3(0.3, 0.5, 0.2));
                float hy = dot(TvTile(tile, IN.atlas.xy + duvy * 2.0, bx, by).rgb, float3(0.3, 0.5, 0.2));
                float dhx = (hx - hgt) * 0.5, dhy = (hy - hgt) * 0.5;
                float3 r1 = cross(dpy, n), r2 = cross(n, dpx);
                float det = dot(dpx, r1);
                float3 grad = sign(det) * (dhx * r1 + dhy * r2);
                n = normalize(abs(det) * n - grad * bump);
            }
            o.WNormal = n;
            half up = n.y * 0.5 + 0.5;
            o.Emission = c * lerp(TV_GROUND_LIGHT, TV_SKY_LIGHT, up);
            o.Alpha = tex.a;
        }

        float3 TvFit (float3 v)
        {
            float3 a = v * (v + 0.0245786) - 0.000090537;
            float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
            return a / b;
        }

        float3 TvTone (float3 c)
        {
            c *= 1.05 / 0.6;
            float3 i = float3(
                dot(c, float3(0.59719, 0.35458, 0.04823)),
                dot(c, float3(0.07600, 0.90834, 0.01566)),
                dot(c, float3(0.02840, 0.13383, 0.83777)));
            i = TvFit(i);
            float3 o = float3(
                dot(i, float3(1.60475, -0.53108, -0.07367)),
                dot(i, float3(-0.10208, 1.10813, -0.00605)),
                dot(i, float3(-0.00327, -0.07276, 1.07602)));
            return saturate(o);
        }

        void tvFinal (Input IN, SurfaceOutputTv o, inout fixed4 color)
        {
            float3 c = TvTone(max(color.rgb, 0.0));
            float fog = smoothstep(_FogStart, _FogEnd, IN.tvDepth);
            color.rgb = lerp(c, TV_FOG, fog);
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
