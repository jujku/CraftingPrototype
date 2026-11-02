Shader "Bar/CueSurface"
{
 Properties { _Strength("Intensity",Range(0,1))=0 _Kind("0 frost / 1 droplets",Float)=0 }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" } Cull Back ZWrite Off ZTest LEqual Offset -1,-1 Blend SrcAlpha OneMinusSrcAlpha
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
 CBUFFER_START(UnityPerMaterial) float _Strength; float _Kind; CBUFFER_END
 V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 half4 frag(V i):SV_Target
 {
   float2 p=i.world.xy*240+i.world.zy*130;
   float2 cell=floor(p),v=frac(p)-.5;float h=hash(cell);
   float frost=smoothstep(.1,.7,h)*(.45+.55*sin(p.x*2)*sin(p.y*3));
   float drop=1-smoothstep(.09,.22,length(v-float2(h-.5,hash(cell+2)-.5)*.4));
   float alpha=lerp(frost,drop*step(h, min(.95,_Strength+.1)),_Kind)*_Strength;
   return half4(lerp(float3(.87,.95,1),float3(.74,.87,.94),_Kind),alpha);
 }
 ENDHLSL }
 }
}
