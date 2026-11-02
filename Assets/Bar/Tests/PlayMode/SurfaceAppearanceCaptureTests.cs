using System.Collections;
using System.IO;
using System.Linq;
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
    /// <summary>壶的起雾→结霜逐档截图、回温化水，以及打开的壶内壁不结霜。真实 Unity 渲染，夹具温度不写回核心。</summary>
    public sealed class SurfaceAppearanceCaptureTests
    {
        private BarSession s;private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../SurfaceCaptures"));
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            s=Object.FindFirstObjectByType<BarSession>();s.enabled=s.Cues.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b is LocalIceWorld||b.GetType().Name=="InputRouter")b.enabled=false;
            foreach(var v in s.Views)if(v.Item is not Fixture&&v.DefinitionId!="glass.highball"&&v.DefinitionId!="glass.coupe"&&!v.DefinitionId.StartsWith("shaker."))v.gameObject.SetActive(false);
            Directory.CreateDirectory(Output);
        }
        private void Seat(ItemView shaker,ItemView part){var anchor=shaker.Anchor(part.Item.Location.Anchor);part.transform.SetParent(anchor,false);var scale=anchor.lossyScale;part.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;}
        private Texture2D Capture(string name){s.GetComponent<HeldItemRendering>().Refresh();var t=PourCaptureTests.Capture(s.Player.ViewCamera,"surface-"+name);File.Copy(Path.Combine(PourCaptureTests.Output,"surface-"+name+".png"),Path.Combine(Output,name+".png"),true);return t;}
        [UnityTest] public IEnumerator FrostLineFollowsWallTemperatureAndStaysOutside()
        {
            yield return Load();var view=s.Views.First(v=>v.Item is CobblerShaker);var sh=(CobblerShaker)view.Item;var world=s.Adapter.World;
            world.Pour(sh.Body.CoreVesselId,"spirit",45);world.Pour(sh.Body.CoreVesselId,"lemon",25);s.Adapter.Advance(0);
            var strainer=s.Views.First(v=>v.DefinitionId=="shaker.strainer");var cap=s.Views.First(v=>v.DefinitionId=="shaker.cap");
            sh.Attach((ShakerStrainer)strainer.Item);sh.Attach((ShakerCap)cap.Item);Seat(view,strainer);Seat(view,cap);
            var visual=view.GetComponentInChildren<ContainerVisual>();var shells=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&!r.name.Contains("Liquid")&&!r.name.Contains("Ice")&&r.name!="InnerCavity").ToArray();
            var effect=new SurfaceAppearance(view,visual,sh.Body,shells);var state=world.GetVessel(sh.Body.CoreVesselId).Snapshot();state.iceGrams=120;/* an iced tin: fog and frost need ice inside (2026-10-28) */float height=view.transform.position.y+.05f;
            var c=s.Player.ViewCamera;c.fieldOfView=36;c.aspect=640f/480;var point=view.transform.position+Vector3.up*.11f;
            c.transform.position=point+new Vector3(.06f,.03f,-.46f);c.transform.LookAt(point);
            float[] temperatures={25,11,-2,-6};float[] frost=new float[temperatures.Length];
            for(int k=0;k<temperatures.Length;k++){state.vesselTemperature=temperatures[k];for(int i=0;i<200;i++)effect.Present(state,height,null,.1f);frost[k]=effect.State.Frost+effect.State.Mist;Object.Destroy(Capture("frost-"+temperatures[k]+"C"));}
            state.vesselTemperature=-4;for(int i=0;i<200;i++)effect.Present(state,height,null,.1f);
            var close=view.transform.position+Vector3.up*.08f;c.transform.position=close+new Vector3(.03f,.03f,-.17f);c.transform.LookAt(close);Object.Destroy(Capture("frost-close-minus4C"));
            c.transform.position=point+new Vector3(.06f,.03f,-.46f);c.transform.LookAt(point);
            // Open tin seen from above at -4 °C: the inner wall and floor must stay bare metal.
            state.vesselTemperature=-4;for(int i=0;i<200;i++)effect.Present(state,height,null,.1f);strainer.gameObject.SetActive(false);cap.gameObject.SetActive(false);
            var top=view.transform.position+Vector3.up*.16f;c.transform.position=top+new Vector3(.05f,.16f,-.12f);c.transform.LookAt(view.transform.position+Vector3.up*.06f);Object.Destroy(Capture("frost-open-inside"));
            // Back on the bar for 4 s at room temperature: frost melts back to wet steel from the top and the patch edges.
            strainer.gameObject.SetActive(true);cap.gameObject.SetActive(true);state.vesselTemperature=25;for(int i=0;i<40;i++)effect.Present(state,height,null,.1f);
            c.transform.position=point+new Vector3(.06f,.03f,-.46f);c.transform.LookAt(point);Object.Destroy(Capture("frost-melting"));
            File.WriteAllText(Path.Combine(Output,"frost-line.txt"),string.Join("\n",temperatures.Select((t,k)=>t+"C fog+frost="+frost[k])));
            effect.Dispose();
            for(int k=1;k<frost.Length;k++)Assert.That(frost[k],Is.GreaterThan(frost[k-1]),"越冷雾和霜越多");
        }
        [UnityTest] public IEnumerator GinTonicBubblesCloseAndFar()
        {
            yield return Load();var view=s.Views.First(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;var world=s.Adapter.World;var visual=view.GetComponentInChildren<ContainerVisual>();
            world.Pour(glass.CoreVesselId,"spirit",45);world.Pour(glass.CoreVesselId,"tonic",120);world.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=80});s.Adapter.Advance(0);visual.GetComponent<LocalIceWorld>().Advance(1f/60);
            s.Cues.Present(0);var state=world.GetVessel(glass.CoreVesselId).Snapshot();float height=visual.transform.TransformPoint(Vector3.up*visual.HeightForLiquid(state.volumeMl)).y;
            var bubbles=visual.GetComponent<BubbleStreams>();for(int i=0;i<240;i++)bubbles.Present(6,12,height,Vector3.up,0,true,1f/60,visual.LiquidVolume.sharedMaterial);
            var c=s.Player.ViewCamera;c.fieldOfView=36;c.aspect=640f/480;var point=view.transform.position+Vector3.up*.07f;
            c.transform.position=point+new Vector3(.03f,.02f,-.19f);c.transform.LookAt(point);Object.Destroy(Capture("gnt-close"));
            c.transform.position=point+new Vector3(.055f,.035f,-.34f);c.transform.LookAt(point);Object.Destroy(Capture("gnt-far"));
            File.WriteAllText(Path.Combine(Output,"gnt-bubbles.txt"),"alive="+bubbles.ActiveCount+" emitted="+bubbles.EmittedCount);
            Assert.That(bubbles.ActiveCount,Is.GreaterThan(0));
        }
    }
}
