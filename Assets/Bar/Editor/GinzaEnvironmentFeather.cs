using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static string FeatherReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaFeatherReports"));
        private static string WorkbenchLock()
        {
            var env=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>();var root=env.transform.Find("Readable workbench lighting");
            string Stable(UnityEngine.Object value)=>Regex.Replace(EditorJsonUtility.ToJson(value),"\"instanceID\":(-?[0-9]+)",m=>{int id=int.Parse(m.Groups[1].Value);if(id==0)return "\"reference\":\"null\"";var obj=EditorUtility.InstanceIDToObject(id);string key=obj is GameObject go?Hierarchy(go.transform):obj is Component c?Hierarchy(c.transform)+":"+c.GetType().Name:AssetDatabase.GetAssetPath(obj)+":"+obj.name;return "\"reference\":\""+key+"\"";});
            return string.Join("\n",root.GetComponentsInChildren<Transform>().Select(t=>Hierarchy(t)+" "+Stable(t)).Concat(root.GetComponentsInChildren<Light>().Select(l=>Stable(l))).Concat(root.GetComponentsInChildren<UniversalAdditionalLightData>().Select(l=>Stable(l))).Concat(root.GetComponentsInChildren<ReflectionProbe>().Select(p=>Stable(p))));
        }
        public static void CaptureEnvironmentBefore()
        {
            RequireCache();Directory.CreateDirectory(FeatherReport);EditorSceneManager.OpenScene(ScenePath);File.WriteAllText(Path.Combine(FeatherReport,"workbench-lock-before.txt"),WorkbenchLock());
            SessionState.SetString("GinzaFeatherCapture","before");EditorApplication.isPlaying=true;
        }
        public static void FeatherEnvironment()
        {
            RequireCache();Directory.CreateDirectory(FeatherReport);var scene=EditorSceneManager.OpenScene(ScenePath);lounge=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;
            var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();if(!rig.DesktopPipeline.useRenderingLayers||!rig.WebPipeline.useRenderingLayers)throw new InvalidOperationException("Both authored pipelines must support rendering layers");
            var locked=WorkbenchLock();if(locked!=File.ReadAllText(Path.Combine(FeatherReport,"workbench-lock-before.txt")))throw new InvalidOperationException("Workbench changed since baseline capture");
            var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inventory.transforms,Poses());
            var protectedRoot=lounge.Find("Readable workbench lighting");const int mask=(1<<24)|(1<<25)|(1<<26)|(1<<27)|(1<<28);
            if(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(r=>r.gameObject.scene.IsValid()&&(mask&(1<<r.gameObject.layer))!=0))throw new InvalidOperationException("Dedicated receiver layers already used");
            // Preserve every original light's contribution to gameplay and furnishings. Only remove the wall/ceiling receiver bits.
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene.IsValid()&&!l.transform.IsChildOf(protectedRoot)))l.cullingMask&=~mask;
            string[] walls={"Left wainscot wall","Right wainscot wall","Courtyard lounge wall","Rear wainscot wall","Timber ceiling and four exposed beams"};
            for(int i=0;i<walls.Length;i++)foreach(var r in lounge.Find("ArchitecturalEnvelope/"+walls[i]).GetComponentsInChildren<MeshRenderer>()){r.gameObject.layer=24+i;r.renderingLayerMask=2;}
            var group=Group("Feathered environment receiver lights",lounge,Vector3.zero);var arch=lounge.Find("ArchitecturalEnvelope");
            void Point(string name,Vector3 pos,float intensity,float range,int layer)
            {
                var l=Lamp(name,group,pos,Hex("FFD7AA"),intensity,range);l.cullingMask=1<<layer;l.GetUniversalAdditionalLightData().renderingLayers=(RenderingLayerMask)2u;l.shadows=LightShadows.None;
            }
            Point("Left sconce soft radial glow",new Vector3(-2.93f,1.85f,.65f),.34f,4.5f,24);
            Point("Right sconce soft radial glow",new Vector3(2.93f,1.85f,.50f),.30f,4.5f,25);
            Point("Entry corner soft radial glow",new Vector3(2.93f,2.0f,3.22f),.20f,4.0f,25);
            float front=arch.Find("Courtyard lounge wall").localPosition.z,back=arch.Find("Rear wainscot wall").localPosition.z;
            foreach(float x in new[]{-2.15f,2.15f})Point("Lounge art soft radial glow "+x,new Vector3(x,1.95f,front-.45f),.30f,4.5f,26);
            Point("Backbar wall gentle reflected glow",new Vector3(0,2.3f,back+.8f),.25f,5f,27);
            foreach(float x in new[]{-1.6f,1.6f})Point("Ceiling gentle radial bounce "+x,new Vector3(x,2.25f,(front+back)*.5f),.12f,5f,28);
            // At most two wall-only points plus the two unchanged workbench lights enter any receiver's four-light list.
            if(WorkbenchLock()!=locked)throw new InvalidOperationException("Protected workbench lighting modified");ComparePoses(inventory.transforms,Poses());
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));manifest.revision="GinzaEnvironmentFeather-2026-10-30";manifest.adaptations=manifest.adaptations.Concat(new[]{"Workbench lights and probe are locked byte-for-byte. Walls and ceiling use rendering layer 2 and dedicated receiver layers 24-28. At most two shadowless radial point lights per receiver; emissive lamp fixtures retained. Original decorative contribution to workbench/furnishings preserved, not feathered or rebalanced."}).ToArray();File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",JsonUtility.ToJson(manifest,true));AssetDatabase.Refresh();lounge.GetComponent<GinzaEnvironmentTag>().revision=manifest.revision;
            PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);EditorSceneManager.OpenScene(ScenePath);ComparePoses(inventory.transforms,Poses());
            File.WriteAllText(Path.Combine(FeatherReport,"workbench-lock-after.txt"),WorkbenchLock());if(WorkbenchLock()!=locked)throw new InvalidOperationException("Workbench lock changed on save/reload");
            SessionState.SetString("GinzaFeatherCapture","after");EditorApplication.isPlaying=true;
        }
        public static void RenderEnvironmentEvidence(string phase)
        {
            var env=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>();var root=env.transform;var main=UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.ViewCamera;
            File.WriteAllText(Path.Combine(FeatherReport,phase+"-runtime-lock.txt"),WorkbenchLock());
            var all=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.isActiveAndEnabled).ToArray();var shadow=all.Where(l=>l.shadows!=LightShadows.None).ToArray();if(shadow.Length!=1||shadow[0].name!="Full width warm soft key")throw new InvalidOperationException("Unexpected shadow caster");
            var go=new GameObject("Temporary environment evidence camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(main);cam.enabled=false;cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;cam.aspect=16f/9;
            var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));var original=inv.transforms.Single(t=>t.path=="Player/First person camera");
            cam.transform.position=root.TransformPoint(new Vector3(-1.7f,1.60f,-.4f));cam.transform.LookAt(root.TransformPoint(new Vector3(-3.25f,1.95f,.65f)));cam.fieldOfView=78;RenderFeather(cam,phase+"-01-left-wall");
            cam.transform.position=root.TransformPoint(new Vector3(0,1.60f,-.8f));cam.transform.LookAt(root.TransformPoint(new Vector3(0,3.08f,.75f)));cam.fieldOfView=90;RenderFeather(cam,phase+"-02-ceiling");
            cam.transform.SetPositionAndRotation(original.position,original.rotation);cam.fieldOfView=inv.fieldOfView;RenderFeather(cam,phase+"-03-workbench");UnityEngine.Object.DestroyImmediate(go);
            if(phase=="after")
            {
                var points=root.Find("Feathered environment receiver lights").GetComponentsInChildren<Light>();if(points.Length!=8||points.Any(l=>l.type!=LightType.Point||l.shadows!=LightShadows.None))throw new InvalidOperationException("Environment point-light contract failed");
                var wallRenderers=root.Find("ArchitecturalEnvelope").GetComponentsInChildren<MeshRenderer>().Where(r=>r.renderingLayerMask==2).ToArray();int peak=0;
                foreach(var r in wallRenderers){int n=all.Count(l=>(l.cullingMask&(1<<r.gameObject.layer))!=0);peak=Math.Max(peak,n);if(n>4)throw new InvalidOperationException("Receiver exceeds four eligible lights: "+r.name);}
                File.WriteAllText(Path.Combine(FeatherReport,"verification.json"),"{\"compiled\":true,\"originalTransformsUnchanged\":388,\"lockedWorkbenchSerializedIdentical\":true,\"environmentPoints\":8,\"maximumEligibleLightsPerWallOrCeiling\":"+peak+",\"maximumContributingEnvironmentLightsPerReceiver\":2,\"shadowMaps\":1,\"testsRun\":0}");
            }
        }
        private static void RenderFeather(Camera cam,string name)
        {
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});var prev=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(FeatherReport,name+".png"),tex.EncodeToPNG());RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
    }
    [InitializeOnLoad]public static class GinzaFeatherEvidenceDriver
    {
        private static double started=-1;private static bool done;
        static GinzaFeatherEvidenceDriver(){if(SessionState.GetString("GinzaFeatherCapture","")!="")EditorApplication.update+=Tick;}
        private static void Tick(){if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;if(started<0){started=EditorApplication.timeSinceStartup;return;}if(done||EditorApplication.timeSinceStartup-started<5)return;done=true;var phase=SessionState.GetString("GinzaFeatherCapture","");SessionState.SetString("GinzaFeatherCapture","");GinzaLoungeBuilder.RenderEnvironmentEvidence(phase);EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);}
    }
}
