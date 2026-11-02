Shader "Bar/CueLiquid"
{
 Properties { [MainColor] _BaseColor("Fallback",Color)=(1,1,1,.2) _FillHeight("World Y level",Float)=0 _BubbleNoise("B microbubble interface",2D)="black"{} _BubbleStrength("B microbubble strength (off in A)",Range(0,1))=0 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial)
  float4 _BaseColor,_Absorption,_ScatterColor,_PlanePivot,_FoamColor,_BubbleAxis;float3 _PlaneNormal;float4x4 _WorldToContainer,_ContainerToWorld;float _FillHeight,_Meniscus,_FoamBand,_Agitation,_MinPath,_MaxPath,_ScatterPath,_ScatterGain,_Turbidity,_CO2,_BubbleStrength,_BubbleTiling,_FoamHeight;
 CBUFFER_END
 TEXTURE2D(_BubbleNoise);SAMPLER(sampler_BubbleNoise);
 // Cheap trilinear value noise for foam pores (no texture, WebGL-safe).
 float hash3(float3 c){return frac(sin(dot(c,float3(127.1,311.7,74.7)))*43758.5453);}
 float vnoise(float3 p){float3 c=floor(p),f=frac(p);f=f*f*(3-2*f);
  return lerp(lerp(lerp(hash3(c),hash3(c+float3(1,0,0)),f.x),lerp(hash3(c+float3(0,1,0)),hash3(c+float3(1,1,0)),f.x),f.y),
              lerp(lerp(hash3(c+float3(0,0,1)),hash3(c+float3(1,0,1)),f.x),lerp(hash3(c+float3(0,1,1)),hash3(c+float3(1,1,1)),f.x),f.y),f.z);}
 // Unresolved pores converge to their mean rather than turning subpixel
 // samples into a television-static pattern at oblique viewing angles.
 float foamNoise(float3 p)
 {
  float footprint=max(length(ddx(p)),length(ddy(p)));
  return lerp(.5,vnoise(p),1-smoothstep(.35,1,footprint));
 }
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
  scatter=1-exp(-max(0,_Turbidity)*_ScatterGain*d);
  // Carbonation is drawn by BubbleStreams' rising, round particles. The old
  // static cylindrical dot grid mixed angular and metre scales, stretching
  // dots into bright vertical scratches on the liquid wall. Keep its legacy
  // CBUFFER inputs for compatibility, but do not paint a second white coat.
  // Claude 2026-10-27: light enters through the surface, so a cloudy drink is brighter near the top and dims with depth.
  float depth=max(0,-plane);cloud*=lerp(.42,1.1,exp(-depth*35));
  // Foam sits on the surface down to an uneven lower edge; fine pores at two scales, lit from above.
  float co2Ring=_FoamBand*saturate(_CO2/6)*.15,foamBand=max(_FoamHeight,co2Ring);
  float pores=foamNoise(local*900)*.6+foamNoise(local*2600)*.4,lower=foamBand*(.7+.6*foamNoise(local*float3(260,40,260)));
  float foam=(1-smoothstep(lower*.75,lower,depth))*step(.00002,foamBand);
  // Without shaken foam, a soda only gathers a broken ring of small bubbles at the meniscus, not a solid white band.
  if(_FoamHeight<co2Ring)
  {
   // The baked cap shares this material with the liquid wall. Depth alone
   // selects the entire cap (including its underside), not a meniscus ring.
   // Keep soda's broken foam on the actual wall at the level. Shaken foam
   // still covers the top and retains its existing height/transfer rules.
   float wall=1-smoothstep(.95,.999,abs(dot(normalize(i.normal),up)));
   foam*=wall*smoothstep(.4,.62,pores)*.7;
  }
  float3 foamColor=_FoamColor.rgb*(.78+.3*pores)*lerp(.85,1.05,saturate(1-depth/max(.0001,foamBand)));
  cloud=lerp(cloud,foamColor,foam);scatter=saturate(scatter+foam*.97);

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
