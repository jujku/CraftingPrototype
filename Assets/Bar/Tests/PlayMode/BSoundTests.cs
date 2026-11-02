using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Data;
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
    public sealed class BSoundTests
    {
        [UnityTest] public IEnumerator SamplePoolLimitsConcurrencyAndDoesNotImmediatelyRepeat()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;session.Cues.enabled=false;
            var catalog=BarAudioCatalog.Load();var observations=new List<SoundObservation>();var pool=new SampledAudioPool(session.transform,session.Player.ViewCamera.transform,catalog,(id,sound)=>observations.Add(sound));var position=session.Player.ViewCamera.transform.position;
            string before=LogReplay.SnapshotHash(session.Adapter.World);
            for(int i=0;i<10;i++){pool.Play("variant:"+i,"ice","test",position,.6f,group:"ice");pool.Stop();}
            Assert.That(observations.Count,Is.EqualTo(10));for(int i=1;i<observations.Count;i++)Assert.That(observations[i].clip,Is.Not.EqualTo(observations[i-1].clip));
            for(int i=0;i<12;i++)pool.Play("limit:"+i,"ice","test",position,.6f,group:"ice");Assert.That(pool.ActiveCount,Is.EqualTo(catalog.iceConcurrency));Assert.That(pool.Sources.Count(),Is.EqualTo(16));
            Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(before));pool.Dispose();
        }
        [UnityTest] public IEnumerator PourAudioFollowsPreparationVisibleFlowAndUprightTail()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;var session=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var source=session.Views.First(v=>v.Item is Bottle b&&b.IngredientId=="spirit");var glass=(Glass)session.Views.First(v=>v.DefinitionId=="glass.highball").Item;((Bottle)source.Item).SetOpen(true);session.Hands.TryPickUp(source.Item);
            float input=(float)(125/ItemCatalog.LoadControls(session.ControlsJson.text).TiltDegreesPerInput);
            void Begin(){var view=session.Views.Single(v=>v.Item==glass);session.Player.AimAt(view.transform.position+Vector3.up*view.Size.y*.5f);Physics.SyncTransforms();session.Targeting();int index=session.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="pour.bottle_to_container"&&o.Actor==source.Item&&o.Target==glass);Assert.That(index,Is.GreaterThanOrEqualTo(0));session.SelectMenu(index,true);double now=session.EventTime(Time.realtimeSinceStartupAsDouble);session.LeftButton(true,now);session.PointerDelta(new Vector2(0,input),now);}
            Begin();yield return new WaitForSecondsRealtime(.18f);Assert.That(session.Sound.PourAudible,Is.False);session.LeftButton(false,session.EventTime(Time.realtimeSinceStartupAsDouble));yield return new WaitForSecondsRealtime(.2f);
            Assert.That(glass.Contents.LiquidMl,Is.Zero);var verifier=session.gameObject.AddComponent<BSoundFrameVerifier>();verifier.Session=session;Begin();float start=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-start<.65f)yield return null;
            Assert.That(verifier.FlowFrames,Is.GreaterThan(0));Assert.That(verifier.Errors,Is.Empty);session.PointerDelta(new Vector2(0,-input),session.EventTime(Time.realtimeSinceStartupAsDouble));
            for(int i=0;i<8;i++)yield return null;Assert.That(verifier.Errors,Is.Empty);
            session.LeftButton(false,session.EventTime(Time.realtimeSinceStartupAsDouble));yield return new WaitForSecondsRealtime(.5f);Assert.That(session.Sound.PourAudible,Is.False);
            Assert.That(session.Journal.ReadLines().Select(SessionJournal.Decode).Any(r=>r.kind=="presentation_sound"&&(r.sound?.bank?.StartsWith("pour")==true)),Is.True);
            session.Sound.Gate.RequireInteraction();session.Sound.Present();Assert.That(session.Sound.Pool.ActiveCount,Is.Zero);session.Sound.Gate.Unlock();
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports")));File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports/pour-sound.txt")),"preparedSilent=true\nvisibleFrames="+verifier.FlowFrames+"\ntailAudible=true\ngateStopsAudio=true\n");
        }
    }
}
