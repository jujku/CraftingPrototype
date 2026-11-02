Shader "Bar/Bubble"
{
 Properties {_ClipSurface("Clip below liquid",Float)=1}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 #include "PresentationLighting.hlsl"
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 float _ClipSurface,_FillHeight,_MinimumDiameter;float4 _PlaneNormal,_PlanePivot;
 CBUFFER_END
 float _BarPourFeatureActive;
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.uv=a.uv;o.color=a.color;return o;}
 half4 bubble(V i):SV_Target
 {
  if(_ClipSurface>.5)clip(-dot(i.world-float3(_PlanePivot.x,_FillHeight,_PlanePivot.z),normalize(_PlaneNormal.xyz)));
  float2 uv=i.uv*2-1;float r=length(uv),aa=max(fwidth(r),.002);float edge=1-smoothstep(1-aa,1+aa,r);clip(edge-.001);
  float3 v=GetWorldSpaceNormalizeViewDir(i.world);float3 viewNormal=normalize(float3(uv,sqrt(saturate(1-r*r))));float3 n=normalize(mul((float3x3)UNITY_MATRIX_I_V,viewNormal));
  float fresnel=.02+.98*pow(1-saturate(viewNormal.z),5);float2 screen=GetNormalizedScreenSpaceUV(i.positionCS);float3 lens=SampleSceneColor(screen-uv*.004*(1-r));
  float3 reflection=BarEnvironment(n,v,i.world,.1,screen);float glint=BarWetSpecular(n,v,i.world,120).r;
  // Claude 2026-10-27: a real bubble has a dark rim (total reflection) and a bright glint up-left; it reads against light ice and dark wood alike.
  float ring=smoothstep(.45,.95,r),spot=1-smoothstep(0,.32,length(uv-float2(-.32,.36)));
  float3 color=lerp(lens*1.05,lerp(lens*.3,reflection,.35),ring)+spot*(.9+glint)+glint*.4;
  return half4(color,saturate(i.color.a*edge*(.3+ring*.6+spot*.8)));

 }
 half4 fallback(V i):SV_Target{clip(.5-_BarPourFeatureActive);return bubble(i);}
 ENDHLSL
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+40" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass {Name "Bubble fallback" Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment fallback
   ENDHLSL}
  Pass {Name "Bubbles before glass" Tags{"LightMode"="BarBubbleRefraction"} HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment bubble
   ENDHLSL}
 }
}
