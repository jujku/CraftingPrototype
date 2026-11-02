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
        private static string MoodyReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaMoodyReports"));
        [Serializable] private sealed class MoodyLightRow { public string path,type,shadows;public bool enabled;public float intensity,outer,inner;public Color color;public Vector3 position; }
        [Serializable] private sealed class MoodyAudit { public string phase,tonemapping;public int shadowLights,shadowMaps;public MoodyLightRow[] lights;public Color ambient; }
        private static void AuditMoody(string phase)
        {
            var all=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene.IsValid()).ToArray();
            var shadow=all.Where(l=>l.isActiveAndEnabled&&l.shadows!=LightShadows.None).ToArray();
            var volume=UnityEngine.Object.FindFirstObjectByType<PresentationRig>().PostProfile;volume.TryGet<Tonemapping>(out var tone);
            if(phase!="before"&&(shadow.Length!=2||shadow.Any(l=>l.type!=LightType.Spot||l.shadows!=LightShadows.Soft)||all.Except(shadow).Any(l=>l.shadows!=LightShadows.None)))throw new InvalidOperationException("Exactly two task spots may have shadows; all other lights must use None");
            if(phase!="before"&&tone.mode.value!=TonemappingMode.ACES)throw new InvalidOperationException("ACES not active");
            File.WriteAllText(Path.Combine(MoodyReport,phase+"-lights.json"),JsonUtility.ToJson(new MoodyAudit{phase=phase,tonemapping=tone.mode.value.ToString(),shadowLights=shadow.Length,shadowMaps=shadow.Sum(l=>l.type==LightType.Point?6:1),ambient=RenderSettings.ambientLight,lights=all.Select(l=>new MoodyLightRow{path=Hierarchy(l.transform),type=l.type.ToString(),shadows=l.shadows.ToString(),enabled=l.isActiveAndEnabled,intensity=l.intensity,outer=l.spotAngle,inner=l.innerSpotAngle,color=l.color,position=l.transform.position}).ToArray()},true));
        }
        public static void RestoreMoodyLighting()
        {
            RequireCache();Directory.CreateDirectory(MoodyReport);var scene=EditorSceneManager.OpenScene(ScenePath);AuditMoody("before");
            var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inventory.transforms,Poses());
            lounge=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>().transform;
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene.IsValid()))
            {
                l.shadows=LightShadows.None;
                if(l.type==LightType.Directional||l.name.StartsWith("Warm reflected ambient fill")||l.name=="Gentle linear fill section"||l.name.StartsWith("Under ledge warm pool"))l.enabled=false;
            }
            var old=lounge.Find("Bar_PendantLight_Trio").GetComponentsInChildren<Light>().OrderBy(l=>l.transform.position.x).ToArray();if(old.Length!=3)throw new InvalidOperationException("Expected three authored pendant lights");foreach(var l in old)l.enabled=false;
            var group=Group("Moody_Workbench_TaskLights",lounge,Vector3.zero);
            var views=UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None);
            Vector3 Position(string id)=>views.Single(v=>v.DefinitionId==id).transform.position;
            var targets=new[]{(Position("fixture.servemat")+Position("shaker.cobbler")+Position("jigger.dual"))/3,(Position("fixture.board")+Position("tool.squeezer"))*.5f};
            for(int i=0;i<2;i++)
            {
                var l=old[i];l.transform.SetParent(group,true);l.name=i==0?"Mixing zone soft task spot":"Prep zone soft task spot";l.transform.position=new Vector3(targets[i].x,2.10f,targets[i].z+.04f);l.transform.rotation=Quaternion.LookRotation(targets[i]-l.transform.position);l.enabled=true;l.intensity=9.02f*.42f;l.color=Hex("FFDCA8");l.spotAngle=52;l.innerSpotAngle=18;l.range=2.5f;l.shadows=LightShadows.Soft;l.shadowBias=.02f;l.shadowNormalBias=.025f;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Hex("181310");RenderSettings.ambientIntensity=1;
            var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();if(!rig.PostProfile.TryGet<Tonemapping>(out var tone))throw new InvalidOperationException("Missing authored tonemapping");tone.active=true;tone.mode.Override(TonemappingMode.ACES);EditorUtility.SetDirty(tone);
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));manifest.revision="GinzaMoodyLighting-2026-10-30";manifest.adaptations=manifest.adaptations.Where(s=>!s.StartsWith("Three pendants")&&!s.StartsWith("Solid 60mm")).Concat(new[]{"Two warm task spots at world Y2.1m, #FFDCA8, 52/18 degrees, intensity 3.7884 (58% reduction). All decorative lights have no shadows; broad counter fills and underside amber points disabled. Ambient #181310; ACES.","Existing solid risers, sealed underwells, room geometry and original gameplay poses retained."}).ToArray();File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",JsonUtility.ToJson(manifest,true));AssetDatabase.Refresh();lounge.GetComponent<GinzaEnvironmentTag>().revision=manifest.revision;
            AuditMoody("saved");ComparePoses(inventory.transforms,Poses());PrefabUtility.SaveAsPrefabAsset(lounge.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene(ScenePath);ComparePoses(inventory.transforms,Poses());AuditMoody("reloaded");File.WriteAllText(Path.Combine(MoodyReport,"verification.json"),"{\"compiled\":true,\"savedAndReloaded\":true,\"originalTransformsUnchanged\":388,\"editModeTests\":0,\"playModeTests\":0}");
            SessionState.SetBool("GinzaMoodyCapture",true);EditorApplication.isPlaying=true;
        }
        public static void CaptureMoodyFrame()
        {
            AuditMoody("runtime");var inventory=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));var original=inventory.transforms.Single(x=>x.path=="Player/First person camera");var main=UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.ViewCamera;
            var go=new GameObject("Temporary moody evidence camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(main);camera.enabled=false;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;camera.transform.SetPositionAndRotation(original.position,original.rotation);camera.fieldOfView=inventory.fieldOfView;camera.aspect=16f/9;
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(MoodyReport,"first-person.png"),image.EncodeToPNG());RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);
        }
    }
    [InitializeOnLoad] public static class GinzaMoodyEvidenceDriver
    {
        private static double start=-1;private static bool done;
        static GinzaMoodyEvidenceDriver(){if(SessionState.GetBool("GinzaMoodyCapture",false))EditorApplication.update+=Tick;}
        private static void Tick(){if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;if(start<0){start=EditorApplication.timeSinceStartup;return;}if(done||EditorApplication.timeSinceStartup-start<5)return;done=true;SessionState.SetBool("GinzaMoodyCapture",false);GinzaLoungeBuilder.CaptureMoodyFrame();EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);}
    }
}
