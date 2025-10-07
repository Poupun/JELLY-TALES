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

        // Depth fog properties (set by UnderwaterFogManager)
        _DepthFogColor ("Deep Water Color", Color) = (0.05, 0.15, 0.3, 1)
        _MaxDepthDarkening ("Max Depth for Darkening", Float) = 20.0
        _DepthFogIntensity ("Depth Fog Intensity", Range(0, 1)) = 0.8
        _WaterAbsorption ("Water Light Absorption", Range(0, 1)) = 0.15
        _OceanFloorFogColor ("Ocean Floor Fog Color", Color) = (0.02, 0.05, 0.1, 1)
        _OceanFloorFogDistance ("Ocean Floor Fog Distance", Float) = 30.0
        _OceanFloorFogIntensity ("Ocean Floor Fog Intensity", Range(0, 1)) = 0.7
        _WaterSideOpacity ("Water Side/Interior Opacity", Range(0, 1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200
        Cull Off
        ZWrite Off
        // Use alpha blending that accumulates darkness through multiple layers
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
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
            float4 _DepthFogColor;
            float _MaxDepthDarkening;
            float _DepthFogIntensity;
            float _WaterAbsorption;
            float4 _OceanFloorFogColor;
            float _OceanFloorFogDistance;
            float _OceanFloorFogIntensity;
            float _WaterSideOpacity;
            CBUFFER_END

            // Global shader properties set by UnderwaterFogManager
            float _UnderwaterFogEnabled;
            float _UnderwaterFogDensity;
            float4 _UnderwaterFogColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 color : COLOR; // Water level data (r = level normalized 0-1)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                float3 viewDir : TEXCOORD4;
                float depth : TEXCOORD5;
                float waterLevel : TEXCOORD6; // Water flow level (0 = source, >0 = flowing)
                float4 vertexColor : COLOR; // Vertex color with shading
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

                // Extract water level from vertex color (red channel) for debugging
                float waterLevel = IN.color.r * 7.0; // Denormalize back to 0-7 range
                OUT.waterLevel = waterLevel;

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

                // Apply wave displacement to all water
                // CRITICAL: Apply to TOP FACE and top vertices of SIDE FACES to keep them connected
                // Bottom face and bottom vertices of sides should NOT animate
                float3 normalOS = IN.normalOS;
                float isTopFace = step(0.99, normalOS.y); // 1.0 if normal.y >= 0.99 (pointing up = top face)
                float isBottomFace = step(0.99, -normalOS.y); // 1.0 if normal.y <= -0.99 (pointing down = bottom face)
                float isSideFace = (1.0 - isTopFace) * (1.0 - isBottomFace); // Side faces have horizontal normals

                // For side faces, only animate top vertices (y close to 1.0)
                float isTopVertex = step(0.5, IN.positionOS.y); // 1.0 if vertex is in upper half

                // Animate: all of top face, OR top vertices of side faces, but NEVER bottom face
                float shouldAnimate = isTopFace + (isSideFace * isTopVertex);
                shouldAnimate = saturate(shouldAnimate); // Clamp to 0-1

                pos.xz += dir * totalWave * 0.3 * shouldAnimate; // Horizontal wave movement
                pos.y += totalWave * _WindVertical * shouldAnimate; // Vertical wave movement (now safe with proper culling)

                // Transform to world and clip space
                float3 ws = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(ws);

                // For animated texture atlases (like Minecraft water), use standard UVs
                // World-space positioning is only used for wave calculations, not texture sampling
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                OUT.positionWS = ws;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.vertexColor = IN.color; // Pass vertex color to fragment

                // Calculate view direction for depth fog
                OUT.viewDir = GetWorldSpaceViewDir(ws);

                // Store depth for fog calculations
                OUT.depth = OUT.positionCS.w;

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

                // Apply vertex color shading (green/blue channels contain face shading)
                // Red channel contains water level data (used in vertex shader only)
                float vertexShade = (IN.vertexColor.g + IN.vertexColor.b) * 0.5; // Average of green/blue
                albedo.rgb *= vertexShade;

                // Simple lighting
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lighting = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // Apply lighting with water-appropriate brightness
                albedo.rgb *= lighting + 0.3; // Ambient water lighting

                // ===== DEPTH FOG EFFECT (Minecraft-style) =====
                // For TRANSPARENT water blocks, we need to darken based on how much water is below
                // This creates accumulative darkening as you look through multiple transparent layers

                // Get camera position and calculate view ray through water
                float3 cameraPos = GetCameraPositionWS();
                float3 waterPos = IN.positionWS;

                // Calculate view direction
                float3 viewRay = waterPos - cameraPos;
                float viewDistance = length(viewRay);
                float3 viewDir = normalize(viewRay);

                // CRITICAL: For transparent water, calculate how deep THIS water block is
                // This represents how much water column exists below this fragment
                float waterColumnDepth = waterPos.y; // Absolute Y position tells us depth from bottom

                // For looking from above, use vertical depth below camera
                float depthBelowCamera = max(0, cameraPos.y - waterPos.y);

                // For angled views, calculate the water column that the ray passes through
                // The steeper the angle, the more water thickness we traverse
                float rayAngle = abs(viewDir.y); // 1.0 = straight down, 0.0 = horizontal

                // Calculate effective thickness:
                // - Looking straight down: use vertical depth
                // - Looking horizontally: use view distance as thickness indicator
                float effectiveThickness = lerp(
                    viewDistance * 0.5, // Horizontal: view distance matters
                    depthBelowCamera,    // Vertical: depth below camera
                    saturate(rayAngle * 2.0) // Blend based on view angle
                );

                // Apply depth darkening - deeper water = more darkening
                // This simulates light absorption through water volume
                float depthFactor = saturate(effectiveThickness / _MaxDepthDarkening);
                depthFactor = pow(depthFactor, 1.2); // Stronger curve for more dramatic effect

                // TRANSPARENCY ACCUMULATION:
                // Instead of replacing color, we darken it additively
                // This way multiple transparent layers naturally accumulate darkness
                float darkeningAmount = depthFactor * _DepthFogIntensity;

                // Apply depth fog when NOT underwater (surface viewing)
                if (_UnderwaterFogEnabled < 0.5)
                {
                    // ===== PART 1: LAYER ABSORPTION (what we just added) =====
                    // Calculate how much THIS water block should darken the view
                    // Each 1-block layer of water absorbs some light
                    float layerAbsorption = _WaterAbsorption * _DepthFogIntensity;

                    // Darken the water color based on depth
                    // This gets darker as you look through more water layers
                    albedo.rgb = lerp(albedo.rgb, _DepthFogColor.rgb, darkeningAmount);

                    // INCREASE alpha based on depth to make deeper water more opaque
                    // This simulates light absorption - each layer blocks more light
                    float depthAlpha = saturate(darkeningAmount * 0.5 + layerAbsorption);
                    albedo.a = saturate(albedo.a + depthAlpha);

                    // ===== PART 2: VOLUMETRIC OCEAN FLOOR FOG =====
                    // Since only the SURFACE water is rendered (optimization), we calculate
                    // the water volume depth by sampling the depth buffer to find terrain below

                    // Get screen-space UV coordinates
                    float2 screenUV = IN.positionCS.xy / _ScreenParams.xy;

                    // Sample scene depth buffer (terrain/objects behind water surface)
                    float rawDepth = SampleSceneDepth(screenUV);
                    float sceneDepthEye = LinearEyeDepth(rawDepth, _ZBufferParams);

                    // Calculate water surface depth from camera
                    float waterDepthEye = IN.depth;

                    // Calculate water thickness (distance from water surface to terrain)
                    // This is the volume of water we're looking through
                    float waterThickness = max(0, sceneDepthEye - waterDepthEye);

                    // Apply volumetric fog based on water column thickness
                    // Deeper water column = more accumulated fog from ocean floor
                    float volumetricFogFactor = saturate(waterThickness / _OceanFloorFogDistance);
                    volumetricFogFactor = pow(volumetricFogFactor, 0.7); // Smooth falloff

                    // Calculate fog amount from ocean floor
                    float volumetricFogAmount = volumetricFogFactor * _OceanFloorFogIntensity;

                    // Only apply if there's actual water depth (not just at edges)
                    if (waterThickness > 0.1)
                    {
                        // Blend towards dark ocean floor fog color
                        albedo.rgb = lerp(albedo.rgb, _OceanFloorFogColor.rgb, volumetricFogAmount);

                        // Increase opacity for deeper water (simulates density)
                        albedo.a = saturate(albedo.a + volumetricFogAmount * 0.3);
                    }
                }

                // ===== UNDERWATER FOG EFFECT =====
                // Apply distance-based fog when camera is underwater
                if (_UnderwaterFogEnabled > 0.5)
                {
                    // Calculate distance from camera to fragment
                    float distance = length(IN.viewDir);

                    // Apply exponential fog
                    float fogFactor = exp(-_UnderwaterFogDensity * distance);
                    fogFactor = saturate(fogFactor);

                    // Blend with underwater fog color
                    albedo.rgb = lerp(_UnderwaterFogColor.rgb, albedo.rgb, fogFactor);
                }

                // ===== FINAL OPACITY ADJUSTMENT =====
                // Apply user-controlled opacity for side/interior water faces
                albedo.a = saturate(albedo.a * _WaterSideOpacity);

                return albedo;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}