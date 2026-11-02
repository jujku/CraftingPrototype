using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace Bar.Unity.Editor
{
    /// <summary>只在有标记的唯一验证工程构建临时冒烟场景，不保存用户场景。</summary>
    public static class VisualBuild
    {
        public static void Build()
        {
            var project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));if(!File.Exists(Path.Combine(project,".u1-validation-copy")))throw new InvalidOperationException("Build only in marked validation cache");
            Environment.SetEnvironmentVariable("BEE_BUILD_THREADS","1");Environment.SetEnvironmentVariable("BINARYEN_CORES","1");Environment.SetEnvironmentVariable("EMCC_CORES","1");PlayerSettings.SetAdditionalIl2CppArgs("--jobs=1 --bee-jobs=1");
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL,ManagedStrippingLevel.High);PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL,UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            var scene=EditorSceneManager.OpenScene("Assets/Bar/Scenes/BarPresentation.unity");var session=UnityEngine.Object.FindFirstObjectByType<BarSession>();session.gameObject.AddComponent<BStageDemo>();if(Environment.GetEnvironmentVariable("BAR_Q_BUILD")=="1")session.gameObject.AddComponent<QWebProbe>();EditorSceneManager.SaveScene(scene,"Assets/VisualSmoke.unity");
            var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").First();var serialized=new SerializedObject(settings);var shaders=serialized.FindProperty("m_AlwaysIncludedShaders");foreach(string name in new[]{"Bar/Glass","Bar/Ice","Bar/IceTransmission","Bar/LiquidFill","Bar/BottleLiquidSurface","Skybox/Panoramic","Bar/CueLiquid","Bar/CueSurface","Bar/PourRibbon","Universal Render Pipeline/Unlit","Universal Render Pipeline/Lit"}){int i=shaders.arraySize;shaders.InsertArrayElementAtIndex(i);shaders.GetArrayElementAtIndex(i).objectReferenceValue=Shader.Find(name);}serialized.ApplyModifiedPropertiesWithoutUndo();
            PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Disabled;PlayerSettings.WebGL.exceptionSupport=WebGLExceptionSupport.FullWithStacktrace;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL,new[]{GraphicsDeviceType.OpenGLES3});
            #if UNITY_WEBGL
            var oldOptimization=UnityEditor.WebGL.UserBuildSettings.codeOptimization;
            if(Environment.GetEnvironmentVariable("BAR_Q_FAST_VERIFY")=="1")UnityEditor.WebGL.UserBuildSettings.codeOptimization=UnityEditor.WebGL.WasmCodeOptimization.BuildTimes;
            #endif
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/VisualSmoke.unity"},target=BuildTarget.WebGL,locationPathName="D:/temp/webgl_smoke",options=BuildOptions.None});
            #if UNITY_WEBGL
            UnityEditor.WebGL.UserBuildSettings.codeOptimization=oldOptimization;
            #endif
            Directory.CreateDirectory(Path.Combine(project,"VisualReports"));File.WriteAllText(Path.Combine(project,"VisualReports/webgl-build.json"),JsonUtility.ToJson(new Result{result=report.summary.result.ToString(),bytes=report.summary.totalSize,seconds=report.summary.totalTime.TotalSeconds,errors=report.summary.totalErrors}));
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("WebGL build failed");
        }
        [Serializable]private sealed class Result{public string result;public ulong bytes;public double seconds;public int errors;}
    }
}
