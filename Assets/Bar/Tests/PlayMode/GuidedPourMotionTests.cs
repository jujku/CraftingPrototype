using System;
using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed class GuidedPourMotionTests
    {
        private BarSession s;private string output;
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
        private void Choose(string action,ItemView target)
        {
            CapSteps.Aim(s,target);var options=s.MenuOptions.ToArray();int i=Array.FindIndex(options,x=>x.Action.Id==action);Assert.That(i,Is.GreaterThanOrEqualTo(0),action);
            if(action=="pour.barspoon_guided")Assert.That(options[0].Action.Id,Is.EqualTo("pour.bottle_to_container"));s.SelectMenu(i,true);s.LeftButton(true,Now());
        }
        private IEnumerator Pick(ItemView view){CapSteps.Aim(s,view);s.RightButton(Now());yield return new WaitForSecondsRealtime(.35f);Assert.That(s.Hands.SideOf(view.Item).HasValue,Is.True);}
        private void Capture(string name){Object.Destroy(PourCaptureTests.Capture(s.Player.ViewCamera,"guided-"+name));File.Copy(Path.Combine(PourCaptureTests.Output,"guided-"+name+".png"),Path.Combine(output,name+".png"),true);}
        [UnityTest]public IEnumerator GuidedEntryFollowFlowReleaseAndRecordedServe()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return Resources.UnloadUnusedAssets();GC.Collect();yield return null;
            s=Object.FindFirstObjectByType<BarSession>();foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"../GuidedPourReports"));Directory.CreateDirectory(output);
            var bottle=View("bottle.tonic");var spoon=View("tool.spoon");var glass=View("glass.highball");
            var camera=s.Player.ViewCamera;var radial=Vector3.ProjectOnPlane(camera.transform.position-glass.transform.position,Vector3.up).normalized;s.Player.transform.position+=glass.transform.position+Vector3.up*.55f+radial*.5f-camera.transform.position;camera.fieldOfView=50;
            yield return CapSteps.Uncap(s,bottle);yield return Pick(bottle);yield return Pick(spoon);
            Capture("setup"); // Warm the capture pass before measuring the entry motion.
            // Early release is a normal input path: no receipt and no source consumption.
            double stock=((Bottle)bottle.Item).RemainingMl;Choose("pour.barspoon_guided",glass);yield return new WaitForSecondsRealtime(.1f);s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.65f);
            Assert.That(((Bottle)bottle.Item).RemainingMl,Is.EqualTo(stock));Assert.That(s.Hands.IsBusy,Is.False);
            Choose("pour.barspoon_guided",glass);yield return new WaitForSecondsRealtime(1f);Capture("upright-ready");
            Assert.That(((Bottle)bottle.Item).RemainingMl,Is.EqualTo(stock));s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.65f);
            Choose("pour.barspoon_guided",glass);s.PointerDelta(new Vector2(0,670),Now()+.001);float start=Time.realtimeSinceStartup;
            float fov=s.Player.ViewCamera.fieldOfView;var cameraPosition=s.Player.ViewCamera.transform.position;var positions=new System.Collections.Generic.List<string>();
            var tip=spoon.Anchor("Tip");var previous=tip.position;int shot=0;bool flowed=false;
            while(Time.realtimeSinceStartup-start<2.2f)
            {
                yield return null;float t=Time.realtimeSinceStartup-start;var display=s.PourDisplay.Presentation;
                positions.Add(t.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+Vector3.Distance(previous,tip.position).ToString("R",System.Globalization.CultureInfo.InvariantCulture));previous=tip.position;
                if(shot<7&&t>shot*.15f){Capture("motion-"+shot.ToString("D2"));shot++;}
                Assert.That(s.Clock.Error,Is.Null);
                if(display.Visible){flowed=true;var input=display.LastInput;Assert.That(input.Guided,Is.True);Assert.That(input.GuideContact.y,Is.GreaterThan(input.MouthCenter.y));Assert.That(input.GuideEnd.y,Is.GreaterThanOrEqualTo(input.LiquidPlaneY-.00001f));Assert.That(Vector3.Distance(input.GuideEnd,input.GuideContact),Is.GreaterThan(.05f));Assert.That(s.Hands.IsBusy,Is.True);}
                if(t<.25f)Assert.That(((Bottle)bottle.Item).RemainingMl,Is.EqualTo(stock),"入勺准备期不能先扣原料");
            }
            Assert.That(flowed,Is.True,"引流准备完成后应有已确认水流");Assert.That(((Bottle)bottle.Item).RemainingMl,Is.LessThan(stock));Assert.That(((Glass)glass.Item).Contents.LiquidMl,Is.GreaterThan(10));
            Capture("guided-flow");
            s.LeftButton(false,Now());yield return null;Capture("release-00");yield return new WaitForSecondsRealtime(.25f);Assert.That(s.PourDisplay.Presentation.Visible,Is.False);Capture("release-01");yield return new WaitForSecondsRealtime(.4f);Capture("release-02");
            Assert.That(s.Hands.IsBusy,Is.False);Assert.That(s.Player.ViewCamera.fieldOfView,Is.EqualTo(fov));Assert.That(s.Player.ViewCamera.transform.position,Is.EqualTo(cameraPosition));
            // Repeating the same completed action on the same target must create a new preparation.
            double repeatStock=((Bottle)bottle.Item).RemainingMl;Choose("pour.barspoon_guided",glass);s.PointerDelta(new Vector2(0,670),Now()+.001);yield return new WaitForSecondsRealtime(1.1f);Assert.That(((Bottle)bottle.Item).RemainingMl,Is.LessThan(repeatStock),"不能把上一轮已结束激活接到新准备上");s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.65f);
            Assert.That(s.Hands.SetDominant(s.Hands.Dominant==Bar.Data.HandSide.Right?Bar.Data.HandSide.Left:Bar.Data.HandSide.Right));
            yield return new WaitForSecondsRealtime(.4f);Choose("pour.barspoon_guided",glass);s.PointerDelta(new Vector2(0,670),Now()+.001);
            yield return new WaitForSecondsRealtime(1.2f);Capture("mirrored-guided-flow");s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.65f);
            // Add a real direct receipt to the same drink, exercising final Mixed attribution.
            Choose("pour.bottle_to_container",glass);s.PointerDelta(new Vector2(0,670),Now()+.001);yield return new WaitForSecondsRealtime(.8f);Capture("direct-flow");s.LeftButton(false,Now());yield return new WaitForSecondsRealtime(.65f);Capture("direct");
            yield return CapSteps.PutDown(s,spoon);yield return CapSteps.PutDown(s,bottle);yield return Pick(glass);Choose("glass.serve",View("fixture.servemat"));s.LeftButton(false,Now()+.001);yield return new WaitForSecondsRealtime(.3f);
            Assert.That(((Glass)glass.Item).IsServed,Is.True);var lines=s.Journal.ReadLines();File.WriteAllLines(Path.Combine(output,"runtime-guided.jsonl"),lines);var replay=new LogReplay();replay.Run(lines);Assert.That(replay.ServedCount,Is.EqualTo(1));
            var rows=lines.Select(SessionJournal.Decode).ToArray();var served=rows.Single(r=>r.kind=="core_operation"&&r.operation.type=="Serve");Assert.That(served.served.carbonatedPourMethod,Is.EqualTo("Mixed"));Assert.That(served.attemptDirectPourVolumeMl,Is.GreaterThan(0));Assert.That(served.attemptGuidedPourVolumeMl,Is.GreaterThan(0));Assert.That(rows.Any(r=>r.kind=="action_selected"&&r.actionId=="pour.barspoon_guided"&&r.defaultActionId=="pour.bottle_to_container"&&r.selectedMenuIndex>0),Is.True);
            Assert.That(s.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(s.Adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));File.WriteAllLines(Path.Combine(output,"motion-step-metres.csv"),new[]{"seconds,tipStepMetres"}.Concat(positions));
        }
    }
}
