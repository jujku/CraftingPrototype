using PostVolumeProfile=UnityEngine.Rendering.VolumeProfile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Bar.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity
{
    /// <summary>新增表现场景的画质入口；灯光、Volume 和烘焙探针在预制体中可编辑，不重写用户原场景。</summary>
    [ExecuteAlways,DefaultExecutionOrder(-250)]public sealed class PresentationRig : MonoBehaviour
    {
        public UniversalRenderPipelineAsset DesktopPipeline,WebPipeline;
        public PostVolumeProfile PostProfile;
        public Cubemap BakedReflection;public ReflectionProbe BakedProbe;
        public GinzaEnvironmentTag Environment;
        public string Tier{get;private set;}
        private readonly List<Material> softened=new List<Material>();
        private readonly List<Material> profileGlassDepth=new List<Material>();
        private UniversalRenderPipelineAsset profilePipeline;private IceRefractionFeature[] profileFeatures=System.Array.Empty<IceRefractionFeature>();
        private readonly List<(MeshRenderer renderer,Material[] original,Material[] depth)> profileGlassRenderers=new List<(MeshRenderer,Material[],Material[])>();
        private int profileDepthState=-1;
        private void Awake(){RestoreProbe();if(Application.isPlaying){Apply(Application.platform==RuntimePlatform.WebGLPlayer);Soften(PresentationSettings.Load().soften);ConfigureSolidProfileGlass();}}
        /// <summary>只给新实体玻璃杯的外层材质副本写深度，防止内壁/内底依三角形顺序覆盖前侧口沿；液体与冰已在此前绘制和拷贝。</summary>
        private void ConfigureSolidProfileGlass()
        {
            var sham=ShamOpticsSettings.Load();
            foreach(var view in FindObjectsByType<ItemView>(FindObjectsSortMode.None))
            {
                if(view.DefinitionId!="glass.highball"&&view.DefinitionId!="glass.coupe")continue;
                var copies=new Dictionary<Material,Material>();
                foreach(var renderer in view.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials=renderer.sharedMaterials;var original=(Material[])materials.Clone();bool changed=false;
                    for(int i=0;i<materials.Length;i++)
                    {
                        var m=materials[i];if(!m||m.shader.name!="Bar/Glass"||m.GetFloat("_Cull")<1.5f||m.GetFloat("_LayerOpacity")<.99f)continue;
                        if(!copies.TryGetValue(m,out var copy))
                        {
                            copy=new Material(m){name=m.name+" (solid profile depth)"};copy.SetFloat("_ZWrite",1);copies[m]=copy;softened.Add(copy);profileGlassDepth.Add(copy);
                            if(view.DefinitionId=="glass.highball")
                            {
                                var binding=view.GetComponent<ContainerVisual>();var shape=binding.Shape;
                                copy.SetVector("_ShamOptics",new Vector4(1,sham.absorptionScale,sham.refractionScale,sham.internalReflection));
                                copy.SetVector("_ShamShape",new Vector4(shape.Bottom,shape.RadiusAt(shape.Bottom),sham.innerFilletMetres,0));
                                copy.SetVector("_ShamAxis",new Vector4(shape.Axis.x,shape.Axis.y,0,0));
                                // Mesh-to-cavity stays constant when the glass is held or moved;
                                // the shader combines it with this draw's Unity object matrix.
                                copy.SetMatrix("_ShamMeshToGlass",binding.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix);
                            }
                        }
                        materials[i]=copy;changed=true;
                    }
                    if(changed){renderer.sharedMaterials=materials;profileGlassRenderers.Add((renderer,original,materials));}
                }
            }
            RenderPipelineManager.beginCameraRendering+=UpdateProfileGlassDepth;
            UpdateProfileGlassDepth(default,null);
        }
        private void UpdateProfileGlassDepth(ScriptableRenderContext context,Camera camera)
        {
            if(profileGlassDepth.Count==0)return;
            var current=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(current!=profilePipeline)
            {
                profilePipeline=current;
                var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var data=current?(ScriptableRendererData[])field.GetValue(current):System.Array.Empty<ScriptableRendererData>();
                profileFeatures=data.Where(d=>d).SelectMany(d=>d.rendererFeatures).OfType<IceRefractionFeature>().ToArray();
            }
            // Without the before-glass scene copy the established fallback stream
            // draws afterwards. Preserve that degraded path instead of hiding it
            // behind the glass depth. Full Desktop/Web rendering keeps self depth.
            bool ready=profileFeatures.Any(f=>f&&f.isActive)&&!IceRefractionFeature.ProfileWithoutCopy&&(!camera||camera.cameraType!=CameraType.Preview);
            // Keep immutable render-state variants: URP may cache a renderer's
            // depth state while its renderer data is rebuilt for fallback.
            if(profileDepthState==(ready?1:0))return;profileDepthState=ready?1:0;
            foreach(var entry in profileGlassRenderers)if(entry.renderer)entry.renderer.sharedMaterials=ready?entry.depth:entry.original;
        }
        private void LateUpdate(){if(Application.isPlaying)UpdateProfileGlassDepth(default,null);}
        private void OnDestroy(){RenderPipelineManager.beginCameraRendering-=UpdateProfileGlassDepth;foreach(var material in softened)if(material)Destroy(material);softened.Clear();profileGlassDepth.Clear();profileGlassRenderers.Clear();}
        private void OnEnable()=>RestoreProbe();
        private void RestoreProbe(){if(BakedProbe&&BakedReflection)BakedProbe.bakedTexture=BakedReflection;}
        [ContextMenu("预览 Desktop 画质")]public void Desktop()=>Apply(false);
        [ContextMenu("预览 Web 画质")]public void Web()=>Apply(true);
        public void Apply(bool web)
        {
            Tier=web?"Web":"Desktop";int index=Array.IndexOf(QualitySettings.names,Tier);if(index>=0)QualitySettings.SetQualityLevel(index,true);
            QualitySettings.renderPipeline=web?WebPipeline:DesktopPipeline;QualitySettings.antiAliasing=4;
            if(Application.isPlaying)LiquidRenderSetup.Install();
        }
        /// <summary>
        /// 2026-10-28 用户试玩：台面中间那道高光偏刺眼，检视时量酒器内壁的反光更刺眼。只在运行时按 BarPresentationSettings.json 的 soften 段
        /// 整体压一点灯光、放软聚光灯边缘、降低环境反射，并给台面和量酒器换上限制了光滑度的材质副本；不改场景、预制体和材质资源。
        /// 参数随 presentationSettingsSHA256 记入日志头。
        /// </summary>
        public void Soften(SoftenPreset p)
        {
            if(p==null)return;
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None)){if(light.GetComponentInParent<GinzaEnvironmentTag>())continue;light.intensity*=p.lightScale;if(light.type==LightType.Spot&&p.innerSpotRatio>0)light.innerSpotAngle=light.spotAngle*p.innerSpotRatio;}
            RenderSettings.reflectionIntensity*=p.reflectionScale;foreach(var probe in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))probe.intensity*=p.reflectionScale;
            // Use the actual tin material as the jigger's steel reference. Its
            // authored colour/map/metallic/smoothness remain the single source.
            var tin=FindObjectsByType<ItemView>(FindObjectsSortMode.None).FirstOrDefault(v=>v.DefinitionId=="shaker.cobbler");
            var tinSteel=tin?tin.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="TinShell_RolledRim").SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.HasProperty("_Metallic")):null;
            var steel=new Dictionary<Material,Material>();
            foreach(var view in FindObjectsByType<ItemView>(FindObjectsSortMode.None))if(view.DefinitionId=="jigger.dual")
            foreach(var renderer in view.GetComponentsInChildren<MeshRenderer>(true))if(renderer.name=="Shell")
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)if(slots[i]&&slots[i].shader.name=="Universal Render Pipeline/Lit")
                {
                    var original=slots[i];
                    if(!steel.TryGetValue(original,out var material))
                    {
                        material=new Material(Resources.Load<Material>("JiggerSteelRuntime"));material.CopyPropertiesFromMaterial(p.jiggerUseShakerSteel&&tinSteel?tinSteel:original);material.name=original.name+" (jigger steel from tin)";
                        if(!p.jiggerUseShakerSteel||!tinSteel){material.SetColor("_BaseColor",p.jiggerSteelColor);material.SetFloat("_Metallic",p.jiggerMetallic);material.SetFloat("_Smoothness",p.jiggerSmoothness);}
                        material.SetFloat("_SteelFinishScale",p.jiggerFinishScale);material.SetFloat("_SteelInnerFinishScale",p.jiggerInnerFinishScale);
                        material.SetFloat("_SteelHighlightKnee",p.jiggerHighlightKnee);material.SetFloat("_SteelHighlightLimit",p.jiggerHighlightLimit);
                        steel[original]=material;softened.Add(material);
                    }
                    slots[i]=material;
                }
                renderer.sharedMaterials=slots;
                // Bind each cup separately. CuePresenter shares its other steel
                // blocks across the jigger; the two cup coordinate frames differ.
                var binding=renderer.GetComponentInParent<ContainerVisual>();
                if(!binding)binding=view.GetComponentsInChildren<ContainerVisual>(true).Where(b=>b.InnerCavity&&b.InnerCavity.sharedMesh).OrderBy(b=>(b.InnerCavity.transform.TransformPoint(b.InnerCavity.sharedMesh.bounds.center)-renderer.bounds.center).sqrMagnitude).FirstOrDefault();
                if(binding)
                {
                    var shape=binding.Shape;var meshToCup=binding.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetMatrix("_SteelMeshToCup",meshToCup);block.SetMatrix("_SteelCupToMesh",meshToCup.inverse);
                    block.SetVector("_SteelRange",new Vector4(shape.Bottom,shape.Top,shape.Axis.x,shape.Axis.y));renderer.SetPropertyBlock(block);
                }
            }
            if(p.smoothnessCaps==null)return;var copies=new Dictionary<Material,Material>();
            foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))foreach(var cap in p.smoothnessCaps)
            {
                if(renderer.name!=cap.renderer||!string.IsNullOrEmpty(cap.item)&&renderer.GetComponentInParent<ItemView>()?.DefinitionId!=cap.item)continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var m=materials[i];if(!m||!m.HasProperty("_Smoothness")||m.GetFloat("_Smoothness")<=cap.cap)continue;
                    if(!copies.TryGetValue(m,out var copy)){copy=new Material(m){name=m.name+" (soft)"};copy.SetFloat("_Smoothness",cap.cap);copies[m]=copy;softened.Add(copy);}
                    materials[i]=copy;
                }
                renderer.sharedMaterials=materials;break;
            }
        }
        [Serializable]private sealed class QualityState{public string tier,colorSpace;public int msaa;public bool opaque,depth,hdr,post;public float scale,shadowDistance;}
        [Serializable]private sealed class PostState{public bool present;public string tonemapping;public float bloomThreshold,bloomIntensity,exposure,contrast,saturation;}
        private static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        /// <summary>对实际生效的画质和后处理值做规范化快照；不依赖易变的 Unity InstanceID。</summary>
        public static void FillHeader(SessionHeader header,Camera camera)
        {
            var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            header.qualityTier=rig?rig.Tier:QualitySettings.names[QualitySettings.GetQualityLevel()];
            var quality=new QualityState{tier=header.qualityTier,colorSpace=QualitySettings.activeColorSpace.ToString(),msaa=pipeline?pipeline.msaaSampleCount:QualitySettings.antiAliasing,opaque=pipeline&&pipeline.supportsCameraOpaqueTexture,depth=pipeline&&pipeline.supportsCameraDepthTexture,hdr=pipeline&&pipeline.supportsHDR,scale=pipeline?pipeline.renderScale:1,shadowDistance=pipeline?pipeline.shadowDistance:QualitySettings.shadowDistance,post=camera&&camera.GetUniversalAdditionalCameraData().renderPostProcessing};
            var post=new PostState();var profile=rig?rig.PostProfile:null;if(rig){var volume=rig.GetComponent<Volume>();if(volume&&volume.HasInstantiatedProfile())profile=volume.profile;}
            if(profile)
            {
                post.present=true;if(profile.TryGet<Tonemapping>(out var tone))post.tonemapping=tone.mode.value.ToString();
                if(profile.TryGet<Bloom>(out var bloom)){post.bloomThreshold=bloom.threshold.value;post.bloomIntensity=bloom.intensity.value;}
                if(profile.TryGet<ColorAdjustments>(out var color)){post.exposure=color.postExposure.value;post.contrast=color.contrast.value;post.saturation=color.saturation.value;}
            }
            header.qualitySettingsJson=JsonUtility.ToJson(quality);header.postProcessingProfileJson=JsonUtility.ToJson(post);
            header.qualitySettingsSHA256=Hash(header.qualitySettingsJson);header.postProcessingProfileSHA256=Hash(header.postProcessingProfileJson);
            var data=Resources.Load<TextAsset>("BarPresentationSettings");header.presentationSettingsSHA256=data?Hash(data.text):null;var hold=HoldViewSettings.Source;header.holdViewSettingsSHA256=hold?Hash(hold.text):null;
            if(rig&&rig.Environment)header.dataHashes=(header.dataHashes??Array.Empty<DataHash>()).Concat(new[]{SessionLogWriter.Hash("GinzaEnvironmentManifest.json",rig.Environment.manifest.bytes)}).ToArray();
            var sham=Resources.Load<TextAsset>("BarShamOptics");if(sham)header.dataHashes=(header.dataHashes??Array.Empty<DataHash>()).Concat(new[]{SessionLogWriter.Hash("BarShamOptics.json",sham.bytes)}).ToArray();
            var bottleOptics=Resources.Load<TextAsset>("BarBottleOptics");if(bottleOptics)header.dataHashes=(header.dataHashes??Array.Empty<DataHash>()).Concat(new[]{SessionLogWriter.Hash("BarBottleOptics.json",bottleOptics.bytes)}).ToArray();
        }
    }
}
