using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>固定 30/60/144 Hz 比较实际工具握点：取消不能清零来时速度；保存原生第一人称装冰帧。</summary>
    public sealed class IceDeliveryFluencyTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../IceDeliveryFluencyReports"));
        [UnityTest]public IEnumerator CancellationRetainsLinearAndAngularMomentumAtThreeFrameRates()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var receiver=s.Views.First(v=>v.DefinitionId=="glass.highball");var vessel=((ILiquidReceiver)receiver.Item).ReceivingContainer;
            var metrics=new List<string>{"tool,fps,incomingSpeed,outgoingProjection,speedRatio,incomingAngularSpeed,outgoingAngularSpeed"};
            var rows=new List<string>{"tool,fps,time,cancelled,x,y,z,gripSpeed"};float worst=1;Directory.CreateDirectory(Output);
            foreach(string id in new[]{"tool.scoop","tool.tongs"})
            {
                var source=s.Views.First(v=>v.DefinitionId==id);Assert.That(s.Hands.TryPickUp(source.Item),Is.True);
                new TakeIceAction(()=>0).Begin(source.Item,s.Views.First(v=>v.Item is IceBin).Item,s.Hands);yield return new WaitForSecondsRealtime(.5f);
                var home=source.transform.position;var rotation=source.transform.rotation;var grip=source.Anchor(s.Hands.SideOf(source.Item)==HandSide.Left?"Grip_L":"Grip_R");
                s.enabled=false;
                foreach(int fps in new[]{30,60,144})
                {
                    source.transform.SetPositionAndRotation(home,rotation);var motion=new IceDeliveryMotion();Assert.That(motion.Begin(source,receiver,vessel,s.Player.ViewCamera.transform),Is.True);
                    float dt=1f/fps,t=0;var last=grip.position;var lastRotation=source.transform.rotation;Vector3 velocity=Vector3.zero;float omega=0;
                    float cancelAt=IceDeliveryMotion.Settings.Load().reachSeconds*.45f;
                    while(t<cancelAt)
                    {
                        source.transform.SetPositionAndRotation(home,rotation);motion.Advance(dt);t+=dt;
                        velocity=(grip.position-last)/dt;omega=Quaternion.Angle(lastRotation,source.transform.rotation)/dt;last=grip.position;lastRotation=source.transform.rotation;
                        rows.Add($"{id},{fps},{t:R},false,{last.x:R},{last.y:R},{last.z:R},{velocity.magnitude:R}");
                    }
                    Assert.That(velocity.magnitude,Is.GreaterThan(.05f));motion.Cancel();source.transform.SetPositionAndRotation(home,rotation);motion.Advance(dt);
                    var outgoing=(grip.position-last)/dt;float outgoingOmega=Quaternion.Angle(lastRotation,source.transform.rotation)/dt;
                    float projection=Vector3.Dot(outgoing,velocity.normalized),ratio=projection/velocity.magnitude;worst=Mathf.Min(worst,ratio);
                    metrics.Add($"{id},{fps},{velocity.magnitude:R},{projection:R},{ratio:R},{omega:R},{outgoingOmega:R}");
                    while(motion.Busy&&t<4){source.transform.SetPositionAndRotation(home,rotation);motion.Advance(dt);t+=dt;var point=grip.position;rows.Add($"{id},{fps},{t:R},true,{point.x:R},{point.y:R},{point.z:R},{Vector3.Distance(last,point)/dt:R}");last=point;}
                    Assert.That(motion.Busy,Is.False);Assert.That(Vector3.Distance(source.transform.position,home),Is.LessThan(.0001f));Assert.That(Quaternion.Angle(source.transform.rotation,rotation),Is.LessThan(.05f));
                }
                Assert.That(s.Hands.TryPlace(s.Hands.SideOf(source.Item).Value,(BarCounter)s.Views.First(v=>v.Item is BarCounter).Item),Is.True);s.enabled=true;yield return null;
            }
            File.WriteAllLines(Path.Combine(Output,"cancel-momentum.csv"),metrics);File.WriteAllLines(Path.Combine(Output,"cancel-trajectories.csv"),rows);
            Assert.That(worst,Is.GreaterThan(.55f),"取消首帧应顺着来时速度让开，不能把速度清零后重新回手");
        }
        [UnityTest]public IEnumerator CaptureBothToolsThroughRealMenuInput()
        {
            foreach(string id in new[]{"tool.scoop","tool.tongs"})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
                var s=Object.FindFirstObjectByType<BarSession>();
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
                var source=s.Views.First(v=>v.DefinitionId==id);var target=s.Views.First(v=>v.DefinitionId=="glass.highball");
                // Walk closer through the normal capsule collision, before picking up;
                // keep the player's normal camera/FOV for both recordings.
                s.Player.AimAt(target.GetComponent<Collider>().bounds.center);s.Player.Move(Vector2.up,.45f);
                s.Hands.SetDominant(id=="tool.scoop"?HandSide.Right:HandSide.Left);Assert.That(s.Hands.TryPickUp(source.Item),Is.True);
                new TakeIceAction(()=>0).Begin(source.Item,s.Views.First(v=>v.Item is IceBin).Item,s.Hands);yield return new WaitForSecondsRealtime(.6f);
                s.Player.AimAt(target.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();s.Targeting();
                int index=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="ice.add"&&o.Actor==source.Item&&o.Target==target.Item);Assert.That(index,Is.GreaterThanOrEqualTo(0));s.SelectMenu(index,true);
                var recorder=s.gameObject.AddComponent<IceDeliveryFrameRecorder>();recorder.Prepare(s.Player.ViewCamera,source,Path.Combine(Output,id));yield return null;yield return null;recorder.StartRecording();
                double now=s.EventTime(Time.realtimeSinceStartupAsDouble);s.LeftButton(true,now);s.LeftButton(false,now+.001);
                float deadline=Time.realtimeSinceStartup+6;while(s.IceDeliveryBusy&&Time.realtimeSinceStartup<deadline)yield return null;yield return null;
                recorder.Finish();Assert.That(s.IceDeliveryBusy,Is.False);Assert.That(s.Clock.Error,Is.Null);Assert.That(recorder.Count,Is.GreaterThan(10));Assert.That(((Tool)source.Item).LoadedCubes,Is.Zero);
                s.Cancel(s.EventTime(Time.realtimeSinceStartupAsDouble));
            }
        }
    }
}
