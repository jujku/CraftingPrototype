using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed class PolishPresentationTests
    {
        private BarSession s;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../U7PolishReports"));
        private ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);
        }
        private Texture2D Shot(string label)
        {var t=PourCaptureTests.Capture(s.Player.ViewCamera,label);File.Copy(Path.Combine(PourCaptureTests.Output,label+".png"),Path.Combine(Output,label+".png"),true);return t;}
        [UnityTest] public IEnumerator TableReceiverLightingAcrossForegroundTransition()
        {
            var metrics=new List<string>{"receiver,meanRgbDifference,changedPixels,brokenMeanRgbDifference"};
            foreach(var id in new[]{"glass.highball","shaker.cobbler"})
            {
                yield return Load();var target=View(id);var source=View("bottle.gin");((Bottle)source.Item).SetOpen(true);Assert.That(s.Hands.TryPickUp(source.Item),Is.True);
                // Keep this a light/shadow comparison, rather than the deliberate foreground-priority
                // change where a nearer transparent bottle overlaps the promoted receiver.
                View("bottle.soda").gameObject.SetActive(false);
                var camera=s.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=640f/480;camera.transform.position=target.transform.position+new Vector3(.10f,.28f,-.44f);camera.transform.LookAt(target.transform.position+Vector3.up*.09f);
                source.transform.position=camera.transform.TransformPoint(new Vector3(.5f,0,.3f));
                var render=s.GetComponent<HeldItemRendering>();render.Refresh();yield return null;
                Object.Destroy(Shot(id+"-warm"));var before=Shot(id+"-before");var original=target.transform.position;var rotation=target.transform.rotation;
                var driver=s.gameObject.AddComponent<PourPresentationDriver>();driver.Session=s;driver.Initialize();typeof(BarSession).GetField("pourPresentation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s,driver);
                var option=new ActionResolver().Register(new BottlePourAction(ItemCatalog.LoadControls(s.ControlsJson.text))).Resolve(target.Item,s.Hands).First(o=>o.Actor==source.Item);
                driver.Presentation.Prepare(option);render.Refresh();var after=Shot(id+"-after");
                Assert.That(target.transform.position,Is.EqualTo(original));Assert.That(target.transform.rotation,Is.EqualTo(rotation));
                float difference=0;int changed=0;
                for(int y=0;y<before.height;y++)for(int x=0;x<before.width;x++)
                {var a=before.GetPixel(x,y);var b=after.GetPixel(x,y);float d=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);difference+=d/3;if(d>.05f)changed++;}
                // Reproduce the old missing-context path at this exact same composition, without
                // modifying the scene or source code. Restore the real bridge immediately after capture.
                var field=typeof(HeldItemRendering).GetField("shadows",BindingFlags.Instance|BindingFlags.NonPublic);var bridge=(ForegroundShadowBridge)field.GetValue(render);
                bridge.Sync(false,null,null);field.SetValue(render,null);Texture2D broken;
                try{broken=Shot(id+"-broken");}finally{field.SetValue(render,bridge);render.Refresh();}
                float brokenDifference=0;
                for(int y=0;y<before.height;y++)for(int x=0;x<before.width;x++){var a=before.GetPixel(x,y);var b=broken.GetPixel(x,y);brokenDifference+=(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/3;}
                brokenDifference/=before.width*before.height;
                Assert.That(brokenDifference,Is.GreaterThan(difference/(before.width*before.height)+.0001f),"同一画面确实复现原投影/遮蔽丢失，修复应明显缩小差异");
                metrics.Add(id+","+(difference/(before.width*before.height)).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+changed+","+brokenDifference.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllLines(Path.Combine(Output,"lighting.csv"),metrics);
                Assert.That(difference/(before.width*before.height),Is.LessThan(.0001f),id+" must retain light and table shadow when promoted");
                Assert.That(render.ShadowProxyCount,Is.GreaterThan(0));
                Object.Destroy(before);Object.Destroy(after);Object.Destroy(broken);
            }
        }
        [UnityTest] public IEnumerator StirringFollowsCavityRatherThanFixedMouthDepth()
        {
            yield return Load();var spoon=View("tool.spoon");var tip=spoon.Anchor("Tip");var bowl=spoon.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="SpoonBowl");
            var lines=new List<string>{"receiver,ml,frame,tipY,bottom,top,liquid,radial,bowlYMin,bowlYMax"};
            foreach(var pair in new[]{("glass.highball",60d),("glass.highball",160d),("shaker.cobbler",90d),("glass.coupe",60d),("jigger.dual",20d)})
            {
                var receiver=View(pair.Item1);var c=receiver.Item is CobblerShaker sh?sh.Body:receiver.Item is Jigger j?j.ActiveCup:(Container)receiver.Item;
                s.Adapter.World.Apply(new Operation{type="DiscardLiquid",from=c.CoreVesselId});s.Adapter.World.Apply(new Operation{type="PourSource",to=c.CoreVesselId,ingredient="spirit",ml=pair.Item2});s.Adapter.Advance(0);
                yield return null; // Let the real liquid mesh consume the new snapshot before screenshots.
                var binding=receiver.GetComponentsInChildren<ContainerVisual>().Single(v=>v.DefinitionId==c.Definition.Id);var shape=binding.Shape;string hash=LogReplay.SnapshotHash(s.Adapter.World);
                for(int i=0;i<24;i++)
                {
                    float phase=i*Mathf.PI/12;spoon.PoseStirring(receiver,c,new Vector2(Mathf.Cos(phase),Mathf.Sin(phase))*36);
                    var t=binding.transform.InverseTransformPoint(tip.position);var b=CavityShape.BoundsIn(bowl,binding.transform);float radial=Vector2.Distance(new Vector2(t.x,t.z),shape.Axis);
                    lines.Add(string.Join(",",pair.Item1,pair.Item2,i,t.y,shape.Bottom,shape.Top,binding.HeightForLiquid(c.Contents.LiquidMl),radial,b.min.y,b.max.y));
                    foreach(var vertex in bowl.sharedMesh.vertices)
                    {
                        var local=binding.transform.InverseTransformPoint(bowl.transform.TransformPoint(vertex));
                        Assert.That(local.y,Is.GreaterThanOrEqualTo(shape.Bottom-.0001f),"整块勺头不能穿杯底");
                        Assert.That(Vector2.Distance(new Vector2(local.x,local.z),shape.Axis),Is.LessThanOrEqualTo(shape.RadiusAt(local.y)+.0002f),"整块勺头不能穿杯壁");
                    }
                    Assert.That(t.y,Is.LessThan(binding.HeightForLiquid(c.Contents.LiquidMl)),"勺头应探入酒液");
                    if(pair.Item1=="glass.highball"||pair.Item1=="shaker.cobbler")
                    {Assert.That(radial,Is.GreaterThan(.016f),"常用容器不再锁在8mm小圈");Assert.That(t.y-shape.Bottom,Is.LessThan(.007f),"应接近杯底而非只露8mm在水里");}
                }
                Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                spoon.PoseStirring(receiver,c,Vector2.zero);var stopped=binding.transform.InverseTransformPoint(tip.position);
                Assert.That(Vector2.Distance(new Vector2(stopped.x,stopped.z),shape.Axis),Is.LessThan(.0001f),"没有鼠标圆周输入时不能自动画圈");
                spoon.PoseStirring(receiver,c,new Vector2(36,0));
                var camera=s.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;camera.transform.position=receiver.transform.position+new Vector3(.10f,.30f,-.32f);camera.transform.LookAt(receiver.transform.position+Vector3.up*receiver.Size.y*.6f);
                Object.Destroy(Shot(pair.Item1+"-stir-"+pair.Item2));
            }
            File.WriteAllLines(Path.Combine(Output,"stirring.csv"),lines);
        }
        [UnityTest,Explicit("订单文字真实悬停截图对比")]public IEnumerator HoverDoesNotBrightenTextShadow()
        {
            if(Application.isBatchMode)Assert.Ignore("需要原生Game视图");
            yield return Load();var glass=(Glass)View("glass.highball").Item;
            var apply=typeof(WorldAdapter).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
            apply.Invoke(s.Adapter,new object[]{new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="spirit",ml=45}});
            apply.Invoke(s.Adapter,new object[]{new Operation{type="Serve",from=glass.CoreVesselId}});
            var overlay=s.GetComponent<OrderOverlay>();overlay.enabled=false;var probe=s.gameObject.AddComponent<OrderHoverProbe>();probe.Overlay=overlay;
            #if UNITY_EDITOR
            var game=UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();
            #endif
            yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(Output,"text-normal.png"));yield return new WaitForSecondsRealtime(.5f);
            probe.Hover=true;yield return new WaitForSecondsRealtime(.3f);ScreenCapture.CaptureScreenshot(Path.Combine(Output,"text-hover.png"));yield return new WaitForSecondsRealtime(.5f);
            var a=new Texture2D(2,2);var b=new Texture2D(2,2);a.LoadImage(File.ReadAllBytes(Path.Combine(Output,"text-normal.png")));b.LoadImage(File.ReadAllBytes(Path.Combine(Output,"text-hover.png")));
            var f=overlay.FeedbackRect;int changed=0;
            for(int y=0;y<a.height;y++)for(int x=0;x<a.width;x++)if(f.Contains(new Vector2(x,a.height-y))){var ca=a.GetPixel(x,y);var cb=b.GetPixel(x,y);if(Mathf.Abs(ca.r-cb.r)+Mathf.Abs(ca.g-cb.g)+Mathf.Abs(ca.b-cb.b)>.015f)changed++;}
            File.WriteAllText(Path.Combine(Output,"hover.json"),"{\"changedTextPixels\":"+changed+"}");Assert.That(changed,Is.Zero,"悬停普通反馈文字不能让黑色阴影变成白色副本");Object.Destroy(a);Object.Destroy(b);
        }
    }
}
