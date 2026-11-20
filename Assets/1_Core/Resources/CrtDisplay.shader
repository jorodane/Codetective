Shader "UI/Codetective/CrtDisplay"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 panel : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
                float2 panel : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _MainTex_ST, _ClipRect, _CrtSignal, _CrtRight;
            float _CrtTime, _CrtScanSeconds;
            float bandDistance(float y) { return abs(frac(y + _CrtTime / _CrtScanSeconds + 0.5) - 0.5); }
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float band = 1 - smoothstep(0.006, 0.045, bandDistance(v.panel.y));
                float slip = band * sin(_CrtTime * 5.7) * _CrtSignal.w * _CrtSignal.x;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                world.xyz += _CrtRight.xyz * slip;
                o.vertex = mul(UNITY_MATRIX_VP, world);
                o.localPosition = mul(unity_WorldToObject, world);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.panel = v.panel; o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                // Fade subpixel scanlines at distant angles to avoid moire and keep text readable.
                float filtered = saturate(1 - fwidth(i.panel.y) * _CrtSignal.z);
                float lines = 0.5 + 0.5 * cos(i.panel.y * _CrtSignal.z * 6.2831853);
                float distance = bandDistance(i.panel.y);
                float band = 1 - smoothstep(0.008, 0.065, distance);
                float edge = 1 - smoothstep(0.0008, 0.003, distance);
                color.rgb *= 1 - _CrtSignal.x * (_CrtSignal.y * lines * filtered + 0.045 * band - 0.025 * edge);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
