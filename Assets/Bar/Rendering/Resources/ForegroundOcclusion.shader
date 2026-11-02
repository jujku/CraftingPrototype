Shader "Bar/ForegroundOcclusion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        // The world camera keeps the table receiver's depth/normal footprint for SSAO.
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        Pass
        {
            Name "No visible color"
            Tags { "LightMode"="UniversalForward" }
            ColorMask 0 ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 position:POSITION):SV_POSITION { return TransformObjectToHClip(position.xyz); }
            half4 frag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
