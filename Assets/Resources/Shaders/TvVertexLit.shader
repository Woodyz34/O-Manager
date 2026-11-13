// The 3D view of a TV control (TV/TvWorldView.cs): plain Lambert light with the colour of every corner of the mesh,
// times _Color. The colours are written in sRGB, so they are turned linear here when the project works in linear
// colour. Casts and gets shadows, and is fogged.
Shader "O-Manager/TvVertexLit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Back

        CGPROGRAM
        #pragma surface surf Lambert addshadow fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;

        struct Input
        {
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            float3 c = IN.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            o.Albedo = c * _Color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
