#ifndef BAR_ROOM_SURFACE_INPUT
#define BAR_ROOM_SURFACE_INPUT
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
TEXTURECUBE(_RoomRadiance); SAMPLER(sampler_RoomRadiance);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST, _BaseColor, _SpecColor, _EmissionColor;
float _Cutoff, _Smoothness, _Metallic, _BumpScale;
float4 _RoomCapture, _RoomBoundsMin, _RoomBoundsMax, _RoomDecode;
float4 _RoomDiffusePX, _RoomDiffuseNX, _RoomDiffusePY, _RoomDiffuseNY, _RoomDiffusePZ, _RoomDiffuseNZ;
float _RoomDiffuseGain, _RoomSpecularGain, _RoomMipCount;
CBUFFER_END
#endif
