Shader "Bar/Glass"
{
 Properties { [MainColor]_BaseColor("Glass tint",Color)=(.85,.94,.97,1) _Refraction("Screen refraction",Float)=.012 _ThicknessMm("Thickness mm",Float)=2.7 _TintStrength("Absorption gain",Float)=.75 _FresnelPower("Fresnel power",Float)=5 _Smoothness("Smoothness",Float)=.96 _SpecularPower("Wet highlight",Float)=160 _Cull("Cull",Float)=2 _LayerOpacity("Layer opacity",Float)=1 _Mist("B condensation interface",Range(0,1))=0 _Droplets("B droplets interface",Range(0,1))=0 _ShadowStrength("Shadow coverage",Range(0,1))=.55 [HideInInspector]_ZWrite("Glass self depth",Float)=0 _ProfileSpecularAA("Profile specular antialiasing",Range(0,1))=1 _ProfileRimGain("Profile glass rim readability",Range(0,1))=.18 _ProfileBaseContrast("Profile solid base contrast",Range(0,1))=.1 _ProfileRefractionGain("Profile thickness refraction",Range(0,1))=.45 _BWaterMask("Packed water normals / height / tracks",2D)="gray"{} }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+30" "RenderType"="Transparent"}
  Pass
  {
   Tags{"LightMode"="UniversalForward"} Cull[_Cull] ZWrite[_ZWrite] Blend SrcAlpha OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   #include "PresentationLighting.hlsl"
   TEXTURE2D_X(_BarSceneWithIce); SAMPLER(sampler_BarSceneWithIce);
   float _BarSceneWithIceValid;
   // Reject undefined HDR samples rather than allowing a single invalid term
   // to contaminate glass highlights. This leaves finite lighting unchanged.
   float3 BarGlassFiniteColor(float3 c){return all(c==c)&&all(abs(c)<60000)?max(c,0):0;}
   // Claude 2026-10-29: scale a colour down (hue kept) so its brightest channel is at most m. Keeps glass terms
   // from reaching values that bloom into a flare or overflow the half-precision output.
   float3 BarGlassCap(float3 c,float m){return c*min(1,m/max(max(c.r,max(c.g,c.b)),.0001));}
   float3 BarGlassBackground(float2 uv){return _BarSceneWithIceValid>.5?SAMPLE_TEXTURE2D_X(_BarSceneWithIce,sampler_BarSceneWithIce,saturate(uv)).rgb:SampleSceneColor(saturate(uv));}
   struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 profile:COLOR;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float4 profile:TEXCOORD2;};
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Refraction,_ThicknessMm,_TintStrength,_FresnelPower,_Smoothness,_SpecularPower,_Cull,_LayerOpacity,_Mist,_Droplets,_ShadowStrength;
   float _ProfileRimGain,_ProfileBaseContrast,_ProfileRefractionGain,_ZWrite,_ProfileSpecularAA;
   float4 _ShamOptics,_ShamShape,_ShamAxis;float4x4 _ShamMeshToGlass;
   float4 _BRange,_BWipe;float4x4 _BWorldToContainer;float _BWipeStrength,_BWipeFeather,_BTextureTiling,_BMistBlurPixels,_BDropRefraction,_BRunPhase,_BRuns,_BMistTintGain,_BDropNormalGain,_BContactLevel,_BRadius,_BDropHighlight;
   CBUFFER_END
   // Trace the existing flat inner floor and its 6 mm circular fillet in metric
   // cavity space. Self depth still protects the near lip, but must not erase
   // a second interface seen through the near wall. No mesh/order-dependent overlay.
   float ShamFloor(float2 p)
   {
    float r=length(p-_ShamAxis.xy),q=clamp(r-_ShamShape.y,0,_ShamShape.z);
    return _ShamShape.x+_ShamShape.z-sqrt(max(0,_ShamShape.z*_ShamShape.z-q*q));
   }
   // Claude 2026-10-29 (user: flashing light-strip points at the left/right ends of the inner floor while moving):
   // footprint is computed in frag before any branch (a derivative taken after the early returns below is undefined
   // where neighbouring pixels take different branches, exactly along the floor ellipse); the curved fillet reflects
   // with a wider lobe (its normal turns faster than a pixel); the reflection is capped at 1 and fades at extreme grazing
   // and near the outer silhouette, where this single-bounce estimate is least reliable.
   float3 ShamInterior(float3 color,float3 background,float3 world,float3 v,float roughness,float2 uv,float transmission,float footprint,float outerNv)
   {
    if(_ShamOptics.x<.5)return color;
    float4x4 worldToGlass=mul(_ShamMeshToGlass,GetWorldToObjectMatrix());
    float3 p=mul(worldToGlass,float4(world,1)).xyz,d=mul((float3x3)worldToGlass,-v);
    if(d.y>=-.001)return color;
    float a=max(0,(_ShamShape.x+_ShamShape.z-p.y)/d.y),b=(_ShamShape.x-p.y)/d.y;
    if(b<=.0003||a>b)return color;
    // A ray through the near solid wall starts below the extended floor
    // surface, then enters air, then hits the inner floor. Find the air-to-
    // glass crossing rather than discarding that important near half.
    float lo=a,hi=b;bool found=false;
    [unroll]for(int k=0;k<16;k++)
    {
     float t=lerp(lo,hi,k/16.0);float3 q=p+d*t;
     if(q.y>=ShamFloor(q.xz)){a=t;found=true;}
    }
    if(!found)return color;
    [unroll]for(int j=0;j<12;j++)
    {
     float m=(a+b)*.5;float3 q=p+d*m;
     if(q.y>ShamFloor(q.xz))a=m;else b=m;
    }
    float t=(a+b)*.5;float3 hit=p+d*t;float2 radial=hit.xz-_ShamAxis.xy;float r=length(radial);
    float coverage=saturate((_ShamShape.y+_ShamShape.z-r)/max(footprint,.0001));
    float q=clamp(r-_ShamShape.y,0,_ShamShape.z);
    float slope=q/sqrt(max(.00000001,_ShamShape.z*_ShamShape.z-q*q));
    float3 normalLocal=normalize(float3(-radial.x*slope/max(r,.00001),1,-radial.y*slope/max(r,.00001)));
    float3 n=normalize(mul(normalLocal,(float3x3)worldToGlass));
    float floorNv=saturate(abs(dot(n,v)));float f=.04+.96*pow(1-floorNv,5);
    float3 at=world-v*t;
    float curved=saturate(q/_ShamShape.z);
    float3 reflected=BarGlassCap(BarGlassFiniteColor(BarEnvironment(n,v,at,max(roughness,lerp(.08,.4,curved)),uv)),1);
    // An air/glass interface reflects a small part of the already transmitted
    // background; it is not a shaded opaque green disc or a painted ellipse.
    float weight=coverage*f*_ShamOptics.w*transmission*smoothstep(0,.12,floorNv)*smoothstep(.06,.3,outerNv);
    float3 interfaceColor=lerp(color,reflected,weight);
    // Match the existing soft rim fill at this actual curved glass/air
    // boundary, so the pedestal remains legible with a dark environment.
    // The flat floor gets no uniform diffuse fill.
    float filletEdge=pow(1-floorNv,2)*curved*smoothstep(.06,.3,outerNv);
    // The rim of the inner floor is the main cue that the bottom is solid: a soft light line, capped and footprint-filtered.
    return interfaceColor+float3(.5,.52,.55)*max(_ProfileBaseContrast,.3)*filletEdge*coverage*_ShamOptics.w*transmission;
   }
   V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.profile=a.profile;return o;}
   half4 frag(V i):SV_Target
   {
    float3 n=SafeNormalize(i.normal);if(_Cull<1.5)n=-n;float3 v=GetWorldSpaceNormalizeViewDir(i.world);float nv=saturate(abs(dot(n,v)));
    // Only the two authored glass profiles carry alpha=0 vertex metadata.
    // Missing colours/default white keep every bottle and unrelated glass unchanged.
    float profiled=saturate(1-i.profile.a),baseWeight=i.profile.g*profiled,rimWeight=i.profile.b*profiled;
    float thickness=lerp(_ThicknessMm,i.profile.r*32,profiled);
    float sham=saturate(_ShamOptics.x)*profiled;
    float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);float2 offset=mul((float3x3)UNITY_MATRIX_V,n).xy*_Refraction*(1-nv)*(1+baseWeight*_ProfileRefractionGain)*lerp(1,_ShamOptics.z,baseWeight*sham);
    // Claude 2026-10-27 (user request): glass condensation removed. CBUFFER keeps the B fields so both passes stay identical.
    float3 background=BarGlassFiniteColor(BarGlassBackground(uv+offset));
    float3 absorption=-log(max(_BaseColor.rgb,.04))*_TintStrength;
    // Claude 2026-10-29: through the solid base the tint is mostly neutral (darker, slightly cool), so the long path
    // reads as thick glass rather than the grey-green block a cyan tint makes over brown wood.
    absorption=lerp(absorption,(absorption.r+absorption.g+absorption.b)/3,.65*baseWeight*sham);float path=thickness*.1/max(nv,.15)*lerp(1,_ShamOptics.y,baseWeight*sham);
    float fresnel=.04+.96*pow(1-nv,lerp(_FresnelPower,3.5,profiled));
    // Pixel-footprint filtering, only for the two profiled glasses: a narrow
    // lobe must not turn subpixel normal changes into flickering hot points.
    float3 nx=ddx(n),ny=ddy(n);float normalVariance=min(.08,max(dot(nx,nx),dot(ny,ny)))*profiled*saturate(_ProfileSpecularAA);
    float roughness=sqrt(saturate((1-_Smoothness)*(1-_Smoothness)+normalVariance));
    float specularPower=rcp(rcp(max(1,_SpecularPower))+.5*normalVariance);
    float3 reflection=BarGlassFiniteColor(BarEnvironment(n,v,i.world,roughness,uv));
    float3 color=background*exp(-absorption*path)*(1-fresnel)+reflection*fresnel+BarGlassCap(BarGlassFiniteColor(BarWetSpecular(n,v,i.world,specularPower)),12)*.18;
    // A restrained neutral edge fill reads the rounded lip and solid pedestal
    // against black; thickness still refracts/absorbs rather than becoming opaque.
    float edge=pow(1-nv,2.5)*_ProfileRimGain*profiled;
    float baseEdge=baseWeight*_ProfileBaseContrast*(.12+.88*pow(1-nv,2))*(1-sham);
    color+=(float3(.19,.21,.22)+reflection*.35)*(edge+baseEdge+rimWeight*.025);
    // Keep a soft cue where the solid sham meets its curved inner floor;
    // this must not depend on drawing that hidden floor over the near wall.
    float shamBoundary=4*baseWeight*(1-baseWeight)*step(6,thickness);
    color+=float3(.20,.22,.23)*shamBoundary*_ProfileBaseContrast*.5*(1-sham);
    // Claude 2026-10-29 (user: the thick base is too clear, no material): solid glass shows a soft internal reflection on the
    // sides of the base, stronger toward grazing; rough and capped, so it reads as thickness, not as a glint.
    float3 baseInternal=BarGlassCap(BarGlassFiniteColor(BarEnvironment(n,v,i.world,max(roughness,.35),uv)),.8);
    color+=baseInternal*.2*baseWeight*sham*_ShamOptics.w*(.3+.7*(1-nv));
    float footprint=length(fwidth(i.world));
    color=ShamInterior(color,background,i.world,v,roughness,uv,1-fresnel,footprint,nv);
    return half4(BarGlassCap(BarGlassFiniteColor(color),4),_LayerOpacity);
   }
   ENDHLSL
  }

  Pass
  {
   // Clear glass passes most light, so it casts a light, dithered shadow; PCF softening blends the
   // pattern. Only the outer layer casts, otherwise the inner layer would darken it a second time.
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0 Cull Off
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex shadowVert
   #pragma fragment shadowFrag
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
   struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};struct V{float4 positionCS:SV_POSITION;};
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Refraction,_ThicknessMm,_TintStrength,_FresnelPower,_Smoothness,_SpecularPower,_Cull,_LayerOpacity,_Mist,_Droplets,_ShadowStrength;
   float _ProfileRimGain,_ProfileBaseContrast,_ProfileRefractionGain,_ZWrite,_ProfileSpecularAA;
   float4 _ShamOptics,_ShamShape,_ShamAxis;float4x4 _ShamMeshToGlass;
   float4 _BRange,_BWipe;float4x4 _BWorldToContainer;float _BWipeStrength,_BWipeFeather,_BTextureTiling,_BMistBlurPixels,_BDropRefraction,_BRunPhase,_BRuns,_BMistTintGain,_BDropNormalGain,_BContactLevel,_BRadius,_BDropHighlight;
   CBUFFER_END
   float3 _LightDirection;
   float3 _LightPosition;
   V shadowVert(A a)
   {
    V o;
    float3 world=TransformObjectToWorld(a.positionOS.xyz);
    float3 normal=TransformObjectToWorldNormal(a.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 toLight=normalize(_LightPosition-world);
    #else
    float3 toLight=_LightDirection;
    #endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(world,normal,toLight));
    #if UNITY_REVERSED_Z
    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #else
    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #endif
    return o;
   }
   half4 shadowFrag(V i):SV_Target
   {
    const float bayer[16]={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
    uint2 cell=(uint2)i.positionCS.xy%4;
    float coverage=_ShadowStrength*step(.5,_LayerOpacity);
    clip(coverage-(bayer[cell.y*4+cell.x]+.5)/16);
    return 0;
   }
   ENDHLSL
  }
 }
}
