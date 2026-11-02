using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Bar.Unity.Editor
{
    /// <summary>从整个室内采集共享辐亮度，供建筑表面的反射与漫反射间接光使用。只在验证副本写资产，不重烘工作台软箱。</summary>
    public static class GinzaRoomLightingBake
    {
        public const string Root="Assets/Bar/Environment/GinzaRoomLighting";
        const string Scene="Assets/Bar/Scenes/BarPresentation.unity";
        const string Prefab="Assets/Bar/Environment/Ginza/GinzaEnvironment.prefab";
        static readonly Vector3[] Axes={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
        [Serializable]public sealed class RadianceRecord
        {
            public string model="Shared interior radiance / ambient cube; authored presentation, not a calibrated simulation input";
            public Vector3 capture,min,max;public Vector4 decode;public Color[] diffuse;public float diffuseGain=2.5f,specularGain=.9f;public int mipCount,receivers,materialCount;public bool chairsUnchanged,lightsUnchanged,workbenchProbeUnchanged;
        }
        static IEnumerable<MeshRenderer> Receivers(Transform room)
        {
            // Spatial ownership: architecture and the enclosing bulkhead. Furniture and gameplay vessels are separate.
            var groups=new[]{room.Find("ArchitecturalEnvelope"),room.Find("BarCounter_CustomerWrap/RiserBulkhead")};
            if(groups.Any(t=>!t))throw new InvalidOperationException("Room architecture roots missing");
            return groups.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Distinct();
        }
        static bool RoomMaterial(Material m)=>m&&new[]{"Universal Render Pipeline/Lit","Bar/RoomSurface"}.Contains(m.shader.name)&&(!m.HasProperty("_Surface")||m.GetFloat("_Surface")==0)&&m.GetColor("_EmissionColor").maxColorComponent==0;
        static void ArchitectureLayers(Transform room)
        {
            foreach(var r in room.Find("ArchitecturalEnvelope/Dark walnut herringbone floor").GetComponentsInChildren<MeshRenderer>().Concat(room.Find("BarCounter_CustomerWrap/RiserBulkhead").GetComponentsInChildren<MeshRenderer>()))
            {
                if(r.GetComponent<Collider>())throw new InvalidOperationException("Architectural receiver must not change collision layer: "+r.name);
                r.gameObject.layer=29;
            }
        }
        static string ChairState(Transform room)=>string.Join("\n",room.Cast<Transform>().Where(t=>t.name.StartsWith("Speakeasy_BarStool_")).SelectMany(t=>t.GetComponentsInChildren<Component>(true)).Where(c=>c is Transform||c is Renderer||c is MeshFilter||c is Collider).Select(c=>EditorJsonUtility.ToJson(c)));
        static string LightState()=>string.Join("\n",Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(l=>l.GetInstanceID()).Select(l=>l.name+"|"+l.enabled+"|"+l.transform.position.ToString("R")+"|"+l.transform.rotation.ToString("R")+"|"+l.intensity.ToString("R")+"|"+l.color+"|"+l.type+"|"+l.range+"|"+l.spotAngle+"|"+l.innerSpotAngle+"|"+l.shadows+"|"+l.shadowStrength+"|"+l.shadowBias+"|"+l.shadowNormalBias+"|"+l.cullingMask+"|"+l.renderingLayerMask));
        public static void Bake()
        {
            if(!File.Exists(".u1-validation-copy"))throw new InvalidOperationException("Author only in the existing validation copy");
            var scene=EditorSceneManager.OpenScene(Scene);var room=Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;
            var floor=room.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Beveled parquet floor slab");
            var roomProbe=room.GetComponentsInChildren<ReflectionProbe>().Single(p=>p.name=="Room reflection probe");
            var softbox=room.GetComponentsInChildren<ReflectionProbe>().Single(p=>p.name=="Original softbox workbench reflection");
            ArchitectureLayers(room);
            // Tool accents already exclude wall/ceiling receiver layers. Complete that spatial ownership
            // for the floor and enclosing bulkhead, without changing their light on tools or furniture.
            foreach(string name in new[]{"Cool fill","Glass rim"})
            {
                var accent=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(l=>l.name==name);
                accent.cullingMask&=~(1<<29);PrefabUtility.RecordPrefabInstancePropertyModifications(accent);
            }
            string chairs=ChairState(room),lights=LightState(),probeState=EditorJsonUtility.ToJson(softbox);
            var additional=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).ToDictionary(l=>l,l=>l.GetComponent<UniversalAdditionalLightData>());
            var rig=Object.FindFirstObjectByType<PresentationRig>();rig.Desktop();
            var prefabOverrides=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(t=>PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject)).Where(g=>g).Distinct().ToDictionary(g=>g,PrefabUtility.GetPropertyModifications);
            Directory.CreateDirectory(Root+"/Materials");AssetDatabase.Refresh();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Bar/Rendering/RoomSurface.shader");if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Room shader missing or failed compilation");
            var targets=Receivers(room).ToArray();var original=targets.ToDictionary(r=>r,r=>r.sharedMaterials);
            var neutral=new Dictionary<Material,Material>();var lit=AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
            foreach(var r in targets)
            {
                var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)if(RoomMaterial(mats[i]))
                {
                    var source=mats[i];if(!neutral.TryGetValue(source,out var copy)){copy=new Material(lit);copy.CopyPropertiesFromMaterial(source);copy.shader=lit;copy.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");neutral.Add(source,copy);}mats[i]=copy;
                }
                r.sharedMaterials=mats;
            }
            var flags=Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(r=>r.gameObject).Distinct().ToDictionary(g=>g,GameObjectUtility.GetStaticEditorFlags);
            foreach(var pair in flags)GameObjectUtility.SetStaticEditorFlags(pair.Key,pair.Value|StaticEditorFlags.ReflectionProbeStatic);
            var intensities=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>!l.GetComponentInParent<GinzaEnvironmentTag>()).ToDictionary(l=>l,l=>l.intensity);
            foreach(var pair in intensities)pair.Key.intensity=pair.Value*PresentationSettings.Load().soften.lightScale;
            var go=new GameObject("Interior radiance authoring capture");var capture=go.AddComponent<ReflectionProbe>();capture.mode=ReflectionProbeMode.Baked;capture.resolution=128;capture.hdr=true;capture.clearFlags=ReflectionProbeClearFlags.SolidColor;capture.backgroundColor=roomProbe.backgroundColor;capture.nearClipPlane=.04f;capture.farClipPlane=30;capture.cullingMask=~((1<<8)|(1<<9)|(1<<31));capture.transform.position=roomProbe.transform.position;capture.size=roomProbe.size;
            bool baked=Lightmapping.BakeReflectionProbe(capture,Root+"/InteriorRadiance.exr");
            foreach(var pair in original)pair.Key.sharedMaterials=pair.Value;
            foreach(var pair in intensities)pair.Key.intensity=pair.Value;
            foreach(var pair in flags)GameObjectUtility.SetStaticEditorFlags(pair.Key,pair.Value);
            foreach(var pair in prefabOverrides)PrefabUtility.SetPropertyModifications(pair.Key,pair.Value);
            foreach(var m in neutral.Values)Object.DestroyImmediate(m);
            if(!baked)throw new InvalidOperationException("Interior radiance bake failed");
            AssetDatabase.ImportAsset(Root+"/InteriorRadiance.exr",ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"/InteriorRadiance.exr");importer.isReadable=true;importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
            foreach(string platform in new[]{"Standalone","WebGL"})importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name=platform,overridden=true,format=TextureImporterFormat.RGBAHalf,maxTextureSize=128,textureCompression=TextureImporterCompression.Uncompressed});
            importer.SaveAndReimport();var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(Root+"/InteriorRadiance.exr");if(!cube)throw new InvalidOperationException("Interior capture is not a cubemap");capture.bakedTexture=cube;Vector4 decode=capture.textureHDRDecodeValues;
            var record=new RadianceRecord{capture=capture.transform.position,min=new Vector3(floor.bounds.min.x,floor.bounds.max.y,floor.bounds.min.z),max=new Vector3(floor.bounds.max.x,room.position.y+3.1f,floor.bounds.max.z),diffuse=Integrate(cube),decode=decode,mipCount=cube.mipmapCount,receivers=targets.Length};
            Object.DestroyImmediate(go);
            var copies=new Dictionary<Material,Material>();var materialPaths=new HashSet<string>();
            void Bind(Material material)
            {
                if(!material.HasProperty("_RoomRadiance"))throw new InvalidOperationException("Material does not expose room radiance: "+material.shader.name);
                material.SetTexture("_RoomRadiance",cube);material.SetVector("_RoomCapture",new Vector4(record.capture.x,record.capture.y,record.capture.z,1));material.SetVector("_RoomBoundsMin",record.min);material.SetVector("_RoomBoundsMax",record.max);material.SetVector("_RoomDecode",decode);material.SetFloat("_RoomMipCount",cube.mipmapCount);material.SetFloat("_RoomDiffuseGain",record.diffuseGain);material.SetFloat("_RoomSpecularGain",record.specularGain);
                string[] names={"PX","NX","PY","NY","PZ","NZ"};for(int i=0;i<6;i++)material.SetVector("_RoomDiffuse"+names[i],record.diffuse[i]);EditorUtility.SetDirty(material);
            }
            Material Convert(Material source)
            {
                if(!RoomMaterial(source))return source;
                if(copies.TryGetValue(source,out var known))return known;
                string name=source.name.StartsWith("Room_")?source.name.Substring(5):source.name;
                string path=Root+"/Materials/Room_"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
                material.CopyPropertiesFromMaterial(source);
                // Unity 6000.3 retains the source property lookup after CopyPropertiesFromMaterial.
                // Assign the destination shader afterwards to establish its room-property layout.
                material.shader=shader;material.shaderKeywords=source.shaderKeywords.Where(k=>k=="_NORMALMAP").ToArray();
                Bind(material);materialPaths.Add(path);copies.Add(source,material);return material;
            }
            foreach(var renderer in targets)renderer.sharedMaterials=renderer.sharedMaterials.Select(Convert).ToArray();
            AssetDatabase.SaveAssets();
            var prefab=PrefabUtility.LoadPrefabContents(Prefab);ArchitectureLayers(prefab.transform);foreach(var renderer in Receivers(prefab.transform))renderer.sharedMaterials=renderer.sharedMaterials.Select(Convert).ToArray();PrefabUtility.SaveAsPrefabAsset(prefab,Prefab);PrefabUtility.UnloadPrefabContents(prefab);
            foreach(var path in materialPaths)Bind(AssetDatabase.LoadAssetAtPath<Material>(path));
            foreach(var pair in additional)if(!pair.Value){var generated=pair.Key.GetComponent<UniversalAdditionalLightData>();if(generated)Object.DestroyImmediate(generated);}
            AssetDatabase.SaveAssets();
            foreach(var path in materialPaths){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var saved=AssetDatabase.LoadAssetAtPath<Material>(path);if(saved.GetTexture("_RoomRadiance")!=cube||saved.GetFloat("_RoomDiffuseGain")!=record.diffuseGain)throw new InvalidOperationException("Room material binding did not persist: "+path);}
            record.materialCount=materialPaths.Count;record.chairsUnchanged=chairs==ChairState(room);record.lightsUnchanged=lights==LightState();record.workbenchProbeUnchanged=probeState==EditorJsonUtility.ToJson(softbox);
            if(!record.chairsUnchanged||!record.lightsUnchanged||!record.workbenchProbeUnchanged)throw new InvalidOperationException("Protected room state changed while authoring radiance");
            File.WriteAllText(Root+"/RoomRadiance.json",JsonUtility.ToJson(record,true));AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Debug.Log("Room radiance authored: "+JsonUtility.ToJson(record));
        }
        /// <summary>按立体角积分六个方向的 Lambert 辐照度/π；与 Unity Core 的 cubemap face basis 一致。</summary>
        public static Color[] Integrate(Cubemap cube)
        {
            int mip=Math.Min(2,cube.mipmapCount-1),n=cube.width>>mip;var sum=new Color[6];var directions=new[]{CubemapFace.PositiveX,CubemapFace.NegativeX,CubemapFace.PositiveY,CubemapFace.NegativeY,CubemapFace.PositiveZ,CubemapFace.NegativeZ};
            for(int face=0;face<6;face++)
            {
                var pixels=cube.GetPixels(directions[face],mip);
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    float u=2*(x+.5f)/n-1,v=2*(y+.5f)/n-1;Vector3 d;
                    switch(face){case 0:d=new Vector3(1,-v,-u);break;case 1:d=new Vector3(-1,-v,u);break;case 2:d=new Vector3(u,1,v);break;case 3:d=new Vector3(u,-1,-v);break;case 4:d=new Vector3(u,-v,1);break;case 5:d=new Vector3(-u,-v,-1);break;default:throw new InvalidOperationException();}
                    d.Normalize();float weight=4f/(n*n*Mathf.Pow(1+u*u+v*v,1.5f)*Mathf.PI);var c=pixels[y*n+x];if(float.IsNaN(c.r)||float.IsInfinity(c.r)||c.r<0||c.g<0||c.b<0)throw new InvalidOperationException("Invalid room radiance texel");for(int i=0;i<6;i++)sum[i]+=c*(Mathf.Max(0,Vector3.Dot(Axes[i],d))*weight);
                }
            }
            return sum;
        }
    }
}
