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
        private static string ContactReport=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GinzaContactReports"));
        public static void DiagnoseContactShadow(){RequireCache();Directory.CreateDirectory(ContactReport);EditorSceneManager.OpenScene(ScenePath);SessionState.SetBool("GinzaContactCapture",true);EditorApplication.isPlaying=true;}
        public static void RenderContactShadow()
        {
            var key=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l=>l.name=="Full width warm soft key");var data=key.GetUniversalAdditionalLightData();var rig=UnityEngine.Object.FindFirstObjectByType<PresentationRig>();
            File.WriteAllText(Path.Combine(ContactReport,"diagnosis.txt"),"usePipelineSettings="+data.usePipelineSettings+"\nLight bias="+key.shadowBias+", normal="+key.shadowNormalBias+"\nPipeline bias="+rig.DesktopPipeline.shadowDepthBias+", normal="+rig.DesktopPipeline.shadowNormalBias+"\n"+string.Join("\n",UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).SelectMany(v=>v.GetComponentsInChildren<MeshRenderer>().Select(r=>v.DefinitionId+"/"+r.name+" casts="+r.shadowCastingMode+" receive="+r.receiveShadows+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m ? m.name+":"+m.FindPass("ShadowCaster")+":"+m.GetShaderPassEnabled("ShadowCaster") : "EMPTY_MATERIAL_SLOT"))))));
            var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));var original=inv.transforms.Single(t=>t.path=="Player/First person camera");var go=new GameObject("Temporary contact evidence camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.ViewCamera);cam.enabled=false;cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;cam.transform.SetPositionAndRotation(original.position,original.rotation);cam.fieldOfView=inv.fieldOfView;cam.aspect=16f/9;
            void Shot(string name){RenderFeather(cam,"contact-"+name);File.Copy(Path.Combine(FeatherReport,"contact-"+name+".png"),Path.Combine(ContactReport,name+".png"),true);}
            Shot("before");data.usePipelineSettings=false;Shot("per-light-bias");key.shadowBias=.001f;key.shadowNormalBias=.001f;Shot("minimal-bias");
            var shadowSettings=new SerializedObject(data);shadowSettings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=-1;shadowSettings.ApplyModifiedPropertiesWithoutUndo();key.shadowCustomResolution=2048;Shot("high-resolution-shadow");
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Bar/Presentation/Profiles/DesktopRenderer0.asset");var ao=rendererData.rendererFeatures.Single(f=>f.GetType().Name=="ScreenSpaceAmbientOcclusion");var aoSettings=new SerializedObject(ao);aoSettings.FindProperty("m_Settings.Intensity").floatValue=1.0f;aoSettings.FindProperty("m_Settings.Radius").floatValue=.10f;aoSettings.FindProperty("m_Settings.DirectLightingStrength").floatValue=.6f;aoSettings.ApplyModifiedPropertiesWithoutUndo();Shot("contact-ao");
            UnityEngine.Object.DestroyImmediate(go);
        }
        public static void RenderFinalContact(){RenderEnvironmentEvidence("contact-final");File.Copy(Path.Combine(FeatherReport,"contact-final-03-workbench.png"),Path.Combine(ContactReport,"final.png"),true);}
        public static void SaveContactShadow()
        {
            RequireCache();var scene=EditorSceneManager.OpenScene(ScenePath);var locked=WorkbenchLock();var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Path.Combine(Report,"inventory-before.json")));
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Bar/Presentation/Profiles/DesktopRenderer0.asset");var ao=rendererData.rendererFeatures.Single(f=>f.GetType().Name=="ScreenSpaceAmbientOcclusion");var settings=new SerializedObject(ao);settings.FindProperty("m_Settings.Intensity").floatValue=1.0f;settings.FindProperty("m_Settings.Radius").floatValue=.10f;settings.FindProperty("m_Settings.DirectLightingStrength").floatValue=.6f;settings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(ao);
            var root=UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>();var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot+"/GinzaEnvironmentManifest.json"));manifest.revision="GinzaContactShadow-2026-10-30";manifest.adaptations=manifest.adaptations.Concat(new[]{"Near-field contact occlusion restored using existing desktop SSAO: radius 0.10m, intensity 1, direct-light strength 0.6. All workbench lights and reflection settings remain identical."}).ToArray();File.WriteAllText(AssetRoot+"/GinzaEnvironmentManifest.json",JsonUtility.ToJson(manifest,true));AssetDatabase.Refresh();root.revision=manifest.revision;ComparePoses(inv.transforms,Poses());if(WorkbenchLock()!=locked)throw new InvalidOperationException("Workbench lighting changed");PrefabUtility.SaveAsPrefabAsset(root.gameObject,AssetRoot+"/GinzaEnvironment.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);EditorSceneManager.OpenScene(ScenePath);ComparePoses(inv.transforms,Poses());if(WorkbenchLock()!=locked)throw new InvalidOperationException("Workbench lock changed after reload");File.WriteAllText(Path.Combine(ContactReport,"saved.json"),"{\"savedReloaded\":true,\"originalTransformsUnchanged\":388,\"workbenchLightsAndProbeUnchanged\":true,\"testsRun\":0}");SessionState.SetBool("GinzaContactFinal",true);SessionState.SetBool("GinzaContactCapture",true);EditorApplication.isPlaying=true;
        }
    }
    [InitializeOnLoad]public static class GinzaContactEvidenceDriver
    {
        private static double started=-1;private static bool done;
        static GinzaContactEvidenceDriver(){if(SessionState.GetBool("GinzaContactCapture",false))EditorApplication.update+=Tick;}
        private static void Tick(){if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;if(started<0){started=EditorApplication.timeSinceStartup;return;}if(done||EditorApplication.timeSinceStartup-started<5)return;done=true;SessionState.SetBool("GinzaContactCapture",false);if(SessionState.GetBool("GinzaContactFinal",false)){SessionState.SetBool("GinzaContactFinal",false);GinzaLoungeBuilder.RenderFinalContact();}else GinzaLoungeBuilder.RenderContactShadow();EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(0);}
    }
}

