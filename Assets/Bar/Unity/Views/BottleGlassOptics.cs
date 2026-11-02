using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity
{
    /// <summary>只为瓶子的外层玻璃创建厚底材质副本；几何、内底和坐标变换取自本瓶的真实内腔。</summary>
    public sealed class BottleGlassOptics:MonoBehaviour
    {
        [Serializable]private sealed class Preset{public string id;public float absorption,refraction,internalReflection,filletMetres,rimGain,baseContrast;}
        [Serializable]private sealed class Settings{public string note;public Preset[] bottles;}
        private readonly List<(MeshRenderer renderer,Material[] original,Material[] copies)> bindings=new List<(MeshRenderer,Material[],Material[])>();
        private readonly List<Material> copies=new List<Material>();private UniversalRenderPipelineAsset pipeline;private IceRefractionFeature[] features=Array.Empty<IceRefractionFeature>();private int state=-1;
        public void Initialize(BottleLiquidView liquid)
        {
            var settings=JsonUtility.FromJson<Settings>(Resources.Load<TextAsset>("BarBottleOptics").text);var p=settings.bottles.Single(x=>"bottle."+x.id==liquid.Geometry.id);var shape=liquid.Shape;
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer.name!="BlueGlassShell"&&renderer.name!="GlassShell")continue;
                var original=renderer.sharedMaterials;var materials=(Material[])original.Clone();bool changed=false;
                for(int i=0;i<materials.Length;i++)
                {
                    var m=materials[i];if(!m||m.shader.name!="Bar/Glass"||m.GetFloat("_Cull")<1.5f||m.GetFloat("_LayerOpacity")<.99f)continue;
                    var copy=new Material(m){name=m.name+" (BottleRefine solid base)"};copy.SetFloat("_ZWrite",1);copy.SetFloat("_ProfileSpecularAA",1);copy.SetFloat("_ProfileRimGain",p.rimGain);copy.SetFloat("_ProfileBaseContrast",p.baseContrast);
                    copy.SetVector("_ShamOptics",new Vector4(1,p.absorption,p.refraction,p.internalReflection));copy.SetVector("_ShamShape",new Vector4(shape.Bottom,shape.RadiusAt(shape.Bottom),p.filletMetres,0));copy.SetVector("_ShamAxis",new Vector4(shape.Axis.x,shape.Axis.y,0,0));copy.SetMatrix("_ShamMeshToGlass",transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix);
                    copies.Add(copy);materials[i]=copy;changed=true;
                }
                if(changed){renderer.sharedMaterials=materials;bindings.Add((renderer,original,materials));}
            }
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;BeforeCamera(default,null);
        }
        private void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            var current=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline!=current)
            {
                pipeline=current;var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var data=current?(ScriptableRendererData[])field.GetValue(current):Array.Empty<ScriptableRendererData>();features=data.Where(x=>x).SelectMany(x=>x.rendererFeatures).OfType<IceRefractionFeature>().ToArray();
            }
            bool ready=features.Any(x=>x&&x.isActive)&&!IceRefractionFeature.ProfileWithoutCopy&&(!camera||camera.cameraType!=CameraType.Preview);if(state==(ready?1:0))return;state=ready?1:0;
            foreach(var b in bindings)if(b.renderer)b.renderer.sharedMaterials=ready?b.copies:b.original;
        }
        private void OnDestroy(){RenderPipelineManager.beginCameraRendering-=BeforeCamera;foreach(var b in bindings)if(b.renderer)b.renderer.sharedMaterials=b.original;foreach(var m in copies)if(m)Destroy(m);}
    }
}
