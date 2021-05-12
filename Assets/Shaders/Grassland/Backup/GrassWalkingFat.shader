Shader "WalkingFat/CpFoliage"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white" {}
        _MainColor("Main Color", Color) = (1 ,1 ,1 ,1)

        _ShadowColor("Shadow Color", Color) = (1 ,1 ,1 ,1)
        _MaskTex("Mask Tex", 2D) = "white" {}
        _EdgeLitRate("Edge Light Rate", range(0,2)) = 0.3
        _Cutoff("Cutoff", range(0,1)) = 0.3
        _AmbientIntensity("Ambient Intensity", Range(0,1)) = 0.5

        // shake with wind
        _OffsetGradientStrength("Offset Gradient Strength", range(0,1)) = 0.7
        _ShakeWindspeed("Shake Wind speed", float) = 0
        _ShakeBending("Shake Bending", float) = 0
        _WindDirRate("Wind Direction Rate", float) = 0.5

        // back light sss
        _InteriorColor("Interior Color", Color) = (1,1,1,1)
        _BackSubsurfaceDistortion("Back Subsurface Distortion", Range(0,1)) = 0.5
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
            // make fog work
            #pragma multi_compile_fog
            // make light work
            #pragma multi_compile_fwdbase
            #include "AutoLight.cginc"
            // make shadow work
            #pragma multi_compile_fwdbase_fullshadows

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
                fixed4 diff : COLOR0; // diffuse lighting color
                fixed3 ambient : COLOR1;
                UNITY_FOG_COORDS(5)
            };

            // proprety
            sampler2D _MainTex, _MaskTex;
            float4 _MainTex_ST, _MaskTex_ST;
            float4 _MainColor, _ShadowColor, _InteriorColor;
            float  _OffsetGradientStrength, _ShakeBending, _EdgeLitRate, _ShakeWindspeed, _WindDirRate;
            float _WindDirectionX, _WindDirectionZ, _WindStrength, _Cutoff, _AmbientIntensity, _BackSubsurfaceDistortion;

            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.1415927;
                // powers for taylor series
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5;
                float4 r1 = r5 * val;
                float4 r2 = r1 * r5;
                float4 r3 = r2 * r5;
                //Vectors for taylor's series expansion of sin and cos
                float4 sin7 = {1, -0.16161616, 0.0083333, -0.00019841};
                float4 cos8 = {-0.5, 0.041666666, -0.0013888889, 0.000024801587};
                // sin
                s = val + r1 * sin7.y + r2 * sin7.z + r3 * sin7.w;
                // cos
                c = 1 + r5 * cos8.x + r6 * cos8.y + r7 * cos8.z + r8 * cos8.w;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                // get bend rate --------------------------
                fixed4 grandientCol = tex2Dlod(_MaskTex, float4 (TRANSFORM_TEX(v.uv, _MaskTex), 0.0, 0.0));
                float grandient = lerp(grandientCol.g, 1, 1 - _OffsetGradientStrength);
                float xyzOffset = o.uv.y * grandient;

                // waving force by wind ==========================================================
                const float _WindSpeed = _ShakeWindspeed;

                const float4 _waveXSize = float4 (0.048, 0.06, 0.24, 0.096);
                const float4 _waveZSize = float4 (0.024, 0.08, 0.08, 0.2);
                const float4 waveSpeed = float4 (1.2, 2, 1.6, 4.8);

                float4 _waveXmove = float4 (0.024, 0.04, -0.12, 0.096);
                float4 _waveZmove = float4 (0.006, 0.02, -0.02, 0.1);

                float4 waves;
                waves = v.vertex.x * _waveXSize;
                waves += v.vertex.z * _waveZSize;

                waves += _Time.x * waveSpeed * _WindSpeed + v.vertex.x + v.vertex.z;

                float4 s, c;
                waves = frac(waves);
                FastSinCos(waves, s, c);
                float waveAmount = v.uv.y * _ShakeBending;

                s *= waveAmount;

                s *= normalize(waveSpeed);

                float fade = dot(s, 1.3);

                float3 waveMove = float3 (0, 0, 0);

                float windDirX = _WindDirectionX * _WindStrength;
                float windDirZ = _WindDirectionZ * _WindStrength;
                float windDirY = _WindStrength;

                waveMove.x = dot(s, _waveXmove * windDirX);
                waveMove.z = dot(s, _waveZmove * windDirZ);
                waveMove.y = dot(s, _waveZmove * windDirY);

                float3 windDirOffset = float3 (windDirX, windDirY, windDirZ) * _WindDirRate * xyzOffset;

                float3 waveForce = -mul((float3x3)unity_WorldToObject, waveMove).xyz * xyzOffset + windDirOffset;

                v.vertex.xyz += waveForce;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);

                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - o.posWorld.xyz);
                o.lightDir = normalize(_WorldSpaceLightPos0.xyz);

                // diffuse light
                //o.normalDir = UnityObjectToWorldNormal (v.normal);
                o.normalDir = normalize(UnityObjectToWorldNormal(v.vertex)); // sphere normal
                o.diff = max(0, dot(o.normalDir, _WorldSpaceLightPos0.xyz));
                o.ambient = ShadeSH9(half4(o.normalDir,1));

                // using fog
                UNITY_TRANSFER_FOG(o, o.pos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sample the texture
                fixed4 texCol = tex2D(_MainTex, i.uv);
                fixed4 maskCol = tex2D(_MaskTex, i.uv);
                fixed4 col = fixed4(texCol.rgb * _MainColor.rgb, 1);

                // back light sss
                float3 backLitDir = i.normalDir * _BackSubsurfaceDistortion + i.lightDir;
                float backSSS = saturate(dot(i.viewDir, -backLitDir));
                backSSS = saturate(pow(backSSS, 3));

                // apply light and shadow
                fixed3 edgeCol = backSSS * _EdgeLitRate * _InteriorColor * texCol.rgb;
                edgeCol += maskCol.r * backSSS * _InteriorColor;
                fixed3 lighting = lerp(_ShadowColor, fixed4(1,1,1,1), i.diff.r + maskCol.r * i.diff.r * 0.6).rgb * _LightColor0 + i.ambient * _AmbientIntensity;

                col.rgb += edgeCol;
                col.rgb *= lighting;

                clip(texCol.a* _Cutoff - 0.5);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }


        Pass 
        {
            Name "ShadowCaster"
            Tags 
            {
                "LightMode" = "ShadowCaster"
            }
            Offset 1, 1
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_fog

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float4 pos : SV_POSITION;
            };

            // proprety
            sampler2D _MainTex, _MaskTex;
            float4 _MainTex_ST, _MaskTex_ST;
            float  _OffsetGradientStrength, _ShakeBending, _EdgeLitRate, _ShakeWindspeed, _WindDirRate;
            float _WindDirectionX, _WindDirectionZ, _WindStrength, _Cutoff;

            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.1415927;
                // powers for taylor series
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5;
                float4 r1 = r5 * val;
                float4 r2 = r1 * r5;
                float4 r3 = r2 * r5;
                //Vectors for taylor's series expansion of sin and cos
                float4 sin7 = {1, -0.16161616, 0.0083333, -0.00019841};
                float4 cos8 = {-0.5, 0.041666666, -0.0013888889, 0.000024801587};
                // sin
                s = val + r1 * sin7.y + r2 * sin7.z + r3 * sin7.w;
                // cos
                c = 1 + r5 * cos8.x + r6 * cos8.y + r7 * cos8.z + r8 * cos8.w;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);


                // get bend rate --------------------------
                fixed4 grandientCol = tex2Dlod(_MaskTex, float4 (TRANSFORM_TEX(v.uv, _MaskTex), 0.0, 0.0));
                float grandient = lerp(grandientCol.g, 1, 1 - _OffsetGradientStrength);
                float xyzOffset = o.uv.y * grandient;

                // waving force by wind ==========================================================
                const float _WindSpeed = _ShakeWindspeed;

                const float4 _waveXSize = float4 (0.048, 0.06, 0.24, 0.096);
                const float4 _waveZSize = float4 (0.024, 0.08, 0.08, 0.2);
                const float4 waveSpeed = float4 (1.2, 2, 1.6, 4.8);

                float4 _waveXmove = float4 (0.024, 0.04, -0.12, 0.096);
                float4 _waveZmove = float4 (0.006, 0.02, -0.02, 0.1);

                float4 waves;
                waves = v.vertex.x * _waveXSize;
                waves += v.vertex.z * _waveZSize;

                waves += _Time.x * waveSpeed * _WindSpeed + v.vertex.x + v.vertex.z;

                float4 s, c;
                waves = frac(waves);
                FastSinCos(waves, s, c);
                float waveAmount = v.uv.y * _ShakeBending;

                s *= waveAmount;

                s *= normalize(waveSpeed);

                float fade = dot(s, 1.3);

                float3 waveMove = float3 (0, 0, 0);

                float windDirX = _WindDirectionX * _WindStrength;
                float windDirZ = _WindDirectionZ * _WindStrength;
                float windDirY = _WindStrength;

                waveMove.x = dot(s, _waveXmove * windDirX);
                waveMove.z = dot(s, _waveZmove * windDirZ);
                waveMove.y = dot(s, _waveZmove * windDirY);

                float3 windDirOffset = float3 (windDirX, windDirY, windDirZ) * _WindDirRate * xyzOffset;

                float3 waveForce = -mul((float3x3)unity_WorldToObject, waveMove).xyz * xyzOffset + windDirOffset;

                v.vertex.xyz += waveForce;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);

                TRANSFER_SHADOW_CASTER(o); // make shadow work

                return o;
            }

            float4 frag(v2f i) : COLOR {
                fixed4 texCol = tex2D(_MainTex, i.uv);
                clip(texCol.a* _Cutoff - 0.5);

                SHADOW_CASTER_FRAGMENT(i);
            }

            ENDCG
        }
    }
    Fallback "VertexLit"
}