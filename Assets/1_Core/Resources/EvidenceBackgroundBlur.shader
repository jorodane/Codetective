Shader "Hidden/Codetective/EvidenceBackgroundBlur"
{
    Properties { [HideInInspector] _RoomPixelStep("Room Pixel Step", Vector) = (0, 0, 0, 0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _RoomPixelStep;
        CBUFFER_END

        half4 Gaussian(float2 uv, float2 step)
        {
            half4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0) * 0.227027;
            float2 first = step * 1.384615;
            float2 second = step * 3.230769;
            color += (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + first, 0)
                    + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - first, 0)) * 0.316216;
            color += (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + second, 0)
                    + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - second, 0)) * 0.070270;
            return color;
        }

        half4 Horizontal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return Gaussian(input.texcoord, float2(_RoomPixelStep.x, 0));
        }

        half4 Vertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 color = Gaussian(input.texcoord, float2(0, _RoomPixelStep.y));
            return half4(color.rgb * 0.78, color.a);
        }
        ENDHLSL

        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Horizontal
            ENDHLSL
        }
        Pass
        {
            Name "Vertical"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Vertical
            ENDHLSL
        }
    }
    Fallback Off
}
