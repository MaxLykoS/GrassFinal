Shader "Custom/GrassInstancedIndirect"
{
    Properties
    {
        [Header(Shading)]
        _MainTex ("Texture", 2D) = "white" {}
        _TranslucentGain("Translucent Gain", Range(0,1)) = 0.5
        
        [Header(BackLightSSS)]
        _InteriorColor("Interior Color", Color) = (1, 1, 1, 1)
        _BackSubsurfaceDistortion("Back Subsurface Distortion", Range(0, 1)) = 0.5
        _EdgeLitRate("Edge Light Rate", range(0, 2)) = 0.3

        [Header(Stamp)]
        _StampTex("脚印贴图", 2D) = "white"{}
        _StampVector("脚印中心", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector" = "True"}
        
        Cull Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing nolightmap nodirlightmap nodynlightmap novertexlight

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct MeshProperties
            {
                float4x4 worldMat;
                float3 worldPos;
            };
            StructuredBuffer<MeshProperties> posVisibleBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normalW : NORMAL;
                float3 viewDir : TEXCOORD2;
                float3 lightDir : TEXCOORD3;
            };

            sampler2D _MainTex;
            sampler2D _StampTex;
            float4 _StampVector;

            float GetStamp(float2 position, float height)
            {
                //_StampVector.xz   踩踏投影的水平坐标点
                //_StampVector.y    最低降低程度
                //_StampVector.w    踩踏投影尺寸
                //以物体减去踩踏投影中心点的的位置采样踩踏数据
                //超出范围不采样
                float2 stampUv = (position.xy - _StampVector.xz) / _StampVector.w + float2(0.5, .5);
                float4 stampP = float4(0, 0, 0, 0);
                if (stampUv.x > 0 && stampUv.x < 1 && stampUv.y>0 && stampUv.y < 1) {
                    stampP = tex2Dlod(_StampTex, float4(stampUv, 0.0, 0.0));
                }
                float y = height * (1 - stampP.a);
                return min(height, max(_StampVector.y, y));
            }

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                
                float4 worldPos = mul(posVisibleBuffer[instanceID].worldMat, v.vertex);

                worldPos.y = GetStamp(worldPos.xz, worldPos.y);

                o.uv = v.uv;

                o.normalW = normalize(UnityObjectToWorldNormal(v.normal));

                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                o.lightDir = normalize(_WorldSpaceLightPos0.xyz);

                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                return o;
            }

            float _TranslucentGain;
            float _BackSubsurfaceDistortion;
            float _EdgeLitRate;
            float4 _InteriorColor;

            fixed4 frag (v2f o, fixed facing : VFACE) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, o.uv);
                clip(col.a-0.6);  

                o.normalW = facing > 0 ? o.normalW : -o.normalW;

                float NdotL = saturate(saturate(dot(o.normalW, _WorldSpaceLightPos0)) + _TranslucentGain);

                // back light sss
                float3 backLitDir = o.normalW * _BackSubsurfaceDistortion + o.lightDir;
                float backSSS = saturate(dot(o.viewDir, -backLitDir));
                backSSS = saturate(pow(backSSS, 3));
                fixed3 edgeCol = backSSS * _EdgeLitRate * _InteriorColor * col.rgb; 
                edgeCol += backSSS * _InteriorColor;

                float3 ambient = ShadeSH9(float4(o.normalW, 1));
                float4 lightIntensity = NdotL * _LightColor0 + float4(ambient, 1);
                return col + fixed4(edgeCol, 1);
            }
            ENDCG
        }
    }
}
