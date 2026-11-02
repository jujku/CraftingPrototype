using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static string GlassReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaGlassReports"));
        public static void CaptureGlassBefore()=>BeginGlassCapture("before");
        public static void CaptureGlassAfter()=>BeginGlassCapture("after");
        private static void BeginGlassCapture(string phase){RequireCache();Directory.CreateDirectory(GlassReport);EditorSceneManager.OpenScene(ScenePath);File.WriteAllText(Path.Combine(GlassReport,phase+"-lighting-lock.txt"),WorkbenchLock());var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));ComparePoses(inv.transforms,Poses());SessionState.SetString("GinzaGlassCapture",phase);EditorApplication.isPlaying=true;}
        public static void RenderGlassPresence(string phase)
        {
            RenderEnvironmentEvidence("glass-"+phase);File.Copy(Path.Combine(FeatherReport,"glass-"+phase+"-03-workbench.png"),Path.Combine(GlassReport,phase+"-workbench.png"),true);
            File.WriteAllText(Path.Combine(GlassReport,phase+"-runtime-lock.txt"),WorkbenchLock());
            var session=UnityEngine.Object.FindFirstObjectByType<BarSession>();var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));var pose=inv.transforms.Single(t=>t.path=="Player/First person camera");var go=new GameObject("Temporary glass detail camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Player.ViewCamera);camera.enabled=false;camera.transform.SetPositionAndRotation(pose.position,pose.rotation);camera.fieldOfView=42;camera.aspect=16f/9;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;RenderFeather(camera,"glass-"+phase+"-detail");File.Copy(Path.Combine(FeatherReport,"glass-"+phase+"-detail.png"),Path.Combine(GlassReport,phase+"-detail.png"),true);UnityEngine.Object.DestroyImmediate(go);
        }
    }
    [InitializeOnLoad]public static class GinzaGlassEvidenceDriver
    {
        private static double started=-1;private static bool done;
        static GinzaGlassEvidenceDriver(){if(SessionState.GetString("GinzaGlassCapture","")!="")EditorApplication.update+=Tick;}
        private static void Tick(){if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;if(started<0){started=EditorApplication.timeSinceStartup;return;}if(done||EditorApplication.timeSinceStartup-started<5)return;done=true;var phase=SessionState.GetString("GinzaGlassCapture","");SessionState.SetString("GinzaGlassCapture","");GinzaLoungeBuilder.RenderGlassPresence(phase);EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);}
    }
}
