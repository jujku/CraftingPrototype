Shader "Hidden/Bar/CloseoutLegacyIce"
{
 Properties { [MainColor]_BaseColor("Ice tint",Color)=(.87,.96,1,.3) _Fog("Internal fog",Float)=.8 _Core("Frost core layer",Float)=0 _Smoothness("Smoothness",Float)=.94 _NormalNoise("Normal ripple",Float)=.018 _FresnelPower("Fresnel power",Float)=4 _SpecularPower("Wet highlight",Float)=128 _EdgeWidth("Bevel edge width",Float)=.25 _ObjectCenter("Mesh centre",Vector)=(0,0,0,0) _ObjectExtents("Mesh extents",Vector)=(1,1,1,0) _ZWrite("Depth",Float)=1 _SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=10 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+499" "RenderType"="Transparent"}
  Pass
  {
   Tags{"LightMode"="UniversalForward"} Cull Back ZWrite[_ZWrite] Blend[_SrcBlend][_DstBlend]
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "../../Rendering/PresentationLighting.hlsl"
   struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 local:TEXCOORD2;};
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_ObjectCenter,_ObjectExtents;float _Fog,_Core,_Smoothness,_NormalNoise,_FresnelPower,_SpecularPower,_EdgeWidth;
   CBUFFER_END
   float _BarIceDebug;
   V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.local=(a.positionOS.xyz-_ObjectCenter.xyz)/max(_ObjectExtents.xyz,.00001);return o;}
   // Output is premultiplied (Blend One OneMinusSrcAlpha): reflections and highlights add light
   // even where the ice transmits almost everything, which is what makes a clear cube read as solid.
   half4 frag(V i):SV_Target
   {
    if(_BarIceDebug>4.5)return half4(1,0,1,1);
    float3 ripple=sin(i.local.yzx*19+cos(i.local.zxy*11));
    float3 n=normalize(i.normal+ripple*_NormalNoise);
    float3 v=GetWorldSpaceNormalizeViewDir(i.world);
    float nv=saturate(abs(dot(n,v)));
    float fresnel=.02+.98*pow(1-nv,_FresnelPower);
    float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
    float3 env=BarEnvironment(n,v,i.world,1-_Smoothness,uv);
    float3 ambient=SampleSH(n);
    float3 spec=BarWetSpecular(n,v,i.world,_SpecularPower);
    if(_Core>.5)
    {
     // Suspended frost: densest where the view crosses the middle of the core, feathered at its silhouette.
     float coreAlpha=saturate(_BaseColor.a*_Fog*pow(nv,1.2)*1.6);
     float3 frost=_BaseColor.rgb*(ambient*1.5+.55)+spec*.1;
     return half4(frost*coreAlpha,coreAlpha);
    }
    // On a face the second-largest box coordinate only approaches 1 near an edge, so this marks the
    // bevels, where total internal reflection makes real ice look silvery.
    float3 a=abs(i.local);
    float hi=max(a.x,max(a.y,a.z));
    float lo=min(a.x,min(a.y,a.z));
    float mid=a.x+a.y+a.z-hi-lo;
    float edge=smoothstep(1-_EdgeWidth,1-_EdgeWidth*.15,mid);
    float3 body=_BaseColor.rgb*(ambient*.55+.06);
    float3 rim=float3(.93,.97,1)*(ambient*1.3+.3);
    float alpha=saturate(_BaseColor.a+edge*.55+fresnel*.45);
    float3 color=lerp(body,rim,edge)*alpha+env*(fresnel+edge*.35)+spec*.9;
    return half4(color,alpha);
   }
   ENDHLSL
  }
 }
}
