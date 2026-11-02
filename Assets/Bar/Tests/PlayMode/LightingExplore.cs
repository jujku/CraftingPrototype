using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// 调灯光柔和度和检视姿态用的探索截图，只在显式点名时运行。读 HoldFraming/lighting.txt，每行 "名字 key=value ..."
    /// （light 灯光倍数、inner 内锥比例、top 台面光滑度上限、shell 量酒器光滑度上限、refl 反射强度、bt/bi 泛光阈值/强度、
    /// x/y/z 检视位置、view 检视角；省略为不改），每行截开场视角和检视大杯、小杯（1920×1080，检视两张裁到量酒器附近），
    /// 拼成 lighting.png；同时记录画面最亮 1% 像素的亮度。
    /// </summary>
    [Explicit]public sealed class LightingExplore
    {
        private BarSession s;
        private double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
        [UnityTest] public IEnumerator RenderCandidates()
        {
            var file=Path.Combine(HoldFramingCaptures.Output,"lighting.txt");if(!File.Exists(file))Assert.Ignore("no lighting.txt");
            var tiles=new List<(Texture2D image,Rect crop)>();var log=new List<string>();
            var inspect=HoldViewSettings.Load().inspect;var center=inspect.center;float view=inspect.viewDegrees;
            foreach(var line in File.ReadAllLines(file).Where(l=>l.Trim().Length>0&&!l.StartsWith("#")))
            {
                var f=line.Split(new[]{' '},System.StringSplitOptions.RemoveEmptyEntries);var map=f.Skip(1).Select(x=>x.Split('=')).ToDictionary(x=>x[0],x=>float.Parse(x[1],CultureInfo.InvariantCulture));
                inspect.center=new Vector3(map.TryGetValue("x",out var px)?px:center.x,map.TryGetValue("y",out var py)?py:center.y,map.TryGetValue("z",out var pz)?pz:center.z);inspect.viewDegrees=map.TryGetValue("view",out var vd)?vd:view;
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;s=Object.FindFirstObjectByType<BarSession>();
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;s.Player.ViewCamera.aspect=16f/9;
                foreach(var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {if(map.TryGetValue("light",out var k))l.intensity*=k;if(l.type==LightType.Spot&&map.TryGetValue("inner",out var r))l.innerSpotAngle=l.spotAngle*r;}
                if(map.TryGetValue("refl",out var refl)){RenderSettings.reflectionIntensity=refl;foreach(var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))p.intensity=refl;}
                var copies=new Dictionary<Material,Material>();
                foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    string key=r.name=="WalnutTop"?"top":r.name=="Shell"&&r.GetComponentInParent<ItemView>()?.DefinitionId=="jigger.dual"?"shell":null;if(key==null||!map.TryGetValue(key,out var cap))continue;
                    var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++){var m=mats[i];if(!m||!m.HasProperty("_Smoothness"))continue;if(!copies.TryGetValue(m,out var c)){c=new Material(m);c.SetFloat("_Smoothness",Mathf.Min(cap,m.GetFloat("_Smoothness")));copies[m]=c;}mats[i]=c;}r.sharedMaterials=mats;
                }
                var volume=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).First();if(volume.profile.TryGet<Bloom>(out var bloom)){if(map.TryGetValue("bt",out var bt))bloom.threshold.Override(bt);if(map.TryGetValue("bi",out var bi))bloom.intensity.Override(bi);}
                yield return new WaitForSecondsRealtime(.5f);var row=f[0];
                tiles.Add(Shot(f[0]+"-start",null,ref row));
                var jig=s.Views.First(v=>v.DefinitionId=="jigger.dual");s.Player.AimAt(jig.transform.position+Vector3.up*jig.Size.y*.5f);Physics.SyncTransforms();s.Targeting();s.RightButton(Now());yield return new WaitForSecondsRealtime(1f);
                s.Inspect(Now());yield return new WaitForSecondsRealtime(.55f);tiles.Add(Shot(f[0]+"-big",jig,ref row));yield return new WaitForSecondsRealtime(.6f);tiles.Add(Shot(f[0]+"-small",jig,ref row));
                log.Add(row);yield return new WaitForSecondsRealtime(2.4f);
            }
            inspect.center=center;inspect.viewDegrees=view;
            int w=640,h=360,columns=3,rows=(tiles.Count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rows,TextureFormat.RGB24,false);
            for(int i=0;i<tiles.Count;i++){var (t,c)=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rows-1-i/columns)*h+y,t.GetPixelBilinear(c.x+c.width*(x+.5f)/w,c.y+c.height*(y+.5f)/h));Object.Destroy(t);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"lighting.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"lighting-log.txt"),log);
        }
        private (Texture2D,Rect) Shot(string name,ItemView focus,ref string row)
        {
            s.GetComponent<HeldItemRendering>().Refresh();var camera=s.Player.ViewCamera;
            var rt=RenderTexture.GetTemporary(1920,1080,24);camera.aspect=16f/9;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(1920,1080,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1920,1080),0,0);t.Apply();RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"light-"+name+".png"),t.EncodeToPNG());
            var lum=t.GetPixels().Select(c=>.2126f*c.r+.7152f*c.g+.0722f*c.b).OrderByDescending(v=>v).ToArray();int clipped=lum.Count(v=>v>.97f);
            row+=" | "+name+" p99 "+lum[lum.Length/100].ToString("F3")+" clipped "+(100f*clipped/lum.Length).ToString("F2")+"%";
            var crop=new Rect(0,0,1,1);
            if(focus)
            {
                var b=focus.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).Select(r=>r.bounds).Aggregate((a,c)=>{a.Encapsulate(c);return a;});
                Vector2 min=Vector2.one,max=Vector2.zero;
                for(int i=0;i<8;i++){var v=camera.WorldToViewportPoint(b.center+Vector3.Scale(b.extents,new Vector3(i&1,(i>>1)&1,(i>>2)&1)*2-Vector3.one));min=Vector2.Min(min,v);max=Vector2.Max(max,v);}
                float height=Mathf.Clamp((max.y-min.y)*1.25f,.2f,1),width=height*9/16f*16f/9;var mid=(min+max)*.5f;
                crop=new Rect(Mathf.Clamp(mid.x-width*.5f,0,1-width),Mathf.Clamp(mid.y-height*.5f,0,1-height),width,height);
            }
            return (t,crop);
        }
    }
}
