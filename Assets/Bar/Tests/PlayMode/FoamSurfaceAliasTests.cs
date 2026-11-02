using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>斜视纯苏打：用同帧泡沫开关区分液面中央的雪花与真实上升气泡，不换模型或关闭碳酸存量。</summary>
    public sealed class FoamSurfaceAliasTests
    {
        [UnityTest]public IEnumerator CarbonationDoesNotPaintStaticAcrossTheSurfaceInterior()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=s.Cues.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");var visual=view.GetComponent<ContainerVisual>();var glass=(Glass)view.Item;
            s.Adapter.World.Pour(glass.CoreVesselId,"soda",350);s.Adapter.Advance(0);Assert.That(s.Hands.TryPickUp(glass),Is.True);
            view.transform.SetPositionAndRotation(view.transform.position+Vector3.up*.7f,Quaternion.identity);
            var camera=s.Player.ViewCamera;camera.fieldOfView=32;camera.aspect=4f/3;s.GetComponent<HeldItemRendering>().Refresh();yield return null;yield return null;s.Cues.Present(0);
            var material=visual.LiquidVolume.sharedMaterial;float level=material.GetFloat("_FillHeight"),band=material.GetFloat("_FoamBand");
            Assert.That(material.GetFloat("_FoamHeight"),Is.LessThan(.00001f));Assert.That(material.GetFloat("_CO2"),Is.GreaterThan(1));
            var centre=new Vector3(view.transform.position.x,level,view.transform.position.z);float radius=visual.Shape.RadiusAt(visual.transform.InverseTransformPoint(centre).y);
            var bubbles=visual.GetComponent<BubbleStreams>();for(int i=0;i<180;i++)bubbles.Present(6,20,level,Vector3.up,0,true,1f/60,material);
            int count=bubbles.ActiveCount;Assert.That(count,Is.GreaterThan(100));var pr=bubbles.Sites[0].System.GetComponent<ParticleSystemRenderer>();pr.enabled=false;
            string hash=LogReplay.SnapshotHash(s.Adapter.World);var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../FoamSurfaceReports"));Directory.CreateDirectory(output);
            var rows=new List<string>{"angle,interiorPixels,foamChangedInterior,changedRatio"};float worst=0;
            var surface=visual.GetComponent<LiquidSurface>().Renderer.GetComponent<MeshFilter>();
            foreach(float angle in new[]{-24f,-12,-6,6,12,24,40,24.025f,24.05f})
            {
                camera.transform.position=centre+Quaternion.AngleAxis(angle,Vector3.right)*new Vector3(0,0,-.36f);camera.transform.LookAt(centre);yield return null;
                var with=Capture(camera,output,angle+"-foam");material.SetFloat("_FoamBand",0);var without=Capture(camera,output,angle+"-no-foam");material.SetFloat("_FoamBand",band);
                var uv=surface.sharedMesh.vertices.Select(v=>camera.WorldToViewportPoint(surface.transform.TransformPoint(v))).ToArray();
                int x0=Mathf.Max(0,Mathf.FloorToInt(uv.Min(v=>v.x)*960)),x1=Mathf.Min(959,Mathf.CeilToInt(uv.Max(v=>v.x)*960));
                int y0=Mathf.Max(0,Mathf.FloorToInt(uv.Min(v=>v.y)*720)),y1=Mathf.Min(719,Mathf.CeilToInt(uv.Max(v=>v.y)*720));
                var a=with.GetPixels32();var b=without.GetPixels32();int total=0,changed=0;var plane=new Plane(Vector3.up,centre);
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/960,(y+.5f)/720,0));if(!plane.Raycast(ray,out float distance))continue;
                    if(Vector3.Distance(ray.GetPoint(distance),centre)>radius*.55f)continue;total++;int i=y*960+x;
                    if(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>12)changed++;
                }
                Assert.That(total,Is.GreaterThan(30));float ratio=(float)changed/total;worst=Mathf.Max(worst,ratio);rows.Add(angle+","+total+","+changed+","+ratio);
                Object.Destroy(with);Object.Destroy(without);
            }
            pr.enabled=true;Object.Destroy(Capture(camera,output,"complete-with-rising-bubbles"));
            File.WriteAllLines(Path.Combine(output,"metrics.csv"),rows);File.WriteAllText(Path.Combine(output,"particles.txt"),"count="+count+" remaining="+bubbles.ActiveCount);
            Assert.That(bubbles.ActiveCount,Is.EqualTo(count));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            Assert.That(worst,Is.LessThan(.05f),"纯苏打泡沫应留在液面边缘，中央不能铺满高频白色雪花");
        }
        private static Texture2D Capture(Camera camera,string output,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;
            var t=new Texture2D(960,720,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,960,720),0,0);t.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),t.EncodeToPNG());RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);return t;
        }
    }
}
