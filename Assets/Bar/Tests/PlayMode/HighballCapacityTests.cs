using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>以实际杯腔、倒料收据和库存核对空杯/四块冰的旧上限、新上限及满杯拒收。</summary>
    public sealed class HighballCapacityTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../HighballCapacityReports"));
        [UnityTest] public IEnumerator EmptyGlassCanContinuePast350AndStopsAt478()=>Check(0);
        [UnityTest] public IEnumerator FourIceCubesKeepTheirDisplacementAndStopAt478Occupied()=>Check(4);
        private static void Op(BarSession s,Operation op)=>typeof(WorldAdapter).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s.Adapter,new object[]{op});
        private static IEnumerator Check(int cubes)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);
            var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");var glass=(Glass)view.Item;var visual=view.GetComponent<ContainerVisual>();
            var bottle=(Bottle)s.Views.Single(v=>v.DefinitionId=="bottle.gin").Item;bottle.SetOpen(true);
            Assert.That(glass.CapacityMl,Is.EqualTo(478));Assert.That(s.Adapter.World.GetVessel(glass.CoreVesselId).capacity,Is.EqualTo(478));
            Assert.That(visual.Profile.CapacityMl,Is.EqualTo(478.047209).Within(.01));Assert.That(visual.Shape.VolumeMl,Is.EqualTo(visual.Profile.CapacityMl).Within(1));
            Assert.That(visual.NominalCapacityIsWorkingFill,Is.False);Assert.That(s.ToolIceCapacity(glass),Is.EqualTo(4));
            Assert.That(s.Hands.TryPickUp(glass),Is.True);
            var camera=s.Player.ViewCamera;camera.transform.rotation=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);camera.fieldOfView=32;camera.aspect=4f/3;
            view.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*.40f-Vector3.up*.075f,Quaternion.identity);s.GetComponent<HeldItemRendering>().Refresh();
            double iceGrams=cubes*s.Adapter.World.Data.icePortionGrams,iceMl=iceGrams/s.Adapter.World.Data.iceDensity;
            if(cubes>0)Op(s,new Operation{type="AddIce",to=glass.CoreVesselId,grams=iceGrams});s.Adapter.Advance(0);
            double stock=bottle.RemainingMl;
            var old=bottle.CreateLiquidRequest(glass,350-iceMl,125,0,0,0);s.Adapter.Submit(old);
            Assert.That(old.ActualAmount,Is.EqualTo(350-iceMl).Within(1e-8));Assert.That(glass.AvailableMl,Is.EqualTo(128).Within(1e-8));
            Assert.That(LiquidTransfer.CanTransfer(bottle,glass,s.Hands),Is.True,"原上限处必须仍能继续倒入");
            var rig=visual.GetComponent<LocalIceWorld>();s.Cues.Present(0);for(int i=0;i<180;i++)rig.Advance(1f/60);yield return null;
            float oldY=visual.HeightForLiquid(glass.Contents.LiquidMl);Capture(camera,$"ice{cubes}-legacy350-occupied");
            var fill=bottle.CreateLiquidRequest(glass,9999,125,0,0,0);s.Adapter.Submit(fill);s.Cues.Present(0);for(int i=0;i<180;i++)rig.Advance(1f/60);yield return null;
            Assert.That(fill.ActualAmount,Is.EqualTo(128).Within(1e-8));Assert.That(glass.AvailableMl,Is.EqualTo(0).Within(1e-8));
            Assert.That(glass.Contents.LiquidMl,Is.EqualTo(478-iceMl).Within(1e-8));Assert.That(glass.Contents.IceVolumeMl,Is.EqualTo(iceMl).Within(1e-8));
            Assert.That(stock-bottle.RemainingMl,Is.EqualTo(glass.Contents.LiquidMl).Within(1e-8));Assert.That(bottle.RemainingMl,Is.GreaterThan(0));
            Assert.That(LiquidTransfer.CanTransfer(bottle,glass,s.Hands),Is.False);Assert.That(LiquidTransfer.Request(bottle,glass,1,125,0,0),Is.Null);
            Assert.Throws<InvalidOperationException>(()=>bottle.CreateLiquidRequest(glass,1,125,0,0,0));
            Assert.That(rig.DisplayCount,Is.EqualTo(cubes));Assert.That(rig.DisplayCapped,Is.False);
            float newY=visual.HeightForLiquid(glass.Contents.LiquidMl);Assert.That(newY,Is.GreaterThan(oldY+.015f));Assert.That(newY,Is.LessThanOrEqualTo(visual.Shape.Top+.00001f));
            if(cubes==0)Assert.That(visual.Shape.Top-newY,Is.LessThan(.0002f));Capture(camera,$"ice{cubes}-new478-occupied");
            var state=s.Adapter.World.GetVessel(glass.CoreVesselId);Assert.That(state.OccupiedVolume,Is.EqualTo(478).Within(1e-8));
            File.WriteAllText(Path.Combine(Output,$"ice{cubes}-metrics.txt"),FormattableString.Invariant($"capacityMl={glass.CapacityMl}\ncavityMl={visual.Shape.VolumeMl}\nprofileMl={visual.Profile.CapacityMl}\niceGrams={iceGrams}\niceVolumeMl={iceMl}\nliquidMl={glass.Contents.LiquidMl}\noccupiedMl={state.OccupiedVolume}\nsourceRemainingMl={bottle.RemainingMl}\noldSurfaceY={oldY}\nnewSurfaceY={newY}\ncavityTopY={visual.Shape.Top}\n"));
            var mat=s.Views.Select(v=>v.Item).OfType<ServeMat>().Single();s.Clock.Begin(new ActionResolver().Register(new ServeAction()).Resolve(mat,s.Hands).Single());
            Assert.That(glass.IsServed,Is.True);var lines=s.Journal.ReadLines();new LogReplay().Run(lines);File.WriteAllLines(Path.Combine(Output,$"ice{cubes}-session.jsonl"),lines);
            Assert.That(s.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(s.Adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));
        }
        private static void Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
