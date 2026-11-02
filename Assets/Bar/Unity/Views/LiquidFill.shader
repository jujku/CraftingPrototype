Shader "Bar/LiquidFill"
{
 Properties { [MainColor] _BaseColor("Fallback",Color)=(1,1,1,.2) _FillHeight("World Y level",Float)=0 _BubbleNoise("B microbubble interface",2D)="black"{} _BubbleStrength("B microbubble strength (off in A)",Range(0,1))=0 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_Absorption,_ScatterColor,_PlanePivot;float3 _PlaneNormal;float4x4 _WorldToContainer,_ContainerToWorld;float _FillHeight,_Meniscus,_FoamBand,_Agitation,_MinPath,_MaxPath,_ScatterPath,_ScatterGain,_Turbidity,_CO2,_BubbleStrength;
 CBUFFER_END
 TEXTURE2D(_BubbleNoise);SAMPLER(sampler_BubbleNoise);
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);return o;}
 void liquid(V i,out float3 transmission,out float3 cloud,out float scatter)
 {
  float3 view=GetWorldSpaceNormalizeViewDir(i.world);float rim=1-abs(dot(normalize(i.normal),view));
  float3 local=mul(_WorldToContainer,float4(i.world,1)).xyz;
  float3 up=length(_PlaneNormal)>.5?normalize(_PlaneNormal):float3(0,1,0);
  float3 pivot=_PlanePivot.xyz;
  float3 localAnchor=mul(_WorldToContainer,float4(pivot.x,_FillHeight,pivot.z,1)).xyz;
  float3 localNormal=mul(transpose((float3x3)_ContainerToWorld),up);
  float plane=dot(local-localAnchor,localNormal);
  clip(_Meniscus*rim-plane+.00001);
  float d=lerp(_MinPath,_MaxPath,1-pow(dot(normalize(i.normal),view),2));
  transmission=exp(-_Absorption.xyz*d);cloud=_ScatterColor.rgb*exp(-_Absorption.xyz*_ScatterPath);
  scatter=saturate(_Turbidity)*(1-exp(-_ScatterGain*d));
  float foam=(1-smoothstep(0,max(.0001,_FoamBand),abs(plane)))*saturate(_Agitation+_CO2*.02);
  cloud=lerp(cloud,float3(1,1,1),foam*.5);scatter=saturate(scatter+foam*.1);
  // Reserved input for B; strength defaults to zero, so A adds no replacement bubble effect.
  float micro=SAMPLE_TEXTURE2D(_BubbleNoise,sampler_BubbleNoise,local.xz).r*_BubbleStrength;
  scatter=saturate(scatter+micro);
 }
 half4 back(V i):SV_Target{float3 t,c;float s;liquid(i,t,c,s);return half4(lerp(t,c,s),.05);}
 half4 absorb(V i):SV_Target{float3 t,c;float s;liquid(i,t,c,s);return half4(t*(1-s),1);}
 half4 scatterPass(V i):SV_Target{float3 t,c;float s;liquid(i,t,c,s);return half4(c*s,1);}
 ENDHLSL
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+498"}
 Pass {Name "Liquid back" Tags{"LightMode"="UniversalForward"} Cull Front ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment back
 ENDHLSL }
 Pass {Name "Beer absorption" Tags{"LightMode"="BarLiquidAbsorb"} Cull Back ZWrite Off ZTest LEqual Blend DstColor Zero
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment absorb
 ENDHLSL }
 Pass {Name "Cloud scattering" Tags{"LightMode"="BarLiquidScatter"} Cull Back ZWrite Off ZTest LEqual Blend One One
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment scatterPass
 ENDHLSL }
 }
}
