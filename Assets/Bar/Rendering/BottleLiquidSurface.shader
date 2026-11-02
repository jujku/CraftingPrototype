Shader "Bar/BottleLiquidSurface"
{
 Properties{_FillHeight("World level",Float)=0 _Strength("Surface highlight",Float)=.12}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+498"}
  Pass
  {
   Cull Off ZWrite Off Blend One One
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "PresentationLighting.hlsl"
   struct A{float4 positionOS:POSITION;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
   CBUFFER_START(UnityPerMaterial)float _FillHeight,_Strength;CBUFFER_END
   V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
   half4 frag(V i):SV_Target
   {
    float3 v=GetWorldSpaceNormalizeViewDir(i.world);float fresnel=.02+.98*pow(1-abs(v.y),5);
    float3 reflection=BarEnvironment(float3(0,1,0),v,i.world,.04,GetNormalizedScreenSpaceUV(i.positionCS));
    return half4((reflection*fresnel+BarWetSpecular(float3(0,1,0),v,i.world,128)) * _Strength,0);
   }
   ENDHLSL
  }
 }
}
