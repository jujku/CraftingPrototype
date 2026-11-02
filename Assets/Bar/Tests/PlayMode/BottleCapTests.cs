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
    internal sealed class BottleCapTests
    {
        private static Transform[] Caps(ItemView bottle)=>bottle.GetComponentsInChildren<Transform>(true)
            .Where(t=>(t.name=="BottleCap"||t.name=="CrownOrScrewCap"||t.name=="Cap")&&t.GetComponent<MeshRenderer>()).ToArray();
        // frame=false keeps the player's own view, so held items stay where the hands are.
        private static void Capture(BarSession session,ItemView bottle,string name,bool frame=true)
        {
            var camera=session.Player.ViewCamera;var position=camera.transform.position;var rotation=camera.transform.rotation;
            if(frame){camera.transform.position=bottle.transform.position+new Vector3(-.1f,.21f,-.32f);camera.transform.LookAt(bottle.transform.position+Vector3.up*.18f);}
            var rt=RenderTexture.GetTemporary(640,480,24);var previous=RenderTexture.active;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
            var texture=new Texture2D(640,480,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,640,480),0,0);texture.Apply();
            var folder=Path.Combine(Application.dataPath,"../BottleCapReports");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());
            Object.Destroy(texture);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);camera.transform.SetPositionAndRotation(position,rotation);
        }
        [UnityTest]public IEnumerator FourCapsComeOffIntoAHandGoDownOnTheCounterAndGoBackOn()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
            ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
            void Aim(ItemView view)=>CapSteps.Aim(s,view);
            IEnumerator Run(string action,Item actor,Item target)
            {
                int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id==action&&o.Actor==actor&&o.Target==target);
                Assert.That(index,Is.GreaterThanOrEqualTo(0),action+" "+target.Definition.Id);Assert.That(s.LookLocked,Is.False);
                s.SelectMenu(index,true);s.LeftButton(true,Now());s.LeftButton(false,Now()+.001);
                yield return new WaitForSecondsRealtime(.2f);Assert.That(s.Clock.Error,Is.Null);
            }
            float Top(ItemView cap,ItemView bottle)=>Caps(cap).Select(t=>t.GetComponent<MeshRenderer>().bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;}).max.y-bottle.transform.position.y;
            float surface=View("fixture.counter").Anchor("Surface").position.y;
            var bottles=s.Views.Where(v=>v.Item is Bottle).OrderBy(v=>v.DefinitionId=="bottle.tonic"?1:0).ThenBy(v=>v.InstanceId).ToArray();
            Assert.That(bottles.Select(v=>v.DefinitionId).Distinct().Count(),Is.EqualTo(4));
            foreach(var view in bottles)
            {
                var bottle=(Bottle)view.Item;var cap=CapSteps.CapOf(s,view);
                Assert.That(bottle.IsOpen,Is.False);Assert.That(cap.Item.Parent,Is.SameAs(bottle));
                Assert.That(Caps(cap).Length,Is.GreaterThan(0),"瓶盖网格已从瓶子模型中分离到独立视图");
                Assert.That(Caps(cap).All(t=>t.gameObject.activeInHierarchy),Is.True);
                Assert.That(cap.transform.parent,Is.SameAs(view.Anchor("Slot_BottleCap")),"装着的瓶盖挂在瓶口插槽上");
                Assert.That(Top(cap,view),Is.EqualTo(view.Size.y).Within(.003f),"瓶盖仍在瓶顶原位 "+view.DefinitionId);
                var size=Caps(cap).Select(t=>t.GetComponent<MeshRenderer>().bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;}).size;
                Assert.That(size.y,Is.EqualTo(cap.Size.y).Within(.002f),"瓶盖数据尺寸与网格一致 "+cap.DefinitionId);
            }
            var gin=View("bottle.gin");Capture(s,gin,"01-capped");
            // Gin, syrup and soda: uncap on the counter with empty hands, then set each cap down.
            foreach(var view in bottles.Where(v=>v.DefinitionId!="bottle.tonic"))
            {
                var bottle=(Bottle)view.Item;var cap=CapSteps.CapOf(s,view);double stock=bottle.RemainingMl;
                Aim(view);yield return Run("bottle.open",null,bottle);
                Assert.That(bottle.IsOpen,Is.True);Assert.That(bottle.RemainingMl,Is.EqualTo(stock));
                Assert.That(s.Hands.SideOf(bottle),Is.Null,"左键只取盖，瓶子留在台面");
                Assert.That(s.Hands.SideOf(cap.Item),Is.EqualTo(HandSide.Right),"瓶盖进入空着的右手");
                Assert.That(cap.transform.parent,Is.SameAs(s.Player.RightAnchor));Assert.That(Caps(cap).All(t=>t.gameObject.activeInHierarchy),Is.True);
                s.Targeting();Assert.That(s.MenuOptions.Any(o=>o.Action.Id=="bottle.open"&&o.Target==bottle),Is.False);
                if(view==gin)Capture(s,gin,"02-cap-in-hand",false);
                yield return CapSteps.PutDown(s,cap);
                Assert.That(cap.Item.Location.Kind,Is.EqualTo(LocationKind.Surface));
                yield return new WaitForSecondsRealtime(.35f);
                Assert.That(cap.transform.position.y,Is.EqualTo(surface+.002f).Within(.0015f),"瓶盖立在台面上（落点比命中点高 2 mm）");
                Assert.That(Vector3.Angle(cap.transform.up,Vector3.up),Is.LessThan(1f));
            }
            Capture(s,gin,"03-gin-open");
            // Tonic: with both hands full there is no hand for the cap; free one and uncap the held bottle.
            var tonic=View("bottle.tonic");var tonicBottle=(Bottle)tonic.Item;var tonicCap=CapSteps.CapOf(s,tonic);
            Aim(gin);s.RightButton(Now());yield return new WaitForSecondsRealtime(.2f);
            Aim(tonic);s.RightButton(Now());yield return new WaitForSecondsRealtime(.2f);
            Assert.That(s.Hands.Left,Is.Not.Null);Assert.That(s.Hands.Right,Is.Not.Null);
            Aim(View("glass.highball"));Assert.That(s.MenuOptions.Any(o=>o.Actor==tonicBottle&&o.Action.Id=="pour.bottle_to_container"),Is.False,"盖着不能倒");
            Assert.That(s.MenuOptions.Any(o=>o.Action.Id=="bottle.open"&&o.Target==tonicBottle),Is.False,"双手都满时没有手接住瓶盖");
            yield return CapSteps.PutDown(s,gin);
            s.Targeting();yield return Run("bottle.open",tonicBottle,tonicBottle);
            Assert.That(tonicBottle.IsOpen,Is.True);Assert.That(s.Hands.SideOf(tonicCap.Item).HasValue,Is.True);
            Assert.That(s.Hands.SideOf(tonicCap.Item),Is.Not.EqualTo(s.Hands.SideOf(tonicBottle)),"瓶盖进入另一只手");
            var glass=View("glass.highball");Aim(glass);
            int pour=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="pour.bottle_to_container"&&o.Actor==tonicBottle&&o.Target==glass.Item);
            Assert.That(pour,Is.GreaterThanOrEqualTo(0),"一手瓶子、一手瓶盖也能倒");s.SelectMenu(pour,true);s.LeftButton(true,Now());s.PointerDelta(new Vector2(0,1000),Now()+.001);
            yield return new WaitForSecondsRealtime(.3f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.25f);
            Assert.That(s.Clock.Error,Is.Null);double poured=((Glass)glass.Item).Contents.LiquidMl;Assert.That(poured,Is.GreaterThan(0));Assert.That(((Bottle)gin.Item).RemainingMl,Is.EqualTo(750));
            // Put the cap back on the held tonic bottle; it locks pouring again and rides on the bottle.
            s.Targeting();yield return Run("bottle.close",tonicCap.Item,tonicBottle);
            Assert.That(tonicBottle.IsOpen,Is.False);Assert.That(tonicCap.Item.Parent,Is.SameAs(tonicBottle));
            Assert.That(tonicCap.transform.parent,Is.SameAs(tonic.Anchor("Slot_BottleCap")));
            Assert.That(s.Hands.SideOf(tonicCap.Item),Is.EqualTo(s.Hands.SideOf(tonicBottle)));
            Aim(glass);Assert.That(s.MenuOptions.Any(o=>o.Actor==tonicBottle&&o.Action.Id=="pour.bottle_to_container"),Is.False,"盖回后不能再倒");
            Assert.That(((Glass)glass.Item).Contents.LiquidMl,Is.EqualTo(poured).Within(.01),"盖回后没有再倒入（核心仍在模拟，允许微小变化）");
            yield return CapSteps.PutDown(s,tonic);
            // Pick the gin cap up from the counter and close the gin bottle standing there.
            var ginCap=CapSteps.CapOf(s,gin);Aim(ginCap);s.RightButton(Now());yield return new WaitForSecondsRealtime(.2f);
            Assert.That(s.Hands.SideOf(ginCap.Item).HasValue,Is.True,"台面上的瓶盖可以右键拿起");
            Aim(gin);yield return Run("bottle.close",ginCap.Item,gin.Item);
            Assert.That(((Bottle)gin.Item).IsOpen,Is.False);Assert.That(s.Hands.Left,Is.Null);Assert.That(s.Hands.Right,Is.Null);
            Assert.That(ginCap.transform.parent,Is.SameAs(gin.Anchor("Slot_BottleCap")));
            Assert.That(Top(ginCap,gin),Is.EqualTo(gin.Size.y).Within(.003f),"盖回到瓶顶原位");
            Capture(s,gin,"04-recapped");
            var records=s.Journal.ReadLines().Select(Bar.Logging.SessionJournal.Decode).Where(r=>r.kind=="action_performed").ToArray();
            Assert.That(records.Count(r=>r.actionId=="bottle.open"),Is.EqualTo(bottles.Length));
            Assert.That(records.Count(r=>r.actionId=="bottle.close"),Is.EqualTo(2));
        }
    }
}
