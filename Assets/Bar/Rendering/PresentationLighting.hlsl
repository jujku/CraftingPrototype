#ifndef BAR_PRESENTATION_LIGHTING_INCLUDED
#define BAR_PRESENTATION_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
half3 BarEnvironment(float3 n,float3 v,float3 p,float roughness,float2 screenUV)
{
    return GlossyEnvironmentReflection(reflect(-v,n),p,roughness,1,screenUV);
}
half3 BarWetSpecular(float3 n,float3 v,float3 p,float power)
{
    Light main=GetMainLight();half3 value=main.color*pow(saturate(dot(n,normalize(main.direction+v))),power)*main.distanceAttenuation;
    #ifdef _ADDITIONAL_LIGHTS
    InputData inputData=(InputData)0;inputData.positionWS=p;float4 clip=TransformWorldToHClip(p);inputData.normalizedScreenSpaceUV=ComputeScreenPos(clip).xy/clip.w;
    uint count=GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
    Light light=GetAdditionalLight(lightIndex,p);value+=light.color*pow(saturate(dot(n,normalize(light.direction+v))),power)*light.distanceAttenuation;
    LIGHT_LOOP_END
    #endif
    return value;
}
#endif
