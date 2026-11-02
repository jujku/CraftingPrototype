using PostVolumeProfile=UnityEngine.Rendering.VolumeProfile;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity.Editor
{
    public static partial class PresentationAssembly
    {
        private const string Root="Assets/Bar/Presentation";
        [Serializable]public sealed class Roles{public Role[] materials;}
        [Serializable]public sealed class Role{public string sourceGuid,sourcePath,role,nativeMaterial;}
        [Serializable]public sealed class BakeRows{public BakeRow[] rows;}
        [Serializable]public sealed class BakeRow{public int index,slot;public string sourceGuid;public BakePaths paths;public Vector2[] uv;}
        [Serializable]public sealed class BakePaths{public string BaseColor,Roughness,Normal,MetallicSmoothness;}
        [Serializable]public sealed class AssemblyReport{public int materialSlots,meshCopies,glassLayers;public bool sourceVerticesAndTrianglesUnchanged;public string probe,scene;}
        public static void Prepare()
        {
            Guard();foreach(var folder in new[]{"Materials","Meshes","Profiles","Environment","Prefabs"})Directory.CreateDirectory(Root+"/"+folder);AssetDatabase.Refresh();
            var settings=PresentationSettings.Load();var export=JsonUtility.FromJson<Export>(File.ReadAllText("Assets/Bar/Editor/AssemblyData/scene-export.json"));
            var roles=JsonUtility.FromJson<Roles>(File.ReadAllText("Assets/Bar/Editor/AssemblyData/material-roles.json")).materials.ToDictionary(r=>r.sourceGuid);
            var bakes=JsonUtility.FromJson<BakeRows>(File.ReadAllText("Assets/Bar/Editor/AssemblyData/bake-manifest.json")).rows;
            foreach(var file in bakes.SelectMany(b=>new[]{b.paths.BaseColor,b.paths.Normal,b.paths.MetallicSmoothness}).Distinct())
            {var importer=(TextureImporter)AssetImporter.GetAtPath(file);importer.textureType=file.EndsWith("_Normal.png")?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=file.EndsWith("_BaseColor.png");importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.mipmapEnabled=true;importer.SaveAndReimport();}
            var desktop=Pipeline("Assets/Settings/PC_RPAsset.asset",settings.desktop);var web=Pipeline("Assets/Settings/Mobile_RPAsset.asset",settings.web);
            QualityLevels(desktop,web);
            var scene=EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");var views=UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).ToDictionary(v=>v.InstanceId);
            var materialCache=new Dictionary<string,Material>();int slotsChanged=0,meshCopies=0,layers=0;
            foreach(var row in export.meshes)
            {
                var view=views[row.instance];var node=view.transform.Find(row.path);if(!node)throw new InvalidOperationException("Missing exact renderer path: "+row.instance+"/"+row.path);
                var renderer=node.GetComponent<MeshRenderer>();var mesh=node.GetComponent<MeshFilter>();var assigned=renderer.sharedMaterials;
                for(int slot=0;slot<row.materials.Length;slot++)
                {
                    if(!roles.TryGetValue(row.materials[slot],out var role))continue;
                    var source=AssetDatabase.LoadAssetAtPath<Material>(role.sourcePath);if(assigned[slot]!=source)throw new InvalidOperationException("Material slot changed since export: "+row.instance+"/"+row.path+" slot "+slot);
                    var bake=bakes.FirstOrDefault(b=>b.index==row.index&&b.slot==slot);string key=bake!=null&&role.role=="wood"?row.index+"_"+slot:role.sourceGuid;
                    if(!materialCache.TryGetValue(key,out var material))
                    {
                        string dest=Root+"/Materials/"+key+".mat";material=AssetDatabase.LoadAssetAtPath<Material>(dest);
                        if(!material)
                        {
                            if(role.role=="metal")material=BarMetalLighting.CreateSlotVariant(source,dest,role.nativeMaterial=="Muted brass"?.8f:1,role.nativeMaterial=="Brushed stainless steel"?.8f:role.nativeMaterial=="Muted brass"?.7f:.84f);
                            else{material=new Material(source){name=source.name+" (presentation copy)"};AssetDatabase.CreateAsset(material,dest);}
                        }
                        if(role.role=="glass")
                        {
                            material.shader=Shader.Find("Bar/Glass");material.SetFloat("_Refraction",settings.glass.refraction);material.SetFloat("_ThicknessMm",settings.glass.thicknessMm);material.SetFloat("_TintStrength",settings.glass.tintStrength);material.SetFloat("_FresnelPower",settings.glass.fresnelPower);material.SetFloat("_Smoothness",settings.glass.smoothness);material.SetFloat("_SpecularPower",settings.glass.specularPower);material.SetFloat("_Cull",2);material.SetFloat("_LayerOpacity",1);material.renderQueue=3030;material.SetShaderPassEnabled("ShadowCaster",true);
                        }
                        if(bake!=null)
                        {
                            material.SetColor("_BaseColor",Color.white);material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(bake.paths.BaseColor));material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(bake.paths.Normal));material.SetFloat("_BumpScale",1);material.EnableKeyword("_NORMALMAP");
                            material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(bake.paths.MetallicSmoothness));material.EnableKeyword("_METALLICSPECGLOSSMAP");material.SetFloat("_Smoothness",1);material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                        }
                        EditorUtility.SetDirty(material);materialCache.Add(key,material);
                    }
                    assigned[slot]=material;slotsChanged++;
                    if(role.role=="glass")
                    {
                        var inside=new GameObject("Presentation glass interior");inside.transform.SetParent(node,false);inside.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;
                        string innerPath=Root+"/Materials/"+role.sourceGuid+"_interior.mat";var inner=AssetDatabase.LoadAssetAtPath<Material>(innerPath);if(!inner){inner=new Material(material);AssetDatabase.CreateAsset(inner,innerPath);}inner.SetFloat("_Cull",1);inner.SetFloat("_LayerOpacity",.18f);inner.renderQueue=3020;inner.SetShaderPassEnabled("ShadowCaster",false);inside.AddComponent<MeshRenderer>().sharedMaterial=inner;layers++;
                    }
                }
                renderer.sharedMaterials=assigned;
                var uvBake=bakes.FirstOrDefault(b=>b.index==row.index);
                if(uvBake!=null&&mesh.sharedMesh.uv.Length!=mesh.sharedMesh.vertexCount)
                {
                    var original=mesh.sharedMesh;string dest=Root+"/Meshes/"+row.index+".asset";var copy=AssetDatabase.LoadAssetAtPath<Mesh>(dest);
                    if(!copy){copy=UnityEngine.Object.Instantiate(original);copy.name=original.name+" (UV copy)";copy.uv=uvBake.uv;copy.RecalculateTangents();AssetDatabase.CreateAsset(copy,dest);}
                    if(!copy.vertices.SequenceEqual(original.vertices)||!copy.triangles.SequenceEqual(original.triangles))throw new InvalidOperationException("Geometry changed during UV copy");mesh.sharedMesh=copy;meshCopies++;
                }
            }
            // The untouched Bar scene is the comparison baseline; disable its original lights only in the new scene.
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))light.enabled=false;
            var rigObject=new GameObject("Bar presentation rig");var rig=rigObject.AddComponent<PresentationRig>();rig.DesktopPipeline=desktop;rig.WebPipeline=web;
            foreach(var item in settings.lights){var go=new GameObject(item.label);go.transform.SetParent(rigObject.transform,false);go.transform.position=item.position;go.transform.LookAt(item.target);var light=go.AddComponent<Light>();light.type=LightType.Spot;light.color=item.color;light.intensity=item.intensity;light.range=item.range;light.spotAngle=item.angle;light.innerSpotAngle=item.angle*.6f;light.shadows=item.shadows?LightShadows.Soft:LightShadows.None;}
            var profile=AssetDatabase.LoadAssetAtPath<PostVolumeProfile>(Root+"/Profiles/BarPost.asset");if(!profile){profile=ScriptableObject.CreateInstance<PostVolumeProfile>();AssetDatabase.CreateAsset(profile,Root+"/Profiles/BarPost.asset");}
            foreach(var component in profile.components.ToArray())UnityEngine.Object.DestroyImmediate(component,true);profile.components.Clear();
            var tone=profile.Add<Tonemapping>(true);tone.mode.Override((TonemappingMode)Enum.Parse(typeof(TonemappingMode),settings.post.tonemapping));var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(settings.post.bloomThreshold);bloom.intensity.Override(settings.post.bloomIntensity);
            var color=profile.Add<ColorAdjustments>(true);color.postExposure.Override(settings.post.exposure);color.contrast.Override(settings.post.contrast);color.saturation.Override(settings.post.saturation);
            foreach(var component in profile.components)if(!AssetDatabase.Contains(component))AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(profile);rig.PostProfile=profile;
            var volume=rigObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;volume.sharedProfile=profile;
            const string hdri=Root+"/Environment/warm_bar_1k.hdr";var hdrImport=(TextureImporter)AssetImporter.GetAtPath(hdri);hdrImport.sRGBTexture=false;hdrImport.textureCompression=TextureImporterCompression.Uncompressed;hdrImport.SaveAndReimport();
            var sky=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Environment/BarSkybox.mat");if(!sky){sky=new Material(Shader.Find("Skybox/Panoramic"));AssetDatabase.CreateAsset(sky,Root+"/Environment/BarSkybox.mat");}sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture>(hdri));sky.SetFloat("_Exposure",settings.environmentExposure);
            // warm_bar averages R:G:B = 1.85:0.81:0.34, which turned every reflection gold; this tint keeps a mild warm 1.25:1:0.8.
            sky.SetColor("_Tint",new Color(.412f,.546f,.729f,.5f));
            RenderSettings.skybox=sky;RenderSettings.ambientMode=AmbientMode.Skybox;RenderSettings.ambientIntensity=settings.environmentIntensity;RenderSettings.customReflectionTexture=null;RenderSettings.defaultReflectionMode=DefaultReflectionMode.Skybox;RenderSettings.reflectionIntensity=settings.reflectionIntensity;
            var probeObject=new GameObject("Baked bar reflection");probeObject.transform.SetParent(rigObject.transform,false);probeObject.transform.position=new Vector3(0,1.15f,.65f);var probe=probeObject.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Baked;probe.resolution=settings.desktop.reflectionResolution;probe.size=new Vector3(3.2f,2.2f,2.5f);rig.BakedProbe=probe;probe.boxProjection=true;probe.hdr=true;probe.nearClipPlane=.05f;probe.farClipPlane=12;
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)){camera.allowHDR=true;camera.allowMSAA=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;}
            PrefabUtility.SaveAsPrefabAssetAndConnect(rigObject,Root+"/Prefabs/BarPresentationRig.prefab",InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene,"Assets/Bar/Scenes/BarPresentation.unity");AssetDatabase.SaveAssets();
            var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(x=>x.path=="Assets/Bar/Scenes/BarPresentation.unity")){scenes.Add(new EditorBuildSettingsScene("Assets/Bar/Scenes/BarPresentation.unity",true));EditorBuildSettings.scenes=scenes.ToArray();}
            Directory.CreateDirectory("PresentationReports");File.WriteAllText("PresentationReports/assembly.json",JsonUtility.ToJson(new AssemblyReport{materialSlots=slotsChanged,meshCopies=meshCopies,glassLayers=layers,sourceVerticesAndTrianglesUnchanged=true,scene="Assets/Bar/Scenes/BarPresentation.unity",probe=Root+"/Environment/BarProbe.exr"},true));
            VisualAudit.Ranges();
        }
        private static UniversalRenderPipelineAsset Pipeline(string source,QualityPreset preset)
        {
            string path=Root+"/Profiles/"+preset.name+".asset";
            if(!AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path))AssetDatabase.CopyAsset(source,path);
            var asset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);asset.supportsHDR=true;asset.supportsCameraOpaqueTexture=true;asset.supportsCameraDepthTexture=true;asset.msaaSampleCount=preset.msaa;asset.renderScale=preset.renderScale;asset.shadowDistance=preset.shadowDistance;asset.mainLightShadowmapResolution=preset.shadowResolution;asset.additionalLightsShadowmapResolution=preset.shadowResolution;
            var serialized=new SerializedObject(asset);serialized.FindProperty("m_OpaqueDownsampling").intValue=1;
            var list=serialized.FindProperty("m_RendererDataList");
            for(int i=0;i<list.arraySize;i++)
            {
                string rendererPath=Root+"/Profiles/"+preset.name+"Renderer"+i+".asset";var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
                if(!renderer)
                {
                    renderer=UnityEngine.Object.Instantiate((ScriptableRendererData)list.GetArrayElementAtIndex(i).objectReferenceValue);var features=renderer.rendererFeatures.ToArray();renderer.rendererFeatures.Clear();AssetDatabase.CreateAsset(renderer,rendererPath);
                    foreach(var feature in features){var copy=UnityEngine.Object.Instantiate(feature);if(copy.GetType().Name=="ScreenSpaceAmbientOcclusion")copy.SetActive(preset.ssao);AssetDatabase.AddObjectToAsset(copy,renderer);renderer.rendererFeatures.Add(copy);}
                }
                renderer.SetDirty();EditorUtility.SetDirty(renderer);list.GetArrayElementAtIndex(i).objectReferenceValue=renderer;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);return asset;
        }
        private static void QualityLevels(UniversalRenderPipelineAsset desktop,UniversalRenderPipelineAsset web)
        {
            var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").First();var so=new SerializedObject(settings);var rows=so.FindProperty("m_QualitySettings");
            foreach(var pair in new[]{("Desktop",desktop),("Web",web)})
            {
                int index=-1;for(int i=0;i<rows.arraySize;i++)if(rows.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue==pair.Item1)index=i;
                if(index<0){index=rows.arraySize;rows.InsertArrayElementAtIndex(index);}
                var row=rows.GetArrayElementAtIndex(index);row.FindPropertyRelative("name").stringValue=pair.Item1;row.FindPropertyRelative("antiAliasing").intValue=4;row.FindPropertyRelative("customRenderPipeline").objectReferenceValue=pair.Item2;var excluded=row.FindPropertyRelative("excludedTargetPlatforms");excluded.ClearArray();foreach(var platform in pair.Item1=="Desktop"?new[]{"WebGL","Android","iPhone"}:new[]{"Standalone","Android","iPhone"}){excluded.InsertArrayElementAtIndex(excluded.arraySize);excluded.GetArrayElementAtIndex(excluded.arraySize-1).stringValue=platform;}
            }
            so.ApplyModifiedPropertiesWithoutUndo();int level=Array.IndexOf(QualitySettings.names,"Desktop");QualitySettings.SetQualityLevel(level,true);QualitySettings.renderPipeline=desktop;
        }
        public static void BakeProbe()
        {
            Guard();EditorSceneManager.OpenScene("Assets/Bar/Scenes/BarPresentation.unity");var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();rig.Desktop();
            var probe=UnityEngine.Object.FindFirstObjectByType<ReflectionProbe>();string path=Root+"/Environment/BarProbe.exr";
            // Only reflection-static objects enter a baked probe; without this the probe held nothing but the dim sky.
            foreach(var view in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None))
                if(view.DefinitionId.StartsWith("fixture."))MarkReflectionStatic(view.gameObject);
            foreach(string room in new[]{"Back wall","Floor","Back shelf"})
            {
                var roomObject=GameObject.Find(room);
                if(roomObject)MarkReflectionStatic(roomObject);
            }
            var cards=ReflectionCards(rig.transform);
            foreach(var card in cards)card.enabled=true;
            bool baked=Lightmapping.BakeReflectionProbe(probe,path);
            foreach(var card in cards)card.enabled=false;
            if(!baked)throw new InvalidOperationException("Reflection bake failed");AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);rig.BakedProbe=probe;rig.BakedReflection=AssetDatabase.LoadAssetAtPath<Cubemap>(path);probe.bakedTexture=rig.BakedReflection;
            if(!probe.bakedTexture)throw new InvalidOperationException("No baked reflection texture");PrefabUtility.ApplyPrefabInstance(rig.gameObject,InteractionMode.AutomatedAction);EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        /// <summary>
        /// The room behind the counter is dark, so polished steel reflected almost nothing but the wood top.
        /// These warm soft panels stand in for bar lamps: they are enabled only while the probe bakes,
        /// so they appear in reflections but never in the camera view and cast no light.
        /// </summary>
        private static MeshRenderer[] ReflectionCards(Transform rig)
        {
            var root=rig.Find("Probe reflection cards");
            if(!root)
            {
                root=new GameObject("Probe reflection cards").transform;
                root.SetParent(rig,false);
            }
            string materialPath=Root+"/Materials/ProbeCard.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material,materialPath);
            }
            material.SetColor("_BaseColor",new Color(1.25f,1.17f,1.04f,1));
            material.SetFloat("_Cull",0);
            EditorUtility.SetDirty(material);
            string fillPath=Root+"/Materials/ProbeFill.mat";
            var fill=AssetDatabase.LoadAssetAtPath<Material>(fillPath);
            if(!fill)
            {
                fill=new Material(material);
                AssetDatabase.CreateAsset(fill,fillPath);
            }
            fill.SetColor("_BaseColor",new Color(1,1,1.02f,1));
            fill.SetFloat("_Cull",0);
            EditorUtility.SetDirty(fill);
            // Seen from the player's height, the camera-facing side of a tin reflects toward the player and
            // slightly downward, so the large dim fill sits low behind the player; bright strips give the rims.
            var layout=new[]
            {
                ("Ceiling softbox",new Vector3(0,2.15f,.65f),new Vector3(90,0,0),new Vector2(2.4f,.9f),material),
                ("Front panel",new Vector3(0,1f,-.55f),Vector3.zero,new Vector2(3f,1.6f),fill),
                ("Left strip",new Vector3(-1.5f,1.25f,.5f),new Vector3(0,90,0),new Vector2(.35f,1.3f),material),
                ("Right strip",new Vector3(1.5f,1.25f,.5f),new Vector3(0,90,0),new Vector2(.35f,1.3f),material),
            };
            var cards=new List<MeshRenderer>();
            foreach(var (name,position,euler,size,cardMaterial) in layout)
            {
                var card=root.Find(name);
                if(!card)
                {
                    var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
                    UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.name=name;
                    card=quad.transform;
                    card.SetParent(root,false);
                }
                card.position=position;
                card.eulerAngles=euler;
                card.localScale=new Vector3(size.x,size.y,1);
                GameObjectUtility.SetStaticEditorFlags(card.gameObject,StaticEditorFlags.ReflectionProbeStatic);
                var renderer=card.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=cardMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off;
                renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                renderer.enabled=false;
                cards.Add(renderer);
            }
            return cards.ToArray();
        }
        private static void MarkReflectionStatic(GameObject root)
        {
            foreach(var child in root.GetComponentsInChildren<Transform>(true))
            {
                var flags=GameObjectUtility.GetStaticEditorFlags(child.gameObject);
                GameObjectUtility.SetStaticEditorFlags(child.gameObject,flags|StaticEditorFlags.ReflectionProbeStatic);
            }
        }
    }
}
