Shader "Bar/FrostMetal"
{
 Properties {[MainColor]_BaseColor("Metal tint",Color)=(1,1,1,1) [MainTexture]_BaseMap("Metal map",2D)="white"{} _Metallic("Metallic",Range(0,1))=1 _Smoothness("Smoothness",Range(0,1))=.88 _BFrostMask("Packed frost",2D)="gray"{} _BFrost("Frost",Float)=0 _BWet("Wet",Float)=0 _Mist("Fog",Float)=0}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_BaseMap_ST,_BRange,_BWipe;float4x4 _BWorldToContainer,_BContainerToWorld;float _Metallic,_Smoothness,_BFrost,_BWet,_Mist,_BTextureTiling,_BFrostSoftness,_BContactLevel,_BWipeStrength,_BWipeFeather,_BFrostGrowthTiling,_BFrostNormalStrength,_BFrostWhiteBlend;
 CBUFFER_END
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_BFrostMask);SAMPLER(sampler_BFrostMask);
 struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;};
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 float4 packed(float3 p,float3 w,float tile){return SAMPLE_TEXTURE2D(_BFrostMask,sampler_BFrostMask,p.yz*tile)*w.x+SAMPLE_TEXTURE2D(_BFrostMask,sampler_BFrostMask,p.xz*tile)*w.y+SAMPLE_TEXTURE2D(_BFrostMask,sampler_BFrostMask,p.xy*tile)*w.z;}
 float3 frostNormal(V i)
 {
  float3 n=normalize(i.normal),local=mul(_BWorldToContainer,float4(i.world,1)).xyz;
  float3 ln=normalize(mul(transpose((float3x3)_BContainerToWorld),n));
  float3 w=pow(abs(ln),4);w/=max(dot(w,1),.001);float4 m=packed(local,w,_BTextureTiling);
  float3 detail=float3(m.b*2-1,m.a*2-1,0);float3 tangent=normalize(cross(abs(ln.y)>.9?float3(1,0,0):float3(0,1,0),ln)),bitangent=cross(ln,tangent);
  float3 localDetail=tangent*detail.x+bitangent*detail.y;
  return normalize(n+mul((float3x3)_BContainerToWorld,localDetail)*_BFrostNormalStrength*_BFrost);
 }
 // Claude 2026-10-27 (round 5): a shaken tin fogs all over, then the fog freezes into a thin velvet frost, heavier low down and in patches;
 // when it warms the frost melts back to wet, darker steel, which then dries. No frost line, no visible crystals.
 float2 hash22(float2 p){p=float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)));return frac(sin(p)*43758.5453);}
 // Round water beads: nearest random centre in a cell grid; radius varies per bead, about a third of cells stay empty.
 float bead(float2 x,out float2 toCentre){float2 c=floor(x),f=frac(x);float best=8;toCentre=0;float radius=0;
  [unroll]for(int j=-1;j<=1;j++)[unroll]for(int k=-1;k<=1;k++){float2 o=float2(k,j),r=o+hash22(c+o)-f;float dd=dot(r,r);if(dd<best){best=dd;toCentre=r;radius=lerp(.15,.42,hash22(c+o+7.1).x)*step(.33,hash22(c+o+3.7).y);}}
  toCentre/=max(radius,.05);return 1-smoothstep(.85,1,sqrt(best)/max(radius,.001));}
 float vnoise(float2 x){float2 c=floor(x),f=frac(x);f=f*f*(3-2*f);return lerp(lerp(hash22(c).x,hash22(c+float2(1,0)).x,f.x),lerp(hash22(c+float2(0,1)).x,hash22(c+1).x,f.x),f.y);}
 half4 frag(V i):SV_Target
 {
  float3 local=mul(_BWorldToContainer,float4(i.world,1)).xyz;
  float3 ln=normalize(mul(transpose((float3x3)_BContainerToWorld),normalize(i.normal)));float3 w=pow(abs(ln),4);w/=max(dot(w,1),.001);
  float R=packed(local,w,_BFrostGrowthTiling).r,mottle=packed(local+float3(.21,.63,.09),w,_BFrostGrowthTiling*.45).r;
  // Only faces that look away from the axis get fog or frost: inner wall, inner floor and rim stay bare.
  float2 radial=local.xz-_BRange.zw;float facing=dot(radial/max(length(radial),.0001),ln.xz);float outward=smoothstep(.15,.4,facing),inward=smoothstep(.15,.4,-facing);
  // Velvet frost: grains far below a millimetre leave only a faint ripple, which fades out once smaller than a pixel.
  float2 wall=float2(atan2(radial.y,radial.x)*length(radial),local.y),grainUv=wall/.0004;
  float detail=saturate(1.4-max(fwidth(grainUv.x),fwidth(grainUv.y))*1.2);float grain=lerp(.5,vnoise(grainUv)*.6+vnoise(grainUv*2.7+9.1)*.4,detail);
  float2 midUv=wall/.003;float mid=lerp(.5,vnoise(midUv+5.3),saturate(1.4-max(fwidth(midUv.x),fwidth(midUv.y))*1.2)),clump=vnoise(wall/.012);
  // Growth field: ice and liquid sit low and slosh everywhere, so the bottom and the wetted wall lead, the cap and strainer lag;
  // centimetre clumps break the front into islands. Frost grows from high values and melts back the same way.
  float h=saturate((local.y-_BRange.x)/max(.001,_BRange.y-_BRange.x));float contact=1-smoothstep(_BContactLevel,_BContactLevel+.02,local.y);
  float parts=1-.6*smoothstep(_BRange.y-.004,_BRange.y+.01,local.y);
  float g=saturate(1.25-h*.75+contact*.1)*(.6+.4*R)*parts+(clump-.5)*.12+(mid-.5)*.09;
  float3 wp=mul(_BWorldToContainer,float4(_BWipe.xyz,1)).xyz;float3 d=local-wp;float ellipse=length(d/float3(max(.001,_BWipe.w),max(.001,_BWipe.w*.65),max(.001,_BWipe.w)));
  float wipe=(1-smoothstep(.8,1.25,ellipse+(R-.5)*.35))*_BWipeStrength*outward;
  float fog=smoothstep(1-_Mist,1-_Mist+.3,lerp(.6,1,g))*_Mist*outward*(1-wipe);
  // How long ago this spot froze sets how thick it is: just past the front a thin veil, well behind it dense white.
  float age=g-(1-_BFrost);float frost=smoothstep(-.03,.12,age)*outward*(1-wipe)*lerp(1,parts,.5);
  float dense=saturate(frost*saturate(.12+age*1.6)*lerp(.6,1.1,mottle)*lerp(.8,1.15,mid));
  // Melted frost leaves water where it stood; a warm finger melts fog and frost under it.
  float wetArea=smoothstep(0,.08,g-(1-saturate(_BFrost+_BWet)));
  float wet=saturate(max(saturate(_BWet*2)*wetArea*(1-frost)*lerp(1,parts,.5),wipe*saturate((_Mist+_BFrost)*2)))*outward;fog*=1-wet;
  float3 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
  SurfaceData s=(SurfaceData)0;s.albedo=lerp(base,base*.85+float3(.08,.085,.09),fog);s.albedo*=1-.25*wet;
  // Melt water sits as a film broken into beads, so wet steel is darker and glossy but not a clean mirror.
  float2 beadUv=wall/.0015,toBead;float drop=bead(beadUv+21.7,toBead)*saturate(1.4-max(fwidth(beadUv.x),fwidth(beadUv.y))*1.2)*wet;
  // Ice crystals are white diffuse scatterers: dense frost hides the steel, a thin veil lets it show through.
  float fresnel=pow(1-saturate(dot(normalize(i.normal),GetWorldSpaceNormalizeViewDir(i.world))),3.5);
  s.albedo=lerp(s.albedo,float3(.9,.93,.96)*(.92+.16*grain),saturate(dense*_BFrostWhiteBlend))+dense*fresnel*float3(.18,.22,.26);
  // Bare metal keeps the authored material (brushed stainless: metallic .75, smoothness .8).
  s.metallic=lerp(lerp(_Metallic,_Metallic*.85,fog),.1,dense);
  s.smoothness=lerp(lerp(lerp(_Smoothness,min(_Smoothness,.62),fog),lerp(.6,.95,drop),wet),lerp(.5,.3,dense)+.08*(grain-.5),frost);
  // Inward faces see the tin, not the room: the reflection probe has no self-occlusion, so dim the inner wall's reflection with depth below the rim.
  s.occlusion=lerp(1,lerp(.7,.2,saturate((_BRange.y-local.y)/.08)),inward);s.alpha=1;s.normalTS=float3(0,0,1);
  float3 n=normalize(i.normal);float3 tangent=normalize(cross(abs(ln.y)>.9?float3(1,0,0):float3(0,1,0),ln)),bitangent=cross(ln,tangent);
  float2 tilt=(hash22(floor(grainUv))*2-1)*detail*.15*dense;tilt-=toBead*drop*.8;n=normalize(n+mul((float3x3)_BContainerToWorld,tangent*tilt.x+bitangent*tilt.y));
  InputData input=(InputData)0;input.positionWS=i.world;input.normalWS=n;input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);input.shadowCoord=TransformWorldToShadowCoord(i.world);input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=1;
  half4 color=UniversalFragmentPBR(input,s);
  // Fog droplets and ice scatter light many times, so what the tin reflects comes out desaturated and cool.
  // Inside the tin a ray bounces between steel walls before it escapes, so the inner wall shows grey steel, not the bar's colours.
  color.rgb=lerp(color.rgb,dot(color.rgb,float3(.3,.59,.11))*float3(.96,1,1.05),max(max(frost*.6,max(fog,wet)*.3),inward*.85));
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
 half4 normals(V i):SV_Target{return half4(frostNormal(i),0);}
 ENDHLSL
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass {Name "Metal and thin frost" Tags{"LightMode"="UniversalForward"} ZWrite On Cull Back HLSLPROGRAM
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
