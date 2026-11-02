Shader "Bar/PourRibbon"
{
 Properties { [MainColor] _BaseColor("Transmission",Color)=(.9,.95,1,.45) _NoiseTex("CC0 scrolling noise",2D)="gray"{} _ImpactClip("Clip impact to liquid section",Float)=0 _ImpactCenter("Liquid section centre",Vector)=(0,0,0,0) _ImpactRadius("Liquid section radius",Float)=0 }
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+40"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
 TEXTURE2D(_NoiseTex);SAMPLER(sampler_NoiseTex);float4 _BaseColor,_ImpactCenter;float _ImpactClip,_ImpactRadius,_BarPourFeatureActive;
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.pos=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
 half4 frag(V i):SV_Target{if(_ImpactClip>.5)clip(_ImpactRadius-length(i.world.xz-_ImpactCenter.xz));float n=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,i.uv*float2(2,8)+float2(0,-_Time.y*3)).r;return half4(_BaseColor.rgb*(.85+.15*n),.42+.15*n);}
 half4 fallbackFrag(V i):SV_Target{clip(.5-_BarPourFeatureActive);return frag(i);}
 ENDHLSL
 Pass {Name "Stream fallback after glass" Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment fallbackFrag
 ENDHLSL}
 Pass {Name "Stream in glass background" Tags{"LightMode"="BarPourRefraction"} HLSLPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 ENDHLSL}
 }
}
