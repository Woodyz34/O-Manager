// Far trees of the run-yourself world as pictures (2026-11-25, Anders: big maps lagged): every tree beyond the far
// trees' reach is one upright card, turned round its trunk to face the camera, with a picture of its kind taken from
// one of four sides (RunTrees bakes them from the real trees when the race loads: already lit and tone mapped as the
// trees themselves, so only the fog is added here).
// Corners: position = the foot of the tree (all four the same); uv0 = corner (0/1 across, 0/1 up); uv1 = width,
// height, the picture's row from the bottom, the tree's yaw; uv2.x = how far below the foot the picture starts;
// colour = the tree's own shade (divided by 1.25).
Shader "O-Manager/TvImpostor"
{
    Properties
    {
        _MainTex ("Pictures (views across, kinds up)", 2D) = "white" {}
        _Views ("Views per kind", Float) = 4
        _Rows ("Kinds", Float) = 15
        _Cutoff ("Alpha cut", Range(0,1)) = 0.5
        _FogStart ("Fog start (m)", Float) = 45
        _FogEnd ("Fog end (m)", Float) = 230
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define TV_FOG half3(0.571, 0.672, 0.701)

            sampler2D _MainTex;
            float _Views, _Rows, _Cutoff, _FogStart, _FogEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
                float4 card : TEXCOORD1;
                float2 extra : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float depth : TEXCOORD1;
                half3 tint : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 foot = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                float2 toCam = _WorldSpaceCameraPos.xz - foot.xz;
                float len = max(length(toCam), 0.001);
                toCam /= len;
                // The camera's right as seen from the tree (the pictures were taken the same way round)
                float3 right = float3(-toCam.y, 0, toCam.x);
                float3 p = foot + right * (v.corner.x - 0.5) * v.card.x + float3(0, v.corner.y * v.card.y - v.extra.x, 0);
                o.pos = UnityWorldToClipPos(float4(p, 1));
                // Which of the four pictures: the side of the tree that faces the camera
                float side = atan2(toCam.x, toCam.y) - v.card.w;
                float view = fmod(floor(side / 6.2831853 * _Views + 0.5) + _Views * 8.0, _Views);
                o.uv = float2((view + v.corner.x) / _Views, (v.card.z + v.corner.y) / _Rows);
                o.depth = -UnityWorldToViewPos(p).z;
                o.tint = v.color.rgb * 1.25;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                half3 col = saturate(c.rgb * i.tint);
                float fog = smoothstep(_FogStart, _FogEnd, i.depth);
                return half4(lerp(col, TV_FOG, fog), 1);
            }
            ENDCG
        }
    }
}
