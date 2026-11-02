using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>真实场景近看空杯、斜看厚底、少量液体与落冰；不更换灯光或反射环境。</summary>
    public sealed class GlassProfileVisualTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GlassProfileReports"));
        [UnityTest]public IEnumerator EmptyGlassThicknessAndContentsFollowTheNewCavity()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);var camera=s.Player.ViewCamera;camera.transform.rotation=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);camera.fieldOfView=32;camera.aspect=4f/3;
            string report="";
            foreach(string id in new[]{"glass.highball","glass.coupe"})
            {
                var view=s.Views.Single(v=>v.DefinitionId==id);var visual=view.GetComponent<ContainerVisual>();var glass=(Glass)view.Item;
                Assert.That(s.Hands.TryPickUp(glass),Is.True);view.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*.37f-Vector3.up*.075f,Quaternion.identity);
                s.GetComponent<HeldItemRendering>().Refresh();yield return null;Capture(camera,id+"-empty-front");
                var origin=view.transform.position;view.transform.rotation=Quaternion.AngleAxis(24,camera.transform.right);yield return null;Capture(camera,id+"-empty-tilted");view.transform.rotation=Quaternion.identity;
                s.Adapter.World.Pour(glass.CoreVesselId,"spirit",1);s.Adapter.Advance(0);yield return null;
                Capture(camera,id+"-1ml");Assert.That(visual.HeightForLiquid(1),Is.GreaterThan(visual.Shape.Bottom));
                s.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=s.Adapter.World.Data.icePortionGrams});s.Adapter.Advance(0);
                var rig=visual.GetComponent<LocalIceWorld>();string hash=LogReplay.SnapshotHash(s.Adapter.World);for(int i=0;i<180;i++)rig.Advance(1f/60);yield return null;
                Capture(camera,id+"-ice");
                foreach(var display in visual.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="IceSurface"&&m.gameObject.activeInHierarchy))foreach(var v in display.sharedMesh.vertices)
                {
                    var q=visual.transform.InverseTransformPoint(display.transform.TransformPoint(v));Assert.That(q.y,Is.GreaterThanOrEqualTo(visual.Shape.Bottom-.0005f),"冰块不能穿进新厚底");
                    if(q.y<=visual.Shape.Top)Assert.That(new Vector2(q.x-visual.Shape.Axis.x,q.z-visual.Shape.Axis.y).magnitude,Is.LessThanOrEqualTo(visual.Shape.RadiusAt(q.y)+.001f));
                }
                Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                report+=id+" floor="+visual.Shape.Bottom+" top="+visual.Shape.Top+" brimMl="+visual.Profile.CapacityMl+" oneMlY="+visual.Profile.HeightForMl(1)+" cubeCapacity="+rig.Capacity+"\n";
                view.gameObject.SetActive(false);
            }
            File.WriteAllText(Path.Combine(Output,"contents.txt"),report);
        }
        private static void Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
