using System;
using System.Collections;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    internal sealed class FullCraftingTests
    {
        private BarSession s;private int placed;
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private float SurfaceY => View("fixture.counter").Anchor("Surface").position.y;
        private void Aim(string id){var view=View(id);s.Player.AimAt(view.Item is Fixture && view.Anchor("Surface") ? view.Anchor("Surface").position : view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view),"真实射线目标 "+id);}
        // A closed bottle is uncapped on the counter first and its cap set down at the back, keeping both hands for the recipe.
        private IEnumerator Pick(string id){yield return View(id).Item is Bottle?CapSteps.Uncap(s,View(id)):null;Aim(id);s.RightButton(Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Hands.SideOf(View(id).Item).HasValue,Is.True,"拿取 "+id+"；"+s.Clock.Error);}
        private IEnumerator Put(string id)
        {
            var side=s.Hands.SideOf(View(id).Item);Assert.That(side.HasValue,Is.True);if(s.Hands.MainHand!=side){s.ToggleMain(Now());yield return new WaitForSecondsRealtime(.14f);}
            Vector3 point = Vector3.zero;
            var placing = View(id);
            var surface = View("fixture.counter").GetComponent<PlacementSurface>();
            for (int attempt = 0; attempt < 30; attempt++)
            {
                point = CapSteps.TablePoint(s,-1.12f + .16f * (placed++ % 15),.345f - placing.Size.z * .5f);
                bool occupied = s.Views.Any(v => v != placing && v.Item is not Fixture &&
                    v.Item.Location.Kind == LocationKind.Surface && v.Item.Parent == null &&
                    Vector2.Distance(new Vector2(point.x,point.z), new Vector2(v.transform.position.x,v.transform.position.z)) <
                    (placing.Size.x + v.Size.x) * .5f + .012f);
                if (occupied || !surface.CanPlace(point, placing.Size)) continue;
                s.Player.AimAt(point);
                Physics.SyncTransforms();
                s.Targeting();
                if (s.Target && s.Target.Item is BarCounter) break;
            }
            s.Player.AimAt(point);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target.Item,Is.TypeOf<BarCounter>());s.RightButton(Now());yield return new WaitForSecondsRealtime(.28f);Assert.That(s.Hands.SideOf(View(id).Item),Is.Null,"放下 "+id+"；"+s.Clock.Error);
        }
        private void Choose(string action,string actor,string target)
        {
            if(s.Hands.SideOf(View(target).Item).HasValue){s.Player.AimAt(CapSteps.TablePoint(s,.9f,-.25f));Physics.SyncTransforms();s.Targeting();}else Aim(target);
            int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id==action&&o.Actor==View(actor).Item&&(o.Target==View(target).Item||o.Target.Parent==View(target).Item));
            Assert.That(index,Is.GreaterThanOrEqualTo(0),action+" · "+actor+" → "+target+"；"+s.Clock.Error);s.SelectMenu(index,true);s.LeftButton(true,Now());
        }
        private IEnumerator Click(string action,string actor,string target)
        {
            Choose(action,actor,target);s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(1.05f);
            float deliveryDeadline=Time.realtimeSinceStartup+4;
            while(s.IceDeliveryBusy&&Time.realtimeSinceStartup<deliveryDeadline)yield return null;
            Assert.That(s.IceDeliveryBusy,Is.False,"等待装冰回手后才执行下一步");Assert.That(s.Clock.Error,Is.Null,action);
        }
        private IEnumerator Tilt(string action,string actor,string target,float seconds)
        {Choose(action,actor,target);s.PointerDelta(new Vector2(0,1000),Now()+.001);yield return new WaitForSecondsRealtime(seconds+.35f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.23f);Assert.That(s.Clock.Error,Is.Null,action);}
        private IEnumerator FillGinToNominalMark()
        {
            Choose("pour.bottle_to_container", "bottle.gin", "jigger.dual");
            double start = Now();
            s.PointerDelta(new Vector2(0,1000), start + .001);
            var jigger = (Jigger)View("jigger.dual").Item;
            float deadline=Time.realtimeSinceStartup+1;
            while(jigger.ActiveCup.Contents.LiquidMl==0&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(jigger.ActiveCup.Contents.LiquidMl,Is.GreaterThan(0));
            var inputControls=ItemCatalog.LoadControls(s.ControlsJson.text);
            double stop=s.Adapter.World.Time+(45-jigger.ActiveCup.Contents.LiquidMl)/inputControls.MaxFlowMlPerSecond;
            s.LeftButton(false,stop);yield return new WaitForSecondsRealtime((float)System.Math.Max(0,stop-Now())+.3f);
            Assert.That(jigger.ActiveCup.CapacityMl, Is.EqualTo(55));
            Assert.That(jigger.ActiveCup.NominalMl, Is.EqualTo(45));
            Assert.That(jigger.ActiveCup.Contents.LiquidMl, Is.EqualTo(45).Within(.01));
            Assert.That(jigger.ActiveCup.AvailableMl, Is.GreaterThan(9.9));
            AssertLedger();
        }
        private IEnumerator Gesture(string action,string actor,string target,bool circle,float seconds)
        {
            Choose(action,actor,target);float start=Time.realtimeSinceStartup;var previous=Vector2.zero;int i=0;
            while(Time.realtimeSinceStartup-start<seconds){float angle=i++*.65f;var position=circle?new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*36:new Vector2(0,i%2==0?36:-36);s.PointerDelta(position-previous,Now());previous=position;yield return new WaitForSecondsRealtime(.035f);}
            s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Clock.Error,Is.Null,action);
        }
        private void AssertLedger(){var l=s.Adapter.World.Ledger();Assert.That(l.massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(l.energyResidual,Is.EqualTo(0).Within(1e-6));}
        private void Capture(string file)
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"../U4Reports"));var rt=RenderTexture.GetTemporary(960,600,24);RenderPipeline.SubmitRenderRequest(s.Player.ViewCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Application.dataPath,"../U4Reports",file+".png"),image.EncodeToPNG());UnityEngine.Object.Destroy(image);RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
        }
        [UnityTest] public IEnumerator GinSourUsesAllSourToolsAndServesWithConservation()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;s=UnityEngine.Object.FindFirstObjectByType<BarSession>();placed=0;
            yield return Pick("jigger.dual");yield return Pick("bottle.gin");yield return FillGinToNominalMark();yield return Put("bottle.gin");yield return Click("pour.jigger_to_container","jigger.dual","shaker.cobbler");
            var shaker=(CobblerShaker)View("shaker.cobbler").Item;Assert.That(shaker.Body.Contents.LiquidMl,Is.EqualTo(45).Within(.01));
            AssertLedger();
            yield return Pick("bottle.syrup");yield return Tilt("pour.bottle_to_container","bottle.syrup","jigger.dual",.50f);yield return Put("bottle.syrup");yield return Click("pour.jigger_to_container","jigger.dual","shaker.cobbler");yield return Put("jigger.dual");
            yield return Pick("tool.squeezer");yield return Pick("produce.lemon_half");yield return Click("lemon.load","produce.lemon_half","tool.squeezer");
            Choose("lemon.press","tool.squeezer","shaker.cobbler");s.PointerDelta(new Vector2(0,-120),Now()+.001);yield return new WaitForSecondsRealtime(.65f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.22f);
            Assert.That(((Squeezer)View("tool.squeezer").Item).LoadedProduce.IsSpent,Is.True);yield return Click("lemon.discard","tool.squeezer","fixture.sink");yield return Put("tool.squeezer");
            yield return Pick("tool.scoop");yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","shaker.cobbler");yield return Put("tool.scoop");
            Assert.That(shaker.Body.Contents.IceCubes,Is.GreaterThan(0));yield return Pick("shaker.strainer");yield return Click("shaker.attach_strainer","shaker.strainer","shaker.cobbler");yield return Pick("shaker.cap");yield return Click("shaker.attach_cap","shaker.cap","shaker.cobbler");
            Assert.That(Vector3.Distance(View("shaker.strainer").transform.lossyScale, Vector3.one), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(View("shaker.cap").transform.lossyScale, Vector3.one), Is.LessThan(.0001f));
            yield return Pick("shaker.cobbler");double before=s.Adapter.World.Time;yield return Gesture("shaker.shake","shaker.cobbler","shaker.cobbler",false,12f);Assert.That(s.Adapter.World.Time-before,Is.InRange(12,13),"摇壶不能重复计时");Capture("sour-shaken");
            // Detach uses an empty actor in the resolver; select by action and logical target.
            s.Targeting();int capIndex=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="shaker.detach_cap");Assert.That(capIndex,Is.GreaterThanOrEqualTo(0));s.SelectMenu(capIndex,true);s.LeftButton(true,Now());s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(.23f);yield return Put("shaker.cap");
            yield return Tilt("shaker.pour","shaker.cobbler","glass.coupe",4.5f);Assert.That(shaker.Body.Contents.LiquidMl,Is.LessThan(.01),"释放后留冰继续融化会产生微量新融水");Assert.That(((Glass)View("glass.coupe").Item).Contents.IceCubes,Is.Zero);yield return Put("shaker.cobbler");yield return Pick("glass.coupe");yield return Click("glass.serve","glass.coupe","fixture.servemat");
            var glass=(Glass)View("glass.coupe").Item;Assert.That(glass.IsServed,Is.True);Assert.That(s.Hands.SideOf(glass),Is.Null);Assert.That(s.Hands.TryPickUp(glass),Is.False);AssertLedger();Capture("sour-served");
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"../U4Reports"));File.WriteAllText(Path.Combine(Application.dataPath,"../U4Reports/sour-result.json"),"{\"served\":true,\"volumeMl\":"+glass.Contents.LiquidMl.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"massResidual\":"+s.Adapter.World.Ledger().massResidual.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
        }
        [UnityTest] public IEnumerator GinAndTonicIncludesCoolingOpeningToppingStirringAndServing()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;s=UnityEngine.Object.FindFirstObjectByType<BarSession>();placed=0;
            yield return Pick("glass.highball");yield return Pick("tool.tongs");yield return Click("ice.take","tool.tongs","fixture.icebin");yield return Click("ice.add","tool.tongs","glass.highball");yield return new WaitForSecondsRealtime(.6f);yield return Click("sink.empty","glass.highball","fixture.sink");
            var glass=(Glass)View("glass.highball").Item;Assert.That(glass.Contents.IceCubes,Is.Zero);Assert.That(glass.Contents.LiquidMl,Is.Zero);Assert.That(s.Adapter.World.Waste.m,Is.GreaterThan(0));
            yield return Click("ice.take","tool.tongs","fixture.icebin");yield return Click("ice.add","tool.tongs","glass.highball");yield return Put("tool.tongs");
            yield return Pick("bottle.gin");yield return Tilt("pour.bottle_to_container","bottle.gin","glass.highball",1.13f);yield return Put("bottle.gin");
            Assert.That(glass.Contents.LiquidMl,Is.GreaterThan(40));yield return Pick("bottle.tonic");yield return Tilt("pour.bottle_to_container","bottle.tonic","glass.highball",8.5f);yield return Put("bottle.tonic");
            Assert.That(glass.Contents.LiquidMl+glass.Contents.IceVolumeMl,Is.LessThanOrEqualTo(glass.CapacityMl+1e-8));Assert.That(glass.Contents.HasBubbles,Is.True);yield return Pick("tool.spoon");yield return Gesture("spoon.stir","tool.spoon","glass.highball",true,2f);yield return Put("tool.spoon");Capture("tonic-stirred");yield return Click("glass.serve","glass.highball","fixture.servemat");Assert.That(glass.IsServed,Is.True);AssertLedger();Capture("tonic-served");
        }
        [UnityTest] public IEnumerator BarKitReverseLoadAndPressureWorkAtBothJiggerEnds()
        {
            foreach (bool smallEnd in new[]{false, true})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
                yield return null;
                s = UnityEngine.Object.FindFirstObjectByType<BarSession>();
                yield return Pick("jigger.dual");
                if (smallEnd) yield return Click("jigger.flip", "jigger.dual", "jigger.dual");
                var jigger = (Jigger)View("jigger.dual").Item;
                Assert.That(jigger.ActiveCup.NominalMl, Is.EqualTo(smallEnd ? 30 : 45));
                var binding = View("jigger.dual").GetComponentsInChildren<ContainerVisual>().Single(v => v.DefinitionId == jigger.ActiveCup.Definition.Id);
                Assert.That(Vector3.Dot(binding.transform.up, View("jigger.dual").transform.up), Is.GreaterThan(.99f));
                yield return Pick("tool.squeezer");
                yield return Click("lemon.load", "tool.squeezer", "produce.lemon_half");
                var tool = (Squeezer)View("tool.squeezer").Item;
                var lemon = (LemonHalf)View("produce.lemon_half").Item;
                Assert.That(tool.Produce, Is.SameAs(lemon));
                Assert.That(View("produce.lemon_half").transform.IsChildOf(View("tool.squeezer").transform), Is.True);
                Assert.That(View("produce.lemon_half").gameObject.activeInHierarchy, Is.True);
                Assert.That(Vector3.Distance(View("produce.lemon_half").transform.lossyScale, Vector3.one), Is.LessThan(.0001f));
                double total = lemon.JuiceRemainingMl;
                Choose("lemon.press", "tool.squeezer", "jigger.dual");
                s.PointerDelta(new Vector2(0,-60), Now() + .001);
                yield return new WaitForSecondsRealtime(.6f);
                Capture(smallEnd ? "barkit-press-30" : "barkit-press-45");
                Assert.That(View("tool.squeezer").GetComponent<SqueezerViewRig>().OpenAmount, Is.LessThan(.34f));
                s.LeftButton(false, Now());
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(jigger.ActiveCup.Contents.LiquidMl, Is.GreaterThan(0));
                Assert.That(jigger.ActiveCup.Contents.LiquidMl + lemon.JuiceRemainingMl, Is.EqualTo(total).Within(1e-8));
                Assert.That(s.Clock.Error, Is.Null);
                AssertLedger();
            }
        }
    }
}
