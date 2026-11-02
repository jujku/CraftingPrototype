using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
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
    /// <summary>冰块碰撞是全局功能：highball、coupe、雪克壶身和量酒器里，冰块在静置、晃动倾斜和搅拌后都不穿出真实内腔，核心状态不变。</summary>
    internal sealed class IceCavityTests
    {
        private static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../VisualReports"));
        [UnityTest] public IEnumerator IceStaysInsideEveryContainerShape()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;
            var spoon=s.Views.First(v=>v.Item is BarSpoon);var report="";Directory.CreateDirectory(Output);
            foreach(string id in new[]{"glass.highball","glass.coupe","shaker.cobbler","jigger.dual"})
            {
                var view=s.Views.First(v=>v.DefinitionId==id);
                Container container=view.Item is CobblerShaker shaker?shaker.Body:view.Item is Jigger jigger?jigger.ActiveCup:(Container)view.Item;
                var binding=view.GetComponentsInChildren<ContainerVisual>().Single(b=>b.DefinitionId==container.Definition.Id);
                var rig=binding.GetComponent<LocalIceWorld>();var shape=rig.Shape;
                Assert.That(shape.FromMesh,Is.True,id);
                // Ask for more ice than fits, so the display cap is exercised too.
                double portion=s.Adapter.World.Data.icePortionGrams;
                s.Adapter.World.Apply(new Operation{type="AddIce",to=container.CoreVesselId,grams=portion*rig.Capacity+1});s.Adapter.Advance(0);
                string before=LogReplay.SnapshotHash(s.Adapter.World);
                var origin=view.transform.position;var rotation=view.transform.rotation;float worst=0;
                // A cube wider than the whole mouth (jigger) wedges in the rim: corners above the rim are outside the cup and unchecked.
                bool fits=shape.LowestFit(.037f*.70710678f,.037f)<shape.Top;float above=fits?.001f:.037f*1.8f;
                void Check(string phase)
                {
                    foreach(var display in binding.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="IceSurface"&&f.gameObject.activeInHierarchy))
                    {
                        var b=display.sharedMesh.bounds;
                        for(int j=0;j<8;j++)
                        {
                            var corner=b.center+Vector3.Scale(b.extents,new Vector3((j&1)==0?-1:1,(j&2)==0?-1:1,(j&4)==0?-1:1));
                            var p=binding.transform.InverseTransformPoint(display.transform.TransformPoint(corner));
                            Assert.That(p.y,Is.InRange(shape.Bottom-.001f,shape.Top+above),id+" "+phase+" 冰块穿出杯底或杯口");
                            if(p.y>shape.Top)continue;
                            float excess=new Vector2(p.x-shape.Axis.x,p.z-shape.Axis.y).magnitude-shape.RadiusAt(p.y);
                            worst=Mathf.Max(worst,excess);
                            Assert.That(excess,Is.LessThanOrEqualTo(.001f),id+" "+phase+" 冰块穿出内壁");
                        }
                    }
                }
                for(int i=0;i<120;i++){rig.Advance(1f/60);}Check("静置");
                Assert.That(rig.DisplayCount,Is.EqualTo(rig.Capacity),id+" 超出容量时只显示能放下的块数");Assert.That(rig.DisplayCapped,Is.True);
                Capture(s,binding,id+"-rest");
                for(int i=0;i<180;i++){view.transform.position=origin+new Vector3(Mathf.Sin(i*.71f)*.4f,Mathf.Cos(i*.4f)*.15f,0);view.transform.rotation=Quaternion.Euler(Mathf.Sin(i*.3f)*60,0,Mathf.Cos(i*.35f)*65)*rotation;rig.Advance(1f/60);Check("晃动倾斜");}
                view.transform.SetPositionAndRotation(origin,rotation);
                rig.PreviewSpoon=spoon.Anchor("Tip");rig.PreviewIntensity=.8f;
                for(int i=0;i<180;i++){spoon.PoseStirring(view,container,new Vector2(Mathf.Cos(i*.18f),Mathf.Sin(i*.18f))*36);rig.Advance(1f/60);Check("搅拌");}
                rig.PreviewSpoon=null;rig.PreviewIntensity=0;
                for(int i=0;i<90;i++)rig.Advance(1f/60);Check("回稳");Capture(s,binding,id+"-after");
                Assert.That(rig.WallEscapes,Is.Zero,id);Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(before),id+" 显示冰不写回核心");
                report+=id+": capacity "+rig.Capacity+", shown "+rig.DisplayCount+", worst corner excess "+(worst*1000).ToString("F2")+" mm (allowed 1.00), fits whole cube "+fits+", mouth r "+(shape.RadiusAt(shape.Top)*100).ToString("F2")+" cm\n";
            }
            File.WriteAllText(Path.Combine(Output,"ice-cavity.txt"),report);
        }
        private static void Capture(BarSession s,ContainerVisual binding,string name)
        {
            var camera=s.Player.ViewCamera;var position=camera.transform.position;var rotation=camera.transform.rotation;
            var centre=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,(binding.Shape.Bottom+binding.Shape.Top)*.5f,binding.Shape.Axis.y));
            camera.transform.position=centre+new Vector3(0,.12f,-.24f);camera.transform.LookAt(centre);
            var rt=RenderTexture.GetTemporary(480,360,24);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(480,360,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,480,360),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Output,"ice-"+name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            camera.transform.SetPositionAndRotation(position,rotation);
        }
    }
}
