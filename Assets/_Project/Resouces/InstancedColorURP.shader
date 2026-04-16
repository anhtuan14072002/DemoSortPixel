Shader "Custom/InstancedColorURP"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 🔥 per-instance color
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);

                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.posWS = posWS;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 lightDir = normalize(float3(0.3, 1, 0.5));
                float NdotL = saturate(dot(IN.normalWS, lightDir));

                float4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);

                // 🔥 simple lighting
                float3 finalColor = color.rgb * (0.4 + NdotL * 0.6);

                return float4(finalColor, 1);
            }

            ENDHLSL
        }
    }
}