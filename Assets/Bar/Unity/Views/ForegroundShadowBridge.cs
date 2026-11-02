using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Bar.Unity
{
    /// <summary>两台相机只画各自颜色层，但阴影都保留完整场景；代理只投影、不绘制颜色、不带碰撞。</summary>
    public sealed class ForegroundShadowBridge
    {
        private sealed class Entry
        {
            public MeshRenderer source,shadow,depth;public MeshFilter mesh,copy,depthMesh;
            public Material[] materials;public MaterialPropertyBlock block=new MaterialPropertyBlock();
            public MaterialPropertyBlock[] slots;
        }
        private readonly List<Entry> entries=new List<Entry>();
        private readonly List<Material> materials=new List<Material>();
        private readonly GameObject root;private readonly PlayerEnvironmentSettings settings;
        private Material depthMaterial;
        public int ActiveCount {get;private set;}
        public ForegroundShadowBridge(Transform parent,PlayerEnvironmentSettings configuration)
        {
            settings=configuration;
            // Inventory before creating proxies, so they can never mirror themselves.
            var sources=Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            root=new GameObject("Foreground shadows (shadow-only, no collision)");root.transform.SetParent(parent,false);
            foreach(var source in sources)
            {
                var mesh=source.GetComponent<MeshFilter>();if(!mesh||!mesh.sharedMesh)continue;
                var go=new GameObject(source.name+" shadow only");go.transform.SetParent(root.transform,false);
                var copy=go.AddComponent<MeshFilter>();var shadow=go.AddComponent<MeshRenderer>();shadow.enabled=false;
                shadow.shadowCastingMode=ShadowCastingMode.ShadowsOnly;shadow.receiveShadows=false;
                entries.Add(new Entry{source=source,shadow=shadow,mesh=mesh,copy=copy});
            }
        }
        /// <summary>另一台相机补同一网格的投影，不复制物品/领域状态，不改变原件材质。</summary>
        public void Sync(bool foregroundPresent,Transform tableReceiver,IReadOnlyDictionary<Transform,int> originalLayers)
        {
            ActiveCount=0;
            foreach(var e in entries)
            {
                bool enabled=foregroundPresent&&e.source&&e.source.enabled&&e.source.gameObject.activeInHierarchy;
                e.shadow.enabled=enabled&&e.source.shadowCastingMode!=ShadowCastingMode.Off;if(e.depth)e.depth.enabled=false;if(!enabled)continue;if(e.shadow.enabled)ActiveCount++;
                bool foreground=(settings.Mask&(1<<e.source.gameObject.layer))!=0;
                int originalLayer=originalLayers.TryGetValue(e.source.transform,out int layer)?layer:e.source.gameObject.layer;
                e.shadow.gameObject.layer=foreground?originalLayer:settings.Held;
                var t=e.source.transform;var clone=e.shadow.transform;clone.SetPositionAndRotation(t.position,t.rotation);clone.localScale=t.lossyScale;
                e.copy.sharedMesh=e.mesh.sharedMesh;e.shadow.renderingLayerMask=e.source.renderingLayerMask;
                e.source.GetSharedMaterials(materials);bool changed=e.materials==null||e.materials.Length!=materials.Count;
                if(!changed)for(int i=0;i<materials.Count;i++)if(e.materials[i]!=materials[i]){changed=true;break;}
                if(changed){e.materials=materials.ToArray();e.shadow.sharedMaterials=e.materials;e.slots=new MaterialPropertyBlock[e.materials.Length];for(int i=0;i<e.slots.Length;i++)e.slots[i]=new MaterialPropertyBlock();}
                e.source.GetPropertyBlock(e.block);e.shadow.SetPropertyBlock(e.block);
                for(int i=0;i<e.slots.Length;i++){e.source.GetPropertyBlock(e.slots[i],i);e.shadow.SetPropertyBlock(e.slots[i],i);}
                bool opaque=e.materials.Length>0;
                // Liquid and ice use an early queue for refraction, but are not opaque depth/normal surfaces.
                foreach(var m in e.materials)if(!m||m.renderQueue>=2500||(m.FindPass("DepthNormals")<0&&m.FindPass("DepthNormalsOnly")<0&&m.FindPass("DepthOnly")<0))opaque=false;
                if(foreground&&tableReceiver&&t.IsChildOf(tableReceiver)&&opaque)
                {
                    if(!e.depth)
                    {
                        if(!depthMaterial)depthMaterial=new Material(Resources.Load<Shader>("ForegroundOcclusion"));
                        var go=new GameObject(e.source.name+" world depth only");go.transform.SetParent(root.transform,false);
                        e.depthMesh=go.AddComponent<MeshFilter>();e.depth=go.AddComponent<MeshRenderer>();e.depth.sharedMaterial=depthMaterial;e.depth.shadowCastingMode=ShadowCastingMode.Off;
                    }
                    e.depth.gameObject.layer=originalLayer;e.depth.enabled=true;e.depthMesh.sharedMesh=e.mesh.sharedMesh;
                    e.depth.transform.SetPositionAndRotation(t.position,t.rotation);e.depth.transform.localScale=t.lossyScale;
                }
            }
        }
        public void Dispose(){if(root)Object.Destroy(root);if(depthMaterial)Object.Destroy(depthMaterial);}
    }
}
