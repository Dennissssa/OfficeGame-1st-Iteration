Shader "Hidden/Yeah/CRTTelevision"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BlitTexture ("Blit", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "CRTRuntime"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define SAMPLE_CRT(uv) SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv)
            #include "CRTTelevision.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return CRTFragment(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CRTEditorPreview"
            HLSLPROGRAM
            #pragma vertex VertPreview
            #pragma fragment FragPreview
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct AttributesPreview
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VaryingsPreview
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            VaryingsPreview VertPreview(AttributesPreview input)
            {
                VaryingsPreview output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                if (_ProjectionParams.x < 0.0)
                    output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            #define SAMPLE_CRT(uv) SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv)
            #include "CRTTelevision.hlsl"

            float4 FragPreview(VaryingsPreview input) : SV_Target
            {
                return CRTFragment(input.uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
