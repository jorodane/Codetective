Shader "Hidden/Codetective/InspectionHighlight"
{
    Properties
    {
        _OutlineColor("Outline color", Color) = (1, 0.81, 0.31, 0.84)
        _OutlineWidth("Outline width (pixels)", Range(1, 8)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        float4 MaskVert(float3 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS);
        }

        half4 MaskFrag() : SV_Target { return 1; }
        ENDHLSL

        Pass
        {
            Name "VisibleSilhouette"
            Cull Back ZWrite Off ZTest LEqual
            Offset -1, -1
            ColorMask R

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "VisibleContents"
            Cull Back ZWrite Off ZTest LEqual
            Offset -1, -1
            ColorMask G

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "FullSilhouette"
            Cull Back ZWrite Off ZTest Always
            ColorMask B

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half Selected(float2 uv)
            {
                half3 mask = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                // Fill occlusion by drawer contents only where the drawer itself projects.
                return max(mask.r, min(mask.g, mask.b));
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                clip(Selected(uv) - 0.5);

                // Keep the stroke inside the visible silhouette so a wall or desk frame can't hide it.
                float2 radius = _BlitTexture_TexelSize.xy * _OutlineWidth;
                half left = Selected(uv - float2(radius.x, 0));
                half right = Selected(uv + float2(radius.x, 0));
                half down = Selected(uv - float2(0, radius.y));
                half up = Selected(uv + float2(0, radius.y));
                half coverage = min(min(left, right), min(down, up));
                float2 diagonal = radius * 0.70710678;
                coverage = min(coverage, min(Selected(uv + diagonal), Selected(uv - diagonal)));
                diagonal.x = -diagonal.x;
                coverage = min(coverage, min(Selected(uv + diagonal), Selected(uv - diagonal)));
                clip(0.499 - coverage);

                float alongEdge = left + right < down + up ? input.positionCS.y : input.positionCS.x;
                clip(frac(alongEdge / 11.0) - 0.38);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
