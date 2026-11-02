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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bar.Tests
{
    internal sealed class GrayboxSmokeTests
    {
        private static double Now(BarSession session)=>session.EventTime(Time.realtimeSinceStartupAsDouble);
        private static ItemView View(BarSession s,string id)=>s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();
        private static void Aim(BarSession session,ItemView view){session.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();session.Targeting();Assert.That(session.Target,Is.SameAs(view),"真实中心射线应命中 "+view.DefinitionId);}
        private static void Click(BarSession s){s.LeftButton(true,Now(s));s.LeftButton(false,Now(s)+.001);}
        private static IEnumerator Uncap(BarSession s,ItemView bottle)=>CapSteps.Uncap(s,bottle);
        private static void Capture(BarSession s,string name)
        {
            string folder=Path.Combine(Application.dataPath,"../U3Reports");Directory.CreateDirectory(folder);
            var texture=RenderTexture.GetTemporary(960,600,24);var request=new UniversalRenderPipeline.SingleCameraRequest{destination=texture};RenderPipeline.SubmitRenderRequest(s.Player.ViewCamera,request);
            var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(960,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(texture);
        }
        [UnityTest] public IEnumerator InputSystemRoutesMouseF3TabAndEscapeToTheScene()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();
            var oldBackground=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            #if UNITY_EDITOR
            var oldEditorRouting=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            #endif
            var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();
            var input=s.GetComponent<Bar.Unity.Input.InputRouter>();var map=(InputActionMap)typeof(Bar.Unity.Input.InputRouter).GetField("map",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input);
            map.devices=new InputDevice[]{mouse,keyboard};var trace=new List<string>();map.FindAction("Left").started+=ctx=>trace.Add("left "+ctx.time+" scene "+s.EventTime(ctx.time)+" now "+Now(s)+" target "+s.Target?.DefinitionId);
            try
            {
                Aim(s,View(s,"bottle.gin"));Assert.That(s.MenuOpen,Is.True);Assert.That(s.LookLocked,Is.False);InputSystem.QueueStateEvent(mouse,new MouseState{buttons=2});InputSystem.Update();yield return new WaitForSecondsRealtime(.25f);
                Directory.CreateDirectory(Path.Combine(Application.dataPath,"../U3Reports"));File.WriteAllLines(Path.Combine(Application.dataPath,"../U3Reports/input-trace.txt"),trace.Concat(new[]{"modelTime "+s.Adapter.World.Time,"target "+s.Target?.DefinitionId,"left "+s.Hands.Left?.Definition.Id,"right "+s.Hands.Right?.Definition.Id,"error "+s.Clock.Error}));
                Assert.That(s.Hands.Right,Is.TypeOf<Bottle>());InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();yield return null;yield return Uncap(s,View(s,"bottle.gin"));
                Aim(s,View(s,"jigger.dual"));InputSystem.QueueStateEvent(mouse,new MouseState{buttons=2});InputSystem.Update();yield return new WaitForSecondsRealtime(.25f);
                Assert.That(s.Hands.Left,Is.TypeOf<Jigger>());InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
                Aim(s,View(s,"shaker.cobbler"));Assert.That(s.MenuOptions.Count,Is.GreaterThanOrEqualTo(2));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit2));InputSystem.Update();InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
                InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});InputSystem.Update();
                InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1,delta=new Vector2(0,700)});InputSystem.Update();yield return new WaitForSecondsRealtime(.55f);
                Assert.That(((Jigger)s.Hands.Left).CupB.Contents.LiquidMl,Is.GreaterThan(0),"数字 2 选择双手倒入并通过真实鼠标输入启动");
                Assert.That(((CobblerShaker)View(s,"shaker.cobbler").Item).Body.Contents.LiquidMl,Is.Zero,"选中的应是手中量酒器，不能误倒进准星下壶身");
                InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();yield return new WaitForSecondsRealtime(.25f);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F3));InputSystem.Update();yield return null;Assert.That(s.DebugVisible,Is.True);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Tab));InputSystem.Update();yield return new WaitForSecondsRealtime(.2f);Assert.That(s.Hands.MainHand,Is.EqualTo(HandSide.Right));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));InputSystem.Update();yield return new WaitForSecondsRealtime(.2f);Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=oldBackground;
                #if UNITY_EDITOR
                InputSystem.settings.editorInputBehaviorInPlayMode=oldEditorRouting;
                #endif
            }
        }
        [UnityTest] public IEnumerator CompleteGreyboxWalkthroughUsesRayTargetsAndTheRealCore()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();Assert.That(session,Is.Not.Null);Assert.That(session.DebugVisible,Is.False);Assert.That(session.Adapter.World.Vessels.Count,Is.EqualTo(5));
            Capture(session,"01-overview");
            yield return Uncap(session,View(session,"bottle.gin"));
            var jigger=View(session,"jigger.dual");Aim(session,jigger);session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.25f);Assert.That(session.Hands.Left,Is.SameAs(jigger.Item));
            var gin=View(session,"bottle.gin");Aim(session,gin);session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.25f);Assert.That(session.Hands.Right,Is.SameAs(gin.Item));yield return Uncap(session,gin);
            session.Player.AimAt(CapSteps.TablePoint(session,.72f,-.25f));Physics.SyncTransforms();session.Targeting();Assert.That(session.MenuOptions.Any(o=>o.Target==jigger.Item),Is.True);double pourStart = Now(session);session.LeftButton(true,pourStart);session.PointerDelta(new Vector2(0,700),pourStart+.001);var activeCup=((Jigger)jigger.Item).CupB;float deadline=Time.realtimeSinceStartup+1;
            while(activeCup.Contents.LiquidMl==0&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(activeCup.Contents.LiquidMl,Is.GreaterThan(0));
            var inputControls=Bar.Data.ItemCatalog.LoadControls(session.ControlsJson.text);
            double pourStop=session.Adapter.World.Time+(45-activeCup.Contents.LiquidMl)/inputControls.MaxFlowMlPerSecond;
            session.LeftButton(false,pourStop);yield return new WaitForSecondsRealtime((float)System.Math.Max(0,pourStop-Now(session))+.3f);
            Assert.That(((Jigger)jigger.Item).CupB.Contents.LiquidMl,Is.EqualTo(45).Within(1e-8));Assert.That(((Bottle)gin.Item).RemainingMl,Is.EqualTo(705).Within(1e-8));Assert.That(Quaternion.Angle(gin.transform.localRotation,Quaternion.identity),Is.LessThan(.001f),"松开后酒瓶应回正");Capture(session,"02-held-jigger-filled");
            // Right click on the worktop puts down the main hand directly, even with both hands full.
            session.Player.AimAt(CapSteps.TablePoint(session,.72f,-.25f));Physics.SyncTransforms();session.Targeting();Assert.That(session.Target.Item,Is.TypeOf<BarCounter>());
            session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.3f);Assert.That(session.Hands.Right,Is.Null);
            var shaker=View(session,"shaker.cobbler");Aim(session,shaker);Click(session);yield return new WaitForSecondsRealtime(1.05f);
            Assert.That(((CobblerShaker)shaker.Item).Body.Contents.LiquidMl,Is.EqualTo(45).Within(1e-8));Assert.That(((Jigger)jigger.Item).CupB.Contents.LiquidMl,Is.Zero);
            // Put down the jigger, then attach the two surface parts using primary actions.
            session.Player.AimAt(CapSteps.TablePoint(session,.40f,-.23f));Physics.SyncTransforms();session.Targeting();session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.3f);Assert.That(session.Hands.Left,Is.Null);
            var strainer=View(session,"shaker.strainer");Aim(session,strainer);session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.25f);Aim(session,shaker);Click(session);yield return new WaitForSecondsRealtime(.25f);
            var cap=View(session,"shaker.cap");Aim(session,cap);session.RightButton(Now(session));yield return new WaitForSecondsRealtime(.25f);Aim(session,shaker);Click(session);yield return new WaitForSecondsRealtime(.25f);
            Assert.That(((CobblerShaker)shaker.Item).State,Is.EqualTo(ShakerState.Sealed));Assert.That(session.MenuOpen,Is.True);session.SelectMenu(0,true);Click(session);yield return new WaitForSecondsRealtime(.25f);
            Assert.That(((CobblerShaker)shaker.Item).State,Is.EqualTo(ShakerState.StrainerOn));Assert.That(session.Hands.Right??session.Hands.Left,Is.TypeOf<ShakerCap>());
            session.ToggleDebug();Assert.That(session.DebugVisible,Is.True);Capture(session,"03-finished");
            Assert.That(session.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(session.Adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));
        }

        [UnityTest] public IEnumerator HandsStayFixedToCameraAcrossYawPitchAndWalking()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var gin=View(s,"bottle.gin");Aim(s,gin);s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.25f);
            yield return Uncap(s,gin);yield return new WaitForSecondsRealtime(1f);
            var left=s.Player.LeftAnchor;var right=s.Player.RightAnchor;var camera=s.Player.ViewCamera;
            Assert.That(left.parent,Is.SameAs(camera.transform));Assert.That(right.parent,Is.SameAs(camera.transform));
            var lp=left.localPosition;var rp=right.localPosition;var before=camera.WorldToViewportPoint(gin.transform.position+gin.transform.up*gin.Size.y*.5f);
            var lr=left.localRotation;var rr=right.localRotation;
            s.Player.Look(new Vector2(1200,450));s.Player.Move(new Vector2(1,1),.2f);yield return null;
            // 2026-10-28 起手锚点由 HoldViewRig 摆放：转头和走路时手有一点滞后和起伏（不超过配置上限），停下后回到原来的屏幕位置。
            var rig=s.GetComponent<HoldViewRig>();Assert.That(rig,Is.Not.Null);Assert.That(rig.Sway.magnitude,Is.LessThanOrEqualTo(HoldViewSettings.Load().sway.maxDegrees*1.5f+1e-3f));
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(left.parent,Is.SameAs(camera.transform));Assert.That(right.parent,Is.SameAs(camera.transform));
            Assert.That(Vector3.Distance(left.localPosition,lp),Is.LessThan(1e-3));Assert.That(Vector3.Distance(right.localPosition,rp),Is.LessThan(1e-3));
            Assert.That(Quaternion.Angle(left.localRotation,lr),Is.LessThan(.2));Assert.That(Quaternion.Angle(right.localRotation,rr),Is.LessThan(.2));
            var after=camera.WorldToViewportPoint(gin.transform.position+gin.transform.up*gin.Size.y*.5f);
            Assert.That(Vector3.Distance(before,after),Is.LessThan(2e-3),"停下后持物在屏幕中的位置应回到原处");
        }
        [UnityTest] public IEnumerator HoverMenuTracksTargetWithoutLockingAndRightClickOwnsPickupAndPlacement()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var gin=View(s,"bottle.gin");var jigger=View(s,"jigger.dual");
            Aim(s,gin);Assert.That(s.MenuOpen,Is.True);Assert.That(s.LookLocked,Is.False);Assert.That(s.MenuOptions.Single().Action.Id,Is.EqualTo("bottle.open"));
            Click(s);yield return new WaitForSecondsRealtime(.2f);Assert.That(s.Hands.Right,Is.SameAs(((Bottle)gin.Item).Cap),"左键只取盖：瓶盖进右手，瓶子留在台面");Assert.That(s.Hands.SideOf(gin.Item),Is.Null);Assert.That(((Bottle)gin.Item).IsOpen,Is.True);
            yield return CapSteps.PutDown(s,CapSteps.CapOf(s,gin));
            Aim(s,gin);s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.25f);Assert.That(s.Hands.Right,Is.SameAs(gin.Item));yield return Uncap(s,gin);
            Aim(s,jigger);Assert.That(s.MenuOptions.Any(o=>o.Action.Id=="pour.bottle_to_container"),Is.True);
            Assert.That(s.MenuOptions.All(o=>o.Action.Id!="item.pick_up"&&o.Action.Id!="item.place"),Is.True);
            var rotation=s.Player.ViewCamera.transform.rotation;s.PointerDelta(new Vector2(24,0),Now(s));Assert.That(Quaternion.Angle(rotation,s.Player.ViewCamera.transform.rotation),Is.GreaterThan(.5));Assert.That(s.LookLocked,Is.False);
            Aim(s,View(s,"shaker.cap"));Assert.That(s.MenuOptions.Any(o=>o.Target==jigger.Item),Is.False,"悬停换目标后不能留下旧倒酒项");
            s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.25f);Assert.That(s.Hands.MainHand,Is.EqualTo(HandSide.Left));
            s.Player.AimAt(CapSteps.TablePoint(s,.72f,-.25f));Physics.SyncTransforms();s.Targeting();s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.3f);
            Assert.That(s.Hands.Left,Is.Null,"两手满时直接放下主手");Assert.That(s.Hands.Right,Is.SameAs(gin.Item));yield return Uncap(s,gin);
            Assert.That(s.MenuOptions.All(o=>o.Action.Id!="item.pick_up"&&o.Action.Id!="item.place"),Is.True);
        }
        [UnityTest] public IEnumerator HeldPourEntryShowsPoseAndReleaseOrEscapeRestoresView()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var jigger=View(s,"jigger.dual");var gin=View(s,"bottle.gin");
            yield return Uncap(s,gin);
            Aim(s,jigger);s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.25f);
            Aim(s,gin);s.RightButton(Now(s));yield return new WaitForSecondsRealtime(.25f);
            yield return Uncap(s,gin);
            s.Player.AimAt(CapSteps.TablePoint(s,.72f,-.25f));Physics.SyncTransforms();s.Targeting();
            Assert.That(s.MenuOptions.Any(o=>o.Target==jigger.Item&&o.Actor==gin.Item),Is.True,"相机固定双手后仍应有双手倒入入口");
            var camera=s.Player.LookRotation;s.LeftButton(true,Now(s));Assert.That(s.PourMode,Is.True);Assert.That(s.LookLocked,Is.True);
            // Use the configured full-flow angle: a fixed 420 input units now gives
            // 75.6 degrees, correctly producing droplets with the repaired Gin geometry.
            var inputControls=ItemCatalog.LoadControls(s.ControlsJson.text);
            s.PointerDelta(new Vector2(0,(float)(inputControls.FullFlowDegrees/inputControls.TiltDegreesPerInput)),Now(s)+.001);yield return new WaitForSecondsRealtime(.55f);
            Assert.That(((Jigger)jigger.Item).CupB.Contents.LiquidMl,Is.GreaterThan(0));Assert.That(Quaternion.Angle(gin.transform.localRotation,Quaternion.identity),Is.GreaterThan(20));
            Assert.That(Quaternion.Angle(camera,s.Player.LookRotation),Is.LessThan(.001),"倒酒时鼠标控制倾角，玩家视角不动（观察镜头的低头不算，见 Tools/HoldView/说明.md）");
            Assert.That(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(l=>l.name=="Core flow parabola"&&l.enabled),Is.True,"实际回执应驱动可见液柱");Capture(s,"04-pouring");
            s.LeftButton(false,Now(s));float releaseAt=Time.realtimeSinceStartup;
            // Stop is timestamped immediately but consumed on the next model tick; the
            // configured visual return then needs its own 0.2 s. Do not assume tick phase.
            float deadline=(float)s.Adapter.World.Data.step+PourSettings.Load().returnSeconds+.05f;
            var returnRows=new List<string>{"seconds,angle,modelTime,actionEnded"};
            do
            {
                yield return null;returnRows.Add((Time.realtimeSinceStartup-releaseAt)+","+Quaternion.Angle(gin.transform.localRotation,Quaternion.identity)+","+s.Adapter.World.Time+","+(s.Clock.Active==null||s.Clock.Active.IsEnded));
            }while((s.PourMode||Quaternion.Angle(gin.transform.localRotation,Quaternion.identity)>=.001f)&&Time.realtimeSinceStartup-releaseAt<deadline);
            var returnFolder=Path.GetFullPath(Path.Combine(Application.dataPath,"../LiquidAudioFixReports"));Directory.CreateDirectory(returnFolder);File.WriteAllLines(Path.Combine(returnFolder,"return-timing.csv"),returnRows);
            Assert.That(Time.realtimeSinceStartup-releaseAt,Is.LessThanOrEqualTo(deadline+.02f));double volume=((Jigger)jigger.Item).CupB.Contents.LiquidMl;
            Assert.That(s.PourMode,Is.False);Assert.That(s.LookLocked,Is.False);Assert.That(Quaternion.Angle(gin.transform.localRotation,Quaternion.identity),Is.LessThan(.001));
            yield return new WaitForSecondsRealtime(.2f);Assert.That(((Jigger)jigger.Item).CupB.Contents.LiquidMl,Is.EqualTo(volume).Within(1e-9),"松开后不能继续倒酒");
            s.Targeting();s.LeftButton(true,Now(s));s.PointerDelta(new Vector2(0,420),Now(s)+.001);yield return new WaitForSecondsRealtime(.15f);
            s.Cancel(Now(s));yield return new WaitForSecondsRealtime(.25f);Assert.That(s.Hands.IsBusy,Is.False);Assert.That(s.MenuOpen,Is.False);Assert.That(s.PourMode,Is.False);
            camera=s.Player.LookRotation;s.PointerDelta(new Vector2(100,100),Now(s));Assert.That(Quaternion.Angle(camera,s.Player.LookRotation),Is.LessThan(.001),"Esc 后暂停输入");
            s.LeftButton(true,Now(s));yield return new WaitForSecondsRealtime(.15f);Assert.That(s.PourMode,Is.False,"重新聚焦的点击不能启动倒酒");
            s.PointerDelta(new Vector2(20,0),Now(s));Assert.That(Quaternion.Angle(camera,s.Player.LookRotation),Is.GreaterThan(.5));
            Assert.That(s.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(s.Adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));
        }
    }
}
