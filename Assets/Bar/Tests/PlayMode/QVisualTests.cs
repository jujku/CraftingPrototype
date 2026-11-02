using System;
using System.Collections;
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
    public sealed class QVisualTests
    {
        private BarSession s;private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../QReports"));
        private ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load(){yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();s.enabled=s.Cues.enabled=false;foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b is LocalIceWorld||b.GetType().Name=="InputRouter")b.enabled=false;foreach(var v in s.Views)if(v.Item is not Fixture&&v.DefinitionId!="glass.highball"&&v.DefinitionId!="glass.coupe"&&!v.DefinitionId.StartsWith("shaker."))v.gameObject.SetActive(false);Directory.CreateDirectory(Output);}
        private Texture2D Capture(string name){s.GetComponent<HeldItemRendering>().Refresh();var t=PourCaptureTests.Capture(s.Player.ViewCamera,"Q-"+name);File.Copy(Path.Combine(PourCaptureTests.Output,"Q-"+name+".png"),Path.Combine(Output,name+".png"),true);return t;}
        private void Camera(ItemView view,bool frost=false){var c=s.Player.ViewCamera;c.fieldOfView=36;c.aspect=640f/480;var point=view.transform.position+Vector3.up*(frost?.12f:.08f);c.transform.position=point+new Vector3(.055f,.035f,frost?-.47f:-.34f);if(frost){var side=s.Hands.SideOf(view.Item);var grip=view.Anchor(side==HandSide.Left?"Grip_L":"Grip_R");if(!grip)grip=view.Anchor("Grip_L");if(grip){var radial=grip.position-view.transform.position;radial.y=0;if(radial.sqrMagnitude>.00001f)c.transform.position=point+radial.normalized*.47f+Vector3.up*.035f;}}c.transform.LookAt(point);}
        private static int Changed(Texture2D a,Texture2D b){var x=a.GetPixels32();var y=b.GetPixels32();int n=0;for(int i=0;i<x.Length;i++)if(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b)>15)n++;return n;}
        [UnityTest] public IEnumerator RealScreenshotsFoamTransferBubblesAndOverlayBackgroundMetrics()
        {
            yield return Load();var shView=View("shaker.cobbler");var sh=(CobblerShaker)shView.Item;var world=s.Adapter.World;var controls=ItemCatalog.LoadControls(s.ControlsJson.text);
            foreach(var pair in new[]{("spirit",45d),("syrup",20d),("lemon",25d)})world.Pour(sh.Body.CoreVesselId,pair.Item1,pair.Item2);world.Apply(new Operation{type="AddIce",to=sh.Body.CoreVesselId,grams=160});s.Adapter.Advance(0);
            sh.Attach((ShakerStrainer)View("shaker.strainer").Item);sh.Attach((ShakerCap)View("shaker.cap").Item);Assert.That(s.Hands.TryPickUp(sh),Is.True);
            var active=new ShakeAction(controls).Begin(sh,sh,s.Hands);for(int i=0;i<120;i++){active.Update(new ActionInput(gestureY:i%2==0?60:-60),.1);foreach(var r in active.Requests)if(r.IsPending)s.Adapter.Submit(r);}active.End();
            sh.Detach(sh.CapSlot.Content);var coupe=(Glass)View("glass.coupe").Item;active=new ShakerPourAction(controls).Begin(sh,coupe,s.Hands);for(int i=0;i<50&&!active.IsEnded;i++){active.Update(new ActionInput(125),.1);foreach(var r in active.Requests)if(r.IsPending)s.Adapter.Submit(r);}active.End();
            var foam=new FoamPresentation(s.Adapter,BPresentationSettings.Load().foam);foam.Dispose(); // presenter was subscribed before these real Domain operations.
            Camera(View("glass.coupe"));s.Cues.Present(0);var cue=s.Cues.Current.First(c=>c.vesselId==coupe.CoreVesselId);Assert.That(cue.foam,Is.GreaterThan(.0001));var sour=Capture("sour-foam");Object.Destroy(sour);
            sh.Attach((ShakerCap)View("shaker.cap").Item);foreach(string partId in new[]{"shaker.strainer","shaker.cap"}){var part=View(partId);var anchor=shView.Anchor(part.Item.Location.Anchor);part.transform.SetParent(anchor,false);var scale=anchor.lossyScale;part.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;}
            var snapshots=world.Vessels.First(v=>v.id==sh.Body.CoreVesselId).Snapshot();var binding=shView.GetComponentInChildren<ContainerVisual>();var shells=shView.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.name!="InnerCavity").ToArray();var effect=new SurfaceAppearance(shView,binding,sh.Body,shells);
            Camera(shView,true);snapshots.vesselTemperature=25;effect.Present(snapshots,shView.transform.position.y+.05f,null,0);Object.Destroy(Capture("frost-0"));snapshots.vesselTemperature=4;
            for(int i=0;i<25;i++)effect.Present(snapshots,shView.transform.position.y+.05f,null,.1f);Object.Destroy(Capture("frost-1"));for(int i=0;i<70;i++)effect.Present(snapshots,shView.transform.position.y+.05f,null,.1f);var unwiped=Capture("frost-2-unwiped");var actualGrip=shView.Anchor(s.Hands.SideOf(sh)==HandSide.Left?"Grip_L":"Grip_R");if(!actualGrip)actualGrip=shView.Anchor("Grip_L");effect.Present(snapshots,shView.transform.position.y+.05f,actualGrip,.1f);var wipedMetal=Capture("frost-2");int fingerPixels=Changed(unwiped,wipedMetal);File.WriteAllText(Path.Combine(Output,"metal-finger.txt"),"changedPixels="+fingerPixels+"; actual grip height projected onto shell contact");Assert.That(fingerPixels,Is.GreaterThan(100));Object.Destroy(unwiped);Object.Destroy(wipedMetal);effect.Dispose();
            int bubblePixels=0;foreach(string id in new[]{"glass.highball","glass.coupe"})
            {
                var view=View(id);var glass=(Glass)view.Item;var visual=view.GetComponentInChildren<ContainerVisual>();if(id=="glass.highball"){world.Pour(glass.CoreVesselId,"spirit",45);world.Pour(glass.CoreVesselId,"tonic",120);world.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=80});s.Adapter.Advance(0);visual.GetComponent<LocalIceWorld>().Advance(1f/60);}
                Camera(view);s.Cues.Present(0);var state=world.GetVessel(glass.CoreVesselId).Snapshot();state.vesselTemperature=8;var rs=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.name!="InnerCavity").ToArray();var surf=new SurfaceAppearance(view,visual,glass,rs);float height=visual.transform.TransformPoint(Vector3.up*visual.HeightForLiquid(state.volumeMl)).y;
                for(int i=0;i<150;i++)surf.Present(state,height,null,.1f);Object.Destroy(Capture(id+"-condensation"));
                if(id=="glass.highball")
                {
                    var bubbles=visual.GetComponent<BubbleStreams>();for(int i=0;i<180;i++)bubbles.Present(6,12,height,Vector3.up,0,true,1f/60,visual.LiquidVolume.sharedMaterial);var with=Capture("gnt-bubbles");int before=bubbles.ActiveCount;var wall=bubbles.Sites.First(site=>site.Kind=="wall");var fixedPoint=wall.LocalPosition;bubbles.Present(6,12,height+.005f,Vector3.up,0,true,0,visual.LiquidVolume.sharedMaterial);Assert.That(wall.LocalPosition,Is.EqualTo(fixedPoint),"杯壁成核点不随液位移动");bubbles.Present(6,12,height,Vector3.up,0,true,0,visual.LiquidVolume.sharedMaterial);
                    var ps=bubbles.Sites[0].System;var particles=new ParticleSystem.Particle[512];int alive=ps.GetParticles(particles);var pr=ps.GetComponent<ParticleSystemRenderer>();File.WriteAllText(Path.Combine(Output,"bubble-debug.txt"),"count="+alive+" bounds="+pr.bounds+" shader="+pr.sharedMaterial.shader.isSupported+" queue="+pr.sharedMaterial.renderQueue+" layer="+pr.gameObject.layer+" level="+height+" frame="+visual.transform.position+" camera="+s.Player.ViewCamera.transform.position+"\n"+string.Join("\n",particles.Take(12).Select(p=>p.position+" size="+p.startSize+" color="+p.startColor+" world="+visual.transform.TransformPoint(p.position))));
                    foreach(var r in rs)r.enabled=false;foreach(var iceRoot in visual.GetComponent<LocalIceWorld>().Displays)iceRoot.gameObject.SetActive(false);for(int k=0;k<alive;k++){particles[k].startSize*=12;particles[k].startColor=Color.white;}ps.SetParticles(particles,alive);Object.Destroy(Capture("bubble-diagnostic-amplified"));for(int k=0;k<alive;k++)particles[k].startSize/=12;ps.SetParticles(particles,alive);foreach(var r in rs)r.enabled=true;foreach(var iceRoot in visual.GetComponent<LocalIceWorld>().Displays)iceRoot.gameObject.SetActive(true);
                    bubbles.Present(6,12,height,Vector3.up,0,false,.01f,visual.LiquidVolume.sharedMaterial);Assert.That(bubbles.ActiveCount,Is.GreaterThan(0),"离屏不清存量");foreach(var site in bubbles.Sites)site.System.Clear();bubbles.Present(0,12,height,Vector3.up,0,true,.01f,visual.LiquidVolume.sharedMaterial);var without=Capture("gnt-no-bubbles");bubblePixels=Changed(with,without);Assert.That(before,Is.GreaterThan(100),"真实粒子仍须持续发射");Assert.That(bubblePixels,Is.GreaterThan(100),"移除静态白线网格后，上升气泡本身仍须可见；SodaGlassLinesTests另检查壁面伪影为零");Object.Destroy(with);Object.Destroy(without);
                    // Held stack must retain a valid opaque background for ice and bubble lens sampling.
                    s.Hands.TryPlace(s.Hands.SideOf(sh).Value,(BarCounter)View("fixture.counter").Item);s.Hands.TryPickUp(glass);s.GetComponent<HeldItemRendering>().Refresh();Object.Destroy(Capture("held-background"));Assert.That(Shader.GetGlobalFloat("_BarSceneWithIceValid"),Is.EqualTo(1));Assert.That(IceRefractionFeature.OverlayOpaqueValid,Is.True,"URP Color input must supply overlay opaque background");
                }surf.Dispose();
            }
            File.WriteAllText(Path.Combine(Output,"metrics.json"),"{\"foamMetres\":"+cue.foam.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"bubbleChangedPixels\":"+bubblePixels+",\"totalPixels\":307200,\"actualUnityRender\":true}");
        }
        [UnityTest] public IEnumerator IdlePresentationReusesBuffersAndShortStrokesStillReverse()
        {
            yield return Load();for(int i=0;i<8;i++)s.Cues.Present(0);long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100;i++)s.Cues.Present(0);long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
            File.WriteAllText(Path.Combine(Output,"allocation.json"),"{\"idleFrames\":100,\"managedAllocatedBytes\":"+bytes+",\"scope\":\"CuePresenter plus sound, surfaces and bubble updates; no model tick or logging boundary\"}");Assert.That(bytes,Is.Zero,"常态呈现帧不再分配，模型节拍/声音事件日志另计");
            var view=View("shaker.cobbler");Assert.That(s.Hands.TryPickUp(view.Item),Is.True);var pose=new ShakePresentation();for(int i=0;i<40;i++)pose.Apply(s,view,true,(i/4)%2==0?60:-60,.03f);Assert.That(pose.ReversalSequence,Is.GreaterThan(4),"短促的真实往复也算折返（2026-10-28 起只用于画面锁相，冲程声改按冰块撞壁）");var calm=new ShakePresentation();for(int i=0;i<40;i++)calm.Apply(s,view,true,i%2==0?8:-8,.03f);Assert.That(calm.ReversalSequence,Is.Zero,"手抖不算折返");
        }
        [UnityTest] public IEnumerator CachedLiquidSurfaceSkipsStaticRebuildsAndPoolStealsWeakest()
        {
            yield return Load();var view=View("glass.highball");var visual=view.GetComponentInChildren<ContainerVisual>();worldPour();s.Cues.Present(0);var surface=visual.GetComponent<LiquidSurface>();float height=visual.transform.TransformPoint(Vector3.up*visual.HeightForLiquid(100)).y;surface.Draw(visual.InnerCavity,height,visual.LiquidVolume.sharedMaterial,true);int before=surface.RebuildCount;
            for(int i=0;i<100;i++)surface.Draw(visual.InnerCavity,height,visual.LiquidVolume.sharedMaterial,true);Assert.That(surface.RebuildCount,Is.EqualTo(before));surface.Draw(visual.InnerCavity,height+.001f,visual.LiquidVolume.sharedMaterial,true);Assert.That(surface.RebuildCount,Is.EqualTo(before+1));
            var p=BarAudioCatalog.Load();p.poolSize=2;p.otherConcurrency=2;var schedules=new System.Collections.Generic.List<SoundObservation>();var pool=new SampledAudioPool(s.transform,s.Player.ViewCamera.transform,p,(id,sound)=>schedules.Add(sound));var point=s.Player.ViewCamera.transform.position;pool.Play("a","ice","x",point,.1f);pool.Play("b","ice","x",point,.2f);pool.Play("c","iceHeavy","x",point,.8f);Assert.That(pool.Stolen,Is.EqualTo(1));Assert.That(pool.PreloadFailures,Is.Zero);pool.Stop();int logged=schedules.Count;double dsp=AudioSettings.dspTime;Assert.That(pool.Play("same-impact","ice","x",point,.6f,group:"ice",scheduledDsp:dsp+.03,applyCooldown:false),Is.True);Assert.That(pool.Play("same-impact","ice","x",point,.6f,group:"ice",scheduledDsp:dsp+.06,applyCooldown:false),Is.True);Assert.That(pool.ActiveCount,Is.EqualTo(2));Assert.That(schedules[logged+1].scheduledDsp-schedules[logged].scheduledDsp,Is.EqualTo(.03).Within(1e-8),"同帧多撞击保留相对间隔");pool.Dispose();
            void worldPour()=>s.Adapter.World.Pour(((Glass)view.Item).CoreVesselId,"tonic",100);
        }
    }
}
