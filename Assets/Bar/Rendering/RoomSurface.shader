Shader "Bar/RoomSurface"
{
 Properties
 {
  [MainTexture]_BaseMap("Albedo",2D)="white"{}
  [MainColor]_BaseColor("Colour",Color)=(1,1,1,1)
  _Metallic("Metallic",Range(0,1))=0
  _Smoothness("Smoothness",Range(0,1))=.62
  _SpecColor("Specular",Color)=(.2,.2,.2,1)
  _BumpMap("Normal",2D)="bump"{}
  _BumpScale("Normal strength",Float)=1
  _EmissionColor("Emission",Color)=(0,0,0,1)
  _Cutoff("Cutoff",Float)=.5
  _Cull("Cull",Float)=2
  [NoScaleOffset]_RoomRadiance("Shared interior radiance",Cube)="black"{}
  _RoomCapture("Room capture position",Vector)=(0,1.44,-.51,1)
  _RoomBoundsMin("Room minimum",Vector)=(-3.25,-.01,-4.01,0)
  _RoomBoundsMax("Room maximum",Vector)=(3.25,3.1,2.99,0)
  _RoomDecode("HDR decode",Vector)=(1,1,0,0)
  [HDR]_RoomDiffusePX("Room diffuse +X",Color)=(0,0,0,1)
  [HDR]_RoomDiffuseNX("Room diffuse -X",Color)=(0,0,0,1)
  [HDR]_RoomDiffusePY("Room diffuse +Y",Color)=(0,0,0,1)
  [HDR]_RoomDiffuseNY("Room diffuse -Y",Color)=(0,0,0,1)
  [HDR]_RoomDiffusePZ("Room diffuse +Z",Color)=(0,0,0,1)
  [HDR]_RoomDiffuseNZ("Room diffuse -Z",Color)=(0,0,0,1)
  _RoomDiffuseGain("Authored indirect exposure",Float)=1
  _RoomSpecularGain("Room reflection intensity",Float)=.9
  _RoomMipCount("Room mip count",Float)=8
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass
  {
   Name "RoomForward" Tags{"LightMode"="UniversalForwardOnly"}
   Cull[_Cull] ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex RoomVertex
   #pragma fragment RoomFragment
   #pragma multi_compile_instancing
   #pragma shader_feature_local _NORMALMAP
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile _ _LIGHT_LAYERS
   #pragma multi_compile_fog
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
   #include "RoomSurfaceInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;float4 shadow:TEXCOORD4;half fog:TEXCOORD5;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   V RoomVertex(A a)
   {
    V v=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
    VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(a.normalOS,a.tangentOS);
    v.positionCS=p.positionCS;v.positionWS=p.positionWS;v.normalWS=n.normalWS;v.tangentWS=float4(n.tangentWS,a.tangentOS.w*GetOddNegativeScale());v.uv=TRANSFORM_TEX(a.uv,_BaseMap);v.shadow=GetShadowCoord(p);v.fog=ComputeFogFactor(p.positionCS.z);return v;
   }
   // One continuous room field. Bounds correct reflection parallax, never its coverage or opacity.
   half3 RoomDiffuse(half3 n)
   {
    half3 x=n.x>=0?_RoomDiffusePX.rgb:_RoomDiffuseNX.rgb;
    half3 y=n.y>=0?_RoomDiffusePY.rgb:_RoomDiffuseNY.rgb;
    half3 z=n.z>=0?_RoomDiffusePZ.rgb:_RoomDiffuseNZ.rgb;
    return (x*n.x*n.x+y*n.y*n.y+z*n.z*n.z)*_RoomDiffuseGain;
   }
   half3 RoomIndirect(BRDFData b,InputData d,half occlusion)
   {
    half3 ray=reflect(-d.viewDirectionWS,d.normalWS);
    ray=BoxProjectedCubemapDirection(ray,d.positionWS,_RoomCapture,_RoomBoundsMin,_RoomBoundsMax);
    half rough=b.perceptualRoughness;half mip=rough*(1.7h-.7h*rough)*(_RoomMipCount-1);
    half4 environmentSample=SAMPLE_TEXTURECUBE_LOD(_RoomRadiance,sampler_RoomRadiance,ray,mip);
    half3 radiance=DecodeHDREnvironment(environmentSample,_RoomDecode)*_RoomSpecularGain;
    return EnvironmentBRDF(b,RoomDiffuse(d.normalWS),radiance,Pow4(1-saturate(dot(d.normalWS,d.viewDirectionWS))))*occlusion;
   }
   void RoomFragment(V i,out half4 outColor:SV_Target0
   #ifdef _WRITE_RENDERING_LAYERS
   ,out uint outRenderingLayers:SV_Target1
   #endif
   )
   {
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    SurfaceData s=(SurfaceData)0;s.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;s.metallic=_Metallic;s.specular=_SpecColor.rgb;s.smoothness=_Smoothness;s.occlusion=1;s.alpha=1;s.emission=_EmissionColor.rgb;
    InputData inputData=(InputData)0;inputData.positionWS=i.positionWS;inputData.normalWS=normalize(i.normalWS);
    #ifdef _NORMALMAP
    half3 normalTS=SampleNormal(i.uv,TEXTURE2D_ARGS(_BumpMap,sampler_BumpMap),_BumpScale);half3 t=normalize(i.tangentWS.xyz);half3 bt=cross(inputData.normalWS,t)*i.tangentWS.w;inputData.normalWS=normalize(TransformTangentToWorld(normalTS,half3x3(t,bt,inputData.normalWS)));
    #endif
    inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);inputData.shadowMask=half4(1,1,1,1);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    inputData.shadowCoord=i.shadow;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    #endif
    BRDFData brdf;InitializeBRDFData(s,brdf);BRDFData coat=(BRDFData)0;AmbientOcclusionFactor ao=CreateAmbientOcclusionFactor(inputData,s);half4 mask=CalculateShadowMask(inputData);uint layers=GetMeshRenderingLayer();LightingData light=CreateLightingData(inputData,s);light.giColor=RoomIndirect(brdf,inputData,ao.indirectAmbientOcclusion);
    Light main=GetMainLight(inputData,mask,ao);
    #ifdef _LIGHT_LAYERS
    if(IsMatchingLightLayer(main.layerMask,layers))
    #endif
    light.mainLightColor=LightingPhysicallyBased(brdf,coat,main,inputData.normalWS,inputData.viewDirectionWS,0,false);
    #ifdef _ADDITIONAL_LIGHTS
    uint count=GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop]for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
    {
     CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
     Light item=GetAdditionalLight(lightIndex,inputData,mask,ao);
     #ifdef _LIGHT_LAYERS
     if(IsMatchingLightLayer(item.layerMask,layers))
     #endif
     light.additionalLightsColor+=LightingPhysicallyBased(brdf,coat,item,inputData.normalWS,inputData.viewDirectionWS,0,false);
    }
    #endif
    LIGHT_LOOP_BEGIN(count)
     Light item=GetAdditionalLight(lightIndex,inputData,mask,ao);
     #ifdef _LIGHT_LAYERS
     if(IsMatchingLightLayer(item.layerMask,layers))
     #endif
     light.additionalLightsColor+=LightingPhysicallyBased(brdf,coat,item,inputData.normalWS,inputData.viewDirectionWS,0,false);
    LIGHT_LOOP_END
    #endif
    outColor=CalculateFinalColor(light,1);outColor.rgb=MixFog(outColor.rgb,i.fog);
    #ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers=EncodeMeshRenderingLayer();
    #endif
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"}
   ZWrite On ZTest LEqual ColorMask 0 Cull[_Cull]
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex ShadowPassVertex
   #pragma fragment ShadowPassFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   #include "RoomSurfaceInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R Cull[_Cull]
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthOnlyVertex
   #pragma fragment DepthOnlyFragment
   #pragma multi_compile_instancing
   #include "RoomSurfaceInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On Cull[_Cull]
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthNormalsVertex
   #pragma fragment DepthNormalsFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
   #include "RoomSurfaceInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
   ENDHLSL
  }
 }
}
