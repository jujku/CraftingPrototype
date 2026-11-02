using System;
using System.Collections;
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
    /// <summary>通过真实会话输入验证准备期零出液，以及每个正毫升回执所在呈现帧的可见水柱。</summary>
    public sealed class CloseoutSyncTests
    {
        private BarSession session;private ItemView source,target;private Controls controls;
        private double Now=>session.EventTime(Time.realtimeSinceStartupAsDouble);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            session=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            controls=ItemCatalog.LoadControls(session.ControlsJson.text);source=session.Views.First(v=>v.DefinitionId=="bottle.soda");target=session.Views.First(v=>v.DefinitionId=="glass.highball");
            ((Bottle)source.Item).SetOpen(true);Assert.That(session.Hands.TryPickUp(source.Item),Is.True);
            yield return null;
        }
        private void Choose(string id,ItemView actor,ItemView receiver)
        {
            session.Player.AimAt(receiver.transform.position+Vector3.up*receiver.Size.y*.5f);Physics.SyncTransforms();session.Targeting();
            int index=session.MenuOptions.ToList().FindIndex(o=>o.Action.Id==id&&o.Actor==actor.Item&&o.Target==receiver.Item);
            Assert.That(index,Is.GreaterThanOrEqualTo(0),"Real target menu must offer the pour action");
            session.SelectMenu(index,true);session.LeftButton(true,Now);
        }
        [UnityTest]public IEnumerator QuickPointTwoSecondTiltHasZeroLiquidAndZeroStream()
        {
            yield return Load();double before=((Bottle)source.Item).RemainingMl,start=Now;Choose("pour.bottle_to_container",source,target);
            session.PointerDelta(new Vector2(0,(float)(120/controls.TiltDegreesPerInput)),Now);
            while(Now-start<.2){yield return null;Assert.That(session.GetComponents<PourRibbon>().Any(r=>r.Visible),Is.False);}
            session.LeftButton(false,Now);yield return new WaitForSeconds(.25f);
            Assert.That(((Bottle)source.Item).RemainingMl,Is.EqualTo(before));Assert.That(((Glass)target.Item).Contents.LiquidMl,Is.Zero);
            Assert.That(session.GetComponents<PourRibbon>().Any(r=>r.Visible),Is.False);
            Directory.CreateDirectory(CloseoutIceTests.Output);File.WriteAllText(Path.Combine(CloseoutIceTests.Output,"quick-tilt.json"),"{\"holdSeconds\":0.2,\"transferredMl\":0,\"streamFrames\":0}");
        }
        [UnityTest]public IEnumerator PointFourSecondHoldShowsEveryPositiveReceiptFrameIncludingReleaseTail()
        {
            yield return Load();double start=Now,first=-1;int frames=0;OperationRequest seen=null;Choose("pour.bottle_to_container",source,target);
            session.PointerDelta(new Vector2(0,(float)(120/controls.TiltDegreesPerInput)),Now);bool released=false;
            while(Now-start<.65)
            {
                yield return null;var request=session.Adapter.LastRequest;
                if(request!=seen&&request?.ActualMl>0)
                {
                    seen=request;frames++;if(first<0)first=Now-start;
                    Assert.That(session.GetComponents<PourRibbon>().Any(r=>r.Visible),Is.True,"每个核心正毫升回执都要在本帧有可见水柱，包括松手前最后一个时间片");
                }
                if(!released&&Now-start>=.4){session.LeftButton(false,Now);released=true;}
            }
            Assert.That(frames,Is.GreaterThan(0));Assert.That(first,Is.GreaterThanOrEqualTo(.25));
            Assert.That(((Glass)target.Item).Contents.LiquidMl,Is.GreaterThan(0));
            File.WriteAllText(Path.Combine(CloseoutIceTests.Output,"hold-tilt.json"),"{\"holdSeconds\":0.4,\"positiveReceiptFrames\":"+frames+",\"firstLiquidSeconds\":"+first.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"stationSeconds\":0.25}");
        }
        [UnityTest]public IEnumerator AllOtherLiquidActionsWaitForTheStationAndCancelSafely()
        {
            foreach(string id in new[]{"pour.jigger_to_container","shaker.pour","lemon.press","sink.empty"})
            {
                yield return Load();session.Hands.TryPlace(session.Hands.SideOf(source.Item).Value,(BarCounter)session.Views.First(v=>v.Item is BarCounter).Item);
                source=session.Views.First(v=>v.DefinitionId==(id=="pour.jigger_to_container"?"jigger.dual":id=="shaker.pour"?"shaker.cobbler":id=="lemon.press"?"tool.squeezer":"glass.highball"));
                target=session.Views.First(v=>v.DefinitionId==(id=="sink.empty"?"fixture.sink":"glass.coupe"));
                Container c=source.Item is Jigger j?j.ActiveCup:source.Item is CobblerShaker sh?sh.Body:source.Item as Container;
                if(c!=null){session.Adapter.World.Apply(new Operation{type="PourSource",to=c.CoreVesselId,ingredient="spirit",ml=30});session.Adapter.Advance(0);}
                if(source.Item is CobblerShaker shaker&&!shaker.CapSlot.IsEmpty)shaker.Detach(shaker.CapSlot.Content);
                if(source.Item is Squeezer sq)sq.Load((ILoadableProduce)session.Views.First(v=>v.Item is ILoadableProduce).Item,session.Hands);
                Assert.That(session.Hands.TryPickUp(source.Item),Is.True);double start=Now;Choose(id,source,target);
                session.PointerDelta(new Vector2(0,id=="lemon.press"?-500:800),Now);
                while(Now-start<.2){yield return null;Assert.That(session.Clock.Active==null||session.Clock.Active.Action.Id!=id,Is.True,id+" cannot begin before station");}
                session.Cancel(Now);yield return new WaitForSeconds(.25f);
                Assert.That(session.Clock.Active==null||session.Clock.Active.Action.Id!=id,Is.True,id+" must not run a cancelled preparation");
                session.LeftButton(true,Now);Choose(id,source,target);session.PointerDelta(new Vector2(0,id=="lemon.press"?-500:800),Now);
                start=Now;bool positive=false;OperationRequest seen=null;
                while(Now-start<.55)
                {
                    yield return null;var request=session.Adapter.LastRequest;
                    if(Now-start<.2)Assert.That(request?.ActualMl??0,Is.Zero,id+" preparatory frame must not transfer");
                    if(request!=seen&&request?.ActualMl>0){positive=true;seen=request;Assert.That(session.GetComponents<PourRibbon>().Any(r=>r.Visible),Is.True,id+" positive receipt must be visible");}
                }
                Assert.That(positive,Is.True,id+" must complete after station");session.Cancel(Now);
            }
        }
    }
}
