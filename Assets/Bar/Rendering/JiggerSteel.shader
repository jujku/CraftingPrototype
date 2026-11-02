Shader "Bar/JiggerSteel"
{
 Properties {[MainColor]_BaseColor("Metal tint",Color)=(.55,.59,.62,1) [MainTexture]_BaseMap("Metal map",2D)="white"{} _Metallic("Metallic",Range(0,1))=.75 _Smoothness("Smoothness",Range(0,1))=.8 _SteelFinishScale("Cone finish",Range(0,1))=.96 _SteelInnerFinishScale("Inner finish",Range(0,1))=.95 _SteelHighlightKnee("Highlight knee",Float)=.18 _SteelHighlightLimit("Highlight limit",Float)=.45}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_BaseMap_ST,_SteelRange;float4x4 _SteelMeshToCup,_SteelCupToMesh;float _Metallic,_Smoothness,_SteelFinishScale,_SteelInnerFinishScale,_SteelHighlightKnee,_SteelHighlightLimit;
 CBUFFER_END
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
 struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;float3 cup:TEXCOORD4;float3 cupNormal:TEXCOORD5;};
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(o.positionCS.z);o.cup=mul(_SteelMeshToCup,a.positionOS).xyz;o.cupNormal=mul(transpose((float3x3)_SteelCupToMesh),a.normalOS);return o;}
 // Tin's bare-steel lighting/cavity treatment with a local cone finish.
 // Fixed mesh-to-cup coordinates keep it stable while held/flipped/pouring.
 half4 frag(V i):SV_Target
 {
  float3 local=i.cup;float3 ln=normalize(i.cupNormal);
  float2 radial=local.xz-_SteelRange.zw;float facing=dot(radial/max(length(radial),.0001),ln.xz);float inward=smoothstep(.15,.4,-facing);
  SurfaceData s=(SurfaceData)0;s.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;s.metallic=_Metallic;s.smoothness=saturate(_Smoothness*_SteelFinishScale*lerp(1,_SteelInnerFinishScale,inward));
  s.occlusion=lerp(1,lerp(.7,.2,saturate((_SteelRange.y-local.y)/.08)),inward);s.alpha=1;s.normalTS=float3(0,0,1);
  float3 n=normalize(i.normal);
  InputData input=(InputData)0;input.positionWS=i.world;input.normalWS=n;input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);input.shadowCoord=TransformWorldToShadowCoord(i.world);input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=1;
  half4 color=UniversalFragmentPBR(input,s);
  color.rgb=lerp(color.rgb,dot(color.rgb,float3(.3,.59,.11))*float3(.96,1,1.05),inward*.85);
  // Compress only the bright shoulder before scene tonemapping. Preserve the
  // steel hue and dark/mid reflections; no whole-object tint/exposure change.
  float luminance=dot(color.rgb,float3(.2126,.7152,.0722)),knee=max(0,_SteelHighlightKnee),span=max(.001,_SteelHighlightLimit-knee);
  float excess=max(0,luminance-knee),soft=knee+span*excess/(span+excess);
  color.rgb*=min(luminance,soft)/max(luminance,.00001);
  color.rgb=MixFog(color.rgb,i.fog);return color;
 }
 float3 _LightDirection,_LightPosition;
 V shadowVert(A a){V o=vert(a);
 #if _CASTING_PUNCTUAL_LIGHT_SHADOW
 float3 direction=normalize(_LightPosition-o.world);
 #else
 float3 direction=_LightDirection;
 #endif
 o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.world,o.normal,direction));
 #if UNITY_REVERSED_Z
 o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
 #else
 o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
 #endif
 return o;}
 half4 depth(V i):SV_Target{return 0;}
 half4 normals(V i):SV_Target{return half4(normalize(i.normal),0);}
 ENDHLSL
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass {Name "Stable bare steel" Tags{"LightMode"="UniversalForward"} ZWrite On Cull Back HLSLPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile _ _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
 #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
 #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #pragma multi_compile_fog
 ENDHLSL}
 Pass {Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ColorMask 0 Cull Back HLSLPROGRAM
 #pragma vertex shadowVert
 #pragma fragment depth
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 ENDHLSL}
 Pass {Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R Cull Back HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment depth
 ENDHLSL}
 Pass {Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On Cull Back HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment normals
 ENDHLSL}
 }
}
