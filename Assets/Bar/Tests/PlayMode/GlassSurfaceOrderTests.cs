using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>木色台面/黑背景下重排同一杯的三角形：杯口和厚底不能随网格提交顺序被后壁覆盖。</summary>
    public sealed class GlassSurfaceOrderTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../GlassOrderReports"));
        [UnityTest]public IEnumerator ClosedGlassKeepsItsNearRimAndBaseWhenTriangleOrderChanges()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var camera=s.Player.ViewCamera;camera.fieldOfView=35;camera.aspect=4f/3;Directory.CreateDirectory(Output);
            var rows=new List<string>();float worst=0;int redPixels=0,motionFrames=0;string hash=LogReplay.SnapshotHash(s.Adapter.World);
            foreach(string id in new[]{"glass.highball","glass.coupe"})
            {
                var view=s.Views.Single(v=>v.DefinitionId==id);var home=view.transform.position;Assert.That(s.Hands.TryPickUp(view.Item),Is.True);
                var filters=view.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Shell"||f.name=="FootStem"||f.name=="Presentation glass interior").ToArray();
                var originals=filters.Select(f=>f.sharedMesh).ToArray();var reversed=new Dictionary<Mesh,Mesh>();
                foreach(var original in originals.Distinct())
                {
                    var copy=Object.Instantiate(original);var t=copy.triangles;var backward=new int[t.Length];for(int j=0;j<t.Length;j+=3){backward[j]=t[t.Length-j-3];backward[j+1]=t[t.Length-j-2];backward[j+2]=t[t.Length-j-1];}copy.triangles=backward;reversed[original]=copy;
                }
                try
                {
                    foreach(string background in new[]{"wood","black"})
                    {
                        view.transform.SetPositionAndRotation(home+(background=="black"?Vector3.up*.65f:Vector3.zero),Quaternion.identity);
                        var centre=view.transform.position+Vector3.up*(id=="glass.highball"?.0775f:.063f);
                        camera.transform.position=centre+(background=="wood"?new Vector3(.025f,.24f,-.24f):new Vector3(.025f,.015f,-.38f));camera.transform.LookAt(centre);
                        s.GetComponent<HeldItemRendering>().Refresh();yield return null;
                        var normal=Capture(camera,id+"-"+background+"-normal");
                        for(int i=0;i<filters.Length;i++)filters[i].sharedMesh=reversed[originals[i]];
                        var other=Capture(camera,id+"-"+background+"-reordered");
                        float difference=0;var a=normal.GetPixels32();var b=other.GetPixels32();for(int i=0;i<a.Length;i++)difference+=(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b))/(255f*3*a.Length);
                        rows.Add(id+","+background+","+difference.ToString("R",System.Globalization.CultureInfo.InvariantCulture));worst=Mathf.Max(worst,difference);Object.Destroy(normal);Object.Destroy(other);
                        for(int i=0;i<filters.Length;i++)filters[i].sharedMesh=originals[i];
                        view.Highlight(true);Object.Destroy(Capture(camera,id+"-"+background+"-hover"));view.Highlight(false);
                        foreach(float angle in new[]{-18f,-9,0,9,18,9,0,-9})
                        {
                            view.transform.rotation=Quaternion.AngleAxis(angle,camera.transform.right);yield return null;
                            var frame=Capture(camera,id+"-"+background+"-motion-"+motionFrames.ToString("D2"));
                            foreach(var pixel in frame.GetPixels32())if(pixel.r>225&&pixel.g<45&&pixel.b<45)redPixels++;
                            motionFrames++;Object.Destroy(frame);
                        }
                        view.transform.rotation=Quaternion.identity;
                    }
                }
                finally{for(int i=0;i<filters.Length;i++)if(filters[i])filters[i].sharedMesh=originals[i];foreach(var m in reversed.Values)Object.Destroy(m);}
                view.gameObject.SetActive(false);
            }
            File.WriteAllLines(Path.Combine(Output,"order.csv"),new[]{"item,background,meanRgbDifference"}.Concat(rows));
            File.WriteAllText(Path.Combine(Output,"motion.txt"),"frames="+motionFrames+" saturatedRedPixels="+redPixels);
            Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            Assert.That(worst,Is.LessThan(.0001f),"相同实体杯的三角形顺序不能改变前沿与厚底；应由最近可见面决定");
            Assert.That(redPixels,Is.Zero,"转动采样中不能出现孤立的饱和红闪；木色背景不在该阈值内");
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return image;
        }
        /// <summary>低分辨率、细小视角移动：分别开关局部高光过滤和 Bloom，测量厚底边缘红色分量变化。</summary>
        [UnityTest]public IEnumerator ProfileGlassSpecularMotionWithAndWithoutBloom()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");Assert.That(s.Hands.TryPickUp(view.Item),Is.True);view.transform.rotation=Quaternion.identity;
            var camera=s.Player.ViewCamera;camera.fieldOfView=55;camera.aspect=4f/3;var centre=view.transform.position+Vector3.up*.0775f;
            var position=centre+new Vector3(.025f,.32f,-.36f);camera.transform.position=position;camera.transform.LookAt(centre);var rotation=camera.transform.rotation;
            var volume=Object.FindFirstObjectByType<PresentationRig>().GetComponent<Volume>();Assert.That(volume.profile.TryGet<Bloom>(out var bloom),Is.True);bool bloomWas=bloom.active,bloomOverride=bloom.intensity.overrideState;float bloomIntensity=bloom.intensity.value;
            var renderers=view.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Glass").ToArray();var original=renderers.Select(r=>r.sharedMaterials).ToArray();var materials=new List<Material>();
            for(int i=0;i<renderers.Length;i++){var copies=original[i].Select(m=>new Material(m)).ToArray();materials.AddRange(copies);renderers[i].sharedMaterials=copies;}
            Directory.CreateDirectory(Output);var rows=new List<string>{"aa,bloom,meanRedChromaStep,maxRedChromaStep,brightRedPixels,chromaticAberrationIntensity,actualBloomIntensity"};string hash=LogReplay.SnapshotHash(s.Adapter.World);
            try
            {
                foreach(bool aa in new[]{false,true})foreach(bool enabled in new[]{false,true})
                {
                    foreach(var m in materials)m.SetFloat("_ProfileSpecularAA",aa?1:0);bloom.active=true;bloom.intensity.overrideState=true;bloom.intensity.value=enabled?bloomIntensity:0;
                    camera.transform.rotation=rotation;yield return null;
                    var warm=RenderTexture.GetTemporary(480,360,24);for(int w=0;w<3;w++)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=warm});RenderTexture.ReleaseTemporary(warm);
                    float sum=0,max=0;int samples=0,red=0;float[] previous=null;
                    for(int step=0;step<32;step++)
                    {
                        camera.transform.rotation=Quaternion.AngleAxis((step-15.5f)*.025f,Vector3.up)*rotation;
                        var rt=RenderTexture.GetTemporary(480,360,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var saved=RenderTexture.active;RenderTexture.active=rt;
                        var frame=new Texture2D(480,360,TextureFormat.RGB24,false);frame.ReadPixels(new Rect(0,0,480,360),0,0);frame.Apply();RenderTexture.active=saved;RenderTexture.ReleaseTemporary(rt);
                        var chroma=new float[48];
                        for(int j=0;j<48;j++)
                        {
                            float theta=j*2*Mathf.PI/48;var point=view.transform.TransformPoint(new Vector3(.029f*Mathf.Cos(theta),.023f,.029f*Mathf.Sin(theta)));var screen=camera.WorldToViewportPoint(point);int x=Mathf.Clamp(Mathf.RoundToInt(screen.x*480),1,478),y=Mathf.Clamp(Mathf.RoundToInt(screen.y*360),1,358);
                            var c=frame.GetPixel(x,y);chroma[j]=c.r-(c.g+c.b)*.5f;if(c.r>.88f&&c.g<.18f&&c.b<.18f)red++;
                            if(previous!=null){float d=Mathf.Abs(chroma[j]-previous[j]);sum+=d;max=Mathf.Max(max,d);samples++;}
                        }
                        previous=chroma;if(step==0||step==31)File.WriteAllBytes(Path.Combine(Output,"bloom-aa-"+aa+"-bloom-"+enabled+"-"+step+".png"),frame.EncodeToPNG());Object.Destroy(frame);
                    }
                    float actualBloom=VolumeManager.instance.stack.GetComponent<Bloom>().intensity.value;
                    rows.Add(aa+","+enabled+","+(sum/Mathf.Max(1,samples)).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+max.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+red+","+VolumeManager.instance.stack.GetComponent<ChromaticAberration>().intensity.value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+actualBloom.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    Assert.That(actualBloom,Is.EqualTo(enabled?bloomIntensity:0).Within(.0001f),"必须验证最终Volume stack，而不是仅改变一个Volume的active标志");
                }
                File.WriteAllLines(Path.Combine(Output,"bloom-aa.csv"),rows);Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            }
            finally{bloom.active=bloomWas;bloom.intensity.value=bloomIntensity;bloom.intensity.overrideState=bloomOverride;for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].sharedMaterials=original[i];foreach(var m in materials)Object.Destroy(m);}
        }
    }
}
