using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>瓶形修订的同机位原生对照，不换灯光、不改变瓶内库存；旧版只在显式点名时捕获。</summary>
    public sealed class BottleRefineCaptures
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../BottleRefineReports"));
        [UnityTest,Explicit]public IEnumerator CaptureBefore()=>Capture("before");
        [UnityTest]public IEnumerator CaptureAfter()=>Capture("after");
        [UnityTest]public IEnumerator BottleShouldersDoNotBlockRaysThroughEmptySpace()
        {
            var rows=new List<string>();
            foreach(string scene in new[]{"Bar","BarPresentation"})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/"+scene+".unity");yield return null;yield return null;
                var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;var glass=s.Views.Single(v=>v.DefinitionId=="glass.highball");s.Player.AimAt(glass.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();s.Targeting();rows.Add(scene+" original collider target="+s.Target.DefinitionId);
                if(s.Views.Where(v=>v.Item is Bottle).All(v=>!v.GetComponent<BoxCollider>()))Assert.That(s.Target,Is.SameAs(glass),"制作后的碰撞轮廓本身必须让真实射线通过");
                foreach(var view in s.Views.Where(v=>v.Item is Bottle))
                {
                    var shell=view.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="BlueGlassShell"||f.name=="GlassShell");var box=view.GetComponent<BoxCollider>();if(box)box.enabled=false;var actual=shell.gameObject.AddComponent<MeshCollider>();actual.sharedMesh=shell.sharedMesh;
                }
                Physics.SyncTransforms();s.Targeting();rows.Add(scene+" actual shell target="+s.Target.DefinitionId);
                Assert.That(s.Target,Is.SameAs(glass),"按真实瓶形，空瓶颈包围盒不能挡住目标");
            }
            Directory.CreateDirectory(Output);File.WriteAllLines(Path.Combine(Output,"targeting.txt"),rows);
        }
        private static IEnumerator Capture(string stage)
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var camera=s.Player.ViewCamera;camera.aspect=4f/3;var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
            var bottles=s.Views.Where(v=>v.Item is Bottle).OrderBy(v=>v.DefinitionId).ToArray();var centre=bottles.Select(v=>v.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/bottles.Length+Vector3.up*.11f;
            var toward=Vector3.ProjectOnPlane(position-centre,Vector3.up).normalized;Directory.CreateDirectory(Path.Combine(Output,stage));
            s.enabled=false;camera.transform.position=centre+toward*.95f+Vector3.up*.40f;camera.transform.LookAt(centre);camera.fieldOfView=45;yield return null;Shot(camera,stage,"table-four");
            var rows=new List<string>{"id,capacityMl,remainingMl,width,height,depth,cavityMl,fullSurfaceLocalY"};
            foreach(var view in bottles)
            {
                camera.transform.position=view.transform.position+toward*.23f+Vector3.up*.15f;camera.transform.LookAt(view.transform.position+Vector3.up*.022f);camera.fieldOfView=42;yield return null;Shot(camera,stage,view.DefinitionId+"-base");
                camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;s.enabled=true;
                Assert.That(s.Hands.TryPickUp(view.Item),Is.True);yield return new WaitForSecondsRealtime(1.2f);s.GetComponent<HeldItemRendering>().Refresh();Shot(camera,stage,view.DefinitionId+"-held");
                var bottle=(Bottle)view.Item;var liquid=view.GetComponent<BottleLiquidView>();
                rows.Add(FormattableString.Invariant($"{view.DefinitionId},{bottle.CapacityMl},{bottle.RemainingMl},{view.Size.x},{view.Size.y},{view.Size.z},{liquid.Profile.CapacityMl},{liquid.Profile.HeightForMl(bottle.RemainingMl)}"));
                Assert.That(bottle.RemainingMl,Is.EqualTo(bottle.CapacityMl),"截图必须用真实满瓶，不能抽掉糖浆做可见液面");
                s.enabled=false;var parent=view.transform.parent;view.transform.SetParent(null,true);var shell=view.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="BlueGlassShell"||r.name=="GlassShell");
                var target=shell.bounds.center;camera.transform.position=target+toward*.31f+Vector3.up*.08f;camera.transform.LookAt(target);camera.fieldOfView=32;s.GetComponent<HeldItemRendering>().Refresh();yield return null;
                Shot(camera,stage,view.DefinitionId+"-label");
                if(stage=="after")
                {
                    var label=view.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="FrontLabel"||f.name=="OriginalLabel");var normal=label.sharedMesh.normals.Aggregate(Vector3.zero,(a,b)=>a+b).normalized;normal=label.transform.TransformDirection(normal).normalized;
                    var at=label.GetComponent<MeshRenderer>().bounds.center;camera.transform.position=at+normal*.24f+view.transform.up*.012f;camera.transform.LookAt(at,view.transform.up);camera.fieldOfView=32;Shot(camera,stage,view.DefinitionId+"-label-front");
                    if(view.DefinitionId=="bottle.syrup")
                    {
                        var surface=at;surface.y=liquid.Level;camera.transform.position=surface+normal*.24f+Vector3.up*.10f;camera.transform.LookAt(surface);camera.fieldOfView=32;Shot(camera,stage,"cane-full-surface");
                    }
                }
                camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;view.transform.SetParent(parent,true);s.enabled=true;
                var counter=(BarCounter)s.Views.Single(v=>v.Item is BarCounter).Item;Assert.That(s.Hands.TryPlace(s.Hands.SideOf(view.Item).Value,counter),Is.True);yield return new WaitForSecondsRealtime(.6f);
            }
            File.WriteAllLines(Path.Combine(Output,stage,"full-bottles.csv"),rows);
        }
        private static void Shot(Camera camera,string stage,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);camera.aspect=4f/3;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,stage,name+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
