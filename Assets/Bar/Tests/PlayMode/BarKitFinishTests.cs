using System.Collections;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>真实场景验证嵌入设施、贴地、四颗独立原料和初始带盖瓶口。</summary>
    internal sealed class BarKitFinishTests
    {
        private static void Capture(Camera camera, string name, Vector3 position, Vector3 target)
        {
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            camera.transform.position = position;
            camera.transform.LookAt(target);
            string folder = Path.Combine(Application.dataPath, "../BarKitFinishReports");
            Directory.CreateDirectory(folder);
            var rt = RenderTexture.GetTemporary(1200,750,24);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(1200,750,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1200,750),0,0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
                Object.Destroy(image);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
            }
        }
        [UnityTest] public IEnumerator IntegratedFacilitiesGroundingFourLemonsAndOpenCapsUseRealScene()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
            yield return null;
            var session = Object.FindFirstObjectByType<BarSession>();
            ItemView View(string id) => session.Views.First(v => v.DefinitionId == id);
            var counter = View("fixture.counter");
            float surface = counter.Anchor("Surface").position.y;
            float floor = GameObject.Find("Floor").GetComponent<Renderer>().bounds.max.y;
            float bottom = counter.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).Min(r=>r.bounds.min.y);
            Assert.That(bottom, Is.EqualTo(floor).Within(.0001f));
            Assert.That(session.Player.ViewCamera.transform.position.y-surface, Is.EqualTo(.55f).Within(.0001f));
            Assert.That(GameObject.Find("Side worktop"), Is.Null);
            foreach (var view in session.Views.Where(v=>v.Item is not Fixture && v.Item.Parent==null))
                Assert.That(counter.GetComponent<PlacementSurface>().CanPlace(view.transform.position, view.Size), Is.True,
                    "初始物品不能跨越槽口或台面边缘："+view.InstanceId);
            var board = View("fixture.board");
            var lemons = session.Views.Where(v=>v.Item is LemonHalf).ToArray();
            Assert.That(lemons.Length, Is.EqualTo(4));
            Assert.That(lemons.Select(v=>v.InstanceId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(lemons.Select(v=>v.Item).Distinct().Count(), Is.EqualTo(4));
            foreach (var lemon in lemons)
            {
                Vector3 local = board.transform.InverseTransformPoint(lemon.transform.position);
                Assert.That(Mathf.Abs(local.x)+lemon.Size.x/2, Is.LessThan(board.Size.x/2));
                Assert.That(Mathf.Abs(local.z)+lemon.Size.z/2, Is.LessThan(board.Size.z/2));
                Assert.That(local.y, Is.EqualTo(board.Size.y+.001f).Within(.001f));
                Assert.That(((LemonHalf)lemon.Item).JuiceRemainingMl, Is.InRange(20,30));
            }
            foreach (string id in new[]{"fixture.icebin","fixture.sink"})
            {
                var fixture = View(id);
                var mouth = fixture.Anchor("Surface").position;
                Assert.That(mouth.y, Is.EqualTo(surface).Within(.0001f));
                Assert.That(counter.GetComponent<PlacementSurface>().IsOpening(mouth), Is.True);
                session.Player.AimAt(mouth);
                Physics.SyncTransforms();
                session.Targeting();
                Assert.That(session.Target, Is.SameAs(fixture), "开口位置必须命中实际设施 "+id);
            }
            foreach (var bottle in session.Views.Where(v=>v.Item is Bottle))
            {
                Assert.That(((Bottle)bottle.Item).IsOpen, Is.False);
                foreach (var t in bottle.GetComponentsInChildren<Transform>(true))
                    if (t.name=="BottleCap" || t.name=="CrownOrScrewCap" || t.name=="Cap")
                        Assert.That(t.gameObject.activeInHierarchy, Is.True);
            }
            Capture(session.Player.ViewCamera,"01-counter-overview",new Vector3(0,1.65f,-1.65f),new Vector3(0,.55f,.65f));
            Capture(session.Player.ViewCamera,"02-integrated-openings",new Vector3(0,1.8f,-.25f),new Vector3(0,surface,.54f));
            Capture(session.Player.ViewCamera,"03-four-lemon-halves",new Vector3(.72f,1.4f,.42f),board.transform.position);
        }
    }
}
