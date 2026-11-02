using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>杯中冰像素回归及关掉第二次拷贝后的环境降级；不改用户资源。</summary>
    public sealed class IceRefractionTests
    {
        [UnityTest]public IEnumerator IceSurvivesGlassAndFeatureDisabledFallback()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var glass=session.Views.First(v=>v.Item is Glass&&v.DefinitionId=="glass.highball");var vessel=(Glass)glass.Item;
            session.Adapter.World.Apply(new Operation{type="PourSource",to=vessel.CoreVesselId,ingredient="spirit",ml=70});
            session.Adapter.World.Apply(new Operation{type="AddIce",to=vessel.CoreVesselId,grams=80});session.Adapter.Advance(0);
            var camera=session.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=640f/480;
            camera.transform.position=glass.transform.position+new Vector3(-.04f,.21f,-.18f);camera.transform.LookAt(glass.transform.position+Vector3.up*glass.Size.y*.42f);
            yield return new WaitForSeconds(.7f);
            var ice=glass.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Ice"&&r.enabled).ToArray();Assert.That(ice.Length,Is.GreaterThan(0));
            var with=PourCaptureTests.Capture(camera,"ice-through-glass");
            foreach(var r in ice)r.enabled=false;
            var without=PourCaptureTests.Capture(camera,"ice-through-glass-hidden");
            float difference=Difference(with,without);Assert.That(difference,Is.GreaterThan(.01f),"杯内 ROI 至少 1% 像素有可见冰差异，防止第二次拷贝丢冰");
            foreach(var r in ice)r.enabled=true;
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",BindingFlags.NonPublic|BindingFlags.Instance);
            var renderers=(ScriptableRendererData[])field.GetValue(pipeline);
            var features=renderers.SelectMany(r=>r.rendererFeatures).OfType<IceRefractionFeature>().ToArray();Assert.That(features.Length,Is.GreaterThan(0));
            foreach(var f in features)f.SetActive(false);foreach(var r in renderers)r.SetDirty();
            yield return null;
            var fallback=PourCaptureTests.Capture(camera,"ice-fallback-feature-disabled");
            Assert.That(fallback.GetPixels().Count(c=>c.r>.85f&&c.b>.85f&&c.g<.15f),Is.LessThan(10),"不能变粉");
            foreach(var r in ice)r.enabled=false;var noFallback=PourCaptureTests.Capture(camera,"ice-fallback-hidden");
            Assert.That(Difference(fallback,noFallback),Is.GreaterThan(.005f),"降级路径仍要看见冰");
            foreach(var r in ice)r.enabled=true;foreach(var f in features)f.SetActive(true);foreach(var r in renderers)r.SetDirty();
            File.WriteAllText(Path.Combine(PourCaptureTests.Output,"ice-pixel-regression.json"),"{\"roi\":[220,100,220,250],\"threshold\":0.04,\"requiredFraction\":0.01,\"changedFraction\":"+difference.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
        }
        private static float Difference(Texture2D a,Texture2D b)
        {
            int changed=0,count=0;
            for(int y=100;y<350;y++)for(int x=220;x<440;x++){var u=a.GetPixel(x,y);var v=b.GetPixel(x,y);if(Mathf.Max(Mathf.Abs(u.r-v.r),Mathf.Max(Mathf.Abs(u.g-v.g),Mathf.Abs(u.b-v.b)))>.04f)changed++;count++;}
            return changed/(float)count;
        }
    }
}
