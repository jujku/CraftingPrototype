using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed class SealedShakeTests
    {
        private BarSession session;private ItemView view;private CobblerShaker shaker;private LocalIceWorld rig;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports"));
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            view=session.Views.First(v=>v.Item is CobblerShaker);shaker=(CobblerShaker)view.Item;
            rig=view.GetComponentInChildren<LocalIceWorld>();rig.enabled=false;
            session.Adapter.World.Apply(new Operation{type="AddIce",to=shaker.Body.CoreVesselId,grams=160});session.Adapter.Advance(0);
            for(int i=0;i<120;i++)rig.Advance(1f/60);Directory.CreateDirectory(Output);
        }
        private void Seal()
        {if(shaker.StrainerSlot.IsEmpty)shaker.Attach((ShakerStrainer)session.Views.First(v=>v.Item is ShakerStrainer).Item);if(shaker.CapSlot.IsEmpty)shaker.Attach((ShakerCap)session.Views.First(v=>v.Item is ShakerCap).Item);}
        private void CheckCorners()
        {
            foreach(var body in rig.Bodies)
            {
                var box=body.GetComponent<BoxCollider>();
                for(int i=0;i<8;i++)
                {
                    var p=body.position+body.rotation*(box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    Assert.That(p.y,Is.InRange(rig.Shape.Bottom-.001f,rig.Shape.Top+.001f));
                    Assert.That(new Vector2(p.x-rig.Shape.Axis.x,p.z-rig.Shape.Axis.y).magnitude,Is.LessThanOrEqualTo(rig.Shape.RadiusAt(p.y)+.001f));
                }
            }
        }
        [UnityTest] public IEnumerator SealedShakeRetainsIdentitiesAndContainsEveryPhysicsStep()
        {
            yield return Load();var ids=rig.Bodies.Select(b=>b.GetInstanceID()).ToArray();Assert.That(ids.Length,Is.EqualTo(4));Seal();
            string hash=LogReplay.SnapshotHash(session.Adapter.World);var origin=view.transform.position;var rotation=view.transform.rotation;int steps=0;
            rig.PhysicsStepped+=()=>{CheckCorners();steps++;};rig.PreviewIntensity=1;var positions=new List<string>{"frame,topCorner,rim,impacts"};
            for(int frame=0;frame<180;frame++)
            {
                float phase=frame/60f*6*Mathf.PI;view.transform.rotation=Quaternion.Euler(0,0,40)*rotation;
                view.transform.position=origin+PourPose.Axis(view)*(Mathf.Sin(phase)*.05f);rig.Advance(1f/60);
                Assert.That(rig.Bodies.Select(b=>b.GetInstanceID()),Is.EqualTo(ids));
                Assert.That(rig.Displays.All(t=>!t.gameObject.activeSelf),Is.True,"封壶只关显示，不销毁代理");
                float top=float.MinValue;foreach(var body in rig.Bodies){var box=body.GetComponent<BoxCollider>();for(int j=0;j<8;j++)top=Mathf.Max(top,(body.position+body.rotation*(box.center+Vector3.Scale(box.size*.5f,new Vector3((j&1)==0?-1:1,(j&2)==0?-1:1,(j&4)==0?-1:1)))).y);}positions.Add(frame+","+top+","+rig.Shape.Top+","+rig.Impacts.Count);
            }
            File.WriteAllLines(Path.Combine(Output,"impact-diagnostic.txt"),rig.Impacts.Recent.Select(e=>e.Kind+" "+e.Impulse));
            File.WriteAllLines(Path.Combine(Output,"body-heights.csv"),positions);
            Assert.That(steps,Is.EqualTo(360));Assert.That(rig.WallEscapes,Is.Zero);Assert.That(rig.Impacts.Count,Is.GreaterThan(0));
            Assert.That(rig.Impacts.Recent.Any(e=>e.Kind==IceImpactKind.Lid),Is.True,"换向时应有盖板撞击");
            var impacts=rig.Impacts.Recent.ToArray();Assert.That(impacts.All(e=>e.Impulse>=BPresentationSettings.Load().impacts.minimumImpulse),Is.True);
            session.ToggleDebug();rig.Advance(1f/60);Assert.That(rig.Displays.All(t=>t.gameObject.activeSelf),Is.True);session.ToggleDebug();
            shaker.Detach(shaker.CapSlot.Content);rig.Advance(1f/60);
            Assert.That(rig.Bodies.Select(b=>b.GetInstanceID()),Is.EqualTo(ids));Assert.That(rig.Displays.All(t=>t.gameObject.activeSelf),Is.True);
            rig.PreviewIntensity=0;view.transform.SetPositionAndRotation(origin,rotation);for(int i=0;i<240;i++)rig.Advance(1f/60);
            int quiet=rig.Impacts.Count;for(int i=0;i<60;i++)rig.Advance(1f/60);Assert.That(rig.Impacts.Count,Is.EqualTo(quiet),"静止稳定后不持续制造撞击");
            Assert.That(rig.Bodies.All(b=>b.IsSleeping()),Is.True);Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));
            File.WriteAllText(Path.Combine(Output,"sealed-identity.txt"),"identities=preserved\nsubsteps="+steps+"\nescapes=0\nquietImpacts=0\ncoreUnchanged=true\n");
            File.WriteAllLines(Path.Combine(Output,"impacts.csv"),new[]{"time,impulse,strength,kind,x,y,z"}.Concat(impacts.Select(e=>$"{e.Time},{e.Impulse},{e.Strength},{e.Kind},{e.Position.x},{e.Position.y},{e.Position.z}")));
        }
        [UnityTest] public IEnumerator AddingLiquidToAStillVesselWakesSleepingIce()
        {
            yield return Load();Assert.That(rig.Bodies.All(b=>b.IsSleeping()),Is.True);
            var ids=rig.Bodies.Select(b=>b.GetInstanceID()).ToArray();
            session.Adapter.World.Apply(new Operation{type="PourSource",to=shaker.Body.CoreVesselId,ingredient="spirit",ml=100});session.Adapter.Advance(0);
            string hash=LogReplay.SnapshotHash(session.Adapter.World);rig.Advance(1f/60);
            Assert.That(rig.Bodies.Any(b=>!b.IsSleeping()),Is.True,"静止容器加料必须唤醒冰块重新计算浮力");
            for(int i=0;i<60;i++)rig.Advance(1f/60);
            Assert.That(rig.Bodies.Select(b=>b.GetInstanceID()),Is.EqualTo(ids));
            Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));
        }
        [UnityTest] public IEnumerator ShakePoseStaysInViewAndProfilesOpenVersusSealed()
        {
            yield return Load();Assert.That(session.Hands.TryPickUp(shaker),Is.True);var pose=new ShakePresentation();var camera=session.Player.ViewCamera;
            var origin=camera.transform.TransformPoint(new Vector3(.05f,-.10f,.6f));/* 2026-10-28: 3 cm higher so the new wind-up dip (3.5 cm) before the toss stays in frame */var rotation=view.transform.rotation;var rows=new List<string>{"mode,frame,stepMilliseconds,count"};
            for(int mode=0;mode<2;mode++)
            {
                if(mode==1)Seal();rig.PreviewIntensity=1;
                for(int frame=0;frame<240;frame++)
                {
                    view.transform.SetPositionAndRotation(origin,rotation);pose.Apply(session,view,true,Math.Sin(frame*.3)*60,1f/60);rig.Advance(1f/60);
                    var bounds=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.name!="InnerCavity").OrderByDescending(r=>r.bounds.size.sqrMagnitude).First().bounds;
                    /* 2026-10-28: while the tin spins in the toss its world box is far larger than the tin, so the toss is checked with the item's own oriented boxes. */
                    bool flight=pose.StageName=="Windup"||pose.StageName=="Toss"||pose.StageName=="Chamber";if(flight){Assert.That(HoldFramingCaptures.Measure(camera,view).Visible,Is.GreaterThan(.97f),"mode "+mode+" frame "+frame+" "+pose.StageName);rows.Add((mode==0?"open":"sealed")+","+frame+","+rig.StepMilliseconds+","+rig.DisplayCount);continue;}
                    /* 2026-10-28: the stroke starts at the catch point, where the tilted tin's world AABB pokes past the 4:3 edge while the tin itself is inside; check the oriented boxes, stricter than in flight. */
                    float visible=HoldFramingCaptures.Measure(camera,view).Visible;Assert.That(visible,Is.GreaterThan(.995f),"mode "+mode+" frame "+frame+" stage "+pose.StageName+" visible "+visible.ToString("F4")+" aspect "+camera.aspect.ToString("F2"));
                    for(int i=0;i<8;i++){var p=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));Assert.That(p.z,Is.GreaterThan(0),"mode "+mode+" frame "+frame+" in front of the camera");}
                    rows.Add((mode==0?"open":"sealed")+","+frame+","+rig.StepMilliseconds+","+rig.DisplayCount);
                }
            }
            File.WriteAllLines(Path.Combine(Output,"shake-native-profile.csv"),rows);
        }
        /// <summary>
        /// 用户反馈起壶接住时"鬼畜"。原来接住瞬间速度一帧清零、再被 14 Hz 弹簧踢一下，60 fps 下壶在接住后 0.5 s 里来回颠 15 次、加速度 265 m/s²。
        /// 现在出手和接住都速度连续：按 30/60/144 fps 固定步长跑起壶，壶中心的加速度、转动的角加速度都有上限，接住后沿来向只让一下再收回。
        /// </summary>
        [UnityTest] public IEnumerator TossCatchKeepsVelocityAndSettlesWithoutJitter()
        {
            yield return Load();Assert.That(session.Hands.TryPickUp(shaker),Is.True);var camera=session.Player.ViewCamera.transform;
            var origin=camera.TransformPoint(new Vector3(.05f,-.10f,.6f));var rotation=view.transform.rotation;view.transform.SetPositionAndRotation(origin,rotation);
            var renderers=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();var b0=renderers[0].bounds;foreach(var r in renderers)b0.Encapsulate(r.bounds);var pivotLocal=view.transform.InverseTransformPoint(b0.center);
            var rows=new List<string>{"fps,frame,stage,x,y,z,speed,angularSpeed"};var report=new List<string>();var checks=new List<Action>();
            foreach(int fps in new[]{30,60,144})
            {
                float dt=1f/fps;var pose=new ShakePresentation();var points=new List<Vector3>();var turns=new List<Quaternion>();var stages=new List<string>();int caught=-1;
                for(int frame=0;frame<fps*2;frame++)
                {
                    view.transform.SetPositionAndRotation(origin,rotation);pose.Apply(session,view,true,0,dt);
                    points.Add(camera.InverseTransformPoint(view.transform.TransformPoint(pivotLocal)));turns.Add(Quaternion.Inverse(camera.rotation)*view.transform.rotation);stages.Add(pose.StageName);
                    if(caught<0&&pose.CatchSequence>0)caught=frame;if(caught>=0&&frame-caught>=.5f/dt)break;
                }
                Assert.That(caught,Is.GreaterThan(0),fps+" fps 接住");
                float maxAcc=0,maxAngularAcc=0,give=0;int reversals=0,lastSign=0;var into=(points[caught]-points[caught-1]).normalized;
                for(int i=1;i<points.Count;i++)
                {
                    float speed=(points[i]-points[i-1]).magnitude/dt,angular=Quaternion.Angle(turns[i-1],turns[i])/dt;
                    rows.Add(fps+","+i+","+stages[i]+","+points[i].x.ToString("F5")+","+points[i].y.ToString("F5")+","+points[i].z.ToString("F5")+","+speed.ToString("F3")+","+angular.ToString("F1"));
                    if(i>=2){maxAcc=Mathf.Max(maxAcc,(points[i]-2*points[i-1]+points[i-2]).magnitude/(dt*dt));maxAngularAcc=Mathf.Max(maxAngularAcc,Mathf.Abs(angular-Quaternion.Angle(turns[i-2],turns[i-1])/dt)/dt);}
                    if(i>caught){give=Mathf.Max(give,(points[i]-points[caught]).magnitude);float along=Vector3.Dot(points[i]-points[i-1],into)/dt;int sign=along>.05f?1:along<-.05f?-1:0;if(sign!=0){if(lastSign!=0&&sign!=lastSign)reversals++;lastSign=sign;}}
                }
                string line=fps+" fps: max acceleration "+maxAcc.ToString("F1")+" m/s^2, max angular acceleration "+maxAngularAcc.ToString("F0")+" deg/s^2, give after the catch "+(give*100).ToString("F2")+" cm, reversals along the incoming direction "+reversals;report.Add(line);
                checks.Add(()=>
                {
                    Assert.That(stages.Distinct().Take(3),Is.EqualTo(new[]{"Windup","Toss","Chamber"}),line);
                    Assert.That(maxAcc,Is.LessThan(100),"出手和接住速度连续，没有一帧内的速度突变（旧写法 60 fps 时 265 m/s²）；"+line);
                    Assert.That(maxAngularAcc,Is.LessThan(30000),"空中转速从零加起，接住时转速顺势缓冲（旧写法接住时 750°/s 一帧清零）；"+line);
                    Assert.That(reversals,Is.LessThanOrEqualTo(1),"接住后只让一下再收回，不来回颠（旧写法 15 次）；"+line);
                    Assert.That(give,Is.InRange(.005f,.03f),"接住时顺着来向让开 0.5–3 cm；"+line);
                });
            }
            Directory.CreateDirectory(HoldFramingCaptures.Output);File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"shake-catch.csv"),rows);File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"shake-catch.txt"),report);
            foreach(var check in checks)check();
        }
    }
}
