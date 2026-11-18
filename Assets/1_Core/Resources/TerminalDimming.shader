Shader "UI/Codetective/TerminalDimming"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            fixed4 _Color;
            float4 _Origin00, _Origin10, _Origin01, _Origin11;
            float4 _Direction00, _Direction10, _Direction01, _Direction11;
            float4 _Panel; // half width, half height, bulge, enabled
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float accepted(float t, float3 o, float3 d)
            {
                float2 p=o.xy+t*d.xy;
                float facing=d.z-_Panel.z*dot(p*d.xy,1.0/(_Panel.xy*_Panel.xy));
                return t>=0 && all(abs(p)<=_Panel.xy) && facing>0.000001;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                if (_Panel.w<0.5) return i.color;
                float3 o=lerp(lerp(_Origin00.xyz,_Origin10.xyz,i.uv.x),lerp(_Origin01.xyz,_Origin11.xyz,i.uv.x),i.uv.y);
                float3 d=lerp(lerp(_Direction00.xyz,_Direction10.xyz,i.uv.x),lerp(_Direction01.xyz,_Direction11.xyz,i.uv.x),i.uv.y);
                d=normalize(d);
                float2 inv=1.0/(_Panel.xy*_Panel.xy);
                float a=0.5*_Panel.z*dot(d.xy*d.xy,inv);
                float b=_Panel.z*dot(o.xy*d.xy,inv)-d.z;
                float c=_Panel.z*(0.5*dot(o.xy*o.xy,inv)-1)-o.z;
                float inside=0;
                if (abs(a)<0.0000001)
                {
                    if (abs(b)>0.0000001) inside=accepted(-c/b,o,d);
                }
                else
                {
                    float disc=b*b-4*a*c;
                    if (disc>=0)
                    {
                        float q=-0.5*(b+(b>=0 ? 1 : -1)*sqrt(disc));
                        float t1=q/a;
                        float t2=abs(q)>0.0000001 ? c/q : -b/(2*a);
                        inside=max(accepted(t1,o,d),accepted(t2,o,d));
                    }
                }
                return fixed4(i.color.rgb,i.color.a*(1-inside));
            }
            ENDCG
        }
    }
}
