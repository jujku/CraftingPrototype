using System.Collections;
using System.IO;
using System.Linq;
using Bar.Unity;
using Bar.Domain;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>P 段同机位基准截图；只建立临时测试物体，不保存场景。</summary>
    public sealed class PourCaptureTests
    {
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../PourReports"));
        public static Texture2D Capture(Camera camera,string name)
        {
            Directory.CreateDirectory(Output);
            var rt=RenderTexture.GetTemporary(640,480,24);
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(640,480,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());return image;
        }
        [UnityTest] public IEnumerator FourIceViewsAndBottles()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Object.FindFirstObjectByType<PresentationRig>().Desktop();
            var camera=session.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=640f/480;
            var board=session.Views.First(v=>v.DefinitionId=="fixture.board");
            var ice=new GameObject("P review ice");
            for(int i=0;i<3;i++){var cube=IceAppearance.Create(i,ice.transform);cube.transform.localScale=Vector3.one*IceAppearance.ConfiguredSide;cube.transform.localPosition=new Vector3((i-1)*.045f,IceAppearance.ConfiguredSide*.5f,0);cube.transform.localRotation=Quaternion.Euler(0,i*23,0);}
            ice.transform.position=board.transform.position+new Vector3(0,board.Size.y+.001f,-.06f);
            camera.transform.position=ice.transform.position+new Vector3(-.05f,.12f,-.2f);camera.transform.LookAt(ice.transform.position+Vector3.up*.012f);
            yield return null;Capture(camera,"ice-table");Object.Destroy(ice);
            foreach(string id in new[]{"glass.highball","shaker.cobbler"})
            {
                var view=session.Views.FirstOrDefault(v=>v.DefinitionId==id);if(!view)continue;
                Container vessel=view.Item is Glass g?g:(view.Item as CobblerShaker)?.Body;
                session.Adapter.World.Apply(new Operation{type="PourSource",to=vessel.CoreVesselId,ingredient="spirit",ml=70});
                session.Adapter.World.Apply(new Operation{type="AddIce",to=vessel.CoreVesselId,grams=80});session.Adapter.Advance(0);
                camera.transform.position=view.transform.position+new Vector3(-.04f,.21f,-.18f);camera.transform.LookAt(view.transform.position+Vector3.up*view.Size.y*.42f);
                yield return new WaitForSeconds(.6f);Capture(camera,"ice-"+id);
                foreach(var r in view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Ice"))r.enabled=false;
                Capture(camera,"no-ice-"+id);
            }
            var tongs=session.Views.First(v=>v.Item is IceTongs);typeof(Tool).GetProperty("LoadedCubes").SetValue(tongs.Item,1);
            camera.transform.position=tongs.transform.position+new Vector3(-.02f,.13f,-.18f);camera.transform.LookAt(tongs.Anchor("HoldPoint").position);
            yield return new WaitForSeconds(.5f);Capture(camera,"ice-tongs");
            var scoop=session.Views.First(v=>v.Item is IceScoop);typeof(Tool).GetProperty("LoadedCubes").SetValue(scoop.Item,1);
            camera.transform.position=scoop.transform.position+new Vector3(-.03f,.14f,-.18f);camera.transform.LookAt(scoop.Anchor("HoldPoint").position);
            yield return new WaitForSeconds(.5f);Capture(camera,"ice-scoop-light");
            Assert.That(scoop.GetComponentsInChildren<IceTransmissionPatch>().Any(p=>p.HasContact),Is.True,"冰铲的真实碗面应接收到透光斑");
            var scoopPatches=scoop.GetComponentsInChildren<IceTransmissionPatch>();scoop.gameObject.SetActive(false);
            Assert.That(scoopPatches.All(p=>!p.HasContact),Is.True,"冰/工具隐藏时独立光斑不能残留");scoop.gameObject.SetActive(true);
            foreach(var bottle in session.Views.Where(v=>v.Item is Bottle))
            {
                camera.transform.position=bottle.transform.position+new Vector3(-.10f,.22f,-.34f);camera.transform.LookAt(bottle.transform.position+Vector3.up*bottle.Size.y*.48f);
                yield return null;Capture(camera,bottle.DefinitionId+"-full");
            }
            Assert.That(File.Exists(Path.Combine(Output,"ice-table.png")),Is.True);
        }
    }
}


