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
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed class BVisualTests
    {
        private BarSession session;private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports"));
        private ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;session.Cues.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            foreach(var rig in Object.FindObjectsByType<LocalIceWorld>(FindObjectsSortMode.None))rig.enabled=false;Directory.CreateDirectory(Output);
        }
        private Texture2D Capture(string name)
        {var image=PourCaptureTests.Capture(session.Player.ViewCamera,"B-"+name);File.Copy(Path.Combine(PourCaptureTests.Output,"B-"+name+".png"),Path.Combine(Output,name+".png"),true);return image;}
        private static int Difference(Texture2D a,Texture2D b)
        {int n=0;var x=a.GetPixels32();var y=b.GetPixels32();for(int i=0;i<x.Length;i++)if(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b)>15)n++;return n;}
        [Test] public void SurfaceOrderAndWarmWetStageAreTimeAccumulated()
        {
            var p=BPresentationSettings.Load().surface;var glass=new SurfaceEvolution(p);
            glass.Step(16,true,false,.5f);Assert.That(glass.Mist,Is.GreaterThan(0));Assert.That(glass.Drops,Is.Zero);Assert.That(glass.Runs,Is.Zero);
            for(int i=0;i<40;i++)glass.Step(16,true,false,.1f);Assert.That(glass.Drops,Is.GreaterThan(0));Assert.That(glass.Runs,Is.Zero);
            for(int i=0;i<100;i++)glass.Step(16,true,false,.1f);Assert.That(glass.Runs,Is.GreaterThan(0));glass.Step(16,true,true,.1f);Assert.That(glass.Wipe,Is.EqualTo(1));glass.Step(16,true,false,1);Assert.That(glass.Wipe,Is.InRange(0f,.99f));
            var metal=new SurfaceEvolution(p);for(int i=0;i<120;i++)metal.Step(-6,false,false,.1f,1);Assert.That(metal.Frost,Is.InRange(.95f,1f));float before=metal.Frost;metal.Step(25,false,false,1);Assert.That(metal.Frost,Is.LessThan(before));Assert.That(metal.Wet,Is.GreaterThan(0));
            for(int i=0;i<1200;i++)metal.Step(25,false,false,.1f);Assert.That(metal.Frost,Is.Zero);Assert.That(metal.Wet,Is.LessThan(.01f));
        }
        [UnityTest] public IEnumerator FixedNucleationBubblesStayInTheActualCavityAndPopAtTheSurface()
        {
            yield return Load();var view=View("glass.highball");var glass=(Glass)view.Item;var binding=view.GetComponentInChildren<ContainerVisual>();var ice=binding.GetComponent<LocalIceWorld>();
            session.Adapter.World.Apply(new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="tonic",ml=120});session.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=80});session.Adapter.Advance(0);ice.Advance(1f/60);
            yield return null;var bubbles=binding.GetComponent<BubbleStreams>();PourPose.Mouth(view,glass,out var mouth,out _,out float height);string hash=LogReplay.SnapshotHash(session.Adapter.World);
            for(int i=0;i<240;i++)bubbles.Present(6,20,height,Vector3.up,0,true,1f/60);
            Assert.That(bubbles.EmittedCount,Is.GreaterThan(30));Assert.That(bubbles.PopCount,Is.GreaterThan(0));Assert.That(bubbles.Sites.Select(s=>s.Kind).Distinct().Count(),Is.EqualTo(3));
            foreach(var site in bubbles.Sites)
            {
                if(site.Kind!="ice"){var p=site.LocalPosition;Assert.That(new Vector2(p.x-binding.Shape.Axis.x,p.z-binding.Shape.Axis.y).magnitude,Is.LessThanOrEqualTo(binding.Shape.RadiusAt(p.y)));}
                var particles=new ParticleSystem.Particle[64];int count=site.System.GetParticles(particles);
                for(int j=0;j<count;j++)if(particles[j].remainingLifetime>0){var world=site.System.transform.TransformPoint(particles[j].position);Assert.That(world.y,Is.LessThanOrEqualTo(height+.001f));var p=binding.transform.InverseTransformPoint(world);Assert.That(new Vector2(p.x-binding.Shape.Axis.x,p.z-binding.Shape.Axis.y).magnitude,Is.LessThanOrEqualTo(binding.Shape.RadiusAt(p.y)));}
            }
            var camera=session.Player.ViewCamera;camera.fieldOfView=42;camera.aspect=640f/480;camera.transform.position=view.transform.position+new Vector3(.07f,.16f,-.28f);camera.transform.LookAt(mouth+Vector3.down*.07f);
            var with=Capture("carbonated");int emitted=bubbles.EmittedCount;foreach(var site in bubbles.Sites)site.System.Clear();bubbles.Present(0,20,height,Vector3.up,0,true,.3f);Assert.That(bubbles.EmittedCount,Is.EqualTo(emitted));var without=Capture("no-carbonation");int changed=Difference(with,without);Assert.That(changed,Is.GreaterThan(300),"气泡必须透过玻璃实际可见");
            Assert.That(bubbles.ActiveCount,Is.EqualTo(0));Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));
            File.WriteAllText(Path.Combine(Output,"bubble-results.txt"),"emitted="+bubbles.EmittedCount+"\npops="+bubbles.PopCount+"\nchangedPixels="+changed+"\ncoreUnchanged=true\n");Object.Destroy(with);Object.Destroy(without);
        }
        [UnityTest] public IEnumerator FrostAndCondensationRenderOnTableAndHeldGlassWithGripWiping()
        {
            yield return Load();var camera=session.Player.ViewCamera;camera.fieldOfView=42;camera.aspect=640f/480;
            foreach(string id in new[]{"shaker.cobbler","glass.highball"})
            {
                var view=View(id);var binding=view.GetComponentInChildren<ContainerVisual>();Container vessel=view.Item is CobblerShaker sh?sh.Body:(Container)view.Item;
                session.Adapter.World.Apply(new Operation{type="PourSource",to=vessel.CoreVesselId,ingredient="spirit",ml=80});session.Adapter.Advance(0);yield return null;
                var shells=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.GetComponentInParent<ItemView>()==view&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.name!="InnerCavity").ToArray();
                var effect=new SurfaceAppearance(view,binding,vessel,shells);var snapshot=session.Adapter.World.Vessels.First(v=>v.id==vessel.CoreVesselId).Snapshot();var originalTemperature=snapshot.vesselTemperature;snapshot.vesselTemperature=id=="shaker.cobbler"?4:16;snapshot.iceGrams=100;/* fog and frost need ice in the tin (2026-10-28) */
                string hash=LogReplay.SnapshotHash(session.Adapter.World);PourPose.Mouth(view,vessel,out _,out _,out float height);
                camera.transform.position=view.transform.position+new Vector3(.09f,.16f,-.3f);camera.transform.LookAt(view.transform.position+Vector3.up*.075f);
                effect.Present(snapshot,height,null,0);var dry=Capture(id+"-dry");for(int i=0;i<120;i++)effect.Present(snapshot,height,null,.1f);var cold=Capture(id+"-cold");int pixels=Difference(dry,cold);if(vessel is Glass)Assert.That(pixels,Is.LessThan(50),"玻璃不再显示冷凝（用户 2026-10-27）");else Assert.That(pixels,Is.GreaterThan(1000),id+" must display a visible surface change");
                Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));File.WriteAllText(Path.Combine(Output,id+"-surface.txt"),"changedPixels="+pixels+"\nwallFixtureC="+snapshot.vesselTemperature+"\ncoreUnchanged=true\noriginalCoreWallC="+originalTemperature);
                Object.Destroy(dry);Object.Destroy(cold);effect.Dispose();
            }
        }
    }
}
