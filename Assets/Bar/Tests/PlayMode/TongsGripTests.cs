using System.Collections;
using System.Collections.Generic;
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
    internal sealed class TongsGripTests
    {
        private static void Capture(BarSession s,ItemView tool,string name)
        {
            var go=new GameObject("Tongs evidence camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Player.ViewCamera);camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.135f;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=s.Player.ViewCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
            var centre=tool.transform.TransformPoint(new Vector3(0,.0055f,.015f));camera.transform.position=centre+tool.transform.TransformDirection(new Vector3(.13f,.24f,.08f));camera.transform.LookAt(centre,tool.transform.forward);
            var rt=RenderTexture.GetTemporary(800,600,24);var old=RenderTexture.active;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
            var image=new Texture2D(800,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();
            var path=Path.Combine(Application.dataPath,"../Tools/TongsGripFix");Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,name+".png"),image.EncodeToPNG());
            Object.Destroy(image);RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(go);
        }
        private static void ClearOfIce(Transform cube,MeshFilter[] jaws,MeshCollider[] colliders)
        {
            var ice=cube.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="IceSurface");var b=ice.sharedMesh.bounds;var corners=new Vector3[8];
            float tolerance=.0001f/Mathf.Max(.0001f,ice.transform.lossyScale.x);
            foreach(var jaw in jaws)foreach(var vertex in jaw.sharedMesh.vertices)
            {
                var p=ice.transform.InverseTransformPoint(jaw.transform.TransformPoint(vertex));
                bool inside=p.x>b.min.x+tolerance&&p.x<b.max.x-tolerance&&p.y>b.min.y+tolerance&&p.y<b.max.y-tolerance&&p.z>b.min.z+tolerance&&p.z<b.max.z-tolerance;
                Assert.That(inside,Is.False,jaw.name+" 夹爪/夹齿顶点穿入显示冰块");
            }
            for(int i=0;i<8;i++)corners[i]=ice.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            foreach(int bit in new[]{1,2,4})for(int i=0;i<8;i++)
            {
                if((i&bit)!=0)continue;var a=corners[i];var z=corners[i|bit];float length=Vector3.Distance(a,z);
                foreach(var collider in colliders)
                {
                    foreach(bool reverse in new[]{false,true})
                    {
                        var ray=new Ray(reverse?z:a,(reverse?a-z:z-a).normalized);
                        if(collider.Raycast(ray,out var hit,length))Assert.That(hit.distance<.0001f||hit.distance>length-.0001f,Is.True,"冰块边缘穿过真实夹爪三角面 "+collider.name);
                    }
                }
            }
        }
        [UnityTest] public IEnumerator TongsOpenClampAndReleaseActualIceWithoutIntersectingIt()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);double Now()=>s.EventTime(Time.realtimeSinceStartupAsDouble);
            void Aim(ItemView view){s.Player.AimAt(view.Item is Fixture?view.Anchor("Surface").position:view.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();s.Targeting();Assert.That(s.Target,Is.SameAs(view));}
            void Click(string action,ItemView target){Aim(target);int i=s.MenuOptions.ToList().FindIndex(o=>o.Action.Id==action);Assert.That(i,Is.GreaterThanOrEqualTo(0));s.SelectMenu(i,true);s.LeftButton(true,Now());s.LeftButton(false,Now()+.001);}
            var tool=View("tool.tongs");var rig=tool.GetComponent<TongsViewRig>();Assert.That(rig&&rig.Ready,Is.True);
            var hold=tool.Anchor("HoldPoint");var holdPosition=hold.localPosition;var holdRotation=hold.localRotation;var grip=tool.Anchor("Grip_R");var gripPosition=grip.localPosition;
            Aim(tool);s.RightButton(Now());yield return new WaitForSecondsRealtime(.25f);Assert.That(s.Hands.SideOf(tool.Item).HasValue,Is.True);Capture(s,tool,"01-empty");
            var jaws=tool.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.StartsWith("Arm")||m.name.StartsWith("Tooth")).ToArray();
            var colliders=new List<MeshCollider>();foreach(var jaw in jaws){var collider=jaw.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=jaw.sharedMesh;colliders.Add(collider);}
            int stock=((IceBin)View("fixture.icebin").Item).StockCubes;Click("ice.take",View("fixture.icebin"));
            int visibleFrames=0;float started=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-started<.45f)
            {
                yield return null;var holder=tool.transform.Find("Held ice (snapshot)");
                var cube=holder.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Ice cube (snapshot)"&&t.gameObject.activeInHierarchy);
                if(!cube)continue;Physics.SyncTransforms();ClearOfIce(cube,jaws,colliders.ToArray());visibleFrames++;
            }
            Assert.That(visibleFrames,Is.GreaterThan(2));Assert.That(rig.OpeningDegrees,Is.GreaterThan(1));Assert.That(((Tool)tool.Item).LoadedCubes,Is.EqualTo(1));Assert.That(((IceBin)View("fixture.icebin").Item).StockCubes,Is.EqualTo(stock-1));
            Assert.That(hold.localPosition,Is.EqualTo(holdPosition));Assert.That(Quaternion.Angle(hold.localRotation,holdRotation),Is.LessThan(.001));Assert.That(grip.localPosition,Is.EqualTo(gripPosition));Capture(s,tool,"02-loaded");
            foreach(var collider in colliders)Object.Destroy(collider);yield return null;
            Click("ice.add",View("glass.highball"));yield return new WaitForSecondsRealtime(.5f);
            float deliveryDeadline=Time.realtimeSinceStartup+4;
            while(s.IceDeliveryBusy&&Time.realtimeSinceStartup<deliveryDeadline)yield return null;
            Assert.That(s.IceDeliveryBusy,Is.False,"伸夹、松夹和回手须在期限内完成");
            Assert.That(((Tool)tool.Item).LoadedCubes,Is.Zero);Assert.That(rig.OpeningDegrees,Is.LessThan(.01f));Assert.That(s.Clock.Error,Is.Null);Capture(s,tool,"03-released");s.Cancel(Now());
        }
    }
}
