Shader "Custom/WaterWaves"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _WindAmp ("Wave Amplitude", Range(0,0.5)) = 0.08
        _WindSpeed ("Wave Speed", Range(0,5)) = 1.5
        _WindScale ("Wave Scale", Range(0,3)) = 0.8
        _WindVertical ("Vertical Factor", Range(0,1)) = 0.9
        _WindDir ("Wave Direction", Vector) = (1,0.3,0,0)
        _WindVar ("Wave Variation", Range(0,1)) = 0.1
        [Toggle] _UseWorldPos ("Use World Position", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #pragma target 3.0
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags{ "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _Color;
            float _WindAmp;
            float _WindSpeed;
            float _WindScale;
            float _WindVertical;
            float4 _WindDir;
            float _WindVar;
            float _UseWorldPos;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Simple hash function for variation
            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float t = _Time.y * _WindSpeed;
                float2 dir = normalize(_WindDir.xy);

                // CRITICAL FIX: Use WORLD POSITION for wave calculations to eliminate chunk seams
                float3 worldPosOriginal = TransformObjectToWorld(IN.positionOS.xyz);
                float3 wavePos = _UseWorldPos > 0.5 ? worldPosOriginal : pos;

                // Multi-layered wave calculation using world coordinates
                float wave1 = sin((wavePos.x * dir.x + wavePos.z * dir.y) * _WindScale + t);
                float wave2 = sin((wavePos.x + wavePos.z) * _WindScale * 0.7 + t * 1.3);
                float wave3 = sin((wavePos.x - wavePos.z) * _WindScale * 1.2 + t * 0.8);
                float directionalWave = sin((wavePos.x * dir.x + wavePos.z * dir.y) * _WindScale + t);

                // Combine waves for realistic water motion
                float totalWave = (wave1 + wave2 * 0.6 + wave3 * 0.4 + directionalWave * 0.8) * _WindAmp;

                // Add minimal variation only if enabled (for perfect uniformity, set _WindVar to 0)
                if (_WindVar > 0.0)
                {
                    float variation = (hash31(wavePos * 3.17) - 0.5) * _WindVar * _WindAmp;
                    totalWave += variation;
                }

                // Apply wave displacement
                pos.xz += dir * totalWave * 0.3; // Horizontal wave movement
                pos.y += totalWave * _WindVertical; // Vertical wave movement

                // Transform to world and clip space
                float3 ws = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(ws);

                // For animated texture atlases (like Minecraft water), use standard UVs
                // World-space positioning is only used for wave calculations, not texture sampling
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                OUT.positionWS = ws;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if defined(SHADOWS_SCREEN)
                    OUT.shadowCoord = GetShadowCoord(OUT.positionCS);
                #else
                    OUT.shadowCoord = TransformWorldToShadowCoord(ws);
                #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Sample texture
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                albedo *= _Color;

                // Simple lighting
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lighting = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // Apply lighting with water-appropriate brightness
                albedo.rgb *= lighting + 0.3; // Ambient water lighting

                return albedo;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}