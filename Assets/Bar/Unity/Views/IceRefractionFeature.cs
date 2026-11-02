using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
namespace Bar.Unity
{
    /// <summary>RenderGraph 顺序：URP 不透明拷贝 → 折射冰 → 水柱 → 第二次拷贝 → 玻璃。未装配时着色器走降级。</summary>
    public sealed class IceRefractionFeature : ScriptableRendererFeature
    {
        private IcePass pass;private FlagPass flags;
        // Only the temporary smoke director changes this diagnostic switch.
        public static bool ProfileWithoutCopy;
        public static bool CaptureHistoricalOrder;
        public static double CopyCpuMilliseconds {get;private set;}
        public static bool OverlayOpaqueValid {get;private set;}public static bool BaseOpaqueValid {get;private set;}
        private static double copyCpuTotal;private static long copies;
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]private static extern void BarArmCopyTimer();
#endif
        public override void Create(){pass=new IcePass();flags=new FlagPass();RenderPipelineManager.beginCameraRendering-=Reset;RenderPipelineManager.beginCameraRendering+=Reset;}
        private static void Reset(ScriptableRenderContext c,Camera camera){Shader.SetGlobalFloat("_BarIceFeatureActive",0);Shader.SetGlobalFloat("_BarSceneWithIceValid",0);Shader.SetGlobalFloat("_BarPourFeatureActive",0);Shader.SetGlobalFloat("_BarIceBackgroundDepthValid",0);}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(data.cameraData.cameraType==CameraType.Preview)return;pass.renderPassEvent=CaptureHistoricalOrder?(RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents-2):RenderPassEvent.BeforeRenderingTransparents;renderer.EnqueuePass(flags);renderer.EnqueuePass(pass);}
        // Keep the one static reset callback for fallback cameras even after a renderer was disposed.
        protected override void Dispose(bool disposing){pass?.Dispose();}
        private sealed class FlagPass:ScriptableRenderPass
        {
            private sealed class Data{}
            public FlagPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                using(var builder=graph.AddRasterRenderPass<Data>("Bar ice fallback gate",out var data))
                {
                    builder.SetRenderAttachment(frame.Get<UniversalResourceData>().activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                    builder.SetRenderFunc((Data d,RasterGraphContext context)=>{context.cmd.SetGlobalFloat("_BarIceFeatureActive",1);context.cmd.SetGlobalFloat("_BarSceneWithIceValid",0);context.cmd.SetGlobalFloat("_BarPourFeatureActive",0);context.cmd.SetGlobalFloat("_BarIceBackgroundDepthValid",0);});
                }
            }
        }
        private sealed class IcePass:ScriptableRenderPass
        {
            private sealed class DrawData{public RendererListHandle list;public TextureHandle opaque,depth,background;}
            private sealed class CopyData{public TextureHandle source;}
            private RTHandle worldDepth;private Camera worldCamera;private int worldFrame=-1;
            public void Dispose(){worldDepth?.Release();worldDepth=null;worldCamera=null;}
            // URP records the entire [AfterSkybox, BeforeTransparents) custom-event bucket BEFORE
            // its opaque copy. Exactly BeforeRenderingTransparents enforces the specified order.
            public IcePass(){renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;ConfigureInput(ScriptableRenderPassInput.Color|ScriptableRenderPassInput.Depth);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var resources=frame.Get<UniversalResourceData>();var camera=frame.Get<UniversalCameraData>();var rendering=frame.Get<UniversalRenderingData>();var light=frame.Get<UniversalLightData>();
                if(camera.renderType==CameraRenderType.Overlay)OverlayOpaqueValid=resources.cameraOpaqueTexture.IsValid();else BaseOpaqueValid=resources.cameraOpaqueTexture.IsValid();
                if(resources.isActiveTargetBackBuffer)return;
                TextureHandle background=resources.cameraDepthTexture;
                if(camera.renderType==CameraRenderType.Base&&background.IsValid()&&SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
                {
                    var depthDescriptor=camera.cameraTargetDescriptor;depthDescriptor.depthBufferBits=0;depthDescriptor.depthStencilFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.None;depthDescriptor.colorFormat=RenderTextureFormat.RFloat;depthDescriptor.msaaSamples=1;depthDescriptor.sRGB=false;depthDescriptor.useMipMap=false;depthDescriptor.autoGenerateMips=false;
                    RenderingUtils.ReAllocateHandleIfNeeded(ref worldDepth,depthDescriptor,FilterMode.Point,TextureWrapMode.Clamp,name:"Bar world refraction depth");
                    worldCamera=camera.camera;worldFrame=Time.frameCount;var retained=graph.ImportTexture(worldDepth);
                    using(var builder=graph.AddRasterRenderPass<CopyData>("Bar retain world depth for held ice",out var data))
                    {
                        data.source=background;builder.UseTexture(data.source,AccessFlags.Read);builder.SetRenderAttachment(retained,0,AccessFlags.Write);builder.AllowPassCulling(false);
                        builder.SetRenderFunc((CopyData d,RasterGraphContext context)=>Blitter.BlitTexture(context.cmd,d.source,new Vector4(1,1,0,0),0,false));
                    }
                }
                else if(camera.renderType==CameraRenderType.Overlay&&worldDepth!=null&&worldFrame==Time.frameCount&&camera.camera.transform.parent==worldCamera.transform)
                    background=graph.ImportTexture(worldDepth);
                using(var builder=graph.AddRasterRenderPass<DrawData>("Bar refractive ice",out var data))
                {
                    var drawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("BarIceRefraction"),rendering,camera,light,SortingCriteria.CommonTransparent);
                    var filtering=new FilteringSettings(RenderQueueRange.all);
                    data.list=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filtering));
                    data.opaque=resources.cameraOpaqueTexture;data.depth=resources.cameraDepthTexture;data.background=background;
                    builder.UseRendererList(data.list);builder.UseAllGlobalTextures(true);
                    if(resources.cameraOpaqueTexture.IsValid())builder.UseTexture(resources.cameraOpaqueTexture,AccessFlags.Read);
                    if(resources.cameraDepthTexture.IsValid())builder.UseTexture(resources.cameraDepthTexture,AccessFlags.Read);
                    if(background.IsValid())builder.UseTexture(background,AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                    builder.SetRenderFunc((DrawData d,RasterGraphContext context)=>{context.cmd.SetGlobalFloat("_BarIceFeatureActive",1);if(d.opaque.IsValid())context.cmd.SetGlobalTexture(Shader.PropertyToID("_CameraOpaqueTexture"),d.opaque);if(d.depth.IsValid())context.cmd.SetGlobalTexture(Shader.PropertyToID("_CameraDepthTexture"),d.depth);if(d.background.IsValid())context.cmd.SetGlobalTexture(Shader.PropertyToID("_BarIceBackgroundDepth"),d.background);context.cmd.SetGlobalFloat("_BarIceBackgroundDepthValid",d.background.IsValid()?1:0);context.cmd.DrawRendererList(d.list);});
                }
                if(ProfileWithoutCopy)return;
                // Glass reconstructs its background from the following copy. Include the stream
                // in that image, otherwise the front glass wall erases its entire inside segment.
                using(var builder=graph.AddRasterRenderPass<DrawData>("Bar pour before glass capture",out var data))
                {
                    var drawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("BarPourRefraction"),rendering,camera,light,SortingCriteria.CommonTransparent);
                    drawing.SetShaderPassName(1,new ShaderTagId("BarBubbleRefraction"));
                    data.list=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,new FilteringSettings(RenderQueueRange.all)));
                    builder.UseRendererList(data.list);builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                    builder.SetRenderFunc((DrawData d,RasterGraphContext context)=>{context.cmd.SetGlobalFloat("_BarPourFeatureActive",1);context.cmd.DrawRendererList(d.list);});
                }
                var descriptor=graph.GetTextureDesc(resources.activeColorTexture);descriptor.name="_BarSceneWithIce";descriptor.clearBuffer=false;descriptor.msaaSamples=MSAASamples.None;
                var copy=graph.CreateTexture(descriptor);
                using(var builder=graph.AddRasterRenderPass<CopyData>("Bar scene with ice copy",out var data))
                {
                    data.source=resources.activeColorTexture;builder.UseTexture(data.source,AccessFlags.Read);builder.SetRenderAttachment(copy,0,AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(copy,Shader.PropertyToID("_BarSceneWithIce"));builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                    builder.SetRenderFunc((CopyData d,RasterGraphContext context)=>
                    {
#if UNITY_WEBGL && !UNITY_EDITOR
                        if(copies%120==0)BarArmCopyTimer();
#endif
                        long began=System.Diagnostics.Stopwatch.GetTimestamp();
                        Blitter.BlitTexture(context.cmd,d.source,new Vector4(1,1,0,0),0,false);
                        context.cmd.SetGlobalFloat("_BarSceneWithIceValid",1);copyCpuTotal+=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000.0/System.Diagnostics.Stopwatch.Frequency;copies++;CopyCpuMilliseconds=copyCpuTotal/copies;
                    });
                }
            }
        }
    }
}
