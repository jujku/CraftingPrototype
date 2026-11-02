using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>每件可拿物品在左右手里的画面截图与取景指标（露出比例、占屏高度、距离），作为持握视图的验收依据。</summary>
    public sealed class HoldFramingCaptures
    {
        internal static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../HoldFraming"));
        internal struct Framing {public float Visible,Height,Width,Distance;public Rect Rect;}
        /// <summary>物品全部渲染器包围盒的屏幕投影：露出比例按面积，占屏高度按画面内的部分。</summary>
        internal static Framing Measure(Camera camera,ItemView view)
        {
            var renderers=view.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r is MeshRenderer).ToArray();
            float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue,depth=0;int count=0;
            foreach(var r in renderers)
            {
                var b=r is MeshRenderer mesh?mesh.localBounds:r.bounds;var t=r.transform;
                for(int i=0;i<8;i++)
                {
                    var p=camera.WorldToViewportPoint(t.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                    if(p.z<=0)continue;minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);depth+=p.z;count++;
                }
            }
            if(count==0)return default;
            var rect=Rect.MinMaxRect(minX,minY,maxX,maxY);float area=rect.width*rect.height;
            float ix=Mathf.Max(0,Mathf.Min(1,maxX)-Mathf.Max(0,minX)),iy=Mathf.Max(0,Mathf.Min(1,maxY)-Mathf.Max(0,minY));
            return new Framing{Visible=area>0?ix*iy/area:0,Height=iy,Width=ix,Distance=depth/count,Rect=rect};
        }
        [UnityTest] public IEnumerator EveryHoldableItemInEitherHand()=>Run(false);
        /// <summary>对照：2026-10-28 之前的固定手锚点（左 (-.19,-.27,.55)、右 (.20,-.27,.55)，不旋转），只在显式点名时运行，输出到 HoldFraming/legacy。</summary>
        [UnityTest,Explicit] public IEnumerator LegacyFixedAnchors()=>Run(true);
        private IEnumerator Run(bool legacy)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();var output=legacy?Path.Combine(Output,"legacy"):Output;
            if(legacy){yield return null;session.GetComponent<HoldViewRig>().enabled=false;session.Player.LeftAnchor.localPosition=new Vector3(-.19f,-.27f,.55f);session.Player.RightAnchor.localPosition=new Vector3(.20f,-.27f,.55f);session.Player.LeftAnchor.localRotation=session.Player.RightAnchor.localRotation=Quaternion.identity;}
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;
            var counterView=session.Views.First(v=>v.Item is BarCounter);var counter=(BarCounter)counterView.Item;var camera=session.Player.ViewCamera;camera.aspect=16f/9;
            // Look at the work area on the counter top, as a player would while holding things.
            var top=counterView.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;});session.Player.AimAt(new Vector3(top.center.x,top.max.y,top.center.z));
            Directory.CreateDirectory(output);var rows=new List<string>{"item,hand,visible,screenHeight,screenWidth,distance,left,bottom,right,top,gripMin,gripMax"};var tiles=new List<(string,Texture2D)>();
            var candidates=session.Views.Where(v=>v.Item.Parent==null&&session.Hands.CanPickUp(v.Item)).GroupBy(v=>v.DefinitionId).Select(g=>g.First()).OrderBy(v=>v.DefinitionId).ToList();
            foreach(var view in candidates)
            {
                Assert.That(session.Hands.TryPickUp(view.Item),Is.True,view.DefinitionId);
                for(int pass=0;pass<2;pass++)
                {
                    if(pass==1)Assert.That(session.Hands.TrySwap(),Is.True);
                    yield return new WaitForSecondsRealtime(.8f);
                    var side=session.Hands.SideOf(view.Item).Value;var m=Measure(camera,view);var grip=HoldViewRig.GripBounds(view,side,Quaternion.identity);string hand=side==HandSide.Left?"L":"R";
                    rows.Add(string.Join(",",view.DefinitionId,hand,F(m.Visible),F(m.Height),F(m.Width),F(m.Distance),F(m.Rect.xMin),F(m.Rect.yMin),F(m.Rect.xMax),F(m.Rect.yMax),V(grip.min),V(grip.max)));
                    session.GetComponent<HeldItemRendering>().Refresh();tiles.Add((view.DefinitionId+" "+hand,Capture(camera,"hold-"+view.DefinitionId+"-"+hand,output)));
                }
                var held=session.Hands.SideOf(view.Item).Value;Assert.That(session.Hands.TryPlace(held,counter),Is.True);yield return new WaitForSecondsRealtime(.6f);
            }
            File.WriteAllLines(Path.Combine(output,"framing.csv"),rows);
            // Contact sheet: four tiles per row at half size.
            int w=384,h=216,columns=4,rowsCount=(tiles.Count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rowsCount,TextureFormat.RGB24,false);
            for(int i=0;i<tiles.Count;i++){var t=tiles[i].Item2;for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rowsCount-1-i/columns)*h+y,t.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(t);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(output,"sheet.png"),sheet.EncodeToPNG());File.WriteAllLines(Path.Combine(output,"sheet-order.txt"),tiles.Select((t,i)=>i+": "+t.Item1));Object.Destroy(sheet);
            Assert.That(candidates.Count,Is.GreaterThan(5));
        }
        /// <summary>16:9 截图（960×540），含手持物前景相机。</summary>
        internal static Texture2D Capture(Camera camera,string name,string folder=null)
        {
            folder??=Output;Directory.CreateDirectory(folder);var rt=RenderTexture.GetTemporary(960,540,24);camera.aspect=16f/9;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());return image;
        }
        private static string F(float v)=>v.ToString("F3",CultureInfo.InvariantCulture);
        private static string V(Vector3 v)=>F(v.x)+" "+F(v.y)+" "+F(v.z);
    }
}
