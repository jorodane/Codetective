Shader "Hidden/Codetective/TerminalBodyMask"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _StencilRef("Stencil reference", Float) = 128
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Stencil { Ref [_StencilRef] Comp Always Pass Replace ReadMask 128 WriteMask 128 }
        Cull Off ZWrite Off ZTest Always
        ColorMask 0
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            fixed4 frag() : SV_Target { return fixed4(1,1,1,1); }
            ENDCG
        }
    }
}
