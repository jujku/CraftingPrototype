using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>杯中和工具共用 BarKit A/B/C 冰外观；预制体以中心为原点、单位包围盒，根缩放使用米。</summary>
    public static class IceAppearance
    {
        private static readonly string[] variants={"ShakingIce_A","ShakingIce_B","ShakingIce_C"};
        public static GameObject Create(int index,Transform parent)
        {
            var prefab=Resources.Load<GameObject>("Ice/"+variants[index%variants.Length]);
            if(!prefab)throw new InvalidOperationException("Missing BarKit ice prefab: "+variants[index%variants.Length]);
            var instance=UnityEngine.Object.Instantiate(prefab,parent,false);
            var renderers=instance.GetComponentsInChildren<MeshRenderer>();var materials=new Material[renderers.Length];var settings=PresentationSettings.Load().ice;
            for(int i=0;i<renderers.Length;i++)
            {
                // Keep the asset a standard, validated URP transparent material. Render-order
                // overrides belong only to disposable instances; URP can otherwise reset them on import.
                bool surface=renderers[i].name=="IceSurface";
                var material=new Material(renderers[i].sharedMaterial);materials[i]=material;material.shader=Shader.Find("Bar/Ice");
                var color=material.GetColor("_BaseColor");color.a=surface?settings.alpha:settings.coreAlpha;material.SetColor("_BaseColor",color);
                material.SetFloat("_Fog",settings.fog);material.SetFloat("_Core",surface?0:1);material.SetFloat("_Smoothness",settings.smoothness);material.SetFloat("_NormalNoise",settings.normalNoise);material.SetFloat("_FresnelPower",settings.fresnelPower);material.SetFloat("_SpecularPower",settings.specularPower);
                var bounds=renderers[i].GetComponent<MeshFilter>().sharedMesh.bounds;material.SetVector("_ObjectCenter",bounds.center);material.SetVector("_ObjectExtents",bounds.extents);
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                // Bar/Ice returns premultiplied colour so highlights survive on the nearly clear body.
                material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite",surface?1:0);material.renderQueue=surface?2499:2498;
                var optics=PourSettings.Load();
                material.SetFloat("_Ior",optics.iceIor);material.SetVector("_Absorption",optics.iceAbsorption);
                material.SetVector("_CoreRadii",optics.iceCoreRadii);material.SetFloat("_CoreDensity",optics.iceCoreDensity);material.SetFloat("_CoreSoftness",optics.iceCoreSoftness);
                material.SetFloat("_RefractionGain",optics.iceRefractionGain);material.SetFloat("_EdgeWidth",optics.iceEdgeWidth);
                material.SetFloat("_Nucleation",optics.nucleation);material.SetFloat("_Frost",optics.frost);
                material.renderQueue=surface?2499:2498;renderers[i].enabled=surface;
                renderers[i].sharedMaterial=material;
            }
            instance.AddComponent<IceAppearanceMaterials>().Owned=materials;
            instance.AddComponent<IceTransmissionPatch>();
            return instance;
        }
        public static float ConfiguredSide
        {
            get
            {
                var settings=Resources.Load<CueSettingsAsset>("BarCueSettings");
                if(!settings||!settings.Json)throw new InvalidOperationException("Missing ice visual settings");
                return JsonUtility.FromJson<CueSettings>(settings.Json.text).visual.iceSide;
            }
        }
    }
    internal sealed class IceAppearanceMaterials : MonoBehaviour
    {
        [NonSerialized] internal Material[] Owned;
        private void OnDestroy(){if(Owned!=null)foreach(var material in Owned)if(material)Destroy(material);}
    }
}
