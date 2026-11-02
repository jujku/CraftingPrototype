using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>
    /// 调持握取景用的探索截图，只在显式点名时运行。读 HoldFraming/explore.txt，每行
    /// "物品id euler.x euler.y euler.z [screenHeight center.x center.y topLimit faceYaw]"，临时覆盖该物品的取景配置后截右手画面，拼成 explore.png。
    /// </summary>
    [Explicit]public sealed class HoldFramingExplore
    {
        [UnityTest] public IEnumerator RenderCandidates()
        {
            var file=Path.Combine(HoldFramingCaptures.Output,"explore.txt");if(!File.Exists(file))Assert.Ignore("no explore.txt");
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b.GetType().Name=="InputRouter")b.enabled=false;
            var counterView=session.Views.First(v=>v.Item is BarCounter);var counter=(BarCounter)counterView.Item;var camera=session.Player.ViewCamera;camera.aspect=16f/9;
            var top=counterView.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;});session.Player.AimAt(new Vector3(top.center.x,top.max.y,top.center.z));
            var settings=HoldViewSettings.Load();var tiles=new List<Texture2D>();var log=new List<string>();
            foreach(var line in File.ReadAllLines(file).Where(l=>l.Trim().Length>0&&!l.StartsWith("#")))
            {
                var f=line.Split(new[]{' '},System.StringSplitOptions.RemoveEmptyEntries);float N(int i,float d)=>f.Length>i?float.Parse(f[i],CultureInfo.InvariantCulture):d;
                var view=session.Views.First(v=>v.DefinitionId==f[0]&&(v.Item.Parent==null||session.Hands.SideOf(v.Item).HasValue));var profile=settings.For(f[0]);
                var saved=(profile.euler,profile.screenHeight,profile.center,profile.topLimit,profile.faceYaw);
                profile.euler=new Vector3(N(1,0),N(2,0),N(3,0));profile.screenHeight=N(4,profile.screenHeight);profile.center=new Vector2(N(5,profile.center.x),N(6,profile.center.y));profile.topLimit=N(7,profile.topLimit);profile.faceYaw=N(8,profile.faceYaw);
                if(!session.Hands.SideOf(view.Item).HasValue)Assert.That(session.Hands.TryPickUp(view.Item),Is.True,f[0]);
                if(session.Hands.SideOf(view.Item)!=Bar.Data.HandSide.Right)Assert.That(session.Hands.TrySwap(),Is.True);
                yield return new WaitForSecondsRealtime(.8f);
                var m=HoldFramingCaptures.Measure(camera,view);log.Add(line+" -> visible "+m.Visible.ToString("F2")+" height "+m.Height.ToString("F2")+" distance "+m.Distance.ToString("F2"));
                session.GetComponent<HeldItemRendering>().Refresh();tiles.Add(HoldFramingCaptures.Capture(camera,"explore-"+tiles.Count));
                (profile.euler,profile.screenHeight,profile.center,profile.topLimit,profile.faceYaw)=saved;
                var held=session.Hands.SideOf(view.Item).Value;Assert.That(session.Hands.TryPlace(held,counter),Is.True);yield return new WaitForSecondsRealtime(.3f);
            }
            int w=480,h=270,columns=3,rows=(tiles.Count+columns-1)/columns;var sheet=new Texture2D(w*columns,h*rows,TextureFormat.RGB24,false);
            for(int i=0;i<tiles.Count;i++){var t=tiles[i];for(int y=0;y<h;y++)for(int x=0;x<w;x++)sheet.SetPixel((i%columns)*w+x,(rows-1-i/columns)*h+y,t.GetPixelBilinear((x+.5f)/w,(y+.5f)/h));Object.Destroy(t);}
            sheet.Apply();File.WriteAllBytes(Path.Combine(HoldFramingCaptures.Output,"explore.png"),sheet.EncodeToPNG());Object.Destroy(sheet);
            File.WriteAllLines(Path.Combine(HoldFramingCaptures.Output,"explore-log.txt"),log);
        }
    }
}
