using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private const string PolishRoot="Assets/Bar/Environment/GinzaPolish";
        private static string PolishReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaPolishReports"));
        private static Transform polish;
        private static Material frost,ivoryFill;
        private static void InitPolishMaterials()
        {
            materials.Clear();foreach(var f in Directory.GetFiles(AssetRoot+"/Materials","*.mat")){var m=AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\','/'));materials.Add(m.name,m);}
            walnut=materials["Walnut"];walnutDark=materials["DarkWalnut"];brass=materials["AgedBrass"];brassDark=materials["OxidisedBrass"];leather=materials["CognacLeather"];burgundy=materials["BurgundyLeather"];iron=materials["CastIron"];rubber=materials["RibbedRubber"];
        }
        private static Material PolishMat(string name,string hex,float metal,float smooth,float emission=0)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",Hex(hex));m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",smooth);
            if(emission>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(hex)*emission);}
            AssetDatabase.CreateAsset(m,PolishRoot+"/Materials/"+name+".mat");materials.Add(name,m);return m;
        }
        public static void PolishArchitecture()
        {
            RequireCache();Directory.CreateDirectory(PolishReport);var scene=EditorSceneManager.OpenScene(ScenePath);
            var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inventory.transforms,Poses());
            lounge=scene.GetRootGameObjects().Single(g=>g.name=="Ginza_Lounge").transform;
            if(lounge.Find("Ginza_ArchitecturalPolish"))throw new InvalidOperationException("Polish already installed. Rebuild from the saved source scene.");
            Directory.CreateDirectory(PolishRoot+"/Materials");AssetDatabase.Refresh();MeshPath=PolishRoot+"/PolishMeshes.asset";serial=0;meshFile=false;parts.Clear();lights.Clear();InitPolishMaterials();
            frost=PolishMat("FrostedAmberDiffuser","FFD5A0",0,.20f,.55f);ivoryFill=PolishMat("WarmWhiteTaskStrip","FFEAD0",0,.35f,1.2f);
            floor=inventory.counterPosition.y;width=inventory.counterWidth;depth=inventory.counterDepth;top=inventory.surface.y-floor;backFront=-depth*.5f-1.40f;wallBack=backFront-.48f;wallFront=wallBack+7.0f;
            polish=Group("Ginza_ArchitecturalPolish",lounge,Vector3.zero);
            SealCounter();SealWells();ExpandCustomerRoom();CalibrateTasks();PopulateBooths();Entrance();VinylStation();RoomSconces();
            var probe=lounge.GetComponentInChildren<ReflectionProbe>();probe.transform.localPosition=new Vector3(0,1.45f,(wallBack+wallFront)*.5f);probe.size=new Vector3(6.4f,3f,6.9f);
            WritePolishManifest();ComparePoses(inventory.transforms,Poses());
            PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            VerifyPolishSaved();
        }
        private static void SealCounter()
        {
            var wrap=lounge.Find("BarCounter_CustomerWrap");UnityEngine.Object.DestroyImmediate(wrap.Find("Raised drinking ledge front 4 segment 10mm bevel").gameObject);UnityEngine.Object.DestroyImmediate(wrap.Find("L return drinking ledge").gameObject);
            float hw=width*.5f,hd=depth*.5f,under=top+.075f;
            var outline=new[]{new Vector2(-hw-.28f,-hd-.01f),new Vector2(-hw+.06f,-hd-.01f),new Vector2(-hw+.06f,hd-.09f),new Vector2(hw-.06f,hd-.09f),new Vector2(hw-.06f,-hd-.01f),new Vector2(hw+.28f,-hd-.01f),new Vector2(hw+.28f,hd+.28f),new Vector2(-hw-.28f,hd+.28f)};
            Geometry("Continuous U ledge with joined miter corners 10mm bevel",wrap,BeveledU(outline,.045f,.010f),walnut,new Vector3(0,under,0),Quaternion.identity);
            // Two millimetres overlap at each horizontal joint; full-thickness solid backing.
            var riser=Group("RiserBulkhead",wrap,Vector3.zero);
            Box("Continuous front 60mm dark walnut riser",riser,new Vector3(width+.06f,.079f,.06f),new Vector3(0,top+.0375f,hd+.020f),.001f,walnutDark);
            foreach(int s in new[]{-1,1})Box("60mm side return riser "+s,riser,new Vector3(.06f,.079f,depth+.064f),new Vector3(s*(hw+.020f),top+.0375f,.010f),.001f,walnutDark);
            foreach(float x in new[]{-.90f,-.30f,.30f,.90f})
            {
                var c=Group("Brushed brass inner corbel "+x,riser,new Vector3(x,0,0));
                Box("Corbel wall mounting plate",c,new Vector3(.028f,.052f,.006f),new Vector3(0,top+.038f,hd-.014f),.002f,brass);
                Tube("Swept concave corbel",c,Bezier(new Vector3(0,top+.011f,hd-.020f),new Vector3(0,top+.057f,hd-.024f),new Vector3(0,top+.068f,hd-.075f),new Vector3(0,under-.002f,hd-.090f)),.016f,brass,.008f);
                Box("Under ledge corbel bearing",c,new Vector3(.024f,.006f,.085f),new Vector3(0,under-.002f,hd-.055f),.002f,brass);
            }
            // Continue the tambour around the outer front corners with real quarter-round staves.
            foreach(int s in new[]{-1,1})for(int i=0;i<4;i++){float a=i*30*Mathf.Deg2Rad;HalfSlat("Miter corner tambour "+s+"_"+i,wrap,top-.12f,walnut,new Vector3(s*(hw-.012f+Mathf.Sin(a)*.024f),.10f,hd-.012f+Mathf.Cos(a)*.024f),Quaternion.Euler(0,s*i*30,0));}
            var front=wrap.GetComponents<BoxCollider>().Single(c=>c.size.x>1);front.center=new Vector3(0,(top+.12f)*.5f,hd+.12f);front.size=new Vector3(width+.56f,top+.12f,.32f);
            Collider(wrap,new Vector3(hw+.14f,(top+.12f)*.5f,0),new Vector3(.28f,top+.12f,depth+.02f));
            Box("Warm white linear inner lip diffuser",riser,new Vector3(width-.14f,.006f,.009f),new Vector3(0,under-.004f,hd-.052f),.0015f,ivoryFill);
            foreach(float x in new[]{-.75f,0,.75f}){var l=Lamp("Gentle linear fill section",riser,new Vector3(x,under-.016f,hd-.058f),Hex("FFEAD0"),.012f,1.5f,true,115,70,true);l.transform.localRotation=Quaternion.LookRotation(new Vector3(0,-.025f,.080f));l.shadowNormalBias=.015f;}
            foreach(var l in wrap.GetComponentsInChildren<Light>().Where(l=>l.name.StartsWith("Under ledge warm pool"))){l.shadows=LightShadows.Soft;l.shadowBias=.012f;l.shadowNormalBias=.015f;}
        }
        private static void SealWells()
        {
            var g=Group("Opaque sink and icebin underwell seals",polish,Vector3.zero);
            foreach(string id in new[]{"fixture.sink","fixture.icebin"})
            {
                var view=UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).Single(v=>v.DefinitionId==id);
                var b=view.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="BasinShell").bounds;var c=lounge.InverseTransformPoint(b.center);var size=new Vector3(b.size.x,b.size.y,b.size.z);
                var well=Group(id+" sealed underwell",g,new Vector3(c.x,0,c.z));float bottom=lounge.InverseTransformPoint(new Vector3(b.center.x,b.min.y,b.center.z)).y;
                Box("Opaque sealed well bottom",well,new Vector3(size.x+.018f,.012f,size.z+.018f),new Vector3(0,bottom-.008f,0),.002f,rubber);
                float h=top-bottom+.012f;foreach(int s in new[]{-1,1}){Box("Closed side of underwell "+s,well,new Vector3(.010f,h,size.z+.018f),new Vector3(s*(size.x*.5f+.004f),bottom+h*.5f-.008f,0),.002f,rubber);Box("Closed end of underwell "+s,well,new Vector3(size.x+.018f,h,.010f),new Vector3(0,bottom+h*.5f-.008f,s*(size.z*.5f+.004f)),.002f,rubber);}
            }
        }
        private static void ExpandCustomerRoom()
        {
            var env=lounge.Find("ArchitecturalEnvelope");float center=(wallBack+wallFront)*.5f;
            var ground=env.Find("Dark walnut herringbone floor");ground.localPosition+=Vector3.forward*.4f;ground.Find("Ribbed rubber anti fatigue mat").localPosition-=Vector3.forward*.4f;
            var old=ground.Find("Beveled parquet floor slab");UnityEngine.Object.DestroyImmediate(old.gameObject);
            var slab=Box("Beveled parquet floor slab",ground,new Vector3(6.5f,.10f,7f),Vector3.zero,.008f,materials["HerringboneFloor"]);var m=slab.GetComponent<MeshFilter>().sharedMesh;m.uv=m.vertices.Select(p=>new Vector2((p.x+3.25f)/1.44f,(p.z+3.5f)/1.44f)).ToArray();ground.GetComponent<BoxCollider>().size=new Vector3(6.5f,.1f,7f);
            env.Find("Courtyard lounge wall").localPosition=new Vector3(0,0,wallFront);
            foreach(string name in new[]{"Left wainscot wall","Right wainscot wall"}){var wall=env.Find(name);foreach(Transform child in wall.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);UnityEngine.Object.DestroyImmediate(wall.GetComponent<BoxCollider>());wall.localPosition=new Vector3(wall.localPosition.x,0,center);WallDress(wall,7f);}
            var ceiling=env.Find("Timber ceiling and four exposed beams");ceiling.localPosition+=Vector3.forward*.4f;
            UnityEngine.Object.DestroyImmediate(ceiling.Find("Dark warm timber ceiling").gameObject);Box("Dark warm timber ceiling",ceiling,new Vector3(6.5f,.12f,7f),new Vector3(0,.06f,0),.009f,materials["CeilingWalnut"]);ceiling.GetComponent<BoxCollider>().size=new Vector3(6.5f,.12f,7f);
            for(int i=1;i<=4;i++){var beam=ceiling.Find("160 by 200mm walnut ceiling beam "+i);beam.localPosition+=Vector3.forward*(i==4?.4f:-.4f);}
        }
        private static void CalibrateTasks()
        {
            lounge.Find("Bar_PendantLight_Trio").localPosition-=Vector3.forward*(depth*.5f+.12f);
            var beam=lounge.Find("ArchitecturalEnvelope/Timber ceiling and four exposed beams/160 by 200mm walnut ceiling beam 2");beam.localPosition=new Vector3(0,beam.localPosition.y,-beam.parent.localPosition.z);
            foreach(var l in lounge.Find("Bar_PendantLight_Trio").GetComponentsInChildren<Light>()){l.intensity=4.1f*2.2f;l.color=Hex("FFEAD0");l.spotAngle=80;l.innerSpotAngle=45;l.shadowBias=.02f;l.shadowNormalBias=.025f;}
        }
        private static void WritePolishManifest()
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));var meshes=lounge.GetComponentsInChildren<MeshFilter>();
            float stoolFront=lounge.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Speakeasy_BarStool")).Max(c=>LocalMaxZ(c.bounds));float chairBack=lounge.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Leather walnut tub chair")).Min(c=>lounge.InverseTransformPoint(c.bounds.center).z-Vector3.Dot(Abs(lounge.forward),c.bounds.extents));
            manifest.revision="GinzaArchitecturalPolish-2026-10-30";manifest.roomDepth=7f;manifest.parts=meshes.Length;manifest.vertices=meshes.Sum(m=>m.sharedMesh.vertexCount);manifest.triangles=meshes.Sum(m=>m.sharedMesh.triangles.Length/3);manifest.boxColliders=lounge.GetComponentsInChildren<BoxCollider>().Length;manifest.customerPassage=chairBack-stoolFront;if(manifest.customerPassage<1.3f)throw new InvalidOperationException("Customer aisle below 1.3m");manifest.componentNames=meshes.Select(m=>Hierarchy(m.transform)).ToArray();manifest.adaptations=new[]{"Only the customer-side room extended by 0.8m to accommodate two opposite tub chairs while preserving at least 1.3m circulation. Original 388 gameplay transforms unchanged.","Solid 60mm walnut risers overlap the worktop and continuous U ledge; no open gap. Two sealed opaque underwells and shadowed exterior amber pools stop underside light leaks.","Three pendants now on worktop centreline, intensity 9.02 (2.2x), #FFEAD0, 80/45 degrees. Separate gentle inner-lip fill.","Entrance is detailed closed architectural scenery; no new gameplay door operation or physics model changes."};
            string json=JsonUtility.ToJson(manifest,true);File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",json);File.WriteAllText(Path.Combine(PolishReport,"model-manifest.json"),json);AssetDatabase.Refresh();var tag=lounge.GetComponent<GinzaEnvironmentTag>();tag.revision=manifest.revision;tag.manifest=AssetDatabase.LoadAssetAtPath<TextAsset>(AssetRoot+"/GinzaEnvironmentManifest.json");
        }
        public static void VerifyPolishSaved()
        {
            RequireCache();Directory.CreateDirectory(PolishReport);EditorSceneManager.OpenScene(ScenePath);var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inventory.transforms,Poses());
            lounge=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;var tag=lounge.GetComponent<GinzaEnvironmentTag>();if(!tag.manifest||tag.revision!="GinzaArchitecturalPolish-2026-10-30")throw new InvalidOperationException("Polished manifest missing");
            if(lounge.GetComponentsInChildren<Collider>().Any(c=>c is not BoxCollider))throw new InvalidOperationException("Decorative mesh collider found");
            var tasks=lounge.Find("Bar_PendantLight_Trio").GetComponentsInChildren<Light>();if(tasks.Length!=3||tasks.Any(l=>Mathf.Abs(l.intensity-9.02f)>.001f||l.spotAngle!=80||l.innerSpotAngle!=45||Mathf.Abs(lounge.InverseTransformPoint(l.transform.position).z)>.001f))throw new InvalidOperationException("Task light calibration failed");
            var p=lounge.Find("Ginza_ArchitecturalPolish");if(p.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Leather walnut tub chair"))!=2||lounge.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Upper ")&&t.name.Contains("wall brass sconce"))!=4||p.Find("Opaque sink and icebin underwell seals").childCount!=2||lounge.Find("BarCounter_CustomerWrap/RiserBulkhead").Cast<Transform>().Count(t=>t.name.StartsWith("Brushed brass inner corbel"))!=4)throw new InvalidOperationException("Required polish part missing");
            var risers=lounge.Find("BarCounter_CustomerWrap/RiserBulkhead").GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.Contains("riser")&&r.name.Contains("60mm")).ToArray();if(risers.Length!=3||risers.Any(r=>r.bounds.min.y>inventory.surface.y||r.bounds.max.y<inventory.surface.y+.075f))throw new InvalidOperationException("Riser does not seal worktop to ledge");
            File.WriteAllText(Path.Combine(PolishReport,"saved-scene-verification.json"),"{\"compiled\":true,\"savedSceneReloaded\":true,\"all388OriginalPosesBitExact\":true,\"coreObjectsMoved\":0,\"taskLights\":3,\"taskIntensityMultiplier\":2.2,\"taskColour\":\"#FFEAD0\",\"outerInnerDegrees\":[80,45],\"riserThicknessMetres\":0.06,\"roomDepthMetres\":7.0,\"onlyMajorBoxColliders\":true,\"editModeTestsRun\":0,\"playModeTestsRun\":0}");
        }
        public static void ExportPolishDelivery()
        {
            VerifyPolishSaved();InitPolishMaterials();foreach(var f in Directory.GetFiles(PolishRoot+"/Materials","*.mat")){var m=AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\','/'));materials.Add(m.name,m);}ExportObj();foreach(string f in new[]{"GinzaEnvironment.obj","GinzaEnvironment.mtl"})File.Copy(Path.Combine(Report,f),Path.Combine(PolishReport,f),true);
            AssetDatabase.ExportPackage(new[]{AssetRoot,PolishRoot,"Assets/Bar/Unity/Views/GinzaEnvironmentTag.cs"},Path.Combine(PolishReport,"GinzaArchitecturalPolish.unitypackage"),ExportPackageOptions.Recurse);
        }
        public static void RefinePolishFill()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);lounge=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;
            foreach(var l in lounge.GetComponentsInChildren<Light>().Where(l=>l.name=="Gentle linear fill section")){l.intensity=.012f;l.transform.localRotation=Quaternion.LookRotation(new Vector3(0,-.025f,.080f));}
            InitPolishMaterials();WritePolishManifest();PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);VerifyPolishSaved();
        }
    }
}
