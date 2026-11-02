using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed class ScoopFitTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../ScoopFitReports"));
        private static int Intersections(Transform cube,MeshFilter[] metal,MeshCollider[] colliders)
        {
            var mesh=cube.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="IceSurface");var box=mesh.sharedMesh.bounds;
            float tolerance=.0001f/mesh.transform.lossyScale.x;int violations=0;
            foreach(var surface in metal)foreach(var v in surface.sharedMesh.vertices)
            {var p=mesh.transform.InverseTransformPoint(surface.transform.TransformPoint(v));if(p.x>box.min.x+tolerance&&p.x<box.max.x-tolerance&&p.y>box.min.y+tolerance&&p.y<box.max.y-tolerance&&p.z>box.min.z+tolerance&&p.z<box.max.z-tolerance)violations++;}
            var corners=new Vector3[8];for(int i=0;i<8;i++)corners[i]=mesh.transform.TransformPoint(box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
            foreach(int bit in new[]{1,2,4})for(int i=0;i<8;i++)if((i&bit)==0)
            {
                var a=corners[i];var b=corners[i|bit];float distance=Vector3.Distance(a,b);
                foreach(var collider in colliders)foreach(bool reverse in new[]{false,true})
                {var ray=new Ray(reverse?b:a,(reverse?a-b:b-a).normalized);if(collider.Raycast(ray,out var hit,distance)&&hit.distance>.0001f&&hit.distance<distance-.0001f)violations++;}
            }
            return violations;
        }
        private static void Capture(BarSession session,ItemView tool,string name,Vector3 offset)
        {
            var go=new GameObject("Scoop diagnostic camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Player.ViewCamera);camera.enabled=false;camera.cullingMask=-1;camera.orthographic=true;camera.orthographicSize=.11f;camera.aspect=4f/3;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
            var centre=tool.transform.TransformPoint(new Vector3(0,.035f,.04f));camera.transform.position=centre+tool.transform.TransformDirection(offset);camera.transform.LookAt(centre,tool.transform.up);
            var rt=RenderTexture.GetTemporary(640,480,24);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(640,480,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(go);
        }
        [UnityTest] public IEnumerator ScoopKeepsOneTwoAndThreeWholeBlocksClearOfTheRealMetalAtAllHeldAngles()
        {
            Directory.CreateDirectory(Output);var rows=new List<string>{"count,frame,metalIntersections,coreUnchanged"};int oldIntersections=0;
            foreach(int wanted in new[]{2,1})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
                var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;session.Cues.enabled=false;
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter"||b is LocalIceWorld)b.enabled=false;
                var tool=session.Views.Single(v=>v.DefinitionId=="tool.scoop");var scoop=(IceScoop)tool.Item;var bin=(IceBin)session.Views.Single(v=>v.Item is IceBin).Item;
                Assert.That(session.Hands.TryPickUp(scoop),Is.True);new TakeIceAction(()=>0).Begin(scoop,bin,session.Hands);Assert.That(scoop.LoadedCubes,Is.EqualTo(2));yield return null;yield return null;
                if(wanted<2)
                {
                    var glass=(Glass)session.Views.Single(v=>v.DefinitionId=="glass.highball").Item;
                    session.Adapter.World.Apply(new Operation{type="AddIce",to=glass.CoreVesselId,grams=(wanted+2)*40});session.Adapter.Advance(0);
                    var resolver=(ActionResolver)typeof(BarSession).GetField("resolver",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
                    var origin=tool.LoadedIceOrigin;session.Clock.Begin(resolver.Resolve(glass,session.Hands).Single(o=>o.Action.Id=="ice.add"));session.Clock.Active.End();
                    Assert.That(Vector3.Distance(session.IceOrigins[glass.CoreVesselId],origin),Is.LessThan(.00001f));yield return null;yield return null;
                }
                Assert.That(scoop.LoadedCubes,Is.EqualTo(wanted));var holder=tool.transform.Find("Held ice (snapshot)");var ice=holder.GetComponent<IceVisual>();var rig=tool.GetComponent<ScoopIceRig>();Assert.That(rig.Ready,Is.True);
                var hold=tool.Anchor("HoldPoint");var holdPosition=hold.localPosition;var holdRotation=hold.localRotation;var grip=tool.Anchor("Grip_R");var gripPosition=grip.localPosition;
                var metal=tool.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="ScoopBowl"||f.name=="Handle").ToArray();var colliders=metal.Select(f=>{var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;return c;}).ToArray();
                var cubeBoxes=ice.Cubes.Take(wanted).Select(c=>{var f=c.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="IceSurface");var b=f.gameObject.AddComponent<BoxCollider>();b.center=f.sharedMesh.bounds.center;b.size=f.sharedMesh.bounds.size;return b;}).ToArray();
                string hash=LogReplay.SnapshotHash(session.Adapter.World);
                if(wanted==2)
                {
                    holder.SetPositionAndRotation(hold.position,hold.rotation);ice.ShowCentered(3,IceAppearance.ConfiguredSide);Physics.SyncTransforms();oldIntersections=ice.Cubes.Take(3).Sum(c=>Intersections(c,metal,colliders));Assert.That(oldIntersections,Is.GreaterThan(0));Capture(session,tool,"before-3",new Vector3(.12f,.09f,-.15f));
                    holder.rotation=rig.HoldingRotation(hold);ice.ShowCentered(3,IceAppearance.ConfiguredSide);rig.Arrange(ice,IceAppearance.ConfiguredSide);
                }
                var position=tool.transform.position;var rotation=tool.transform.rotation;
                for(int frame=0;frame<60;frame++)
                {
                    tool.transform.SetPositionAndRotation(position+new Vector3(.01f*Mathf.Sin(frame*.2f),0,0),rotation*Quaternion.Euler(Mathf.Sin(frame*.1f)*35,frame*3,Mathf.Cos(frame*.13f)*20));yield return null;Physics.SyncTransforms();
                    int cuts=ice.Cubes.Take(wanted).Sum(c=>Intersections(c,metal,colliders));Assert.That(cuts,Is.Zero,"持冰不能穿入铲底、侧壁或柄");
                    for(int a=0;a<cubeBoxes.Length;a++)for(int b=a+1;b<cubeBoxes.Length;b++)if(Physics.ComputePenetration(cubeBoxes[a],cubeBoxes[a].transform.position,cubeBoxes[a].transform.rotation,cubeBoxes[b],cubeBoxes[b].transform.position,cubeBoxes[b].transform.rotation,out _,out float depth))Assert.That(depth,Is.LessThan(.0001f),"冰块之间不能重叠");
                    Assert.That(LogReplay.SnapshotHash(session.Adapter.World),Is.EqualTo(hash));rows.Add(wanted+","+frame+","+cuts+",true");
                }
                tool.transform.SetPositionAndRotation(position,rotation);yield return null;Capture(session,tool,"after-"+wanted,new Vector3(.12f,.09f,-.15f));if(wanted==2)Capture(session,tool,"after-2-side",new Vector3(.16f,.015f,0));
                Assert.That(hold.localPosition,Is.EqualTo(holdPosition));Assert.That(hold.localRotation,Is.EqualTo(holdRotation));Assert.That(grip.localPosition,Is.EqualTo(gripPosition));Assert.That(scoop.LoadedCubes,Is.EqualTo(wanted));
            }
            File.WriteAllLines(Path.Combine(Output,"frames.csv"),rows);File.WriteAllText(Path.Combine(Output,"result.txt"),"frames=180\nmetalIntersections=0\niceOverlaps=0\noldFormulaIntersections="+oldIntersections+"\nanchorsUnchanged=true\ncoreUnchanged=true\n");
        }
        /// <summary>这只铲斗一层最多两块；装两块时平放一排、各有朝向，不穿铲、不互穿。</summary>
        [UnityTest] public IEnumerator ScoopHoldsOneFlatLayerOfTwo()
        {
            Directory.CreateDirectory(Output);var lines=new List<string>();
            foreach(double unit in new[]{0d,.999d})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
                var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;session.Cues.enabled=false;
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter"||b is LocalIceWorld)b.enabled=false;
                var tool=session.Views.Single(v=>v.DefinitionId=="tool.scoop");var scoop=(IceScoop)tool.Item;var bin=(IceBin)session.Views.Single(v=>v.Item is IceBin).Item;
                Assert.That(session.Hands.TryPickUp(scoop),Is.True);new TakeIceAction(()=>unit).Begin(scoop,bin,session.Hands);Assert.That(scoop.LoadedCubes,Is.EqualTo(2));yield return null;yield return null;
                var ice=tool.transform.Find("Held ice (snapshot)").GetComponent<IceVisual>();var cubes=ice.Cubes.Where(c=>c.gameObject.activeSelf).ToArray();Assert.That(cubes.Length,Is.EqualTo(2));
                var metal=tool.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="ScoopBowl"||f.name=="Handle").ToArray();var colliders=metal.Select(f=>{var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;return c;}).ToArray();
                var boxes=cubes.Select(c=>{var f=c.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="IceSurface");var b=f.gameObject.AddComponent<BoxCollider>();b.center=f.sharedMesh.bounds.center;b.size=f.sharedMesh.bounds.size;return b;}).ToArray();
                Physics.SyncTransforms();int cuts=cubes.Sum(c=>Intersections(c,metal,colliders)),overlaps=0;
                for(int a=0;a<boxes.Length;a++)for(int b=a+1;b<boxes.Length;b++)if(Physics.ComputePenetration(boxes[a],boxes[a].transform.position,boxes[a].transform.rotation,boxes[b],boxes[b].transform.position,boxes[b].transform.rotation,out _,out float depth)&&depth>.0001f)overlaps++;
                // Capacity from the real geometry: cube side (unrotated drawn size) against the bowl's length and width.
                var rig=tool.GetComponent<ScoopIceRig>();var bowl=rig.BowlSize;var surface=cubes[0].GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="IceSurface");float cube=surface.sharedMesh.bounds.size.x*surface.transform.lossyScale.x;
                float bottoms=cubes.Max(c=>tool.transform.InverseTransformPoint(c.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="IceSurface").bounds.min).y)-cubes.Min(c=>tool.transform.InverseTransformPoint(c.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="IceSurface").bounds.min).y);
                float spread=Quaternion.Angle(cubes[1].localRotation,cubes[0].localRotation);
                lines.Add("unit="+unit+" bowl(x,y,z)="+bowl.ToString("F4")+" cubeSide="+cube.ToString("F4")+" alongLength="+(int)(bowl.z/cube)+" across="+(int)(bowl.x/cube)+" metalIntersections="+cuts+" overlaps="+overlaps+" bottomSpread="+bottoms.ToString("F4")+" angle="+spread.ToString("F1"));
                Capture(session,tool,"layer-"+(unit<.5?"a":"b"),new Vector3(.12f,.09f,-.15f));Capture(session,tool,"layer-"+(unit<.5?"a":"b")+"-side",new Vector3(.16f,.015f,0));
                Assert.That((int)(bowl.z/cube)*(int)(bowl.x/cube),Is.EqualTo(2),"这只铲斗一层只放得下两块");
                Assert.That(cuts,Is.Zero,"持冰不能穿入铲底、侧壁或柄");Assert.That(overlaps,Is.Zero,"冰块之间不能互穿");
                Assert.That(bottoms,Is.LessThan(.008f),"两块都在同一层");Assert.That(spread,Is.GreaterThan(1),"两块不能完全同一朝向");
            }
            File.WriteAllLines(Path.Combine(Output,"layer.txt"),lines);
        }
    }
}
