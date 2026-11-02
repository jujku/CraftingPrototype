using System.Collections;
using System.Collections.Generic;
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
    public sealed class LiquidAudioFixTests
    {
        private BarSession session;private LineRenderer guide;private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../LiquidAudioFixReports"));
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;session.Cues.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter"||b is LocalIceWorld)b.enabled=false;
            session.Sound.Pool.Stop();Directory.CreateDirectory(Output);
            guide=new GameObject("Diagnostic liquid boundary only").AddComponent<LineRenderer>();guide.loop=true;guide.useWorldSpace=true;guide.positionCount=64;guide.widthMultiplier=.0007f;
            guide.sharedMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));guide.sharedMaterial.SetColor("_BaseColor",Color.cyan);guide.sharedMaterial.renderQueue=3100;
        }
        private void Capture(ContainerVisual binding,float height,string filename)
        {
            var block=new MaterialPropertyBlock();binding.LiquidVolume.GetPropertyBlock(block);block.SetFloat("_FillHeight",height);binding.LiquidVolume.SetPropertyBlock(block);
            binding.LiquidVolume.sharedMaterial.SetFloat("_FillHeight",height);var surface=binding.GetComponent<LiquidSurface>();if(!surface)surface=binding.gameObject.AddComponent<LiquidSurface>();surface.Draw(binding.InnerCavity,height,binding.LiquidVolume.sharedMaterial,true);
            float local=binding.transform.InverseTransformPoint(new Vector3(binding.transform.position.x,height,binding.transform.position.z)).y;
            for(int i=0;i<guide.positionCount;i++){float angle=i*Mathf.PI*2/guide.positionCount,r=binding.Shape.RadiusAt(local);guide.SetPosition(i,binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x+Mathf.Cos(angle)*r,local,binding.Shape.Axis.y+Mathf.Sin(angle)*r)));}
            var image=PourCaptureTests.Capture(session.Player.ViewCamera,filename);File.WriteAllBytes(Path.Combine(Output,filename+".png"),image.EncodeToPNG());Object.Destroy(image);
        }
        [UnityTest] public IEnumerator SmallMeltVolumesMatchTheActualFreeCavityInEveryVessel()
        {
            yield return Load();var rows=new List<string>{"vessel,liquidMl,iceGrams,oldHeight,newHeight,freeMl,coreUnchanged"};
            foreach(var view in session.Views.Where(v=>v.Item is Glass||v.Item is Jigger||v.Item is CobblerShaker))
            foreach(var binding in view.GetComponentsInChildren<ContainerVisual>())
            {
                Container vessel=view.Item is CobblerShaker sh?sh.Body:view.Item is Jigger j?(binding.DefinitionId==j.CupA.Definition.Id?j.CupA:j.CupB):(Container)view.Item;
                var rig=binding.GetComponent<LocalIceWorld>();int blocks=Mathf.Min(rig.Capacity,view.Item is Glass g&&g.Definition.Id=="glass.coupe"?1:4);
                double grams=System.Math.Min(blocks*40,(vessel.CapacityMl-5)*session.Adapter.World.Data.iceDensity*.95);
                session.Adapter.World.Apply(new Operation{type="AddIce",to=vessel.CoreVesselId,grams=grams});session.Adapter.Advance(0);
                foreach(double added in new[]{.25,.75,4d})
                {
                    session.Adapter.World.Apply(new Operation{type="PourSource",to=vessel.CoreVesselId,ingredient="spirit",ml=added});session.Adapter.Advance(0);
                    string hash=LogReplay.SnapshotHash(session.Adapter.World);for(int i=0;i<120;i++)rig.Advance(1f/60);
                    var core=session.Adapter.World.Vessels.First(v=>v.id==vessel.CoreVesselId);float corrected=binding.HeightForLiquid(core.volume),old=binding.Profile.HeightForMl(core.volume+core.ice/session.Adapter.World.Data.iceDensity);
                    double perCube=core.ice/System.Math.Max(1,System.Math.Ceiling(core.ice/40))/session.Adapter.World.Data.iceDensity;
                    var slices=rig.Bodies.Select(b=>{var box=b.GetComponent<BoxCollider>();return new IceVolumeSlice(b.position,b.rotation,box.center,box.size,perCube);}).ToArray();
                    double free=binding.Profile.VolumeBelowHeight(corrected)-IceDisplacement.SubmergedMl(corrected,slices,slices.Length);
                    Assert.That(free,Is.EqualTo(core.volume).Within(.02),binding.DefinitionId);Assert.That(corrected,Is.LessThan(old-.001f),binding.DefinitionId+"不能把露出液面的冰算成水");
                    Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));rows.Add(binding.DefinitionId+","+core.volume+","+core.ice+","+old+","+corrected+","+free+",true");
                    if(System.Math.Abs(core.volume-1)<.001)
                    {
                        session.Cues.Present(0);var camera=session.Player.ViewCamera;camera.fieldOfView=42;camera.aspect=640f/480;
                        var aim=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,Mathf.Lerp(binding.Shape.Bottom,binding.Shape.Top,.35f),binding.Shape.Axis.y));
                        camera.transform.position=aim+new Vector3(.08f,.15f,-.26f);camera.transform.LookAt(aim);
                        var actualBlock=new MaterialPropertyBlock();binding.LiquidVolume.GetPropertyBlock(actualBlock);
                        if(!(view.Item is Jigger jigger)||ReferenceEquals(jigger.ActiveCup,vessel))Assert.That(actualBlock.GetFloat("_FillHeight"),Is.EqualTo(binding.transform.TransformPoint(Vector3.up*corrected).y).Within(.00001f));
                        Capture(binding,binding.transform.TransformPoint(Vector3.up*corrected).y,binding.DefinitionId+"-1ml-after");
                        Capture(binding,binding.transform.TransformPoint(Vector3.up*old).y,binding.DefinitionId+"-1ml-before");
                        Capture(binding,binding.transform.TransformPoint(Vector3.up*corrected).y,binding.DefinitionId+"-1ml-after");
                    }
                }
            }
            Assert.That(rows.Count,Is.EqualTo(16));File.WriteAllLines(Path.Combine(Output,"levels.csv"),rows);
            Object.Destroy(guide.sharedMaterial);Object.Destroy(guide.gameObject);
        }
        [UnityTest] public IEnumerator RecordingLoopFadesInAndOutWithoutTheRejectedBanks()
        {
            yield return Load();var p=BarAudioCatalog.Load();Assert.That(p.banks.First(b=>b.id=="pourThin").paths,Is.Empty);Assert.That(p.banks.First(b=>b.id=="press").paths,Is.Empty);
            var sounds=new List<SoundObservation>();var pool=new SampledAudioPool(session.transform,session.Player.ViewCamera.transform,p,(id,sound)=>sounds.Add(sound));
            var rows=new List<string>{"phase,frame,gain"};float first=0,previous=0;var position=session.Player.ViewCamera.transform.position;
            for(int i=0;i<24;i++)
            {
                pool.BeginFrame();Assert.That(pool.Play("pour","pourThick","v",position,p.pourThickGain,loop:true,group:"pour"),Is.True);pool.EndFrame(1f/60);
                float value=pool.Sources.Max(s=>s.volume);if(i==0)first=value;Assert.That(value,Is.GreaterThanOrEqualTo(previous-1e-6));previous=value;rows.Add("attack,"+i+","+value);
            }
            Assert.That(first,Is.GreaterThan(0));Assert.That(first,Is.LessThan(previous*.5f));Assert.That(previous,Is.LessThan(.22f));float peak=previous;
            for(int i=0;i<24;i++)
            {
                pool.BeginFrame();pool.EndFrame(1f/60);float value=pool.Sources.Max(s=>s.volume);Assert.That(value,Is.LessThanOrEqualTo(previous+1e-6));Assert.That(previous-value,Is.LessThanOrEqualTo(peak/p.releaseSeconds/60+.00001));previous=value;rows.Add("release,"+i+","+value);
            }
            Assert.That(previous,Is.Zero);Assert.That(pool.ActiveCount,Is.Zero);Assert.That(sounds.First().clip,Does.StartWith("pour1Fill2-"));
            Assert.That(sounds.Any(s=>s.bank=="pourThin"||s.bank=="press"),Is.False);File.WriteAllLines(Path.Combine(Output,"audio-envelope.csv"),rows);pool.Dispose();
        }
    }
}
