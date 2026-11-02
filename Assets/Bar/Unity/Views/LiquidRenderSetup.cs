using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity
{
    /// <summary>运行时克隆 URP 设置，液体背面→冰→液体正面在屏幕拷贝之前绘制；不保存用户资源。</summary>
    public static class LiquidRenderSetup
    {
        private static readonly System.Collections.Generic.HashSet<int> installed=new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.Dictionary<int,UniversalRenderPipelineAsset> prepared=new System.Collections.Generic.Dictionary<int,UniversalRenderPipelineAsset>();
        public static void Install()
        {
            var current=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;if(!current||installed.Contains(current.GetInstanceID()))return;
            if(prepared.TryGetValue(current.GetInstanceID(),out var cached)&&cached){QualitySettings.renderPipeline=cached;return;}
            var asset=Object.Instantiate(current);asset.supportsCameraOpaqueTexture=true;asset.supportsCameraDepthTexture=true;
            var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);var list=(ScriptableRendererData[])field.GetValue(asset);list=(ScriptableRendererData[])list.Clone();
            for(int i=0;i<list.Length;i++){var data=Object.Instantiate(list[i]);foreach(int pass in new[]{1,2}){var feature=ScriptableObject.CreateInstance<RenderObjects>();feature.name="Liquid front "+pass;feature.settings.Event=RenderPassEvent.AfterRenderingOpaques;feature.settings.filterSettings.LayerMask=(1<<31)|(1<<PlayerEnvironmentSettings.Load().Liquid);feature.settings.filterSettings.RenderQueueType=RenderQueueType.Opaque;feature.settings.overrideMode=RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Shader;feature.settings.overrideShader=Shader.Find("Bar/CueLiquid");feature.settings.overrideShaderPassIndex=pass;feature.Create();data.rendererFeatures.Add(feature);}list[i]=data;data.SetDirty();}
            field.SetValue(asset,list);prepared[current.GetInstanceID()]=asset;installed.Add(asset.GetInstanceID());QualitySettings.renderPipeline=asset;
        }
    }
}
