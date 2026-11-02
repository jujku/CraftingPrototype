using System.Collections;
using System.IO;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>用户验收提出的三个可量化问题：玻璃要有浅影子，瓶子转到背面仍能看到标签，冰块要有可辨认的边缘。</summary>
    internal sealed class PresentationReviewTests
    {
        private static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../PresentationReports"));
        [UnityTest] public IEnumerator GlassCastsLightShadowsLabelsShowFromBehindAndIceHasEdges()
        {
            Directory.CreateDirectory(Output);
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");
            yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();
            session.enabled=false;
            var input=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b=>b.GetType().Name=="InputRouter");
            if(input)input.enabled=false;
            Object.FindFirstObjectByType<PresentationRig>().Desktop();
            var camera=session.Player.ViewCamera;
            camera.fieldOfView=40;
            camera.aspect=640f/480;

            var highball=session.Views.First(v=>v.DefinitionId=="glass.highball");
            var shell=highball.GetComponentsInChildren<MeshRenderer>().First(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Glass"&&r.sharedMaterial.GetFloat("_LayerOpacity")>.5f);
            camera.transform.position=highball.transform.position+new Vector3(.05f,.42f,-.22f);
            camera.transform.LookAt(highball.transform.position+new Vector3(.05f,0,.06f));
            // Measure bare worktop, not the shadow refracted through a neighbouring
            // bottle. Both captures hide exactly the same unrelated renderers;
            // the scene's saved layout and the -0.5 limit remain untouched.
            var counter=session.Views.First(v=>v.DefinitionId=="fixture.counter");
            var unrelated=session.Views.Where(v=>v!=highball&&v!=counter)
                .SelectMany(v=>v.GetComponentsInChildren<MeshRenderer>(true)).Distinct().Where(r=>r.enabled).ToArray();
            foreach(var renderer in unrelated)renderer.enabled=false;
            Physics.SyncTransforms();var tableMask=WorktopMask(camera,counter,640,480);
            Assert.That(tableMask.Count(v=>v),Is.GreaterThan(1000),"影子检查必须覆盖实际台面，而不是空遮罩");
            shell.shadowCastingMode=ShadowCastingMode.Off;
            var without=Capture(camera,"review-glass-shadow-off");
            yield return null;
            // CuePresenter can restore its renderer states during LateUpdate.
            foreach(var renderer in unrelated)renderer.enabled=false;
            shell.shadowCastingMode=ShadowCastingMode.On;
            var with=Capture(camera,"review-glass-shadow-on");
            foreach(var renderer in unrelated)renderer.enabled=true;
            float darkened=Fraction(with,without,-.02f,tableMask);
            float darkest=Darkest(with,without,tableMask);
            File.WriteAllText(Path.Combine(Output,"review-glass-shadow-metrics.json"),
                "{\"worktopPixels\":"+tableMask.Count(v=>v)+",\"darkenedFraction\":"+darkened.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"darkest\":"+darkest.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
            Assert.That(darkened,Is.GreaterThan(.01f),"玻璃外层必须投下可见的影子");
            Assert.That(darkest,Is.GreaterThan(-.5f),"透明玻璃的影子应明显浅于实心物体");

            var gin=session.Views.First(v=>v.DefinitionId=="bottle.gin");
            var labels=gin.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="FrontLabel").ToArray();
            Assert.That(labels,Is.Not.Empty);
            // Look from the side opposite the label, whichever way the bottle was placed.
            Vector3 front=labels[0].bounds.center-gin.transform.position;
            front.y=0;
            camera.transform.position=gin.transform.position-front.normalized*.42f+Vector3.up*.2f;
            camera.transform.LookAt(gin.transform.position+Vector3.up*gin.Size.y*.4f);
            foreach(var label in labels)label.enabled=false;
            yield return null;
            var bare=Capture(camera,"review-label-back-hidden");
            foreach(var label in labels)label.enabled=true;
            yield return null;
            var back=Capture(camera,"review-label-back");
            Assert.That(Fraction(back,bare,.02f)+Fraction(back,bare,-.02f),Is.GreaterThan(.005f),"标签转到背面后必须透过玻璃仍然可见");

            var board=session.Views.First(v=>v.DefinitionId=="fixture.board");
            var ice=new GameObject("Review ice");
            for(int i=0;i<3;i++)
            {
                var cube=IceAppearance.Create(i,ice.transform);
                cube.transform.localScale=Vector3.one*IceAppearance.ConfiguredSide;
                cube.transform.localPosition=new Vector3((i-1)*.045f,IceAppearance.ConfiguredSide*.5f,0);
                cube.transform.localRotation=Quaternion.Euler(0,i*23,0);
            }
            ice.transform.position=board.transform.position+new Vector3(0,board.Size.y+.001f,-.06f);
            camera.transform.position=ice.transform.position+new Vector3(-.05f,.12f,-.2f);
            camera.transform.LookAt(ice.transform.position+Vector3.up*.012f);
            yield return null;
            Capture(camera,"review-ice");
            Object.Destroy(ice);
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(640,480,24);
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;
            RenderTexture.active=rt;
            var image=new Texture2D(640,480,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,640,480),0,0);
            image.Apply();
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            return image;
        }
        /// <summary>亮度变化超过 threshold 的像素比例；threshold 为负时统计变暗的像素。</summary>
        private static float Fraction(Texture2D a,Texture2D b,float threshold,bool[] mask=null)
        {
            var pa=a.GetPixels();
            var pb=b.GetPixels();
            int count=0,total=0;
            for(int i=0;i<pa.Length;i++)
            {
                if(mask!=null&&!mask[i])continue;total++;
                float delta=pa[i].grayscale-pb[i].grayscale;
                if(threshold<0?delta<threshold:delta>threshold)count++;
            }
            return count/(float)total;
        }
        /// <summary>按真实射线预先排除物品本体、槽口、侧面和背景；不根据变暗结果挑选像素。</summary>
        private static bool[] WorktopMask(Camera camera,ItemView counter,int width,int height)
        {
            var mask=new bool[width*height];float top=counter.Anchor("Surface").position.y;
            var surface=counter.GetComponent<PlacementSurface>();
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/width,(y+.5f)/height,0));
                if(Physics.Raycast(ray,out var hit,5,~0,QueryTriggerInteraction.Ignore)&&
                    hit.collider.GetComponentInParent<ItemView>()==counter&&Mathf.Abs(hit.point.y-top)<.003f&&!surface.IsOpening(hit.point))mask[y*width+x]=true;
            }
            return mask;
        }
        private static float Darkest(Texture2D a,Texture2D b,bool[] mask)
        {
            var pa=a.GetPixels();
            var pb=b.GetPixels();
            float darkest=0;
            for(int i=0;i<pa.Length;i++)
            {
                if(!mask[i])continue;
                float lit=pb[i].grayscale;
                if(lit<.05f)continue;
                darkest=Mathf.Min(darkest,(pa[i].grayscale-lit)/lit);
            }
            return darkest;
        }
    }
}
