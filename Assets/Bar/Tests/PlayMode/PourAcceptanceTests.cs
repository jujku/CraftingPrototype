using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>六种真实领域倒料的逐可见帧几何检查，以及瓶内库存、水平面和几何起流同步检查。</summary>
    public sealed class PourAcceptanceTests
    {
        private BarSession session;
        private ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            session=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
        }
        private void Fill(Container target,double ml)
        {session.Adapter.World.Apply(new Operation{type="PourSource",to=target.CoreVesselId,ingredient="spirit",ml=ml});session.Adapter.Advance(0);}
        [UnityTest]public IEnumerator StreamLandingIncludesIceDisplacementShownByTheLiquidRenderer()
        {
            yield return Load();session.enabled=false;var view=View("glass.highball");var glass=(Glass)view.Item;
            Fill(glass,30);session.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=40});session.Adapter.Advance(0);
            yield return null;yield return new WaitForFixedUpdate();
            var binding=view.GetComponentsInChildren<ContainerVisual>().First(v=>v.DefinitionId==glass.Definition.Id);
            var block=new MaterialPropertyBlock();binding.LiquidVolume.GetPropertyBlock(block);
            PourPose.Mouth(view,glass,out _,out _,out float landing);
            Assert.That(landing,Is.EqualTo(block.GetFloat("_FillHeight")).Within(.002f),"落点必须与实际绘制液面一致，包含冰排水体积");
            float withoutIce=binding.transform.TransformPoint(Vector3.up*binding.Profile.HeightForMl(glass.Contents.LiquidMl)).y;
            Assert.That(landing-withoutIce,Is.GreaterThan(.002f),"本例必须能检出忽略冰排水的旧错误");
        }
        [UnityTest]public IEnumerator SixPourCombinationsStayOnMouthAndLandOnLiquid()
        {
            var rows=new List<FrameRow>();
            for(int test=0;test<6;test++)
            {
                yield return Load();var controls=ItemCatalog.LoadControls(session.ControlsJson.text);
                var resolver=ActionResolver.CreateU4(controls,40,session.Adapter.World.Data.iceDensity,()=>.5);
                ItemView source,target;string action;
                if(test<2){source=View("bottle.soda");target=View(test==0?"glass.highball":"jigger.dual");((Bottle)source.Item).SetOpen(true);action="pour.bottle_to_container";}
                else if(test==2){source=View("jigger.dual");target=View("shaker.cobbler");Fill(((Jigger)source.Item).ActiveCup,20);action="pour.jigger_to_container";}
                else if(test==3){source=View("shaker.cobbler");target=View("glass.coupe");var sh=(CobblerShaker)source.Item;Fill(sh.Body,80);if(!sh.CapSlot.IsEmpty)sh.Detach(sh.CapSlot.Content);action="shaker.pour";}
                else if(test==4){source=View("tool.squeezer");target=View("glass.highball");((Squeezer)source.Item).Load((ILoadableProduce)session.Views.First(v=>v.Item is ILoadableProduce).Item,session.Hands);action="lemon.press";}
                else{source=View("glass.highball");target=View("fixture.sink");Fill((Container)source.Item,60);action="sink.empty";}
                Assert.That(session.Hands.TryPickUp(source.Item),Is.True);
                if(test==1)Assert.That(session.Hands.TryPickUp(target.Item),Is.True);
                var camera=session.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;
                camera.transform.position=target.transform.position+new Vector3(-.12f,.30f,-.52f);camera.transform.LookAt(target.PourPoint);
                if(target.Item is Sink)
                {
                    PourPose.Mouth(target,null,out var basinMouth,out _,out float basinFloor);
                    var bounds=target.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="BasinShell").bounds;
                    var drain=target.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Drain").bounds;
                    Assert.That(basinMouth.y,Is.EqualTo(bounds.max.y).Within(.0001f));Assert.That(basinFloor,Is.EqualTo(drain.max.y).Within(.0001f));
                    camera.transform.position=basinMouth+new Vector3(.12f,.30f,-.40f);camera.transform.LookAt(basinMouth);
                }
                yield return null;yield return null;
                Object.Destroy(PourCaptureTests.Capture(camera,"setup-"+test));
                var option=resolver.Resolve(target.Item,session.Hands).First(o=>o.Action.Id==action);
                double now=session.EventTime(Time.realtimeSinceStartupAsDouble);
                typeof(BarSession).GetField("pendingAngle",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,110d);
                typeof(BarSession).GetField("press",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,1d);
                session.Clock.Enqueue(now,c=>{c.Begin(option);c.SetTilt(110);c.SetGesture(0,100,1);});
                yield return new WaitUntil(()=>session.Clock.Active?.Action.Id==action);
                var verifier=session.gameObject.AddComponent<PourFrameVerifier>();verifier.Session=session;verifier.Source=source;
                float started=Time.realtimeSinceStartup;int visible=0,videoFrame=0;bool captured=false;
                while(Time.realtimeSinceStartup-started<.52f)
                {
                    yield return null;yield return new WaitForFixedUpdate();
                    Directory.CreateDirectory(Path.Combine(PourCaptureTests.Output,"video"));
                    Object.Destroy(PourCaptureTests.Capture(camera,"video/"+test+"-"+(videoFrame++).ToString("D4")));
                    var ribbon=session.GetComponents<PourRibbon>().FirstOrDefault(r=>r.Visible);
                    if(!ribbon)continue;visible++;var input=ribbon.LastInput;var points=ribbon.Centerline;
                    float mouthError=Vector3.Distance(points[0],source.PourPoint);
                    Assert.That(mouthError,Is.LessThan(.001f),action+" origin");
                    float apex=Mathf.Max(0,input.ExitVelocity.y/PourSettings.Load().gravity),previous=float.PositiveInfinity;
                    Assert.That(PourTrajectory.PlaneTime(points[0],input.ExitVelocity,input.LiquidPlaneY,PourSettings.Load().gravity,out float duration),Is.True);
                    for(int i=0;i<points.Length;i++)if(duration*i/(points.Length-1)>apex){Assert.That(points[i].y,Is.LessThanOrEqualTo(previous+.000001f));previous=points[i].y;}
                    Assert.That(PourTrajectory.PlaneTime(points[0],input.ExitVelocity,input.MouthCenter.y,PourSettings.Load().gravity,out float crossing),Is.True);
                    Vector3 cross=PourTrajectory.Point(points[0],input.ExitVelocity,crossing,PourSettings.Load().gravity)-input.MouthCenter;
                    float offset=new Vector2(cross.x,cross.z).magnitude;
                    Assert.That(offset,Is.LessThan(input.MouthRadius),action+" opening");
                    float endError=Mathf.Abs(points[points.Length-1].y-input.LiquidPlaneY);
                    Assert.That(endError,Is.LessThan(.002f),action+" impact");
                    rows.Add(new FrameRow{scenario=test,action=action,mouthErrorMetres=mouthError,mouthOffsetMetres=offset,mouthRadiusMetres=input.MouthRadius,endErrorMetres=endError});
                    if(!captured){PourCaptureTests.Capture(camera,"pour-"+test);captured=true;}
                }
                Assert.That(visible,Is.GreaterThan(0),action+" must draw confirmed liquid");
                Assert.That(verifier.VisibleFrames,Is.GreaterThan(0));Assert.That(verifier.Errors,Is.Empty,string.Join(";",verifier.Errors));
                File.WriteAllText(Path.Combine(PourCaptureTests.Output,"render-frames-"+test+".txt"),verifier.VisibleFrames.ToString());
                session.Clock.Stop(true);
            }
            File.WriteAllText(Path.Combine(PourCaptureTests.Output,"pour-frame-checks.json"),JsonUtility.ToJson(new FrameRows{rows=rows.ToArray()},true));
        }
        [UnityTest]public IEnumerator BottleInventoryGeometryAndHorizontalSurfaces()
        {
            yield return Load();session.enabled=false;var errors=new List<BottleRow>();
            foreach(var view in session.Views.Where(v=>v.Item is Bottle))
            {
                var bottle=(Bottle)view.Item;var liquid=view.GetComponent<BottleLiquidView>();Assert.That(liquid.Geometry,Is.Not.Null);
                var d=liquid.Geometry;double total=d.volumesMl.Last();bool valid=total>=bottle.CapacityMl;
                Assert.That(valid,Is.True,view.DefinitionId+" 修正后的内腔必须容得下定义容量");
                if(view.DefinitionId=="bottle.gin")
                {
                    float fullHeight=BottleGeometryMath.PlaneForMl(d,view.transform,bottle.CapacityMl)-view.transform.position.y;
                    Assert.That(fullHeight,Is.InRange(.175f,.230f),"750 ml 满瓶液位应在瓶肩到瓶颈之间");
                }
                foreach(double fraction in new[]{1,.75,.5,.25,.05})
                {
                    double ml=bottle.CapacityMl*fraction;float plane=BottleGeometryMath.PlaneForMl(d,view.transform,ml);
                    double represented=BottleGeometryMath.VolumeBelow(d,0,plane-view.transform.position.y);
                    double error=Math.Abs(represented-ml)/ml;
                    if(valid||ml<=total)Assert.That(error,Is.LessThan(.01),view.DefinitionId+" volume");
                    errors.Add(new BottleRow{id=view.DefinitionId,fraction=fraction,requestedMl=ml,representedMl=represented,relativeError=error,capacityValid=valid});
                }
                var original=view.transform.rotation;view.transform.rotation=Quaternion.Euler(0,0,60)*original;yield return null;
                var section=liquid.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f=>f.sharedMesh&&f.sharedMesh.name=="Horizontal liquid section");
                if(valid){Assert.That(section,Is.Not.Null);var points=section.sharedMesh.vertices.Select(section.transform.TransformPoint).ToArray();Assert.That(points.Length,Is.GreaterThan(2));Assert.That(points.Max(v=>v.y)-points.Min(v=>v.y),Is.LessThan(.00001f));var normal=Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized;if(normal.y<0)normal=-normal;Assert.That(Vector3.Angle(normal,Vector3.up),Is.LessThan(1));}
                view.transform.rotation=original;
                bottle.SetOpen(true);var target=(Glass)View("glass.highball").Item;
                session.Adapter.Submit(bottle.CreateLiquidRequest(target,45,100,1,0,0));yield return null;
                Assert.That(bottle.RemainingMl,Is.EqualTo(bottle.CapacityMl-45).Within(1e-8));
                float expected=liquid.Profile.HeightForMl(Math.Min(bottle.RemainingMl,total))+view.transform.position.y;
                Assert.That(liquid.Level,Is.EqualTo(expected).Within(.00002));
                var camera=session.Player.ViewCamera;camera.fieldOfView=40;camera.transform.position=view.transform.position+new Vector3(-.10f,.22f,-.34f);camera.transform.LookAt(view.transform.position+Vector3.up*view.Size.y*.48f);
                PourCaptureTests.Capture(camera,view.DefinitionId+"-after45");
                if(valid)
                {
                    var controls=ItemCatalog.LoadControls(session.ControlsJson.text);var flow=new TiltFlowModel(controls);
                    foreach(double fraction in new[]{1,.75,.5,.25,.05})
                    {
                        float angle=(float)flow.StartAngle(bottle.CapacityMl*fraction,bottle.CapacityMl,view.DefinitionId);
                        double radians=angle*Math.PI/180,plane=d.heights.Last()*Math.Cos(radians)-d.radii.Last()*Math.Sin(radians);
                        double atLip=BottleGeometryMath.VolumeBelow(d,radians,plane);
                        Assert.That(Math.Abs(atLip-bottle.CapacityMl*fraction)/bottle.CapacityMl,Is.LessThan(.01));
                    }
                }
            }
            File.WriteAllText(Path.Combine(PourCaptureTests.Output,"bottle-volume-checks.json"),JsonUtility.ToJson(new BottleRows{rows=errors.ToArray()},true));
        }
        [UnityTest]public IEnumerator ChangingTiltBetweenCoreTicksKeepsTheStreamAndBottleContinuous()
        {
            yield return Load();session.enabled=false;
            var source=View("bottle.soda");var target=View("glass.highball");
            ((Bottle)source.Item).SetOpen(true);
            Assert.That(session.Hands.TryPickUp(source.Item),Is.True);
            Assert.That(session.Hands.TryPickUp(target.Item),Is.True);
            var resolver=ActionResolver.CreateU4(ItemCatalog.LoadControls(session.ControlsJson.text),40,session.Adapter.World.Data.iceDensity,()=>.5);
            session.Clock.Begin(resolver.Resolve(target.Item,session.Hands).First(o=>o.Action.Id=="pour.bottle_to_container"));
            var presentation=new PourPresentation(session);
            var camera=session.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;
            var sourceHome=source.transform.position;var targetHome=target.transform.position;
            var sourceRotation=source.transform.rotation;var targetRotation=target.transform.rotation;
            const float step=1f/60;double time=session.Adapter.World.Time;
            float maxLipStep=0,maxFlowStep=0;var previousLip=Vector3.zero;float previousFlow=0;
            for(int i=0;i<82;i++)
            {
                // Input and presentation run at 60 Hz; the real clock still confirms at 10 Hz.
                float angle=i<22?110:100+10*Mathf.Cos((i-22)*.12f);
                session.Clock.SetTilt(angle);time+=step;session.Clock.AdvanceFrame(time);
                source.transform.SetPositionAndRotation(sourceHome,sourceRotation);
                target.transform.SetPositionAndRotation(targetHome,targetRotation);
                double beforeTime=session.Adapter.World.Time,beforeMl=((Glass)target.Item).Contents.LiquidMl;
                presentation.Tick(step,angle,0);
                Assert.That(session.Adapter.World.Time,Is.EqualTo(beforeTime));
                Assert.That(((Glass)target.Item).Contents.LiquidMl,Is.EqualTo(beforeMl),"表现不能额外转移液体");
                if(i>=22)
                {
                    Assert.That(presentation.LastInput.ConfirmedMlPerSecond,Is.GreaterThan(0),"调整倾角时不能把尚有效的确认流量清零");
                    Assert.That(presentation.Visible,Is.True,"持续倾倒过程中不能闪断");
                    maxLipStep=Mathf.Max(maxLipStep,Vector3.Distance(previousLip,source.PourPoint));
                    maxFlowStep=Mathf.Max(maxFlowStep,Mathf.Abs(previousFlow-presentation.LastInput.ConfirmedMlPerSecond));
                    Assert.That(Vector3.Distance(previousLip,source.PourPoint),Is.LessThan(.003f),"瓶口不能随确认节拍跳位");
                    Assert.That(Mathf.Abs(previousFlow-presentation.LastInput.ConfirmedMlPerSecond),Is.LessThan(8f),"流量在确认样本间应连续过渡");
                }
                previousLip=source.PourPoint;previousFlow=presentation.LastInput.ConfirmedMlPerSecond;
                yield return null;
            }
            Object.Destroy(PourCaptureTests.Capture(camera,"continuity-adjusting-tilt"));
            // Lowering below the start angle must still stop; sample holding cannot flow forever.
            session.Clock.SetTilt(0);
            for(int i=0;i<18;i++){time+=step;session.Clock.AdvanceFrame(time);presentation.Tick(step,0,0);yield return null;}
            Assert.That(presentation.Visible,Is.False,"没有后续确认时应停流");
            Assert.That(presentation.LastInput.ConfirmedMlPerSecond,Is.Zero);
            session.Clock.SetTilt(110);
            for(int i=0;i<18;i++){time+=step;session.Clock.AdvanceFrame(time);presentation.Tick(step,110,0);yield return null;}
            Assert.That(presentation.Visible,Is.True,"重新倾倒应恢复");
            presentation.ReleaseContinuous();presentation.Tick(step,110,0);
            Assert.That(presentation.Visible,Is.False,"松键立即停止显示");
            File.WriteAllText(Path.Combine(PourCaptureTests.Output,"continuity-check.json"),JsonUtility.ToJson(new ContinuityRow{adjustingFrames=60,maxLipStepMm=maxLipStep*1000,maxFlowStepMlPerSecond=maxFlowStep},true));
            session.Clock.Stop(true);
        }
        [Serializable]private sealed class ContinuityRow{public int adjustingFrames;public float maxLipStepMm,maxFlowStepMlPerSecond;}
        [Serializable]private sealed class FrameRows{public FrameRow[] rows;}
        [Serializable]private sealed class FrameRow{public int scenario;public string action;public float mouthErrorMetres,mouthOffsetMetres,mouthRadiusMetres,endErrorMetres;}
        [Serializable]private sealed class BottleRows{public BottleRow[] rows;}
        [Serializable]private sealed class BottleRow{public string id;public double fraction,requestedMl,representedMl,relativeError;public bool capacityValid;}
    }
}
