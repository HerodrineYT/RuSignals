Shader "RuSignals/Weathered structure with original cutouts"
{
    Properties
    {
        _MainTex ("DV-style albedo", 2D) = "white" {}
        _OriginalAlpha ("Original RUS cutout", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = .5
        _Glossiness ("Smoothness", Range(0,1)) = .14
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard alphatest:_Cutoff fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex, _OriginalAlpha;
        fixed4 _Color;
        half _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            o.Alpha = tex2D(_OriginalAlpha, IN.uv_MainTex).a;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/Diffuse"
}
