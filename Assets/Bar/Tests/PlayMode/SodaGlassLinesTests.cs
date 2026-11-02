using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>复现无冰满杯汽水；分离上升粒子与液体壁面图案，防止白色线纹冒充碳酸气泡。</summary>
    public sealed class SodaGlassLinesTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../SodaGlassLinesReports"));
        [UnityTest]public IEnumerator SodaKeepsRisingBubblesWithoutAWhiteWallPattern()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=s.Cues.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");var visual=view.GetComponent<ContainerVisual>();var glass=(Glass)view.Item;
            s.Adapter.World.Pour(glass.CoreVesselId,"soda",350);s.Adapter.Advance(0);Assert.That(s.Hands.TryPickUp(glass),Is.True);
            var camera=s.Player.ViewCamera;camera.transform.rotation=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);camera.fieldOfView=32;camera.aspect=4f/3;
            view.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*.37f-Vector3.up*.075f,Quaternion.identity);s.GetComponent<HeldItemRendering>().Refresh();yield return null;
            s.Cues.Present(0);var liquid=visual.LiquidVolume.sharedMaterial;var bubbles=visual.GetComponent<BubbleStreams>();
            float level=visual.transform.TransformPoint(Vector3.up*visual.Profile.HeightForMl(350)).y;for(int i=0;i<180;i++)bubbles.Present(6,20,level,Vector3.up,0,true,1f/60,liquid);
            int count=bubbles.ActiveCount;Assert.That(count,Is.GreaterThan(100));string hash=LogReplay.SnapshotHash(s.Adapter.World);
            Directory.CreateDirectory(Output);var visible=Capture(camera,"soda-complete");
            var ps=bubbles.Sites[0].System;var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.enabled=false;
            var wall=Capture(camera,"soda-particles-hidden");float strength=liquid.GetFloat("_BubbleStrength");liquid.SetFloat("_BubbleStrength",0);
            var clear=Capture(camera,"soda-clear-reference");
            int wallPixels=Changed(wall,clear),bubblePixels=Changed(visible,wall);
            File.WriteAllText(Path.Combine(Output,"metrics.txt"),"particles="+count+" wallPatternPixels="+wallPixels+" risingBubblePixels="+bubblePixels);
            renderer.enabled=true;liquid.SetFloat("_BubbleStrength",strength);Object.Destroy(visible);Object.Destroy(wall);Object.Destroy(clear);
            Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            Assert.That(bubblePixels,Is.GreaterThan(100),"真实上升气泡必须仍然可见");
            Assert.That(wallPixels,Is.Zero,"移除粒子后，清澈汽水不能留下静态白线网格");
        }
        private static int Changed(Texture2D a,Texture2D b)
        {
            var x=a.GetPixels32();var y=b.GetPixels32();int n=0;for(int i=0;i<x.Length;i++)if(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b)>12)n++;return n;
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);return image;
        }
    }
}
