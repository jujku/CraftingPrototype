using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>
    /// 用户反馈 Highball 厚底在晃动时，内底圆的左右两端会出现灯带一样的闪光。高球杯放在台面上，
    /// 相机在 40°、55°、65° 俯角、0.32 m 处绕杯每步 0.1° 扫 ±12°（像玩家转头看杯子），每帧统计杯底附近过曝（接近纯白）的像素，
    /// 并与"关掉内底界面"的同一扫描对照。只读画面，不改模型或核心。
    /// </summary>
    public sealed class GlassBaseSparkleTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GlassBaseReports"));
        [UnityTest]public IEnumerator HighballBaseDoesNotFlashWhileTheViewMoves()=>Check("glass.highball");
        [UnityTest]public IEnumerator GinBaseDoesNotFlashWhileTheViewMoves()=>Check("bottle.gin");
        [UnityTest]public IEnumerator SyrupBaseDoesNotFlashWhileTheViewMoves()=>Check("bottle.syrup");
        [UnityTest]public IEnumerator SodaBaseDoesNotFlashWhileTheViewMoves()=>Check("bottle.soda");
        [UnityTest]public IEnumerator TonicBaseDoesNotFlashWhileTheViewMoves()=>Check("bottle.tonic");
        private static IEnumerator Check(string id)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=s.Views.Where(v=>v.DefinitionId==id).OrderBy(v=>v.InstanceId).First();var shell=view.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Shell"||r.name=="BlueGlassShell"||r.name=="GlassShell");
            string output=id=="glass.highball"?Output:Path.Combine(Output,id);var camera=s.Player.ViewCamera;camera.fieldOfView=60;camera.aspect=4f/3;Directory.CreateDirectory(output);
            var bottom=view.transform.position;var rows=new List<string>{"variant,elevation,yaw,hotPixels,brightest"};var report=new List<string>();
            var rt=RenderTexture.GetTemporary(960,720,24);var image=new Texture2D(960,720,TextureFormat.RGB24,false);var worst=new Dictionary<string,(int hot,float yaw,float elevation,Color32[] pixels)>();
            foreach(string variant in new[]{"current","no-interface"})
            {
                int frames=0,flashing=0,peak=0;
                foreach(float elevation in new[]{40f,55f,65f})for(float yaw=-12;yaw<=12.001f;yaw+=.1f)
                {
                    var material=shell.sharedMaterial;var optics=material.GetVector("_ShamOptics");
                    if(variant=="no-interface")material.SetVector("_ShamOptics",new Vector4(optics.x,optics.y,optics.z,0));
                    var look=bottom+Vector3.up*.03f;camera.transform.position=look+Quaternion.Euler(elevation,yaw,0)*new Vector3(0,0,-.32f);camera.transform.LookAt(look);
                    s.GetComponent<HeldItemRendering>().Refresh();
                    RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
                    var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();RenderTexture.active=previous;
                    if(variant=="no-interface")material.SetVector("_ShamOptics",optics);
                    // The base region: the bottom 4 cm of the glass on screen, with a margin.
                    var box=ScreenBox(camera,view.transform,image.width,image.height);var pixels=image.GetPixels32();int hot=0;float brightest=0;
                    for(int y=box.yMin;y<box.yMax;y++)for(int x=box.xMin;x<box.xMax;x++){var c=pixels[y*image.width+x];int low=Mathf.Min(c.r,Mathf.Min(c.g,c.b));if(low>=235)hot++;brightest=Mathf.Max(brightest,(c.r+c.g+c.b)/765f);}
                    rows.Add(variant+","+elevation+","+yaw.ToString("F1",CultureInfo.InvariantCulture)+","+hot+","+brightest.ToString("F3",CultureInfo.InvariantCulture));
                    frames++;if(hot>=12)flashing++;peak=Mathf.Max(peak,hot);
                    if(!worst.TryGetValue(variant,out var w)||hot>w.hot)worst[variant]=(hot,yaw,elevation,pixels);
                    yield return null;
                }
                report.Add(variant+": frames "+frames+", frames with >= 12 near-white pixels at the base "+flashing+", worst "+peak+" (yaw "+worst[variant].yaw.ToString("F1")+", elevation "+worst[variant].elevation+")");
            }
            foreach(var pair in worst){image.SetPixels32(pair.Value.pixels);image.Apply();File.WriteAllBytes(Path.Combine(output,"worst-"+pair.Key+".png"),image.EncodeToPNG());}
            RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);
            File.WriteAllLines(Path.Combine(output,"sweep.csv"),rows);File.WriteAllLines(Path.Combine(output,"sweep.txt"),report);
            Assert.That(worst["current"].hot,Is.LessThan(12),"杯底在视角移动时不应出现过曝闪点；"+string.Join(" | ",report));
        }
        private static RectInt ScreenBox(Camera camera,Transform glass,int width,int height)
        {
            float xMin=float.MaxValue,yMin=float.MaxValue,xMax=float.MinValue,yMax=float.MinValue;
            for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI/4;var p=glass.position+new Vector3(Mathf.Cos(angle)*.045f,(i<8?0:.045f),Mathf.Sin(angle)*.045f);
                var v=camera.WorldToViewportPoint(p);xMin=Mathf.Min(xMin,v.x);xMax=Mathf.Max(xMax,v.x);yMin=Mathf.Min(yMin,v.y);yMax=Mathf.Max(yMax,v.y);
            }
            int x0=Mathf.Clamp(Mathf.FloorToInt(xMin*width),0,width),x1=Mathf.Clamp(Mathf.CeilToInt(xMax*width),0,width),y0=Mathf.Clamp(Mathf.FloorToInt(yMin*height),0,height),y1=Mathf.Clamp(Mathf.CeilToInt(yMax*height),0,height);
            return new RectInt(x0,y0,x1-x0,y1-y0);
        }
    }
}
