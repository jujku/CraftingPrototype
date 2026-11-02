using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class OrderLoopTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../U7Reports"));
        private BarSession session;
        private static void Op(BarSession s,Operation op)
        {typeof(WorldAdapter).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s.Adapter,new object[]{op});}
        private void TakeSession()
        {
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;
        }
        private void Serve(string variant="")
        {
            var order=session.Orders.Current;var sh=session.Views.Select(v=>v.Item).OfType<CobblerShaker>().Single();
            var glass=session.Views.Select(v=>v.Item).OfType<Glass>().Single(g=>g.Definition.Id=="glass."+order.glass);
            foreach(var op in OrderReference.Recipe(order,sh.Body.CoreVesselId,glass.CoreVesselId,variant).Where(o=>o.type!="Serve"))Op(session,op);
            session.Adapter.Advance(0);Assert.That(session.Hands.TryPickUp(glass),Is.True);
            var mat=session.Views.Select(v=>v.Item).OfType<ServeMat>().Single();
            session.Clock.Begin(new ActionResolver().Register(new ServeAction()).Resolve(mat,session.Hands).Single());
            Assert.That(glass.IsServed,Is.True);Assert.That(session.OrderInputBlocked,Is.True);
        }
        private IEnumerator Advance(bool retry)
        {
            session.Orders.MarkPresented();var old=session;session.Orders.Advance(retry);
            float deadline=Time.realtimeSinceStartup+25;
            while(old&&Time.realtimeSinceStartup<deadline)yield return null;
            while(!Object.FindFirstObjectByType<BarSession>()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(Time.realtimeSinceStartup,Is.LessThan(deadline));TakeSession();yield return null;
        }
        [UnityTest] public IEnumerator ReferencesDeviationsRetryAndSegmentedReplay()
        {
            PlayerPrefs.DeleteKey("Bar.Handedness");Directory.CreateDirectory(Output);
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");TakeSession();
            string file=session.Journal.FilePath,initial=session.Orders.InitialWorldHash,layout=session.Orders.InitialLayout;
            // Reference quantities go through the unchanged core; Serve goes through the real domain action.
            Serve();Assert.That(session.Orders.FeedbackIds,Is.EqualTo(new[]{"satisfied"}));yield return null;
            Assert.That(session.Hud.enabled,Is.False,"反馈期间暂停提示不应叠在订单按钮上；原HUD脚本保持冻结");
            session.ToggleDebug();Assert.That(session.Orders.DebugVisible,Is.True);session.ToggleDebug();
            session.Clock.Stop(true);
            // Dirty non-core state as well, to prove scene reload restores it rather than just clearing liquids.
            var lemon=session.Views.Select(v=>v.Item).OfType<LemonHalf>().First();lemon.Consume(lemon.RemainingMl);
            var bottle=session.Views.First(v=>v.Item is Bottle);((Bottle)bottle.Item).SetOpen(true);bottle.transform.position+=Vector3.up;
            Assert.That(session.Hands.TryPickUp(bottle.Item),Is.True);
            yield return Advance(true);Assert.That(session.Orders.AttemptIndex,Is.EqualTo(2));
            Assert.That(session.Orders.InitialWorldHash,Is.EqualTo(initial));Assert.That(session.Orders.InitialLayout,Is.EqualTo(layout));
            Assert.That(session.Adapter.DebugExposed,Is.True,"F3暴露跨尝试保留");Assert.That(session.DebugVisible,Is.False);
            Assert.That(session.Views.Select(v=>v.Item).OfType<LemonHalf>().Count(l=>!l.IsSpent),Is.EqualTo(4));Assert.That(session.Hands.Left,Is.Null);Assert.That(session.Hands.Right,Is.Null);
            Serve("no-syrup");Assert.That(session.Orders.FeedbackIds.Any(id=>id.StartsWith("sugarAcidRatio.low")),Is.True);Assert.That(session.Orders.FeedbackIds.Length,Is.LessThanOrEqualTo(2));yield return null;
            yield return Advance(false);Assert.That(session.Orders.Current.id,Is.EqualTo("gin-tonic"));Assert.That(session.Orders.AttemptIndex,Is.EqualTo(1));
            Serve();Assert.That(session.Orders.FeedbackIds,Is.EqualTo(new[]{"satisfied"}));yield return null;
            yield return Advance(true);Assert.That(session.Orders.InitialWorldHash,Is.EqualTo(initial));
            Serve("no-ice");Assert.That(session.Orders.FeedbackIds.Any(id=>id.StartsWith("temperature.high")),Is.True);yield return null;
            yield return Advance(false);Assert.That(session.Orders.Current.id,Is.EqualTo("tom-collins"));
            Serve();Assert.That(session.Orders.FeedbackIds,Is.EqualTo(new[]{"satisfied"}));yield return null;
            yield return Advance(true);Serve("no-soda");
            Assert.That(session.Orders.FeedbackIds.Any(id=>id.StartsWith("co2Retention.missing")||id.StartsWith("co2Retention.low")),Is.True);yield return null;
            session.Orders.MarkPresented();session.Orders.Advance(false);Assert.That(session.Orders.Complete,Is.True);
            Assert.That(session.Journal.FilePath,Is.EqualTo(file));var lines=session.Journal.ReadLines();
            var rows=lines.Select(SessionJournal.Decode).ToArray();Assert.That(rows.Count(r=>r.kind=="session_header"),Is.EqualTo(1));
            Assert.That(rows.Count(r=>r.kind=="order_started"),Is.EqualTo(6));Assert.That(rows.Count(r=>r.kind=="order_feedback"),Is.EqualTo(6));Assert.That(rows.Count(r=>r.kind=="order_advanced"),Is.EqualTo(6));
            Assert.That(rows.Skip(1).All(r=>!string.IsNullOrEmpty(r.orderId)&&r.attemptIndex.HasValue),Is.True);
            var replay=new LogReplay();replay.Run(lines);Assert.That(replay.ServedCount,Is.EqualTo(6));Assert.That(replay.AttemptCount,Is.EqualTo(6));
            File.WriteAllLines(Path.Combine(Output,"six-attempts.jsonl"),lines);
            File.WriteAllText(Path.Combine(Output,"csharp-replay.json"),"{\"served\":6,\"attempts\":6,\"maximumError\":"+replay.MaximumError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
            File.WriteAllText(Path.Combine(Output,"reset.json"),"{\"initialWorldHash\":\""+initial+"\",\"retryEqual\":true,\"lemonsRestored\":4}");
            PlayerPrefs.DeleteKey("Bar.Handedness");
        }
        [UnityTest,Explicit("原生 Game 视图订单截图")] public IEnumerator CaptureOrderInterface()
        {
            if(Application.isBatchMode)Assert.Ignore("只在显式原生 Game 视图截图运行");
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");TakeSession();
            #if UNITY_EDITOR
            var game=UnityEditor.EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();
            #endif
            Directory.CreateDirectory(Output);yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"order-card.png"));yield return new WaitForSecondsRealtime(.5f);
            Serve("no-syrup");yield return new WaitForSecondsRealtime(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"feedback.png"));yield return new WaitForSecondsRealtime(.5f);
            yield return Advance(false);Serve();yield return null;yield return Advance(false);Serve();yield return null;
            session.Orders.MarkPresented();session.Orders.Advance(false);yield return new WaitForSecondsRealtime(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"closing.png"));yield return new WaitForSecondsRealtime(.5f);
            Assert.That(File.Exists(Path.Combine(Output,"closing.png")),Is.True);
        }
    }
}
