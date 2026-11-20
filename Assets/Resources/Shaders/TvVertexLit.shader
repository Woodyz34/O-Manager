// The 3D view of a TV control (TV/TvWorldView.cs). The light is worked out here and not taken from the scene, so the
// picture looks the same whatever lights and quality settings the game has: a warm sun (the scene's sun gives only the
// direction and the shadows), light from the sky above and from the ground below, film-like tone mapping (ACES) and fog
// into the sky colour far away. The colour comes from every corner of the mesh (written in sRGB), times _Color. The
// ground also gets a fine grain (_DetailTex, repeated every 5 metres). Also used by the run-yourself game mode's world
// (RunYourself/RunWorld.cs), which sets the fog to its own view distance.
// Everything else (houses, trees, stones, people) can get a surface grain too (_SurfaceTex, 2026-11-20, Anders: the
// objects as lively as the ground): soft blotches and specks laid on from all three sides by the surface's direction,
// lighter and darker and richer in colour, so no face is one flat colour.
Shader "O-Manager/TvVertexLit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _DetailTex ("Ground grain", 2D) = "white" {}
        _Detail ("Ground grain strength", Range(0,1)) = 0
        _SurfaceTex ("Surface grain", 2D) = "white" {}
        _Surface ("Surface grain strength", Range(0,1)) = 0
        _FogStart ("Fog start (m)", Float) = 45
        _FogEnd ("Fog end (m)", Float) = 230
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Back

        CGPROGRAM
        #pragma surface surf Tv vertex:tvVert finalcolor:tvFinal addshadow noambient novertexlights noforwardadd nofog nolightmap
        #pragma target 3.0

        // Linear colours, already times their strength
        #define TV_SUN half3(1.75, 1.54, 1.18)
        #define TV_SKY_LIGHT half3(0.592, 0.708, 0.812)
        #define TV_GROUND_LIGHT half3(0.078, 0.070, 0.038)
        #define TV_FOG half3(0.571, 0.672, 0.701)

        fixed4 _Color;
        sampler2D _DetailTex;
        half _Detail;
        sampler2D _SurfaceTex;
        half _Surface;
        // The TV's fog starts at 45 m and the sky takes over at 230 m; the run-yourself world sets its own (RunWorld)
        float _FogStart;
        float _FogEnd;

        struct Input
        {
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            float tvDepth;
        };

        void tvVert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.tvDepth = -UnityObjectToViewPos(v.vertex).z;
        }

        half4 LightingTv (SurfaceOutput s, half3 lightDir, half atten)
        {
            half nl = saturate(dot(s.Normal, lightDir));
            half4 c;
            c.rgb = s.Albedo * TV_SUN * (nl * atten);
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            float3 c = IN.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            c *= _Color.rgb;
            half grain = tex2D(_DetailTex, IN.worldPos.xz * 0.2).r;
            c *= lerp(1.0, grain, _Detail);
            if (_Surface > 0.001)
            {
                // From the side that faces the surface most: mostly the fine grain, a little of the soft blotches
                float3 w = abs(IN.worldNormal);
                w = w * w * w * w;
                w /= (w.x + w.y + w.z + 1e-4);
                float3 p = IN.worldPos;
                half sx = tex2D(_SurfaceTex, p.zy * 0.25).r * 0.3 + tex2D(_SurfaceTex, p.zy * 1.1 + 0.37).r * 0.7;
                half sy = tex2D(_SurfaceTex, p.xz * 0.25).r * 0.3 + tex2D(_SurfaceTex, p.xz * 1.1 + 0.37).r * 0.7;
                half sz = tex2D(_SurfaceTex, p.xy * 0.25).r * 0.3 + tex2D(_SurfaceTex, p.xy * 1.1 + 0.37).r * 0.7;
                // -0.24 .. +0.19 round the colour itself (the grain is 0.84 on average)
                half v = (sx * w.x + sy * w.y + sz * w.z) / 0.84 - 1.0;
                // Not dirt but life (Anders, 2026-11-20): the grain goes both lighter and darker, and the light specks
                // make the colour stronger, never greyer; the colours as a whole a little richer
                c *= 1.0 + v * 0.75 * _Surface;
                half l = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = max(0.0, lerp(l.xxx, c, 1.0 + _Surface * (0.14 + max(v, 0.0) * 1.4)));
            }
            o.Albedo = c;
            // Light from the sky and the ground
            half up = IN.worldNormal.y * 0.5 + 0.5;
            o.Emission = c * lerp(TV_GROUND_LIGHT, TV_SKY_LIGHT, up);
            o.Alpha = 1;
        }

        float3 TvFit (float3 v)
        {
            float3 a = v * (v + 0.0245786) - 0.000090537;
            float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
            return a / b;
        }

        // ACES film tone mapping, as in the look test
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

        void tvFinal (Input IN, SurfaceOutput o, inout fixed4 color)
        {
            float3 c = TvTone(max(color.rgb, 0.0));
            float fog = smoothstep(_FogStart, _FogEnd, IN.tvDepth);
            color.rgb = lerp(c, TV_FOG, fog);
        }
        ENDCG
    }
    Fallback "Diffuse"
}
