using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>按实际滤盖装配选择独立出口，并验证可编辑锚点、弹道和只读站位。</summary>
    public sealed class ShakerOutletTests
    {
        private BarSession session;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../ShakerOutletReports"));
        private ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load(bool filtered)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            session=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var shaker=(CobblerShaker)View("shaker.cobbler").Item;
            if(!shaker.CapSlot.IsEmpty)shaker.Detach(shaker.CapSlot.Content);
            if(!shaker.StrainerSlot.IsEmpty)shaker.Detach(shaker.StrainerSlot.Content);
            if(filtered)shaker.Attach((ShakerStrainer)View("shaker.strainer").Item);
            yield return null;yield return null;
        }
        [UnityTest] public IEnumerator AttachedStrainerAndOpenBodyKeepTheirOwnEditablePourLip()
        {
            yield return Load(false);var source=View("shaker.cobbler");var shaker=(CobblerShaker)source.Item;
            var filter=View("shaker.strainer");var bodyLip=source.transform.Find("Body/BodyMesh/PourLip");var filterLip=filter.transform.Find("Visual/PourLip");
            Assert.That(bodyLip,Is.Not.Null);Assert.That(filterLip,Is.Not.Null);
            Assert.That(Vector3.Distance(source.PourPoint,bodyLip.position),Is.LessThan(.00001f));
            shaker.Attach((ShakerStrainer)filter.Item);yield return null;yield return null;
            Assert.That(Vector3.Distance(filterLip.position,bodyLip.position),Is.GreaterThan(.01f),"夹具必须分得出滤盖口和壶身口");
            Assert.That(Vector3.Distance(source.PourPoint,filterLip.position),Is.LessThan(.00001f),"装滤盖后必须读滤盖出口");
            filterLip.position+=new Vector3(.006f,.003f,-.004f);yield return null;
            Assert.That(Vector3.Distance(source.PourPoint,filterLip.position),Is.LessThan(.00001f),"滤盖锚点拖动后不得被固定坐标覆盖");
            shaker.Detach((ShakerStrainer)filter.Item);
            Assert.That(Vector3.Distance(source.PourPoint,bodyLip.position),Is.LessThan(.00001f),"即使旧部件显示尚未移出层级，也应立即按领域状态恢复壶身口");
            yield return null;yield return null;
            bodyLip.position+=new Vector3(-.005f,.002f,.003f);yield return null;
            Assert.That(Vector3.Distance(source.PourPoint,bodyLip.position),Is.LessThan(.00001f));
            shaker.Attach((ShakerStrainer)filter.Item);yield return null;yield return null;
            Assert.That(Vector3.Distance(source.PourPoint,filterLip.position),Is.LessThan(.00001f),"重新装回仍读取该部件的已编辑点");
        }
        [UnityTest] public IEnumerator OpenAndFilteredPourStartAtThePhysicalOutletAndEnterTheGlass()
        {
            Directory.CreateDirectory(Output);var rows=new List<string>{"filtered,frame,outletErrorMetres,mouthOffsetMetres,mouthRadiusMetres"};float widestShakerStream=0;
            for(int scenario=0;scenario<2;scenario++)
            {
                bool filtered=scenario==1;yield return Load(filtered);session.enabled=false;
                var source=View("shaker.cobbler");var shaker=(CobblerShaker)source.Item;var target=View("glass.highball");var glass=(Glass)target.Item;
                var expected=filtered?View("shaker.strainer").transform.Find("Visual/PourLip"):source.transform.Find("Body/BodyMesh/PourLip");
                session.Adapter.World.Apply(new Operation{type="PourSource",to=shaker.Body.CoreVesselId,ingredient="spirit",ml=80});session.Adapter.Advance(0);
                Assert.That(session.Hands.TryPickUp(shaker),Is.True);
                var model=session.Adapter.World.Data;var resolver=ActionResolver.CreateU4(ItemCatalog.LoadControls(session.ControlsJson.text),model.icePortionGrams,model.iceDensity,()=>0);
                var active=resolver.Resolve(glass,session.Hands).Single(o=>o.Action.Id=="shaker.pour").Begin(session.Hands);active.Update(new ActionInput(110),.1);
                foreach(var pending in active.Requests.Where(r=>r.IsPending))session.Adapter.Submit(pending);
                var receipt=active.Requests.Last();Assert.That(receipt.ActualMl,Is.GreaterThan(0));Assert.That(receipt.OperationType,Is.EqualTo(filtered?"StrainTransfer":"TransferWithIce"));
                float flow=(float)(receipt.ActualMl.Value/receipt.DurationSeconds);active.End();string hash=LogReplay.SnapshotHash(session.Adapter.World);
                var camera=session.Player.ViewCamera;camera.fieldOfView=42;camera.aspect=640f/480;
                PourPose.Mouth(target,glass,out var mouth,out _,out _);camera.transform.position=mouth+new Vector3(.2f,.18f,-.4f);camera.transform.LookAt(mouth+Vector3.up*.04f);
                var pose=new PourPose();pose.Begin(source,target,camera.transform,false);var stream=session.gameObject.AddComponent<PourRibbon>();float maximumError=0;
                for(int frame=0;frame<84;frame++)
                {
                    var input=pose.Step(1f/60,110,flow,glass);if(pose.Progress<1)continue;stream.Draw(input);
                    float error=Vector3.Distance(stream.Centerline[0],expected.position);maximumError=Mathf.Max(maximumError,error);
                    var tube=stream.transform.Find("Core flow parabola").GetComponent<MeshFilter>();
                    foreach(var vertex in tube.sharedMesh.vertices){float nearest=float.PositiveInfinity;foreach(var point in stream.Centerline)nearest=Mathf.Min(nearest,Vector3.Distance(tube.transform.TransformPoint(vertex),point));widestShakerStream=Mathf.Max(widestShakerStream,nearest);}
                    Assert.That(PourTrajectory.PlaneTime(input.Outlet.position,input.ExitVelocity,input.MouthCenter.y,PourSettings.Load().gravity,out float time),Is.True);
                    var cross=PourTrajectory.Point(input.Outlet.position,input.ExitVelocity,time,PourSettings.Load().gravity)-input.MouthCenter;float offset=new Vector2(cross.x,cross.z).magnitude;
                    Assert.That(offset,Is.LessThan(input.MouthRadius));Assert.That(stream.Centerline.Last().y,Is.EqualTo(input.LiquidPlaneY).Within(.002f));
                    rows.Add(filtered+","+frame+","+error+","+offset+","+input.MouthRadius);
                }
                session.GetComponent<HeldItemRendering>()?.Refresh();
                var image=PourCaptureTests.Capture(camera,filtered?"shaker-filtered-outlet":"shaker-open-outlet");Object.Destroy(image);
                File.Copy(Path.Combine(PourCaptureTests.Output,filtered?"shaker-filtered-outlet.png":"shaker-open-outlet.png"),Path.Combine(Output,filtered?"filtered.png":"open.png"),true);
                File.WriteAllLines(Path.Combine(Output,"frames.csv"),rows);File.WriteAllText(Path.Combine(Output,filtered?"filtered-result.txt":"open-result.txt"),"maximumOutletErrorMetres="+maximumError+"\ncoreUnchanged="+(hash==LogReplay.SnapshotHash(session.Adapter.World)));
                Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));Assert.That(maximumError,Is.LessThan(.001f),"水柱首点须在独立读取的实际出液部件锚点上");
                File.WriteAllText(Path.Combine(Output,filtered?"filtered-width.txt":"open-width.txt"),"outletRadiusMetres="+PourPose.OutletRadius(source)+"\nmaximumStreamRadiusMetres="+widestShakerStream);
                var cases=new List<string>{"angle,flow,outletRadius,maximumRadius,mouthOffset"};
                foreach(float angle in new[]{110f,125f,140f,160f})foreach(float q in new[]{3f,20f,45f})
                {
                    var input=pose.Step(1f/60,angle,q,glass);stream.Draw(input);
                    var tube=stream.transform.Find("Core flow parabola").GetComponent<MeshFilter>();float largest=0;
                    var points=stream.Centerline;var vertices=tube.sharedMesh.vertices;int sides=PourSettings.Load().streamSides;
                    for(int i=0;i<points.Length;i++)for(int j=0;j<sides;j++)largest=Mathf.Max(largest,Vector3.Distance(tube.transform.TransformPoint(vertices[i*sides+j]),points[i]));
                    widestShakerStream=Mathf.Max(widestShakerStream,largest);
                    Assert.That(PourTrajectory.PlaneTime(input.Outlet.position,input.ExitVelocity,input.MouthCenter.y,PourSettings.Load().gravity,out float time),Is.True);
                    var offset=PourTrajectory.Point(input.Outlet.position,input.ExitVelocity,time,PourSettings.Load().gravity)-input.MouthCenter;offset.y=0;
                    Assert.That(offset.magnitude,Is.LessThan(input.MouthRadius));
                    Assert.That(Vector3.Distance(points[0],expected.position),Is.LessThan(.00001f));
                    cases.Add(angle+","+q+","+PourPose.OutletRadius(source)+","+largest+","+offset.magnitude);
                }
                File.WriteAllLines(Path.Combine(Output,filtered?"filtered-matrix.csv":"open-matrix.csv"),cases);
                Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));
            }
            Assert.That(widestShakerStream,Is.LessThan(.006f),"雪克壶只从唇缘/滤盖小口形成水柱，不可把整个壶口当成满截面管流");
        }
        [UnityTest]public IEnumerator OpenAndFilteredDiscardFallIntoBasinInsteadOfShootingAcrossCounter()
        {
            Directory.CreateDirectory(Output);float maximumSideways=0,maximumRise=0;var rows=new List<string>{"filtered,angle,flow,horizontalSpeed,lateralTravel,upwardTravel"};
            for(int variant=0;variant<2;variant++)
            {
                bool filtered=variant==1;yield return Load(filtered);session.enabled=false;
                var source=View("shaker.cobbler");var shaker=(CobblerShaker)source.Item;var target=View("fixture.sink");
                session.Adapter.World.Apply(new Operation{type="PourSource",to=shaker.Body.CoreVesselId,ingredient="spirit",ml=150});session.Adapter.Advance(0);
                Assert.That(session.Hands.TryPickUp(shaker),Is.True);var data=session.Adapter.World.Data;
                var resolver=ActionResolver.CreateU4(ItemCatalog.LoadControls(session.ControlsJson.text),data.icePortionGrams,data.iceDensity,()=>0);
                var active=resolver.Resolve(target.Item,session.Hands).Single(o=>o.Action.Id=="sink.empty"&&o.Actor==shaker).Begin(session.Hands);
                foreach(var request in active.Requests.Where(r=>r.IsPending))session.Adapter.Submit(request);
                active.Update(new ActionInput(),.1);foreach(var request in active.Requests.Where(r=>r.IsPending))session.Adapter.Submit(request);
                var receipt=active.Requests.Last(r=>r.ActualMl.HasValue&&r.ActualMl>0);
                float actualFlow=(float)(receipt.ActualMl.Value/System.Math.Max(receipt.DurationSeconds,ItemCatalog.LoadControls(session.ControlsJson.text).JiggerPourSeconds));active.End();
                var camera=session.Player.ViewCamera;PourPose.Mouth(target,null,out var mouth,out _,out _);
                camera.fieldOfView=50;camera.aspect=640f/480;camera.transform.position=mouth+new Vector3(.4f,.47f,-.52f);camera.transform.LookAt(mouth);
                var pose=new PourPose();pose.Begin(source,target,camera.transform,false);var stream=session.gameObject.AddComponent<PourRibbon>();string hash=LogReplay.SnapshotHash(session.Adapter.World);
                for(int i=0;i<60;i++)stream.Draw(pose.Step(1f/60,125,actualFlow));
                session.GetComponent<HeldItemRendering>()?.Refresh();var label=filtered?"sink-filtered":"sink-open";
                var image=PourCaptureTests.Capture(camera,label);Object.Destroy(image);File.Copy(Path.Combine(PourCaptureTests.Output,label+".png"),Path.Combine(Output,label+".png"),true);
                foreach(float angle in new[]{80f,110f,125f,160f})foreach(float q in new[]{3f,20f,45f,150f,450f,actualFlow})
                {
                    var input=pose.Step(1f/60,angle,q);stream.Draw(input);float side=0,rise=0;
                    foreach(var point in stream.Centerline){var delta=point-input.Outlet.position;side=Mathf.Max(side,new Vector2(delta.x,delta.z).magnitude);rise=Mathf.Max(rise,delta.y);}
                    maximumSideways=Mathf.Max(maximumSideways,side);maximumRise=Mathf.Max(maximumRise,rise);
                    rows.Add(filtered+","+angle+","+q+","+new Vector2(input.ExitVelocity.x,input.ExitVelocity.z).magnitude+","+side+","+rise);
                    Assert.That(stream.Centerline[0],Is.EqualTo(input.Outlet.position));
                    Assert.That(stream.Centerline.Last().y,Is.EqualTo(input.LiquidPlaneY).Within(.002f));
                }
                Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));
            }
            File.WriteAllLines(Path.Combine(Output,"sink-gravity-matrix.csv"),rows);
            File.WriteAllText(Path.Combine(Output,"sink-gravity-summary.txt"),"maximumLateralTravelMetres="+maximumSideways+"\nmaximumUpwardTravelMetres="+maximumRise);
            Assert.That(maximumSideways,Is.LessThan(.02f),"雪克壶应就近下落，不应横跨台面喷射");
            Assert.That(maximumRise,Is.LessThan(.0001f),"出口后的水柱不能先朝上喷再落下");
        }
    }
}
