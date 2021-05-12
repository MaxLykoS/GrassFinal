Shader "Custom/TestShaderBackSSS"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white" {}
        _AmbientIntensity("Ambient Intensity", Range(0,1)) = 0.5
        // back light sss
        _InteriorColor("Interior Color", Color) = (1,1,1,1)
        _BackSubsurfaceDistortion("Back Subsurface Distortion", Range(0,1)) = 0.5
        _EdgeLitRate("Edge Light Rate", range(0,2)) = 0.3
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }

        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityLightingCommon.cginc" // for _LightColor0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 lightDir : TEXCOORD3;
                float3 viewDir : TEXCOORD4;
                float4 pos : SV_POSITION;
                //fixed4 diff : COLOR0; // diffuse lighting color
                //fixed3 ambient : COLOR1;
            };

            // proprety
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _InteriorColor;
            float _EdgeLitRate;
            float _BackSubsurfaceDistortion;
            float _AmbientIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);

                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - o.posWorld.xyz);
                o.lightDir = normalize(_WorldSpaceLightPos0.xyz);

                // diffuse light
                o.normalDir = UnityObjectToWorldNormal (v.normal);
                //o.normalDir = normalize(UnityObjectToWorldNormal(v.vertex)); // sphere normal
                //o.diff = max(0, dot(o.normalDir, _WorldSpaceLightPos0.xyz));
                //o.ambient = ShadeSH9(half4(o.normalDir,1));

                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                i.normalDir = facing > 0 ? i.normalDir : -i.normalDir;
                // sample the texture
                //fixed4 texCol = tex2D(_MainTex, i.uv);
                fixed4 texCol = fixed4(0, 1, 0, 1);
                //fixed4 col = fixed4(texCol.rgb, 1);

                // back light sss
                float3 backLitDir = i.normalDir * _BackSubsurfaceDistortion + i.lightDir;
                float backSSS = saturate(dot(i.viewDir, -backLitDir));
                backSSS = saturate(pow(backSSS, 3));
                fixed3 edgeCol = backSSS * _EdgeLitRate * _InteriorColor * texCol.rgb;
                edgeCol += backSSS * _InteriorColor;
                return fixed4(edgeCol, 1);
                /*fixed3 lighting = _LightColor0 + i.ambient * _AmbientIntensity;

                col.rgb += edgeCol;
                //col.rgb *= lighting;

                return col;*/
            }
            ENDCG
        }
    }
}