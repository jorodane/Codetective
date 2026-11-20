Shader "UI/Codetective/ScreenOverlap"
{
    Properties
    {
        [PerRendererData] _MainTex("Previous screen", 2D) = "white" {}
        _Darkness("Midpoint darkness", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        // The source carries the old-frame weight; alpha carries the new-frame weight.
        // Their sum dips below one at the midpoint, so the overlap cannot add a bright flash.
        Blend One SrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex;
            float _Darkness;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float progress=saturate(1-i.color.a);
                float brightness=1-saturate(_Darkness)*4*progress*(1-progress);
                return fixed4(tex2D(_MainTex,i.uv).rgb*i.color.rgb*(1-progress)*brightness,progress*brightness);
            }
            ENDCG
        }
    }
}
