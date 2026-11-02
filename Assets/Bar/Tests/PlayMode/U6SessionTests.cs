using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Bar.Tests
{
    internal sealed class U6SessionTests
    {
        [UnityTest] public IEnumerator GinSourActualInputLogReplaysInCSharp()
        {yield return new FullCraftingTests().GinSourUsesAllSourToolsAndServesWithConservation();VerifyLog("gin-sour");}
        [UnityTest] public IEnumerator GinAndTonicActualInputLogReplaysInCSharp()
        {yield return new FullCraftingTests().GinAndTonicIncludesCoolingOpeningToppingStirringAndServing();VerifyLog("gin-tonic");}
        private void VerifyLog(string name)
        {
            var s=Object.FindFirstObjectByType<BarSession>();var lines=s.Journal.ReadLines();
            Assert.That(new System.Text.UTF8Encoding(false).GetByteCount(string.Join("\n",lines)),Is.LessThan(3*1024*1024),"未压缩单杯日志预算");
            var replay=new LogReplay();replay.Run(lines);Assert.That(replay.ServedCount,Is.EqualTo(1));Assert.That(replay.MaximumError,Is.LessThanOrEqualTo(1e-9));
            var rows=lines.Select(SessionJournal.Decode).ToArray();Assert.That(rows.Any(r=>r.kind=="cue_frame"),Is.True);Assert.That(rows.Any(r=>r.kind=="operation_confirmed"&&r.intendedAmount.HasValue&&r.actualAmount.HasValue),Is.True);
            string output=Path.Combine(Application.dataPath,"../U6Reports");Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,name+".jsonl"),lines,new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output,name+"-csharp.json"),"{\"served\":"+replay.ServedCount+",\"maximumError\":"+replay.MaximumError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
        }
        [UnityTest] public IEnumerator RealRenderersRespectLidDebugAndSnapshotHash()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var view=s.Views.Single(v=>v.DefinitionId=="shaker.cobbler");var sh=(CobblerShaker)view.Item;
            sh.Attach((ShakerStrainer)s.Views.Single(v=>v.DefinitionId=="shaker.strainer").Item);sh.Attach((ShakerCap)s.Views.Single(v=>v.DefinitionId=="shaker.cap").Item);
            s.Adapter.World.Apply(new Operation{type="PourSource",to=sh.Body.CoreVesselId,ingredient="spirit",ml=45});
            s.Adapter.World.Apply(new Operation{type="PourSource",to=sh.Body.CoreVesselId,ingredient="lemon",ml=25});
            s.Adapter.World.Apply(new Operation{type="AddIce",to=sh.Body.CoreVesselId,grams=40});yield return new WaitForSecondsRealtime(.15f);
            var binding=view.GetComponentInChildren<ContainerVisual>();s.Player.AimAt(view.transform.position+Vector3.up*.1f);s.Cues.Present();
            Assert.That(binding.LiquidVolume.enabled,Is.False);Assert.That(binding.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.Contains("Liquid surface")).All(r=>!r.enabled),Is.True);
            string before=LogReplay.SnapshotHash(s.Adapter.World);s.Cues.Present();string after=LogReplay.SnapshotHash(s.Adapter.World);Assert.That(after,Is.EqualTo(before));Capture(s,"sealed-normal");
            File.WriteAllText(Path.Combine(Application.dataPath,"../U6Reports/readonly-snapshot.json"),"{\"before\":\""+before+"\",\"after\":\""+after+"\",\"equal\":true}");
            s.ToggleDebug();s.Cues.Present();Assert.That(binding.LiquidVolume.enabled,Is.True);Capture(s,"sealed-F3");s.ToggleDebug();s.Cues.Present();Assert.That(binding.LiquidVolume.enabled,Is.False);
            var debug=s.Journal.ReadLines().Select(SessionJournal.Decode).Where(r=>r.kind=="debug_toggled").ToArray();Assert.That(debug.Length,Is.EqualTo(2));Assert.That(debug[0].debugVisible,Is.True);Assert.That(debug[1].debugVisible,Is.False);
        }
        [UnityTest] public IEnumerator GlassShowsIceBubblesAndCondensationThroughTransparentLiquid()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;
            s.Adapter.World.Apply(new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="spirit",ml=45,temperature=4});
            s.Adapter.World.Apply(new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="tonic",ml=160,temperature=4});
            s.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=60});s.Adapter.World.Apply(new Operation{type="Stir",target=glass.CoreVesselId,seconds=15,intensity=.6});
            yield return new WaitForSecondsRealtime(.15f);
            s.Player.ViewCamera.transform.position=view.transform.position+new Vector3(-.12f,.15f,-.35f);s.Player.AimAt(view.transform.position+Vector3.up*.09f);s.Cues.Present();
            var binding=view.GetComponentInChildren<ContainerVisual>();var cue=s.Cues.Current.Single(c=>c.vesselId==glass.CoreVesselId);
            Assert.That(binding.LiquidVolume.enabled,Is.True);Assert.That(binding.LiquidVolume.sharedMaterial.shader.name,Is.EqualTo("Bar/CueLiquid"));Assert.That(binding.LiquidVolume.sharedMaterial.GetColor("_BaseColor").a,Is.LessThan(.5));
            Assert.That(cue.iceVisible,Is.True);Assert.That(cue.bubbleCount,Is.GreaterThan(0));Assert.That(cue.condensation,Is.Zero,"玻璃不再显示冷凝，线索如实记 0（用户 2026-10-27）");
            var iceRenderers=binding.GetComponent<IceVisual>().GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="IceSurface"&&r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            Assert.That(iceRenderers.Length,Is.GreaterThan(0));
            foreach(var ice in iceRenderers){Assert.That(ice.sharedMaterial.renderQueue,Is.EqualTo(2499));Assert.That(ice.sharedMaterial.GetFloat("_ZWrite"),Is.EqualTo(1));}
            var mesh=binding.InnerCavity.sharedMesh.bounds;var cavity=new Bounds();
            for(int corner=0;corner<8;corner++)
            {var point=binding.InnerCavity.transform.TransformPoint(mesh.center+Vector3.Scale(mesh.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));if(corner==0)cavity=new Bounds(point,Vector3.zero);else cavity.Encapsulate(point);}
            foreach(var ice in iceRenderers){Assert.That(ice.bounds.min.x,Is.GreaterThanOrEqualTo(cavity.min.x-.001f));Assert.That(ice.bounds.max.x,Is.LessThanOrEqualTo(cavity.max.x+.001f));Assert.That(ice.bounds.min.z,Is.GreaterThanOrEqualTo(cavity.min.z-.001f));Assert.That(ice.bounds.max.z,Is.LessThanOrEqualTo(cavity.max.z+.001f));}
            Capture(s,"glass-ice-bubbles-condensation");
        }
        [UnityTest] public IEnumerator FlipAnimationDeferralIsLogged()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();var view=s.Views.Single(v=>v.DefinitionId=="jigger.dual");
            s.Player.AimAt(view.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();s.Targeting();s.RightButton(s.EventTime(Time.realtimeSinceStartupAsDouble));yield return new WaitForSecondsRealtime(.23f);
            s.Targeting();int i=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id=="jigger.flip");Assert.That(i,Is.GreaterThanOrEqualTo(0));s.SelectMenu(i,true);s.LeftButton(true,s.EventTime(Time.realtimeSinceStartupAsDouble));s.LeftButton(false,s.EventTime(Time.realtimeSinceStartupAsDouble)+.001);
            yield return new WaitForSecondsRealtime(.12f);Assert.That(view.IsAnimating,Is.True);
            s.LeftButton(true,s.EventTime(Time.realtimeSinceStartupAsDouble));Assert.That(s.Journal.ReadLines().Select(SessionJournal.Decode).Any(r=>r.kind=="interaction_deferred"&&r.reason=="view_animation"&&r.deferred),Is.True);
        }
        private static void Capture(BarSession s,string name)
        {
            var dir=Path.Combine(Application.dataPath,"../U6Reports");Directory.CreateDirectory(dir);var rt=RenderTexture.GetTemporary(960,600,24);RenderPipeline.SubmitRenderRequest(s.Player.ViewCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
