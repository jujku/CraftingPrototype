Shader "Bar/IceTransmission"
{
 Properties{_Intensity("Transmitted light",Float)=.24}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+496"}
  Pass
  {
   ZWrite Off Cull Off Blend One One
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
   CBUFFER_START(UnityPerMaterial)float _Intensity;CBUFFER_END
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;return o;}
   half4 frag(V i):SV_Target{float r=length(i.uv*2-1);clip(1-r);return half4(float3(.7,.9,1)*exp(-r*r*5)*_Intensity,0);}
   ENDHLSL
  }
 }
}
