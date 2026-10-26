Shader "RuSignals/Depth tested lamp halo"
{
    Properties { [HDR] _TintColor ("Native lamp colour", Color) = (0,0,0,0) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _TintColor;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float r=length((i.uv-.5)*2);
                float halo=exp2(-r*r*9)*pow(saturate(1-r),2)*.18;
                return float4(_TintColor.rgb*halo,0);
            }
            ENDCG
        }
    }
}
