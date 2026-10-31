// For stand-ins in the editor only. It approximates the game's main shader with the same texture slots,
// because the real shaders can't be extracted. It draws both sides where the game's materials do, which
// Unity's Standard shader can't. It never ships in a mod.
Shader "OCK/StandIn"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Cutoff ("Alpha Cutoff (negative for none)", Range(-0.01,1)) = -0.01
        _DetailStrength ("Detail Strength (0 in Fast mode)", Range(0,1)) = 0
        _DetTex ("Detail Albedo", 2D) = "gray" {}
        _DetNormTex ("Detail Normal", 2D) = "bump" {}
        _DetailNormalStrength ("Detail Normal Strength", Float) = 0.35
        _DetailTiling ("Detail Tiling", Vector) = (1,1,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _DetTex;
        sampler2D _DetNormTex;
        fixed4 _Color;
        half _Glossiness;
        half _NormalStrength;
        half _DetailStrength;
        half _DetailNormalStrength;
        float4 _DetailTiling;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float2 detailUv = IN.uv_MainTex * _DetailTiling.xy;

            // Mid-gray detail leaves the colour unchanged, so it can darken and brighten.
            c.rgb *= lerp(fixed3(1, 1, 1), tex2D(_DetTex, detailUv).rgb * 2, _DetailStrength);
            o.Albedo = c.rgb;
            o.Alpha = c.a;

            half3 normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _NormalStrength);
            half3 detailNormal = UnpackScaleNormal(tex2D(_DetNormTex, detailUv), _DetailNormalStrength * _DetailStrength);
            o.Normal = normalize(half3(normal.xy + detailNormal.xy, normal.z * detailNormal.z));
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
