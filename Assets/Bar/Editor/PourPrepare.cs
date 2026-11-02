using System.IO;
using System.Linq;
using Bar.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Bar.Unity.Editor
{
    /// <summary>只新增 P 段内腔数据并给 Desktop/Web 渲染配置装配折射功能；不保存场景或模型预制体。</summary>
    public static class PourPrepare
    {
        public static void Run()
        {
            BottleCavityBake.Run();
            foreach(string path in new[]{"Assets/Bar/Presentation/Profiles/DesktopRenderer0.asset","Assets/Bar/Presentation/Profiles/WebRenderer0.asset"})
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if(!renderer.rendererFeatures.OfType<IceRefractionFeature>().Any())
                {
                    var feature=ScriptableObject.CreateInstance<IceRefractionFeature>();feature.name="Bar ice refraction and second scene copy";
                    AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);renderer.SetDirty();EditorUtility.SetDirty(renderer);
                }
            }
            AssetDatabase.SaveAssets();File.WriteAllText("PourReports/prepare-complete.txt","P geometry bake and renderer assembly completed");
        }
    }
}
