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
        _Tiles ("Tiles per side", Float) = 5
        _Pad ("Padding share of a tile", Float) = 0.03125
        _Cutoff ("Alpha cut", Range(0,1)) = 0.5
        _Surface ("Surface grain strength", Range(0,1)) = 0.25
        _Wind ("Leaves moving in the wind", Range(0,1)) = 1
        _Bump ("Relief of bark, stone and walls (m)", Range(0,0.05)) = 0.012
        _FogStart ("Fog start (m)", Float) = 45
        _FogEnd ("Fog end (m)", Float) = 230
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Tv vertex:tvVert finalcolor:tvFinal addshadow alphatest:_Cutoff noambient novertexlights noforwardadd nofog nolightmap
        #pragma target 3.0

        #define TV_SUN half3(1.75, 1.54, 1.18)
        #define TV_SKY_LIGHT half3(0.592, 0.708, 0.812)
        #define TV_GROUND_LIGHT half3(0.078, 0.070, 0.038)
        #define TV_FOG half3(0.571, 0.672, 0.701)

        sampler2D _MainTex;
        float _Tiles;
        float _Pad;
        half _Surface;
        half _Wind;
        float _Bump;
        float _FogStart;
        float _FogEnd;

        struct Input
        {
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            float tvDepth;
            float3 atlas;
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
            o.atlas = v.texcoord.xyz;
            // Leaves and needles stir a little in the wind: more at the tip of a card (v near 1) than at its base
            // (not the cards lying flat on the ground, litter and leaf tops: 2026-11-25, Anders: the litter moved)
            if (_Wind > 0.001 && abs(v.normal.y) < 0.85 && ((v.texcoord.z >= 4.5 && v.texcoord.z < 19.5) || (v.texcoord.z >= 32.5 && v.texcoord.z < 34.5) || (v.texcoord.z >= 36.5 && v.texcoord.z < 37.5)))
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

        half TvSurfaceGrain (float3 p, float depth)
        {
            float fine = saturate(1.0 - depth / 40.0), finest = saturate(1.0 - depth / 14.0);
            float big = TvNoise(p * 0.83) * 0.6 + TvNoise(p * 2.31 + 17.3) * 0.4;
            float small = (TvNoise(p * 6.17 + 41.9) - 0.5) * 0.55 * fine + (TvNoise(p * 15.3 + 7.7) - 0.5) * 0.45 * finest;
            return (big - 0.5) * 0.06 + small * 0.4 + 0.02;
        }

        void surf (Input IN, inout SurfaceOutputTv o)
        {
            float3 c = IN.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            // The tile's picture: u, v repeat inside the tile's inner part (its padding keeps the neighbours out)
            float tile = floor(IN.atlas.z + 0.5);
            float inner = 1.0 - 2.0 * _Pad;
            float2 slopeX = ddx(IN.atlas.xy) * inner / _Tiles, slopeY = ddy(IN.atlas.xy) * inner / _Tiles;
            float2 f = frac(IN.atlas.xy);
            float2 cell = float2(fmod(max(tile, 0.0), _Tiles), floor(max(tile, 0.0) / _Tiles));
            float2 fromTop = float2(cell.x + _Pad + f.x * inner, cell.y + _Pad + (1.0 - f.y) * inner) / _Tiles;
            half4 tex = tex2Dgrad(_MainTex, float2(fromTop.x, 1.0 - fromTop.y), slopeX, slopeY);
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
                    float n = TvNoise(wp * 2.3) * 0.5 + TvNoise(wp * 7.1 + 3.1) * 0.33 + TvNoise(wp * 21.0 + 9.7) * 0.17;
                    float th = 1.02 - moss * 0.75;
                    float mm = moss > 0.02 ? smoothstep(th - 0.05, th + 0.05, n) : 0.0;
                    float3 mc = lerp(float3(0.40, 0.52, 0.14), float3(0.64, 0.74, 0.26), TvNoise(wp * 37.0));
                    mc *= 0.7 + 0.45 * TvNoise(wp * 90.0);
                    mc = mc * mc;
                    float ln = TvNoise(wp * 9.0 + 11.0) * 0.6 + TvNoise(wp * 26.0 + 5.0) * 0.4;
                    float lm = li > 1.5 ? smoothstep(0.68 - li * 0.03, 0.72 - li * 0.03, ln) * (0.12 + 0.08 * li) : 0.0;
                    float3 lc = float3(0.6, 0.62, 0.56) * (0.8 + 0.3 * TvNoise(wp * 50.0));
                    lc = lc * lc;
                    c = lerp(c, lc, lm * 0.85);
                    // A birch's foot: black, cracked up and down
                    if (tile > 2.5 && tile < 3.5)
                    {
                        float cr = TvNoise(float3(wp.x * 14.0, wp.y * 1.6, wp.z * 14.0)) * 0.7 + TvNoise(wp * 40.0) * 0.3;
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
                c = lerp(c, c * float3(0.78, 0.8, 0.85), smoothstep(0.6, 0.8, TvNoise(wp * 0.45 + 40.0)));
                if (tile > 20.5)
                {
                    c *= 0.88 + 0.2 * smoothstep(0.25, 0.7, TvNoise(float3(wp.x * 2.2, wp.y * 0.22, wp.z * 2.2)));
                    c = lerp(c, c * float3(1.2, 0.95, 0.75), smoothstep(0.74, 0.86, TvNoise(float3(wp.x * 1.3, wp.y * 0.3, wp.z * 1.3) + 9.0)) * 0.4);
                }
                float mossAmt = code > 0.01 ? saturate((code - 0.02) / 0.47) : (tile > 20.5 ? 0.45 : 0.0);
                float n3 = TvNoise(wp * 2.1) * 0.55 + TvNoise(wp * 6.3 + 2.0) * 0.3 + TvNoise(wp * 19.0 + 7.0) * 0.15;
                float upf = saturate((wn.y - 0.1) * 1.4);
                float m = upf * (0.35 + mossAmt * 0.7) + (n3 - 0.5) * 0.8 + mossAmt * 0.25 - 0.32;
                float mm = mossAmt > 0.01 ? smoothstep(0.0, 0.08, m) : 0.0;
                float3 mc = lerp(float3(0.36, 0.48, 0.13), float3(0.62, 0.72, 0.24), TvNoise(wp * 31.0));
                mc *= 0.7 + 0.45 * TvNoise(wp * 80.0);
                mc = mc * mc;
                c = lerp(c, mc, mm);
            }
            if (_Surface > 0.001)
            {
                half v = TvSurfaceGrain(IN.worldPos, IN.tvDepth);
                c *= 1.0 + v * 0.75 * _Surface;
            }
            o.Albedo = c;
            o.Foliage = (tile >= 4.5 && tile < 19.5) || (tile >= 32.5 && tile < 34.5) || (tile >= 35.5 && tile < 37.5) ? 1.0 : 0.0;
            // Relief (2026-11-25, Anders: the bark looked flat, like a picture): the picture's light and dark read
            // as height, bent into the normal from the screen's own slopes (no tangents needed); fades out far away
            float3 n = normalize(IN.worldNormal);
            bool solid = tile > -0.5 && (tile < 4.5 || (tile > 19.5 && tile < 32.5) || (tile > 34.5 && tile < 35.5) || tile > 37.5);
            float bump = solid ? _Bump * saturate(1.0 - IN.tvDepth / 30.0) : 0.0;
            if (bump > 0.0)
            {
                float hgt = dot(tex.rgb, float3(0.3, 0.5, 0.2));
                float3 dpx = ddx(IN.worldPos), dpy = ddy(IN.worldPos);
                float dhx = ddx(hgt), dhy = ddy(hgt);
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
