using System.Collections.Generic;
using Bar.Domain;
using CraftingResearch.CoreV2;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只覆盖运行时材质/属性块：金属同一颜色 pass 加霜，玻璃同一 pass 加冷凝与握点擦除。</summary>
    public sealed class SurfaceAppearance
    {
        private readonly ItemView view;private readonly ContainerVisual visual;private readonly bool glass;private readonly MeshRenderer[] shells;private readonly SurfaceDisplaySettings p;
        private readonly Dictionary<MeshRenderer,Material[]> originals=new Dictionary<MeshRenderer,Material[]>();private readonly List<Material> owned=new List<Material>();
        private Vector3 wipeLocal;private float time;public SurfaceEvolution State {get;}
        private readonly MaterialPropertyBlock block=new MaterialPropertyBlock();private readonly Texture2D frost,water,phases;private readonly LocalIceWorld ice;
        private readonly List<MeshRenderer> iceShells=new List<MeshRenderer>();private int iceCount=-1;
        private readonly Camera viewer;
        public SurfaceAppearance(ItemView view,ContainerVisual visual,Container container,MeshRenderer[] shells)
        {
            this.view=view;this.visual=visual;this.shells=shells;glass=container is Glass;p=BPresentationSettings.Load().surface;State=new SurfaceEvolution(p);
            frost=Resources.Load<Texture2D>("BTextures/frost-mask");water=Resources.Load<Texture2D>("BTextures/water-mask");phases=Resources.Load<Texture2D>("BTextures/water-phases");ice=visual.GetComponent<LocalIceWorld>();
            viewer=Camera.main;
            if(container is not ShakerBody)return;
            foreach(var shell in shells)
            {
                var original=shell.sharedMaterials;var materials=(Material[])original.Clone();bool changed=false;
                for(int i=0;i<materials.Length;i++)if(materials[i]&&materials[i].shader.name=="Universal Render Pipeline/Lit")
                {var mat=new Material(Shader.Find("Bar/FrostMetal"));mat.CopyPropertiesFromMaterial(materials[i]);materials[i]=mat;owned.Add(mat);changed=true;}
                if(changed){originals[shell]=original;shell.sharedMaterials=materials;}
            }
        }
        /// <param name="shakeIntensity">核心正在收到的摇壶强度（WorldAdapter.Agitation），决定霜长得多快。</param>
        public void Present(VesselState snapshot,float height,Transform grip,float dt,float shakeIntensity=0)
        {
            // glasses no longer show condensation; State stays zero so the cue log reports nothing shown.
            if(glass){PresentIce(snapshot);return;}
            State.Step((float)(snapshot.vesselTemperature??25),glass,grip,dt,shakeIntensity,snapshot.iceGrams>.5);time+=dt;
            if(grip)
            {
                wipeLocal=visual.transform.InverseTransformPoint(grip.position);
                if(!glass){var s=visual.Shape;float y=Mathf.Clamp(wipeLocal.y,s.Bottom,s.Top);var radial=new Vector2(wipeLocal.x-s.Axis.x,wipeLocal.z-s.Axis.y);float radius=s.RadiusAt(y);
                    if(radial.magnitude<radius*.5f){var approach=viewer?visual.transform.InverseTransformPoint(viewer.transform.position)-wipeLocal:-visual.transform.InverseTransformDirection(grip.forward);radial=new Vector2(approach.x,approach.z);}
                    if(radial.sqrMagnitude<1e-8f)radial=Vector2.down;radial=radial.normalized*(radius+p.metalContactOffset);wipeLocal=new Vector3(s.Axis.x+radial.x,y,s.Axis.y+radial.y);}
            }
            var wipe=visual.transform.TransformPoint(wipeLocal);var shape=visual.Shape;float level=visual.transform.InverseTransformPoint(new Vector3(visual.transform.position.x,height,visual.transform.position.z)).y;
            foreach(var shell in shells)
            {
                if(!shell)continue;shell.GetPropertyBlock(block);
                block.SetFloat(BShaderIds.BFrost,State.Frost);block.SetFloat(BShaderIds.BWet,State.Wet);block.SetFloat(BShaderIds.Mist,State.Mist);block.SetFloat(BShaderIds.Droplets,State.Drops);block.SetFloat(BShaderIds.BRuns,State.Runs);
                block.SetTexture(BShaderIds.BFrostMask,frost);block.SetTexture(BShaderIds.BWaterMask,water);block.SetTexture(BShaderIds.BWaterPhases,phases);block.SetMatrix(BShaderIds.BContainerToWorld,visual.transform.localToWorldMatrix);
                block.SetMatrix(BShaderIds.BWorldToContainer,visual.transform.worldToLocalMatrix);block.SetVector(BShaderIds.BRange,new Vector4(shape.Bottom,shape.Top,shape.Axis.x,shape.Axis.y));
                block.SetVector(BShaderIds.BWipe,new Vector4(wipe.x,wipe.y,wipe.z,glass?p.wipeRadius:p.frostWipeRadius));block.SetFloat(BShaderIds.BWipeStrength,State.Wipe);block.SetFloat(BShaderIds.BWipeFeather,p.wipeFeather);
                block.SetFloat(BShaderIds.BTextureTiling,p.textureTiling);block.SetFloat(BShaderIds.BFrostSoftness,p.frostSoftness);block.SetFloat(BShaderIds.BContactLevel,level);
                block.SetFloat(BShaderIds.BRadius,shape.RadiusAt((shape.Top+shape.Bottom)*.5f));block.SetFloat(BShaderIds.BFrostGrowthTiling,p.frostGrowthTiling);block.SetFloat(BShaderIds.BFrostNormalStrength,p.frostNormalStrength);block.SetFloat(BShaderIds.BFrostWhiteBlend,p.frostWhiteBlend);block.SetFloat(BShaderIds.BDropHighlight,p.dropHighlight);
                block.SetFloat(BShaderIds.BMistBlurPixels,p.mistBlurPixels);block.SetFloat(BShaderIds.BDropRefraction,p.dropRefraction);block.SetFloat(BShaderIds.BRunPhase,time*p.runSpeed);shell.SetPropertyBlock(block);
                block.SetFloat(BShaderIds.BMistTintGain,p.mistTintGain);block.SetFloat(BShaderIds.BDropNormalGain,p.dropNormalGain);shell.SetPropertyBlock(block);
            }
            PresentIce(snapshot);
        }
        private void PresentIce(VesselState snapshot)
        {
            if(ice&&iceCount!=ice.DisplayCount){iceCount=ice.DisplayCount;iceShells.Clear();foreach(var root in ice.Displays)foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))if(r.name=="IceSurface")iceShells.Add(r);}
            foreach(var renderer in iceShells)if(renderer){renderer.GetPropertyBlock(block);block.SetFloat(BShaderIds.Frost,p.iceFrost*Mathf.Clamp01((p.frostOnset-(float)(snapshot.temperature??25))/(p.frostOnset-p.frostFull)));renderer.SetPropertyBlock(block);}
        }
        public void Dispose(){foreach(var entry in originals)if(entry.Key)entry.Key.sharedMaterials=entry.Value;foreach(var material in owned)Object.Destroy(material);}
    }
}
