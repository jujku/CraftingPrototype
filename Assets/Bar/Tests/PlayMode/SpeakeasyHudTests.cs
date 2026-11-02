using System.Collections;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    public sealed class SpeakeasyHudTests
    {
        [UnityTest]public IEnumerator CleanMenuKeepsScrollBindingsPriorityAndLiveJiggerState()
        {
            PlayerPrefs.DeleteKey("Bar.Handedness");
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;
            ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
            var bottle=(Bottle)View("bottle.gin").Item;bottle.SetOpen(true);var jigger=(Jigger)View("jigger.dual").Item;
            Assert.That(s.Hands.TryPickUp(jigger),Is.True);Assert.That(s.Hands.TryPickUp(bottle),Is.True);yield return new WaitForSecondsRealtime(1);
            var target=View("shaker.cobbler");s.Player.AimAt(target.transform.position+Vector3.up*target.Size.y*.45f);yield return null;s.Targeting();
            Assert.That(s.Target,Is.SameAs(target));Assert.That(s.MenuOptions[0].Action.Id,Is.EqualTo("pour.bottle_to_container"));
            Assert.That(HudText.Line(s.MenuOptions[0]),Is.EqualTo("倒入雪克壶"));Assert.That(HudText.Hold(s.MenuOptions[0]),Is.True);
            var bottlePour=s.MenuOptions[0];Assert.That(HudText.Badge(bottlePour,s.Hands),Is.EqualTo("[按住]"),"主手不重复显示手别");
            foreach(var o in s.MenuOptions){var text=HudText.Line(o);Assert.That(text,Does.Not.Contain("→"));Assert.That(text,Does.Not.Contain("->"));Assert.That(text,Does.Not.Contain("台面"));}
            yield return new WaitForSecondsRealtime(.5f);var anchor=s.Hud.MenuPosition;var position=target.transform.position;
            target.transform.position+=s.Player.ViewCamera.transform.right*.015f;Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(target));
            yield return new WaitForSecondsRealtime(.4f);Assert.That(Vector2.Distance(s.Hud.MenuPosition,anchor),Is.GreaterThan(1),"菜单应跟随目标而非始终钉在射线中心");
            target.transform.position=position;Physics.SyncTransforms();s.Targeting();yield return new WaitForSecondsRealtime(.4f);
            // Fast reversal and wrap-around may animate the accent, never the option geometry.
            yield return new WaitForSecondsRealtime(.5f);var fixedRows=s.MenuOptions.Select((o,i)=>s.Hud.ActionRect(i)).ToArray();
            for(int step=0;step<24;step++)
            {
                s.SelectMenu(step%3==0?-1:1,false);yield return null;
                for(int i=0;i<fixedRows.Length;i++)
                {Assert.That(s.Hud.ActionRect(i).y-s.Hud.MenuPosition.y,Is.EqualTo(fixedRows[i].y-fixedRows[0].y+40).Within(.001f));Assert.That(s.Hud.ActionRect(i).size,Is.EqualTo(fixedRows[i].size));}
            }
            s.SelectMenu(0,true);
            int flip=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="jigger.flip");Assert.That(flip,Is.GreaterThan(0));
            int last=s.MenuOptions.Count-1;s.SelectMenu(last,true);s.SelectMenu(1,false);Assert.That(s.SelectedMenuIndex,Is.Zero);s.SelectMenu(-1,false);Assert.That(s.SelectedMenuIndex,Is.EqualTo(last));
            s.SelectMenu(flip,true);s.Targeting();Assert.That(s.MenuOptions[s.SelectedMenuIndex].Action.Id,Is.EqualTo("jigger.flip"));
            var end=jigger.ActiveEnd;double now=s.EventTime(Time.realtimeSinceStartupAsDouble);s.LeftButton(true,now);s.LeftButton(false,now+.001);yield return new WaitForSecondsRealtime(1);
            Assert.That(jigger.ActiveEnd,Is.Not.EqualTo(end));Assert.That(HudText.Slot(jigger),Does.Contain(jigger.ActiveCup.NominalMl.ToString("0")+"ml"));
            s.Hands.ToggleMain();yield return new WaitForSecondsRealtime(.6f);Assert.That(s.Hud.HandPosition,Is.EqualTo(s.Hands.MainHand==HandSide.Left?0:1).Within(.03f));
            Assert.That(HudText.Badge(bottlePour,s.Hands),Does.Contain("[右手]"),"Tab切换后，副手动作仍明确标注来源");
            s.Targeting();s.SelectMenu(0,true);yield return new WaitForSecondsRealtime(.7f);
            Assert.That(s.Hud.InspectPromptRect(HandSide.Left).Overlaps(s.Hud.TabPromptRect),Is.False);
            Assert.That(s.Hud.InspectPromptRect(HandSide.Right).Overlaps(s.Hud.TabPromptRect),Is.False);
            Assert.That(s.Hud.InspectPromptRect(HandSide.Left).yMin,Is.GreaterThan(s.Hud.TabPromptRect.yMax));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../SpeakeasyHudReports"));Directory.CreateDirectory(output);
            if(!Application.isBatchMode)
            {
                #if UNITY_EDITOR
                var gameView=UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor"));gameView.Show();gameView.Focus();
                #endif
                yield return new WaitForSecondsRealtime(.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"menu-and-hands.png"));yield return new WaitForSecondsRealtime(2);
                Assert.That(File.Exists(Path.Combine(output,"menu-and-hands.png")),Is.True);
                s.Hands.ToggleMain();yield return new WaitForSecondsRealtime(.6f);Assert.That(HudText.Badge(bottlePour,s.Hands),Is.EqualTo("[按住]"));
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"primary-hand.png"));yield return new WaitForSecondsRealtime(.5f);
                s.Hands.ToggleMain();yield return new WaitForSecondsRealtime(.6f);
            }
            int pour=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="pour.bottle_to_container"&&o.Target==target.Item);s.SelectMenu(pour,true);
            now=s.EventTime(Time.realtimeSinceStartupAsDouble);s.LeftButton(true,now);s.PointerDelta(new Vector2(0,1500),now+.001);yield return new WaitForSecondsRealtime(1.4f);
            Assert.That(s.HudHolding,Is.True);Assert.That(s.Hud.HoldProgress,Is.GreaterThan(0));
            if(!Application.isBatchMode){ScreenCapture.CaptureScreenshot(Path.Combine(output,"hold-progress.png"));yield return new WaitForSecondsRealtime(.5f);}
            s.LeftButton(false,s.EventTime(Time.realtimeSinceStartupAsDouble));yield return new WaitForSecondsRealtime(.5f);
            var old=s.Hands.Get(s.Hands.MainHand);var counter=View("fixture.counter");s.Player.AimAt(counter.transform.position+new Vector3(-.5f,counter.Size.y,.15f));yield return null;s.Targeting();
            // Existing RHS dispatch still routes only ownership, never the selected self action.
            if(s.SecondaryHint!=null){now=s.EventTime(Time.realtimeSinceStartupAsDouble);s.RightButton(now);yield return new WaitForSecondsRealtime(.4f);Assert.That(s.Clock.Error,Is.Null);}
            if(!Application.isBatchMode)
            {ScreenCapture.CaptureScreenshot(Path.Combine(output,"secondary-hint.png"));yield return new WaitForSecondsRealtime(.4f);}
            PlayerPrefs.DeleteKey("Bar.Handedness");
        }
    }
}
