Shader "Bar/Ice"
{
 Properties
 {
  [MainColor]_BaseColor("Ice tint",Color)=(.87,.96,1,.18)
  _Fog("Internal fog",Float)=.8 _Core("Legacy core",Float)=0 _Smoothness("Smoothness",Float)=.94
  _NormalNoise("Normal ripple",Float)=.018 _FresnelPower("Fresnel power",Float)=5 _SpecularPower("Wet highlight",Float)=128
  _EdgeWidth("Bevel edge width",Float)=.12 _ObjectCenter("Mesh centre",Vector)=(0,0,0,0) _ObjectExtents("Mesh extents",Vector)=(1,1,1,0)
  _Ior("Ice IOR",Float)=1.31 _Absorption("Absorption per metre",Vector)=(2.8,.6,.35,0)
  _CoreRadii("Core ellipsoid radii",Vector)=(.4,.52,.4,0) _CoreDensity("Core density per metre",Float)=65 _CoreSoftness("Gaussian softness",Float)=.65
  _RefractionGain("Refraction gain",Float)=1 _Nucleation("B1 interface",Float)=0 _Frost("B2 interface",Float)=0
  _NucleationWorld("Nucleation world position/radius",Vector)=(0,0,0,.001)
  _ZWrite("Depth",Float)=1 _SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=10
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+499" "RenderType"="Transparent"}
  HLSLINCLUDE
  #include "PresentationLighting.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
  struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};
  struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 local:TEXCOORD2;};
  CBUFFER_START(UnityPerMaterial)
  float4 _BaseColor,_ObjectCenter,_ObjectExtents,_Absorption,_CoreRadii,_NucleationWorld;
  float _Fog,_Core,_Smoothness,_NormalNoise,_FresnelPower,_SpecularPower,_EdgeWidth,_Ior,_CoreDensity,_CoreSoftness,_RefractionGain,_Nucleation,_Frost;
  CBUFFER_END
  float _BarIceFeatureActive;
  TEXTURE2D_X_FLOAT(_BarIceBackgroundDepth);SAMPLER(sampler_BarIceBackgroundDepth);
  float _BarIceBackgroundDepthValid;
  float _BarIceDebug,_BarIceNoRefraction;
  V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.local=(a.positionOS.xyz-_ObjectCenter.xyz)/max(_ObjectExtents.xyz,.00001);return o;}
  // Integrate a smooth cloud along the actual world-space optical segment.
  // Radii are normalized box coordinates; ds remains metres under nonuniform transforms.
  float CloudPath(float3 origin,float3 direction,float lengthLimit)
  {
   float sum=0,ds=lengthLimit/8;
   [unroll]for(int k=0;k<8;k++)
   {
    float3 q=origin+direction*((k+.5)*ds);
    float3 scaled=q/max(_CoreRadii.xyz,.001);
    float density=exp(-dot(scaled,scaled)/(2*max(_CoreSoftness*_CoreSoftness,.01)));
    float noise=1+.10*sin(dot(q,float3(29,17,23)))*sin(dot(q,float3(13,-31,19)));
    sum+=density*noise*ds;
   }
   return sum;
  }
  float BoxExit(float3 origin,float3 direction,out float3 normal)
  {
   float3 safe=direction+step(abs(direction),.000001)*.000001;
   float3 t=(sign(safe)-origin)/safe;
   float3 axis=step(t,t.yzx)*step(t,t.zxy);
   normal=TransformObjectToWorldNormal(axis*sign(direction)/max(_ObjectExtents.xyz,.00001));
   return max(.000001,min(t.x,min(t.y,t.z)));
  }
  half4 IceColor(V i,bool sceneRefraction)
  {
   float3 ripple=sin(i.local.yzx*19+cos(i.local.zxy*11));
   float3 n=normalize(i.normal+ripple*_NormalNoise),v=GetWorldSpaceNormalizeViewDir(i.world);
   float nv=saturate(abs(dot(n,v))),fresnel=.02+.98*pow(1-nv,_FresnelPower);
   float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
   float3 env=BarEnvironment(n,v,i.world,1-_Smoothness,uv),ambient=SampleSH(n),spec=BarWetSpecular(n,v,i.world,_SpecularPower);
   float3 inside=refract(-v,n,1/max(_Ior,1.001));
   float3 direction=mul((float3x3)unity_WorldToObject,inside)/max(_ObjectExtents.xyz,.00001);
   float3 exitNormal;
   float distance=BoxExit(i.local,direction,exitNormal);
   float3 exitLocal=i.local+direction*distance;
   float3 exitWorld=i.world+inside*distance;
   float3 outside=refract(inside,-exitNormal,max(_Ior,1.001));
   bool tir=dot(outside,outside)<.00001;
   float corePath=CloudPath(i.local,direction,distance);
   if(tir)
   {
    float3 bounced=reflect(inside,exitNormal);
    float3 bounceDirection=mul((float3x3)unity_WorldToObject,bounced)/max(_ObjectExtents.xyz,.00001);
    // Move a small world-space epsilon inward to avoid re-hitting the same face.
    float3 origin=exitLocal+bounceDirection*.000001;
    float secondDistance=BoxExit(origin,bounceDirection,exitNormal);
    corePath+=CloudPath(origin,bounceDirection,secondDistance);
    exitWorld+=bounced*(secondDistance+.000001);
    distance+=secondDistance;
    outside=refract(bounced,-exitNormal,max(_Ior,1.001));
   }
   bool stillTir=dot(outside,outside)<.00001;
   bool sampledScene=false;
   float3 transmitted=BarEnvironment(exitNormal,-inside,exitWorld,1-_Smoothness,uv);
   if(sceneRefraction&&!stillTir)
   {
    // The foreground camera clears depth so held tools stay on top. Its depth
    // is not the background behind an ice cube; retain the base camera's sample.
    float raw=_BarIceBackgroundDepthValid>.5?SAMPLE_TEXTURE2D_X_LOD(_BarIceBackgroundDepth,sampler_BarIceBackgroundDepth,uv,0).r:SampleSceneDepth(uv);
    #if !UNITY_REVERSED_Z
    raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
    #endif
    float3 background=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
    float depthDistance=clamp(length(background-exitWorld),.02,2);
    float4 projected=TransformWorldToHClip(exitWorld+outside*depthDistance);
    // Invert ComputeWorldSpacePosition with the matching SRP helper, including API Y orientation.
    float2 projectedUv=ComputeNormalizedDeviceCoordinatesWithZ(exitWorld+outside*depthDistance,UNITY_MATRIX_VP).xy;
    float2 refractedUv=lerp(uv,projectedUv,_RefractionGain);
    if(projected.w>0&&all(refractedUv>=0)&&all(refractedUv<=1)){transmitted=SampleSceneColor(refractedUv);sampledScene=true;}
   }
   float3 attenuation=exp(-max(_Absorption.xyz,0)*distance);
   float fog=1-exp(-corePath*_CoreDensity*_Fog);
   // B1 and B2 extend these zero-initialized inputs without replacing this optical path.
   float seed=1-smoothstep(0,max(_NucleationWorld.w,.0001),length(i.world-_NucleationWorld.xyz));
   fog=saturate(fog+_Nucleation*.25*seed+_Frost);
   if(_BarIceDebug>6.5)return half4(SampleSceneColor(uv),1);
   if(_BarIceDebug>5.5)return stillTir?half4(1,0,0,1):sampledScene?half4(0,0,1,1):half4(0,1,0,1);
   if(_BarIceDebug>4.5)return half4(1,0,1,1);
   if(_BarIceDebug>3.5)return half4(fog.xxx,1);
   if(_BarIceDebug>2.5)return half4(attenuation,1);
   if(_BarIceDebug>1.5)return tir?half4(1,0,0,1):half4(0,1,0,1);
   if(_BarIceDebug>.5)return half4(transmitted,1);
   float3 a=abs(i.local);float hi=max(a.x,max(a.y,a.z)),lo=min(a.x,min(a.y,a.z));float mid=a.x+a.y+a.z-hi-lo;
   float edge=smoothstep(1-_EdgeWidth,1-_EdgeWidth*.15,mid);
   Light main=GetMainLight();
   float3 cloudLight=ambient*.7+.35+main.color*(.25+.25*saturate(dot(n,main.direction)))*main.distanceAttenuation;
   float3 color=lerp(transmitted*attenuation,float3(.90,.96,1)*cloudLight,fog*.7);
   color=lerp(color,env, saturate(fresnel+edge*.22))+spec*.45;
   // Scene colour has already included the transmitted background; premultiply the full optical result.
   float alpha=sceneRefraction?.96:saturate(_BaseColor.a+fog*.55+edge*.55+fresnel*.45);
   return half4(color*alpha,alpha);
  }
  half4 fallbackFrag(V i):SV_Target {clip(.5-_BarIceFeatureActive);return IceColor(i,false);}
  half4 refractFrag(V i):SV_Target
  {
   // Smoke-only ablation: skip the entire refractive/volumetric path, keep an environment shell.
   if(_BarIceNoRefraction>.5)
   {
    float3 n=normalize(i.normal),v=GetWorldSpaceNormalizeViewDir(i.world);
    float f=.02+.98*pow(1-saturate(abs(dot(n,v))),5);
    float alpha=saturate(_BaseColor.a+.1+f*.45);
    float3 env=BarEnvironment(n,v,i.world,1-_Smoothness,GetNormalizedScreenSpaceUV(i.positionCS));
    return half4(_BaseColor.rgb*(SampleSH(n)*.55+.06)*alpha+env*f+BarWetSpecular(n,v,i.world,_SpecularPower)*.45,alpha);
   }
   return IceColor(i,true);
  }
  ENDHLSL
  Pass
  {
   Name "Environment fallback" Tags{"LightMode"="UniversalForward"} Cull Back ZWrite[_ZWrite] Blend One OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment fallbackFrag
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   ENDHLSL
  }
  Pass
  {
   Name "Refractive ice" Tags{"LightMode"="BarIceRefraction"} Cull Back ZWrite[_ZWrite] Blend One OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment refractFrag
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   ENDHLSL
  }
 }
}
