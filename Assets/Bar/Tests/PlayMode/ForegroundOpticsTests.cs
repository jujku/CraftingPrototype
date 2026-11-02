using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>只比较量酒器/载冰高球杯的同姿态前景切换，不运行倒液或热力学流程。</summary>
    public sealed class ForegroundOpticsTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../ForegroundOpticsReports"));
        [UnityTest]public IEnumerator TableJiggerAndIcedGlassKeepAppearanceWhenPouringBegins()
        {
            Directory.CreateDirectory(Output);var rows=new List<string>{"item,roiDifference,iceDifference,icePixels,lowerLuminance,shader"};var errors=new List<string>();
            foreach(string id in new[]{"jigger.dual","glass.highball"})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
                var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
                var target=s.Views.First(v=>v.DefinitionId==id);var bottle=s.Views.First(v=>v.DefinitionId=="bottle.gin");((Bottle)bottle.Item).SetOpen(true);
                if(target.Item is Glass glass)
                {s.Adapter.World.Pour(glass.CoreVesselId,"spirit",70);s.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=80});s.Adapter.Advance(0);yield return new WaitForSecondsRealtime(.8f);}
                s.Cues.Present(0);s.Cues.enabled=false;
                foreach(var ice in target.GetComponentsInChildren<LocalIceWorld>())ice.enabled=false;
                Assert.That(s.Hands.TryPickUp(bottle.Item),Is.True);s.GetComponent<HoldViewRig>().enabled=false;
                foreach(var view in s.Views)view.enabled=false;
                var camera=s.Player.ViewCamera;camera.fieldOfView=40;camera.aspect=4f/3;
                camera.transform.position=target.transform.position+new Vector3(.055f,.18f,-.30f);camera.transform.LookAt(target.transform.position+Vector3.up*.07f);
                bottle.transform.position=camera.transform.TransformPoint(new Vector3(.6f,.05f,.3f));
                var render=s.GetComponent<HeldItemRendering>();render.Refresh();yield return null;
                var iceRenderers=target.GetComponentsInChildren<MeshRenderer>().Where(m=>m.enabled&&m.sharedMaterial&&m.sharedMaterial.shader.name=="Bar/Ice").ToArray();
                if(id=="glass.highball")Assert.That(iceRenderers.Length,Is.GreaterThan(0));
                var warm=Shot(camera,id+"-warm");Object.Destroy(warm);var before=Shot(camera,id+"-before");
                foreach(var ice in iceRenderers)ice.enabled=false;var hidden=Shot(camera,id+"-hidden");foreach(var ice in iceRenderers)ice.enabled=true;
                var position=target.transform.position;var rotation=target.transform.rotation;string hash=LogReplay.SnapshotHash(s.Adapter.World);
                var driver=s.gameObject.AddComponent<PourPresentationDriver>();driver.Session=s;driver.Initialize();typeof(BarSession).GetField("pourPresentation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s,driver);
                var option=new ActionResolver().Register(new BottlePourAction(ItemCatalog.LoadControls(s.ControlsJson.text))).Resolve(target.Item,s.Hands).First(o=>o.Actor==bottle.Item);
                driver.Presentation.Prepare(option);render.Refresh();var after=Shot(camera,id+"-after");
                Assert.That(target.transform.position,Is.EqualTo(position));Assert.That(target.transform.rotation,Is.EqualTo(rotation));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                Vector3 bottom=camera.WorldToViewportPoint(position),top=camera.WorldToViewportPoint(position+Vector3.up*target.Size.y);
                int x0=Mathf.Clamp((int)(Mathf.Min(bottom.x,top.x)*640)-60,0,639),x1=Mathf.Clamp((int)(Mathf.Max(bottom.x,top.x)*640)+60,0,639),y0=Mathf.Clamp((int)(Mathf.Min(bottom.y,top.y)*480)-5,0,479),y1=Mathf.Clamp((int)(Mathf.Max(bottom.y,top.y)*480)+5,0,479);
                float sum=0,iceSum=0,lower=0;int count=0,iceCount=0,lowerCount=0;
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {var a=before.GetPixel(x,y);var b=after.GetPixel(x,y);var h=hidden.GetPixel(x,y);float d=(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/3;sum+=d;count++;
                    if(Mathf.Max(Mathf.Abs(a.r-h.r),Mathf.Max(Mathf.Abs(a.g-h.g),Mathf.Abs(a.b-h.b)))>.025f){iceSum+=d;iceCount++;}
                    if(y>y0+10&&y<y0+(y1-y0)*.35f&&Mathf.Abs(x-(x0+x1)/2)<12){lower+=a.grayscale;lowerCount++;}}
                float difference=sum/Mathf.Max(1,count),iceDifference=iceSum/Mathf.Max(1,iceCount);string shader=target.GetComponentsInChildren<MeshRenderer>().First(m=>m.name=="Shell").sharedMaterial.shader.name;
                rows.Add(id+","+difference+","+iceDifference+","+iceCount+","+(lower/Mathf.Max(1,lowerCount))+","+shader);
                if(difference>.008f)errors.Add(id+" promotion changes target pixels: "+difference);
                if(id=="glass.highball"&&(iceCount<30||iceDifference>.015f))errors.Add("ice disappears/changes across promotion: "+iceCount+" / "+iceDifference);
                Object.Destroy(before);Object.Destroy(after);Object.Destroy(hidden);
            }
            File.WriteAllLines(Path.Combine(Output,"metrics.csv"),rows);Assert.That(errors,Is.Empty,string.Join("; ",errors));
        }
        private static Texture2D Shot(Camera camera,string label)
        {var image=PourCaptureTests.Capture(camera,"foreground-optics-"+label);File.Copy(Path.Combine(PourCaptureTests.Output,"foreground-optics-"+label+".png"),Path.Combine(Output,label+".png"),true);return image;}
    }
}
