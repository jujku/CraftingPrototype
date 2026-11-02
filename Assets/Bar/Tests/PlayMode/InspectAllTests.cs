using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>V4真实场景固定步长检视：安全口沿、核心只读、两段取景、速度连续性和输入打断。</summary>
    public sealed class InspectAllTests
    {
        private BarSession s;private HoldViewRig rig;private readonly List<LogRecord> events=new List<LogRecord>();
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../InspectAllReports"));
        private static readonly string[] Items={"jigger.dual","tool.spoon","tool.tongs","tool.scoop","tool.squeezer","shaker.cobbler","bottle.gin","bottle.syrup","bottle.soda","bottle.tonic","glass.highball","glass.coupe"};
        private ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            PlayerPrefs.DeleteKey(BarSession.HandednessKey);
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            s=Object.FindFirstObjectByType<BarSession>();rig=s.GetComponent<HoldViewRig>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            s.Player.ViewCamera.aspect=4f/3;s.Player.AimAt(CapSteps.TablePoint(s,0,0));events.Clear();
            s.Adapter.Events.Published+=e=>{if(e.Observation is LogRecord row)events.Add(row);};Directory.CreateDirectory(Output);
        }
        [TearDown]public void Restore(){PlayerPrefs.DeleteKey(BarSession.HandednessKey);}
        private void FlushInput(){s.Clock.AdvanceFrame((s.Clock.Ticks+1)*.1);}
        private void StartInspect(){s.Inspect(s.Clock.Ticks*.1);FlushInput();}
        private Transform Anchor(ItemView view)=>s.Hands.SideOf(view.Item)==HandSide.Left?s.Player.LeftAnchor:s.Player.RightAnchor;
        private void Step(float dt)
        {
            rig.AdvancePresentation(dt);
            // Same read-only presenters as live LateUpdate. No clock/core advancement.
            foreach(var v in s.Views)
            {
                var tongs=v.GetComponent<TongsViewRig>();if(tongs){var ice=v.GetComponentInChildren<IceVisual>(true);if(ice)tongs.Advance(((Tool)v.Item).LoadedCubes>0,ice.transform,dt);}
                var squeezer=v.GetComponent<SqueezerViewRig>();if(squeezer)squeezer.AdvanceInspectionReturn(dt);
            }
            s.Cues.Present(dt);
        }
        private void Contents(ItemView v,string mode)
        {
            if(mode=="empty")
            {if(v.Item is Bottle b)typeof(Bottle).GetProperty("RemainingMl").SetValue(b,0d);return;}
            if(v.Item is Bottle bottle){bottle.SetOpen(true);return;}
            var c=InspectionPolicy.Container(v.Item);if(c==null)return;
            double amount=mode=="full"?c.Definition.CapacityMl:mode=="ice"?35:80;
            if(mode=="ice")s.Adapter.World.Apply(new Operation{type="AddIce",to=c.CoreVesselId,grams=40});
            s.Adapter.World.Pour(c.CoreVesselId,mode=="gas"?"soda":"spirit",amount);s.Adapter.Advance(0);
        }
        private static Vector3 Centre(ItemView view,HandSide side,Vector3 pivot)
        {var grip=HoldViewRig.Grip(view,side);return (grip?grip:view.transform).TransformPoint(pivot);}
        private void Rim(ItemView view)
        {
            if(!InspectionPolicy.Loaded(view.Item))return;
            if(view.Item is Bottle b)
            {if(b.IsOpen){float safe=Mathf.Max(0,(float)new TiltFlowModel(s.GestureControls).StartAngle(b.RemainingMl,b.CapacityMl,b.Definition.Id)-10);Assert.That(Vector3.Angle(view.transform.up,Vector3.up),Is.LessThanOrEqualTo(safe+.05f),"开瓶安全起倒角 "+view.DefinitionId);}return;}
            var c=InspectionPolicy.Container(view.Item);if(c==null||!c.IsOpenTop)return;var visual=InspectionPolicy.Binding(view);
            var m=visual.LiquidVolume.sharedMaterial;var normal=(Vector3)m.GetVector("_PlaneNormal");var origin=(Vector3)m.GetVector("_PlanePivot");float height=m.GetFloat("_FillHeight");
            // Independent geometric test of the rendered plane at 96 points on the actual rim.
            var shape=visual.Shape;float radius=shape.RadiusAt(shape.Top),worst=float.MaxValue;
            for(int i=0;i<96;i++){float a=i*2*Mathf.PI/96;var local=new Vector3(shape.Axis.x+radius*Mathf.Cos(a),shape.Top,shape.Axis.y+radius*Mathf.Sin(a));var p=visual.transform.TransformPoint(local);float distance=Vector3.Dot(normal,p-origin)-normal.y*(height-origin.y);worst=Mathf.Min(worst,distance);}
            Assert.That(worst,Is.GreaterThanOrEqualTo(-.00015f),view.DefinitionId+" 液面越过口沿");
        }
        private void Shot(string name)
        {
            s.GetComponent<HeldItemRendering>().Refresh();var camera=s.Player.ViewCamera;var rt=RenderTexture.GetTemporary(960,720,24);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());Object.Destroy(image);
        }
        [UnityTest]public IEnumerator Fixed30FpsAllItems()=>Matrix(30);
        [UnityTest]public IEnumerator Fixed60FpsAllItemsAndNativeFrames()=>Matrix(60);
        [UnityTest]public IEnumerator Fixed144FpsAllItems()=>Matrix(144);
        private IEnumerator Matrix(int fps)
        {
            var summary=new List<string>{"item,contents,fps,visible,acceleration,angularAcceleration,returnMetres,returnDegrees,coreIdentical"};
            foreach(string id in Items)
            foreach(string mode in (id.StartsWith("glass.")?new[]{"empty","liquid","gas","full","ice"}:id=="shaker.cobbler"?new[]{"empty","liquid","ice"}:id.StartsWith("bottle.")?new[]{"empty","liquid"}:new[]{"empty"}))
            {
                yield return Load();var v=View(id);Contents(v,mode);Assert.That(s.Hands.TryPickUp(v.Item),Is.True,id);yield return new WaitForSecondsRealtime(.8f);
                s.enabled=false;rig.enabled=false;var anchor=Anchor(v);var side=s.Hands.SideOf(v.Item).Value;for(int k=0;k<120;k++)Step(1f/120);
                var rest=new Pose(anchor.localPosition,anchor.localRotation);float fov=s.Player.ViewCamera.fieldOfView;StartInspect();Assert.That(rig.Inspecting(side),Is.True,id);
                string hash=LogReplay.SnapshotHash(s.Adapter.World);var start=events.Last(r=>r.kind=="view_inspect");Assert.That(start.inspection.definitionId,Is.EqualTo(id));Assert.That(start.before,Is.Not.Null);
                var p=HoldViewSettings.Load().inspectAll.For(id);float look=id=="jigger.dual"?1.45f:p.lookSeconds,playEnd=id=="jigger.dual"?2.35f:look+p.playSeconds+p.settleSeconds,lift=id=="jigger.dual"?.3f:p.liftSeconds;
                var pivot=HoldViewRig.GripBounds(v,side,Quaternion.identity).center;Vector3 previous=Centre(v,side,pivot),velocity=Vector3.zero;Quaternion previousQ=v.transform.rotation;float omega=0,maxA=0,maxAngular=0,worst=1,dt=1f/fps;bool shot1=false,shot2=false;
                var trace=new List<string>{"t,x,y,z,qx,qy,qz,qw,speed,angularSpeed,visible"};float duration=rig.InspectionDuration(v);
                for(int i=1;i<=Mathf.CeilToInt((duration+.3f)*fps);i++)
                {
                    Step(dt);float t=i*dt;Vector3 now=Centre(v,side,pivot),speed=(now-previous)/dt;float angular=Quaternion.Angle(previousQ,v.transform.rotation)/dt;
                    if(i>1){maxA=Mathf.Max(maxA,(speed-velocity).magnitude/dt);maxAngular=Mathf.Max(maxAngular,Mathf.Abs(angular-omega)/dt);}
                    float visible=HoldFramingCaptures.Measure(s.Player.ViewCamera,v).Visible;
                    // User clarification: ≥90% during look/play only; keep V1 resting framing in entry/exit transitions.
                    if(t>=lift&&t<=playEnd)worst=Mathf.Min(worst,visible);Rim(v);
                    if(fps==60){var q=v.transform.rotation;trace.Add(FormattableString.Invariant($"{t},{now.x},{now.y},{now.z},{q.x},{q.y},{q.z},{q.w},{speed.magnitude},{angular},{visible}"));
                        if(!shot1&&t>=Mathf.Max(lift+.05f,look*.5f)){Shot(id+"-"+mode+"-look");shot1=true;}
                        if(!shot2&&t>=look+(playEnd-look)*.45f){Shot(id+"-"+mode+"-play");shot2=true;}}
                    previous=now;velocity=speed;previousQ=v.transform.rotation;omega=angular;
                }
                float metres=Vector3.Distance(anchor.localPosition,rest.position),degrees=Quaternion.Angle(anchor.localRotation,rest.rotation);
                summary.Add(FormattableString.Invariant($"{id},{mode},{fps},{worst},{maxA},{maxAngular},{metres},{degrees},{hash==LogReplay.SnapshotHash(s.Adapter.World)}"));
                File.WriteAllLines(Path.Combine(Output,"summary-"+fps+".csv"),summary);if(fps==60)File.WriteAllLines(Path.Combine(Output,id+"-"+mode+"-60fps.csv"),trace);
                Assert.That(worst,Is.GreaterThanOrEqualTo(.9f),id+" "+mode+" 端详/把玩画面占比");
                // Same conservative hard acceleration limits as SealedShakeTests: catches may rebound, never teleport.
                Assert.That(maxA,Is.LessThan(100),id+" "+mode+" 位置加速度");Assert.That(maxAngular,Is.LessThan(30000),id+" "+mode+" 角加速度");
                Assert.That(metres,Is.LessThan(.01f));Assert.That(degrees,Is.LessThan(3));Assert.That(s.Player.ViewCamera.fieldOfView,Is.EqualTo(fov));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                var end=events.Last(r=>r.kind=="view_inspect_ended");Assert.That(end.outcome,Is.EqualTo("completed"));Assert.That(end.activationId,Is.EqualTo(start.activationId));Assert.That(end.durationSeconds,Is.EqualTo(duration).Within(.001));
            }
        }
        [UnityTest]public IEnumerator LoadedToolsRejectFAndHudMatches()
        {
            foreach(string id in new[]{"jigger.dual","tool.tongs","tool.scoop"})
            {
                yield return Load();var v=View(id);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return null;
                Assert.That(HudText.Inspectable(v.Item),Is.True);
                if(v.Item is Jigger j){s.Adapter.World.Pour(j.ActiveCup.CoreVesselId,"spirit",1);s.Adapter.Advance(0);}
                else if(v.Item is IceTongs||v.Item is IceScoop)new TakeIceAction(()=>0).Begin(v.Item,View("fixture.icebin").Item,s.Hands);
                else ((Squeezer)v.Item).Load((ILoadableProduce)s.Views.First(x=>x.Item is LemonHalf).Item,s.Hands);
                Assert.That(HudText.Inspectable(v.Item),Is.False);s.enabled=false;StartInspect();Assert.That(rig.Inspecting(s.Hands.SideOf(v.Item).Value),Is.False);Assert.That(events.Any(r=>r.kind=="view_inspect"),Is.False);
                foreach(var other in s.Views.Where(x=>x.Item is Attachment or Fixture or LemonHalf))Assert.That(HudText.Inspectable(other.Item),Is.False);
            }
        }
        [UnityTest]public IEnumerator EqualElapsedWorkflowHasByteIdenticalCoreAndLinkedLogs()
        {
            yield return Load();var v=View("glass.highball");Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);s.enabled=false;rig.enabled=false;
            // Replay the actual initial operations into a twin that never receives F.
            var twin=new World(s.Adapter.World.Data,new WorldOptions{vessels=Array.Empty<VesselDefinition>(),recording=false});
            foreach(var r in s.Journal.ReadLines().Select(SessionJournal.Decode).Where(r=>r.kind=="core_operation"&&!r.rejected))twin.Apply(r.operation);
            Assert.That(LogReplay.SnapshotHash(twin),Is.EqualTo(LogReplay.SnapshotHash(s.Adapter.World)));
            var c=(Glass)v.Item;var pour=new Operation{type="PourSource",to=c.CoreVesselId,ingredient="tonic",ml=120};var ice=new Operation{type="AddIce",to=c.CoreVesselId,grams=80};
            s.Adapter.World.Apply(pour);twin.Apply(pour);s.Adapter.World.Apply(ice);twin.Apply(ice);s.Adapter.Advance(0);
            s.Adapter.Events.Published+=e=>{if(e.Kind=="core_operation"&&e.Observation is LogRecord r&&!r.rejected)twin.Apply(r.operation);};
            StartInspect();long first=s.Clock.Ticks;float dt=1f/60;
            for(int i=1;i<=240;i++){Step(dt);s.Clock.AdvanceFrame(first*.1+i/60d);}
            Assert.That(rig.Inspecting(s.Hands.SideOf(c).Value),Is.False);
            string actual=LogReplay.SnapshotHash(s.Adapter.World),expected=LogReplay.SnapshotHash(twin);Assert.That(actual,Is.EqualTo(expected),"相同核心时间与操作，F不能改变体积/温度/CO₂/冰/账本的任何字节");
            var begin=events.Last(r=>r.kind=="view_inspect");var end=events.Last(r=>r.kind=="view_inspect_ended");
            var decoded=s.Journal.ReadLines().Select(SessionJournal.Decode).Last(r=>r.kind=="view_inspect");Assert.That(decoded.inspection.carbonated,Is.True);Assert.That(decoded.before,Is.Not.Null,"落盘压缩不能丢掉检视内容");Assert.That(decoded.before.Single(x=>x.id==c.CoreVesselId).iceGrams,Is.GreaterThan(0));Assert.That(end.activationId,Is.EqualTo(begin.activationId));
            File.WriteAllText(Path.Combine(Output,"equal-elapsed-core.json"),"{\"withInspection\":\""+actual+"\",\"withoutInspection\":\""+expected+"\",\"equal\":true}");
            File.WriteAllLines(Path.Combine(Output,"inspection-log-sample.jsonl"),new[]{SessionJournal.Encode(begin),SessionJournal.Encode(end)});
        }
        [UnityTest]public IEnumerator SealedShakerWithContentsKeepsUprightDuringAxialHop()
        {
            yield return Load();var v=View("shaker.cobbler");var shaker=(CobblerShaker)v.Item;Contents(v,"ice");shaker.Attach((ShakerStrainer)s.Views.First(x=>x.Item is ShakerStrainer).Item);shaker.Attach((ShakerCap)s.Views.First(x=>x.Item is ShakerCap).Item);
            Assert.That(s.Hands.TryPickUp(shaker),Is.True);yield return new WaitForSecondsRealtime(.8f);s.enabled=false;rig.enabled=false;StartInspect();string before=LogReplay.SnapshotHash(s.Adapter.World);
            float largest=0;for(int i=0;i<360;i++){Step(1f/120);largest=Mathf.Max(largest,Vector3.Angle(v.transform.up,Vector3.up));}
            Assert.That(largest,Is.LessThan(25),"用户新编排允许封盖壶轻抛轴转，仍不倒置");Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(before));
        }
        [UnityTest]public IEnumerator HandednessInterruptKeepsFastReturnAcrossTheHandTransfer()
        {
            foreach(string id in new[]{"jigger.dual","tool.scoop"})
            {
                yield return Load();var v=View(id);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);
                s.Inspect(s.EventTime(Time.realtimeSinceStartupAsDouble));yield return new WaitForSecondsRealtime(.6f);var oldSide=s.Hands.SideOf(v.Item).Value;
                var newSide=oldSide==HandSide.Left?HandSide.Right:HandSide.Left;rig.Solve(v,newSide,s.Player.ViewCamera,out var target,out var targetRotation);
                var grip=HoldViewRig.Grip(v,newSide);float distance=Vector3.Distance(s.Player.ViewCamera.transform.InverseTransformPoint(grip.position),target);
                s.ToggleHandedness(s.EventTime(Time.realtimeSinceStartupAsDouble));Assert.That(rig.Inspecting(oldSide),Is.False);yield return new WaitForSecondsRealtime(.1f);
                // Input queue is on the unchanged 0.1s core tick. Budget starts at its consumed hand transfer.
                Assert.That(s.Hands.SideOf(v.Item),Is.EqualTo(newSide));yield return new WaitForSecondsRealtime(.15f);
                var anchor=Anchor(v);Assert.That(Vector3.Distance(anchor.localPosition,target),Is.LessThanOrEqualTo(Mathf.Max(.002f,distance*.05f)),"H换手也沿独立快弹簧回位");Assert.That(Quaternion.Angle(anchor.localRotation,targetRotation),Is.LessThan(3));
            }
        }
        [UnityTest]public IEnumerator EmptyTongsArticulateTwiceAndSqueezerClosesWithoutLoadingAnything()
        {
            foreach(string id in new[]{"tool.tongs","tool.squeezer"})
            {
                yield return Load();var v=View(id);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);s.enabled=false;rig.enabled=false;StartInspect();string hash=LogReplay.SnapshotHash(s.Adapter.World);
                var tongs=v.GetComponent<TongsViewRig>();var upper=v.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="UpperPivot");Quaternion open=upper?upper.localRotation:Quaternion.identity;float previous=0,largest=0,closure=0;int peaks=0;bool rising=false;
                for(int i=0;i<360;i++)
                {
                    Step(1f/120);float t=i/120f;
                    if(tongs&&t<1.25f){float a=tongs.OpeningDegrees;largest=Mathf.Max(largest,a);if(a>previous+.02f)rising=true;else if(rising&&a<previous-.02f){peaks++;rising=false;}previous=a;}
                    if(upper&&t>1.8f&&t<2.2f)closure=Mathf.Max(closure,Quaternion.Angle(open,upper.localRotation));
                }
                if(tongs){Assert.That(largest,Is.GreaterThan(8));Assert.That(peaks,Is.EqualTo(2),"实际夹臂必须夹两下");Assert.That(tongs.OpeningDegrees,Is.LessThan(.1f));}
                else {Assert.That(closure,Is.GreaterThan(30),"实际压碗铰链闭合，不能只转整个工具");Assert.That(((Squeezer)v.Item).Produce,Is.Null);}
                Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            }
        }
        [UnityTest]public IEnumerator InputInterruptsAllThreePhasesAndStillExecutes()
        {
            foreach(string id in new[]{"jigger.dual","tool.scoop"})foreach(float stage in (id=="jigger.dual"?new[]{.55f,1.9f,2.85f}:new[]{.55f,1.34f,1.53f}))foreach(string input in new[]{"left","right","tab","h","esc","action"})
            {
                yield return Load();var v=View(id);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);if(input!="right")Assert.That(s.Hands.TryPickUp(View("bottle.gin").Item),Is.True);s.Hands.SelectMain(s.Hands.SideOf(v.Item).Value);yield return new WaitForSecondsRealtime(.8f);
                if(input=="right")CapSteps.Aim(s,View("bottle.gin"));else s.Player.AimAt(CapSteps.TablePoint(s,.6f,-.25f));Physics.SyncTransforms();s.Targeting();s.enabled=false;rig.enabled=false;for(int k=0;k<120;k++)Step(1f/120);
                var side=s.Hands.SideOf(v.Item).Value;var anchor=Anchor(v);var rest=new Pose(anchor.localPosition,anchor.localRotation);StartInspect();for(int k=0;k<Mathf.RoundToInt(stage*120);k++)Step(1f/120);
                float before=Vector3.Distance(anchor.localPosition,rest.position),angleBefore=Quaternion.Angle(anchor.localRotation,rest.rotation);double time=s.Clock.Ticks*.1;
                var endBefore=v.Item is Jigger j?j.ActiveEnd:default;
                if(input=="left")
                {if(v.Item is Jigger){int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="jigger.flip");Assert.That(index,Is.GreaterThanOrEqualTo(0));s.SelectMenu(index,true);}s.LeftButton(true,time);s.LeftButton(false,time+.001);}
                else if(input=="right")s.RightButton(time);
                else if(input=="tab")s.ToggleMain(time);
                else if(input=="h")s.ToggleHandedness(time);
                else if(input=="esc")s.Cancel(time);
                else s.Adapter.Publish("action_began",new LogRecord{actionId="test.action"});
                Assert.That(rig.Inspecting(side),Is.False,id+" "+stage+" "+input);FlushInput();for(int k=0;k<18;k++)Step(1f/120);
                if(input!="h")
                {Assert.That(Vector3.Distance(anchor.localPosition,rest.position),Is.LessThanOrEqualTo(Mathf.Max(.001f,before*.05f)),"95%位置回位 "+input);Assert.That(Quaternion.Angle(anchor.localRotation,rest.rotation),Is.LessThanOrEqualTo(Mathf.Max(.2f,angleBefore*.05f)),"95%角度回位 "+input);}
                if(input=="left"&&v.Item is Jigger after)Assert.That(after.ActiveEnd,Is.Not.EqualTo(endBefore),"原左键仍执行翻转");
                if(input=="tab")Assert.That(s.Hands.MainHand,Is.Not.EqualTo(side),"原Tab仍切手");
                if(input=="right")Assert.That(s.Hands.SideOf(View("bottle.gin").Item).HasValue,Is.True,"原右键仍拿起");
                var end=events.Last(r=>r.kind=="view_inspect_ended");Assert.That(end.outcome,Is.EqualTo("interrupted"));Assert.That(end.reason,Is.Not.Null.And.Not.Empty);
            }
        }
    }
}
