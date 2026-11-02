using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using PostVolumeProfile=UnityEngine.Rendering.VolumeProfile;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        [Serializable] private sealed class Manifest
        {
            public string revision="GinzaSpeakeasy-2026-10-30",geometry="Contoured multi-part meshes; four-segment bevels, lathe profiles, 24-rib shades and real recessed panels";
            public int parts,triangles,vertices,boxColliders,bottles=24,stools=3,pendants=3,paddedStoolChannels=5,boothChannels=9;
            public float roomWidth=6.5f,roomDepth=6.2f,roomHeight=3.1f,backbarClearance=1.35f,sideClearance,customerPassage;
            public float stoolSeatHeight=.68f,stoolOverallHeight=.84f,pendantBottomWorldY=2.02f;
            public string[] componentNames,adaptations;
        }
        private static void Materials()
        {
            materials.Clear();
            walnut=Mat("Walnut","FFFFFF",0,.56f,"Walnut");walnutDark=Mat("DarkWalnut","95816C",0,.44f,"Walnut");brass=Mat("AgedBrass","FFFFFF",.86f,.66f,"AgedBrass");brassDark=Mat("OxidisedBrass","8E7852",.80f,.48f,"AgedBrass");
            leather=Mat("CognacLeather","FFFFFF",0,.38f,"CognacLeather");burgundy=Mat("BurgundyLeather","FFFFFF",0,.40f,"BurgundyLeather");iron=Mat("CastIron","181B19",.75f,.30f);stone=Mat("CharcoalStone","FFFFFF",.05f,.67f,"CharcoalStone");rubber=Mat("RibbedRubber","1B1B18",0,.18f);
            amberGlow=Mat("ConcealedAmberLED","FFAA44",0,.4f,"",1,2.8f);filament=Mat("EdisonFilament","FFD28A",0,.5f,"",1,7.5f);
            glassAmber=Mat("AmberGlass","C58939",0,.80f,"",.46f,.12f);Mat("EmeraldGlass","286A4A",0,.82f,"",.42f);Mat("CobaltGlass","274382",0,.84f,"",.46f);Mat("ClearGlass","D8E1D9",0,.9f,"",.18f);
            Mat("WhiskyLiquid","8D572B",.05f,.71f,"",.79f);Mat("BotanicalLiquid","415541",0,.74f,"",.72f);Mat("BottleLabel","DECEAA",0,.22f);Mat("LampLining","FFD28A",.25f,.65f,"",1,.45f);
            Mat("ForestPlaster","FFFFFF",0,.18f,"ForestPlaster");Mat("HerringboneFloor","FFFFFF",0,.62f,"HerringboneWalnut");Mat("CeilingWalnut","30251D",0,.30f,"Walnut");Mat("ShojiDiffuser","FFB76D",0,.22f,"",1,.9f);Mat("PaperIvory","D3C7A9",0,.13f);Mat("VintageGinza","FFFFFF",0,.12f,"PrintGinza");Mat("VintageJazz","FFFFFF",0,.12f,"PrintJazz");
        }
        private static void PostProcessing(PresentationRig rig)
        {
            var profile=ScriptableObject.CreateInstance<PostVolumeProfile>();profile.name="Ginza warm neutral post";AssetDatabase.CreateAsset(profile,AssetRoot+"/GinzaPost.asset");
            foreach(var c in rig.PostProfile.components){var copy=UnityEngine.Object.Instantiate(c);copy.name=c.name;profile.components.Add(copy);AssetDatabase.AddObjectToAsset(copy,profile);}
            profile.TryGet<ColorAdjustments>(out var color);color.postExposure.Override(.10f);color.contrast.Override(6);color.saturation.Override(-3);
            profile.TryGet<Bloom>(out var bloom);bloom.threshold.Override(1.05f);bloom.intensity.Override(.28f);rig.PostProfile=profile;rig.GetComponent<Volume>().sharedProfile=profile;
        }
        public static void Build()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);var oldPoses=Poses();var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));
            var roots=scene.GetRootGameObjects();var existing=roots.SingleOrDefault(g=>g.name=="Ginza_Lounge");if(existing)UnityEngine.Object.DestroyImmediate(existing);
            Directory.CreateDirectory(AssetRoot+"/Materials");foreach(var file in Directory.GetFiles(AssetRoot+"/Materials","*.mat"))AssetDatabase.DeleteAsset(file.Replace('\\','/'));
            foreach(var asset in new[]{MeshPath,AssetRoot+"/GinzaPost.asset",AssetRoot+"/GinzaEnvironment.prefab"})if(File.Exists(asset))AssetDatabase.DeleteAsset(asset);
            AssetDatabase.Refresh();parts.Clear();lights.Clear();serial=0;meshFile=false;
            var session=UnityEngine.Object.FindFirstObjectByType<BarSession>();var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))l.enabled=false;
            foreach(string name in new[]{"Floor","Back wall","Back shelf"})roots.Single(g=>g.name==name).SetActive(false);
            floor=inventory.counterPosition.y;width=inventory.counterWidth;depth=inventory.counterDepth;top=inventory.surface.y-floor;
            backFront=-depth*.5f-1.40f;wallBack=backFront-.48f;wallFront=wallBack+6.2f;
            lounge=Group("Ginza_Lounge",null,new Vector3(inventory.surface.x,floor,inventory.surface.z),Quaternion.LookRotation(inventory.customer,Vector3.up));var tag=lounge.gameObject.AddComponent<GinzaEnvironmentTag>();rig.Environment=tag;
            Materials();Room();CounterWrap();Stool(-.75f,-8,1);Stool(0,4,2);Stool(.75f,11,3);Backbar();Pendants();BoothAndTable();
            Lamp("Warm reflected ambient fill left",lounge,new Vector3(-2.60f,2.50f,.85f),Hex("FFE4B8"),1.15f,6.4f);Lamp("Warm reflected ambient fill right",lounge,new Vector3(2.60f,2.50f,.85f),Hex("FFE4B8"),1.15f,6.4f);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Hex("211813");RenderSettings.ambientIntensity=1;RenderSettings.fog=false;RenderSettings.skybox=null;RenderSettings.reflectionIntensity=1;
            if(rig.BakedProbe)rig.BakedProbe.enabled=false;
            var probe=Group("Room reflection probe",lounge,new Vector3(0,1.45f,(wallBack+wallFront)*.5f)).gameObject.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;probe.resolution=256;probe.size=new Vector3(6.4f,3f,6.1f);probe.boxProjection=true;probe.intensity=.9f;probe.clearFlags=ReflectionProbeClearFlags.SolidColor;probe.backgroundColor=Hex("211813");
            PostProcessing(rig);
            Physics.SyncTransforms();var meshes=lounge.GetComponentsInChildren<MeshFilter>();float side=3.19f-(width*.5f+.28f);float stoolFront=lounge.GetComponentsInChildren<BoxCollider>().Where(c=>c.transform.name.StartsWith("Speakeasy_BarStool")).Max(c=>LocalMaxZ(c.bounds));float frontPassage=2.79f-.31f-stoolFront;
            if(side<1.3f||frontPassage<1.3f)throw new InvalidOperationException("Authored clear aisle below 1.3m");
            var manifest=new Manifest{parts=parts.Count,triangles=meshes.Sum(m=>m.sharedMesh.triangles.Length/3),vertices=meshes.Sum(m=>m.sharedMesh.vertexCount),boxColliders=lounge.GetComponentsInChildren<BoxCollider>().Length,sideClearance=side,customerPassage=frontPassage,componentNames=parts.ToArray(),adaptations=new[]{"Existing 0.94m worktop fixes tambour net height at about 0.83m, with the specified 0.10m base and 0.04m trim.","0.68m seat plus 0.15m bucket back needs about 0.84m overall, resolving the contradictory 0.76m total.","Room depth 6.2m preserves 1.35m behind the fixed workbench and more than 1.3m circulation past stools/table.","The existing gameplay serve mat stays at its exact transform; the new brass/leather ledge pad is decorative.","The unchanged 25-degree-down first-person camera does not see 2.02m-high shades over the bar; their specified safe height is retained and the additional view shows them."}};
            string text=JsonUtility.ToJson(manifest,true);File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",text);File.WriteAllText(Path.Combine(Report,"model-manifest.json"),text);AssetDatabase.Refresh();tag.manifest=AssetDatabase.LoadAssetAtPath<TextAsset>(AssetRoot+"/GinzaEnvironmentManifest.json");
            ComparePoses(oldPoses,Poses());File.WriteAllText(Path.Combine(Report,"transform-audit.json"),"{\"allOriginalWorldPosesExactlyEqual\":true,\"originalTransforms\":"+oldPoses.Length+",\"coreObjectsMoved\":0}");
            PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            ExportObj();
        }
        private static float LocalMaxZ(Bounds bounds)=>lounge.InverseTransformPoint(bounds.center).z+Vector3.Dot(Abs(lounge.forward),bounds.extents);
        private static void ComparePoses(PoseRow[] a,PoseRow[] b)
        {
            if(a.Length!=b.Length)throw new InvalidOperationException("Original transform count changed");for(int i=0;i<a.Length;i++)if(a[i].path!=b[i].path||!a[i].position.Equals(b[i].position)||!a[i].rotation.Equals(b[i].rotation)||!a[i].scale.Equals(b[i].scale))throw new InvalidOperationException("Original pose changed: "+a[i].path);
        }
        public static void RefineFinalMaterials()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);var root=scene.GetRootGameObjects().Single(g=>g.name=="Ginza_Lounge");
            foreach(var file in Directory.GetFiles(AssetRoot+"/Materials","*.mat")){var m=AssetDatabase.LoadAssetAtPath<Material>(file.Replace('\\','/'));if(m.GetFloat("_Surface")==1){m.SetFloat("_Cull",2);EditorUtility.SetDirty(m);}}
            foreach(var l in root.GetComponentsInChildren<Light>())if(l.name.StartsWith("Backbar concealed shelf wash"))l.intensity=.09f;
            PrefabUtility.SaveAsPrefabAsset(root,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);VerifySaved();
        }
        public static void VerifySaved()
        {
            RequireCache();EditorSceneManager.OpenScene(ScenePath);var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inventory.transforms,Poses());
            var env=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>();if(!env.manifest)throw new InvalidOperationException("Ginza manifest binding missing");if(env.GetComponentsInChildren<Collider>().Any(c=>c is not BoxCollider))throw new InvalidOperationException("Decorative mesh collider detected");
            File.WriteAllText(Path.Combine(Report,"saved-scene-verification.json"),"{\"compiled\":true,\"savedSceneReloaded\":true,\"all388OriginalPosesBitExact\":true,\"environmentManifestBound\":true,\"onlyMajorBoxColliders\":true}");
        }
        public static void FinishCraftDetails()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);lounge=scene.GetRootGameObjects().Single(g=>g.name=="Ginza_Lounge").transform;meshFile=true;serial=AssetDatabase.LoadAllAssetsAtPath(MeshPath).Length;materials.Clear();foreach(var f in Directory.GetFiles(AssetRoot+"/Materials","*.mat")){var m=AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\','/'));materials.Add(m.name,m);}brass=materials["AgedBrass"];rubber=materials["RibbedRubber"];
            foreach(Transform lamp in lounge.Find("Bar_PendantLight_Trio")){if(!lamp.name.StartsWith("24 rib amber pendant"))continue;if(lamp.Find("Knurled brass socket band"))throw new InvalidOperationException("Craft details already present; use full Build to regenerate");var core=lamp.Find("5mm dark braided cord");core.name="5mm dark braided cord core";var m=core.GetComponent<MeshFilter>().sharedMesh;var v=m.vertices;for(int i=0;i<v.Length;i++){v[i].x*=.84f;v[i].z*=.84f;}m.vertices=v;m.RecalculateNormals();m.RecalculateBounds();PendantCraftDetails(lamp);}
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));var meshes=lounge.GetComponentsInChildren<MeshFilter>();manifest.parts=meshes.Length;manifest.vertices=meshes.Sum(f=>f.sharedMesh.vertexCount);manifest.triangles=meshes.Sum(f=>f.sharedMesh.triangles.Length/3);manifest.componentNames=meshes.Select(f=>Hierarchy(f.transform)).ToArray();string text=JsonUtility.ToJson(manifest,true);File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",text);File.WriteAllText(Path.Combine(Report,"model-manifest.json"),text);AssetDatabase.Refresh();AssetDatabase.SaveAssets();PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);ExportObj();VerifySaved();
        }
        public static void ExportDeliveryPackage()
        {VerifySaved();AssetDatabase.ExportPackage(new[]{AssetRoot,"Assets/Bar/Unity/Views/GinzaEnvironmentTag.cs"},Path.Combine(Report,"GinzaEnvironment.unitypackage"),ExportPackageOptions.Recurse);}
        public static void FinishStemwareAndExport()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);lounge=scene.GetRootGameObjects().Single(g=>g.name=="Ginza_Lounge").transform;meshFile=true;serial=AssetDatabase.LoadAllAssetsAtPath(MeshPath).Length;materials.Clear();foreach(var f in Directory.GetFiles(AssetRoot+"/Materials","*.mat")){var m=AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\','/'));materials.Add(m.name,m);}brass=materials["AgedBrass"];
            var cabinet=lounge.Find("Backbar_Cabinet");foreach(float x in new[]{-.84f,0,.84f})StemwareRack(cabinet,x);
            foreach(var t in lounge.GetComponentsInChildren<Transform>().Where(t=>t.name=="Tiny brass corner rivet")){var pos=t.localPosition;pos.z=.0025f;t.localPosition=pos;}
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));var meshes=lounge.GetComponentsInChildren<MeshFilter>();manifest.parts=meshes.Length;manifest.vertices=meshes.Sum(f=>f.sharedMesh.vertexCount);manifest.triangles=meshes.Sum(f=>f.sharedMesh.triangles.Length/3);manifest.componentNames=meshes.Select(f=>Hierarchy(f.transform)).ToArray();string text=JsonUtility.ToJson(manifest,true);File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",text);File.WriteAllText(Path.Combine(Report,"model-manifest.json"),text);AssetDatabase.Refresh();AssetDatabase.SaveAssets();PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);ExportObj();ExportDeliveryPackage();
        }
        private static void ExportObj()
        {
            var obj=new StringBuilder("# Ginza Speakeasy, metres, Y up. Multi-part contoured source.\nmtllib GinzaEnvironment.mtl\n");int offset=1;string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
            foreach(var filter in lounge.GetComponentsInChildren<MeshFilter>())
            {
                var mesh=filter.sharedMesh;var matrix=lounge.worldToLocalMatrix*filter.transform.localToWorldMatrix;obj.AppendLine("o "+filter.name.Replace(' ','_'));obj.AppendLine("usemtl "+filter.GetComponent<MeshRenderer>().sharedMaterial.name);
                foreach(var v in mesh.vertices){var q=matrix.MultiplyPoint3x4(v);obj.AppendLine("v "+F(q.x)+" "+F(q.y)+" "+F(-q.z));}
                foreach(var uv in mesh.uv)obj.AppendLine("vt "+F(uv.x)+" "+F(uv.y));foreach(var v in mesh.normals){var q=matrix.MultiplyVector(v).normalized;obj.AppendLine("vn "+F(q.x)+" "+F(q.y)+" "+F(-q.z));}
                var t=mesh.triangles;for(int i=0;i<t.Length;i+=3){var ids=new[]{t[i]+offset,t[i+2]+offset,t[i+1]+offset};obj.AppendLine("f "+string.Join(" ",ids.Select(id=>id+"/"+id+"/"+id)));}offset+=mesh.vertexCount;
            }
            File.WriteAllText(Path.Combine(Report,"GinzaEnvironment.obj"),obj.ToString());var mtl=new StringBuilder();foreach(var kv in materials){var c=kv.Value.GetColor("_BaseColor");mtl.AppendLine("newmtl "+kv.Key+"\nKd "+F(c.r)+" "+F(c.g)+" "+F(c.b)+"\nd "+F(c.a)+"\nNs "+F(kv.Value.GetFloat("_Smoothness")*200));var texture=kv.Value.GetTexture("_BaseMap");if(texture)mtl.AppendLine("map_Kd Textures/"+texture.name+".png");}File.WriteAllText(Path.Combine(Report,"GinzaEnvironment.mtl"),mtl.ToString());
        }
    }
}
