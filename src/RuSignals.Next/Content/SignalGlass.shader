Shader "RuSignals/Signal glass four rings"
{
    Properties
    {
        _MainTex ("UV mapping", 2D) = "white" {}
        _Color ("Glass tint", Color) = (.1,.1,.1,1)
        [HDR] _EmissionColor ("Lamp colour", Color) = (0,0,0,1)
        _Glossiness ("Smoothness", Range(0,1)) = .81
        _Metallic ("Metallic", Range(0,1)) = .05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing
        struct Input { float2 uv_MainTex; };
        fixed4 _Color;
        half3 _EmissionColor;
        half _Glossiness, _Metallic;
        // Per-lamp uniform. Zero retains the original shared four-ring glass.
        half _LensVariant;
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = (IN.uv_MainTex - .5) * 2;
            float radius = length(p);
            // Four broad optical grooves, with a continuous luminous centre.
            float4 centres = _LensVariant < .5 ? float4(.20,.40,.60,.80) : float4(.12,.26,.40,.54);
            float width = _LensVariant < .5 ? 1400 : 2400;
            float4 distance = radius - centres;
            float4 rings = exp2(-distance * distance * width);
            float groove = dot(rings, float4(1,1,1,1));
            float slope = dot(-distance * rings, float4(1,1,1,1)) * 65;
            if (_LensVariant > .5)
            {
                float2 extraDistance = radius - float2(.68,.82);
                float2 extraRings = exp2(-extraDistance * extraDistance * width);
                groove += dot(extraRings,float2(1,1));
                slope += dot(-extraDistance * extraRings,float2(1,1)) * 65;
            }
            o.Normal = normalize(float3(p * .3 + p / max(radius,.001) * slope, 1));
            o.Albedo = _Color.rgb * (.55 + .08 * groove);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            float centre = exp2(-radius * radius * 18);
            // Keep the entire lens in its signal colour, including its centre.
            o.Emission = _EmissionColor * (.09 + .65 * centre + .13 * groove) * saturate((1-radius)*18);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
