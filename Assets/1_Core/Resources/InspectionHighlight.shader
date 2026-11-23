Shader "Hidden/Codetective/InspectionHighlight"
{
    Properties
    {
        _OutlineColor("Outline color", Color) = (1, 0.81, 0.31, 0.78)
        _Outline("Outline", Float) = 0
        _ColorMask("Color write mask", Float) = 0
        _Cull("Cull", Float) = 2
        _StencilComp("Stencil comparison", Float) = 8
        _StencilWrite("Stencil write mask", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+35" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask [_ColorMask]
            Cull [_Cull]
            ZWrite Off ZTest LEqual
            Offset -1, -1
            Stencil { Ref 4 ReadMask 4 WriteMask [_StencilWrite] Comp [_StencilComp] Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _Outline;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.direction = 0;
                if (_Outline > 0.5)
                {
                    float2 normal = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS)).xy;
                    output.direction = normal / max(0.0001, length(normal));
                    output.positionCS.xy += output.direction * (4.0 / _ScaledScreenParams.xy) * output.positionCS.w;
                }
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_Outline < 0.5) return 0;
                float alongEdge = abs(input.direction.x) > abs(input.direction.y) ? input.positionCS.y : input.positionCS.x;
                clip(frac(alongEdge / 9.0) - 0.4);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
