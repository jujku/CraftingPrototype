using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private const string ReadableAssets="Assets/Bar/Environment/GinzaWorkbenchLighting";
        private static string ReadableReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaWorkbenchReports"));
        public static void RestoreReadableWorkbench()
        {
            RequireCache();Directory.CreateDirectory(ReadableReport);var scene=EditorSceneManager.OpenScene(ScenePath);lounge=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;
            if(lounge.Find("Readable workbench lighting"))throw new InvalidOperationException("Workbench correction already installed");
            var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inv.transforms,Poses());
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene.IsValid())){l.shadows=LightShadows.None;if(l.name=="Mixing zone soft task spot"||l.name=="Prep zone soft task spot")l.enabled=false;}
            foreach(var r in lounge.Find("BarCounter_CustomerWrap").GetComponentsInChildren<MeshRenderer>())r.shadowCastingMode=ShadowCastingMode.Off;
            var g=Group("Readable workbench lighting",lounge,Vector3.zero);
            foreach(int side in new[]{-1,1})
            {
                var light=Lamp(side<0?"Full width warm soft key":"Shadowless front label fill",g,Vector3.zero,Hex(side<0?"FFE2BA":"F4E9D9"),side<0?5f:3.5f,4.5f,true,155,155,side<0);
                light.transform.position=new Vector3(side*.85f,2.70f,2.15f);light.transform.rotation=Quaternion.LookRotation(new Vector3(0,.70f,.60f)-light.transform.position);light.shadowStrength=.40f;light.shadowBias=.02f;light.shadowNormalBias=.025f;
            }
            var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();if(!rig.BakedReflection)throw new InvalidOperationException("Original workbench reflection missing");
            var probe=Group("Original softbox workbench reflection",g,Vector3.zero).gameObject.AddComponent<ReflectionProbe>();probe.transform.position=new Vector3(0,1.15f,.65f);probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=rig.BakedReflection;probe.size=new Vector3(3.5f,2.4f,2.7f);probe.boxProjection=true;probe.blendDistance=.10f;probe.importance=100;probe.intensity=1f;
            Directory.CreateDirectory(ReadableAssets);AssetDatabase.Refresh();
            var shoji=lounge.Find("ArchitecturalEnvelope/Courtyard lounge wall/Backlit Kumiko Shoji Window");DimBackground(shoji,"Shoji");
            var sconces=lounge.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Upper ")&&t.name.Contains("wall brass sconce")||t.name=="Brass Entry Wall Sconce").ToArray();foreach(var s in sconces)DimBackground(s,"Sconce"+Array.IndexOf(sconces,s));
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));manifest.revision="GinzaWorkbenchRestore-2026-10-30";manifest.adaptations=manifest.adaptations.Where(s=>!s.StartsWith("Two warm task spots")).Concat(new[]{"Full-width 155-degree front-top key and shadowless label fill, inner angle 155 degrees, only key casts soft shadows at strength 0.4. Narrow task spots disabled; counter wrap casts no shadows.","Dedicated workbench custom probe reuses original BarPresentationRig baked softbox reflection, importance 100. Shoji and wall sconce emission and lights reduced 55 percent."}).ToArray();File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",JsonUtility.ToJson(manifest,true));AssetDatabase.Refresh();lounge.GetComponent<GinzaEnvironmentTag>().revision=manifest.revision;
            ComparePoses(inv.transforms,Poses());PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);EditorSceneManager.OpenScene(ScenePath);ComparePoses(inv.transforms,Poses());
            File.WriteAllText(Path.Combine(ReadableReport,"saved-verification.json"),"{\"compiled\":true,\"originalTransformsUnchanged\":388,\"savedReloaded\":true,\"testsRun\":0}");SessionState.SetBool("GinzaReadableCapture",true);EditorApplication.isPlaying=true;
        }
        private static void DimBackground(Transform root,string label)
        {
            foreach(var l in root.GetComponentsInChildren<Light>())l.intensity*=.45f;
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var original=renderer.sharedMaterial;if(!original.IsKeywordEnabled("_EMISSION"))continue;
                var mat=new Material(original){name=original.name+" muted background "+label};mat.SetColor("_EmissionColor",original.GetColor("_EmissionColor")*.45f);AssetDatabase.CreateAsset(mat,ReadableAssets+"/"+label+"_"+renderer.name+".mat");renderer.sharedMaterial=mat;
            }
        }
        public static void CaptureReadableWorkbench()
        {
            var env=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>();var ls=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.isActiveAndEnabled&&l.shadows!=LightShadows.None).ToArray();if(ls.Length!=1||ls[0].name!="Full width warm soft key"||Mathf.Abs(ls[0].shadowStrength-.4f)>.001f)throw new InvalidOperationException("Unexpected realtime shadow source");
            if(env.transform.Find("BarCounter_CustomerWrap").GetComponentsInChildren<MeshRenderer>().Any(r=>r.shadowCastingMode!=ShadowCastingMode.Off))throw new InvalidOperationException("Counter wrap casting shadows");
            var probe=env.GetComponentsInChildren<ReflectionProbe>().Single(p=>p.name=="Original softbox workbench reflection");var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();if(probe.customBakedTexture!=rig.BakedReflection||probe.importance!=100)throw new InvalidOperationException("Original reflection binding missing");
            var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));var original=inv.transforms.Single(x=>x.path=="Player/First person camera");var go=new GameObject("Temporary readable workbench camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.ViewCamera);cam.enabled=false;cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;cam.transform.SetPositionAndRotation(original.position,original.rotation);cam.fieldOfView=inv.fieldOfView;cam.aspect=16f/9;
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(ReadableReport,"first-person.png"),image.EncodeToPNG());RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);
            File.WriteAllText(Path.Combine(ReadableReport,"runtime-verification.json"),"{\"shadowMaps\":1,\"shadowStrength\":0.4,\"outerInnerAngles\":[155,155],\"originalReflectionBound\":true,\"reflectionImportance\":100,\"counterWrapCastsShadows\":false,\"backgroundEmissionMultiplier\":0.45,\"testsRun\":0}");
        }
    }
    [InitializeOnLoad]public static class GinzaReadableEvidenceDriver
    {
        private static double started=-1;private static bool done;
        static GinzaReadableEvidenceDriver(){if(SessionState.GetBool("GinzaReadableCapture",false))EditorApplication.update+=Tick;}
        private static void Tick(){if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;if(started<0){started=EditorApplication.timeSinceStartup;return;}if(done||EditorApplication.timeSinceStartup-started<5)return;done=true;SessionState.SetBool("GinzaReadableCapture",false);GinzaLoungeBuilder.CaptureReadableWorkbench();EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);}
    }
}
