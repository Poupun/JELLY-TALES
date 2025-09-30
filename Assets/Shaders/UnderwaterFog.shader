Shader "Hidden/UnderwaterFog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "UnderwaterFogPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Global properties set by UnderwaterFogManager
            float _UnderwaterFogEnabled;
            float _UnderwaterFogDensity;
            float4 _UnderwaterFogColor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sample scene color
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // Only apply if underwater
                if (_UnderwaterFogEnabled < 0.5)
                    return color;

                // Sample depth
                float depth = SampleSceneDepth(IN.uv);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);

                // Calculate fog based on depth
                float fogFactor = exp(-_UnderwaterFogDensity * linearDepth);
                fogFactor = saturate(fogFactor);

                // Blend with fog color
                color.rgb = lerp(_UnderwaterFogColor.rgb, color.rgb, fogFactor);

                return color;
            }
            ENDHLSL
        }
    }
}