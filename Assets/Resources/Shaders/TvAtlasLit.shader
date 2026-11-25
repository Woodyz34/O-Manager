// The trees, bushes, undergrowth, forest-floor litter and town walls of the 3D world with real pictures on them (2026-11-25):
// bark, needles, leaves, bilberry, grass, ferns, twigs (the forest atlas, TV/TvFlora.cs). Lit like TvVertexLit (its
// own sun, sky and ground light, ACES tone mapping, fog), so it sits in the same picture.
// Each corner carries u, v and the atlas tile in its first uv (x, y, z); tile -1 = no picture (the corner's colour only).
// Tiles 0-4 are bark, 20-24 stone and wood, 25-31 walls, roofs and glass, 32 moss at a trunk's foot (lit as solid
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
            if (_Wind > 0.001 && ((v.texcoord.z >= 4.5 && v.texcoord.z < 19.5) || v.texcoord.z >= 32.5))
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
            if (_Surface > 0.001)
            {
                half v = TvSurfaceGrain(IN.worldPos, IN.tvDepth);
                c *= 1.0 + v * 0.75 * _Surface;
            }
            o.Albedo = c;
            o.Foliage = (tile >= 4.5 && tile < 19.5) || tile >= 32.5 ? 1.0 : 0.0;
            // Relief (2026-11-25, Anders: the bark looked flat, like a picture): the picture's light and dark read
            // as height, bent into the normal from the screen's own slopes (no tangents needed); fades out far away
            float3 n = normalize(IN.worldNormal);
            bool solid = tile > -0.5 && (tile < 4.5 || (tile > 19.5 && tile < 32.5));
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
