using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>倒进手持量酒器和摇壶时的画面（含手持物前景相机），作为观察运镜的验收截图；检查倒酒时镜头低头、松手后低头并入玩家视角，V2 的摇完收势和擦干，摇壶全程壶身留在画面内。</summary>
    public sealed class ObservationCaptures
    {
        private BarSession s;private int placed;private readonly List<Texture2D> tiles=new List<Texture2D>();
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private void Aim(string id){var view=View(id);s.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view),"真实射线目标 "+id);}
        private IEnumerator Pick(string id){yield return View(id).Item is Bottle?CapSteps.Uncap(s,View(id)):null;Aim(id);s.RightButton(Now());yield return new WaitForSecondsRealtime(.24f);Assert.That(s.Hands.SideOf(View(id).Item).HasValue,Is.True,"拿取 "+id+"；"+s.Clock.Error);}
        private IEnumerator Put(string id){yield return CapSteps.PutDown(s,View(id));}
        private void Choose(string action,string actor,string target)
        {
            if(s.Hands.SideOf(View(target).Item).HasValue){s.Player.AimAt(CapSteps.TablePoint(s,.9f,-.25f));Physics.SyncTransforms();s.Targeting();}else{var view=View(target);s.Player.AimAt(view.Item is Fixture&&view.Anchor("Surface")?view.Anchor("Surface").position:view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();s.Targeting();}
            int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id==action&&o.Actor==View(actor).Item&&(o.Target==View(target).Item||o.Target.Parent==View(target).Item));
            Assert.That(index,Is.GreaterThanOrEqualTo(0),action+" · "+actor+" → "+target+"；"+s.Clock.Error);s.SelectMenu(index,true);s.LeftButton(true,Now());
        }
        private IEnumerator Click(string action,string actor,string target)
        {
            Choose(action,actor,target);s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(1.05f);
            // 2026-10-29: adding ice now reaches the tool to the glass, tips it and brings the hands back (IceDeliveryMotion); input waits until that ends.
            float deadline=Time.realtimeSinceStartup+4;while(s.IceDeliveryBusy&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(s.IceDeliveryBusy,Is.False,"等待装冰回手后才执行下一步");Assert.That(s.Clock.Error,Is.Null,action);
        }
        private void Shot(string name){s.GetComponent<HeldItemRendering>().Refresh();tiles.Add(HoldFramingCaptures.Capture(s.Player.ViewCamera,"observe-"+name));}
        [UnityTest] public IEnumerator HeldJiggerPourAndShakeStayReadable()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();placed=0;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;s.Player.ViewCamera.aspect=16f/9;
            yield return Pick("jigger.dual");yield return Pick("bottle.gin");yield return new WaitForSecondsRealtime(.8f);Shot("0-holding");
            Choose("pour.bottle_to_container","bottle.gin","jigger.dual");s.PointerDelta(new Vector2(0,1000),Now()+.001);
            var look=s.Player.LookRotation;var rig=s.GetComponent<HoldViewRig>();
            yield return new WaitForSecondsRealtime(.3f);Shot("1-pour-start");yield return new WaitForSecondsRealtime(.7f);Shot("2-pour");
            var jig=View("jigger.dual");var toJig=jig.transform.position+Vector3.up*jig.Size.y*.5f-s.Player.ViewCamera.transform.position;
            string sight="pitch "+s.Player.Pitch.ToString("F1")+" lean "+rig.Lean.ToString("F1")+" sight "+(Mathf.Asin(-toJig.normalized.y)*Mathf.Rad2Deg).ToString("F1")+" jiggerTilt "+Vector3.Angle(PourPose.Axis(jig),Vector3.up).ToString("F1");
            // The observation camera leans in over the held jigger; the player's own look is untouched and the view returns afterwards.
            Assert.That(rig.Lean,Is.GreaterThan(1f),"往手里的量酒器倒时镜头应低头看进杯里");Assert.That(Quaternion.Angle(look,s.Player.LookRotation),Is.LessThan(.001f));
            s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(1.5f);Shot("3-after-pour");
            Assert.That(rig.Lean,Is.EqualTo(0));Assert.That(Quaternion.Angle(s.Player.ViewCamera.transform.rotation,s.Player.LookRotation),Is.LessThan(.001f),"松手后低头并入玩家视角，镜头不再自己动");
            yield return Put("bottle.gin");yield return Click("pour.jigger_to_container","jigger.dual","shaker.cobbler");yield return Put("jigger.dual");
            yield return Pick("tool.scoop");yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","shaker.cobbler");
            yield return Click("ice.take","tool.scoop","fixture.icebin");yield return Click("ice.add","tool.scoop","shaker.cobbler");yield return Put("tool.scoop");
            yield return Pick("shaker.strainer");yield return Click("shaker.attach_strainer","shaker.strainer","shaker.cobbler");yield return Pick("shaker.cap");yield return Click("shaker.attach_cap","shaker.cap","shaker.cobbler");
            yield return Pick("shaker.cobbler");yield return new WaitForSecondsRealtime(.8f);Shot("4-shaker-held");
            var shaker=View("shaker.cobbler");var camera=s.Player.ViewCamera;float worst=1;
            Choose("shaker.shake","shaker.cobbler","shaker.cobbler");float start=Time.realtimeSinceStartup;float previous=0;int frame=0;float travelled=0,moving=0;var last=shaker.transform.position;
            while(Time.realtimeSinceStartup-start<4f)
            {
                // A player's stroke: about two reversals per second, 160 px each way.
                float t=Time.realtimeSinceStartup-start,position=Mathf.Sin(t*2*Mathf.PI*1.1f)*160;s.PointerDelta(new Vector2(0,position-previous),Now());previous=position;yield return null;frame++;
                if(t>.6f){var m=HoldFramingCaptures.Measure(camera,shaker);worst=Mathf.Min(worst,m.Visible);travelled+=(shaker.transform.position-last).magnitude;moving+=Time.unscaledDeltaTime;}last=shaker.transform.position;
                if(t>1.2f&&tiles.Count<9&&frame%7==0)Shot("5-shake-"+tiles.Count);
            }
            s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.6f);Shot("6-admire");float admireHeight=HoldFramingCaptures.Measure(camera,shaker).Height;
            yield return new WaitForSecondsRealtime(2.2f);Shot("7-after-admire");float restHeight=HoldFramingCaptures.Measure(camera,shaker).Height;
            Assert.That(admireHeight,Is.GreaterThan(restHeight*1.05f),"摇完收势时壶端近赏霜，之后回到持握位");
            // V2 wipe: display only; the cold tin's fog, frost and water come off, then may fog again by the usual rules.
            var surface=s.Cues.SurfaceFor(((CobblerShaker)shaker.Item).Body.CoreVesselId);float before=surface.Mist+surface.Frost+surface.Wet;
            Assert.That(before,Is.GreaterThan(.06f),"摇过的冷壶应有雾或霜可擦");double inputs=s.Adapter.World.Inputs.m;
            Choose("shaker.wipe","shaker.cobbler","shaker.cobbler");s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(.25f);Shot("8-wiping");
            yield return new WaitForSecondsRealtime(.3f);float after=surface.Mist+surface.Frost+surface.Wet;Shot("9-wiped");
            Assert.That(after,Is.LessThan(before*.5f),"擦干后雾、霜、水明显减少");Assert.That(s.Adapter.World.Inputs.m,Is.EqualTo(inputs),"擦干不改核心");
            string v2="mean tin speed while shaking "+(travelled/Mathf.Max(.01f,moving)).ToString("F2")+" m/s | admire height "+admireHeight.ToString("F3")+" rest "+restHeight.ToString("F3")+" | wipe surface "+before.ToString("F3")+" -> "+after.ToString("F3");
            int w=480,h=270,columns=3,rows=(tiles.Count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rows,TextureFormat.RGB24,false);
            for(int i=0;i<tiles.Count;i++){var tile=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rows-1-i/columns)*h+y,tile.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(tile);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"observe.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllText(Path.Combine(HoldFramingCaptures.Output,"observe.txt"),"worst shaker visible fraction while shaking: "+worst.ToString("F3")+" | pour: "+sight+" | "+v2);
            Assert.That(worst,Is.GreaterThan(.75f),"摇壶时壶身至少四分之三在画面内");
        }
    }
}
