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
    /// <summary>试玩反馈：同步旋转进站、竖直侧候、真实浅液位截面及飞溅像素裁切。</summary>
    public sealed class PourEntryRimTests
    {
        private BarSession session;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../EntryRimReports"));
        private ItemView View(string id)=>session.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            Directory.CreateDirectory(Output);
        }
        private static void Capture(Camera camera,string name)
        {
            Object.Destroy(PourCaptureTests.Capture(camera,"entry-rim-"+name));
            File.Copy(Path.Combine(PourCaptureTests.Output,"entry-rim-"+name+".png"),Path.Combine(Output,name+".png"),true);
        }
        [UnityTest]public IEnumerator SourceWaitsBesideReceiverAndRotatesDuringTranslation()
        {
            yield return Load();var source=View("bottle.gin");var target=View("jigger.dual");((Bottle)source.Item).SetOpen(true);
            Assert.That(session.Hands.TryPickUp(source.Item),Is.True);Assert.That(session.Hands.TryPickUp(target.Item),Is.True);
            var camera=session.Player.ViewCamera;camera.fieldOfView=45;camera.aspect=640f/480;
            var sourceRotation=source.transform.rotation;var targetRotation=target.transform.rotation;
            var cameraPosition=camera.transform.position;
            var rows=new List<string>();
            foreach(float angle in new[]{0f,40f,110f})
            {
                var sourceHome=camera.transform.TransformPoint(new Vector3(.18f,-.35f,.5f));
                var targetHome=camera.transform.TransformPoint(new Vector3(-.14f,-.26f,.5f));
                source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                var pose=new PourPose();pose.Begin(source,target,camera.transform,true);
                for(int i=0;i<16;i++)
                {
                    source.transform.SetPositionAndRotation(sourceHome,sourceRotation);target.transform.SetPositionAndRotation(targetHome,targetRotation);
                    var input=pose.Step(1f/60,angle,0,((Jigger)target.Item).ActiveCup);
                    float expected=angle*Mathf.SmoothStep(0,1,pose.Progress);
                    Assert.That(Vector3.Angle(PourPose.Axis(source),Vector3.up),Is.EqualTo(expected).Within(.02f),"进站旋转与平移使用同一插值");
                    if(i==15&&angle==0)
                    {
                        float side=new Vector2(source.PourPoint.x-input.MouthCenter.x,source.PourPoint.z-input.MouthCenter.z).magnitude;
                        Assert.That(side,Is.GreaterThanOrEqualTo(input.MouthRadius+Mathf.Max(source.Size.x,source.Size.z)*.5f+PourSettings.Load().entrySideGap-.0001f),"竖直待倒时瓶身必须在杯子旁边");
                        rows.Add("upright lip separation metres: "+side);
                    }
                    if(angle==110&&(i==0||i==6||i==15)){yield return null;Capture(camera,"entry-"+i.ToString("D2"));}
                }
                var flowing=pose.Step(1f/60,110,30,((Jigger)target.Item).ActiveCup);
                Assert.That(PourTrajectory.PlaneTime(flowing.Outlet.position,flowing.ExitVelocity,flowing.MouthCenter.y,9.81f,out float time),Is.True);
                var cross=PourTrajectory.Point(flowing.Outlet.position,flowing.ExitVelocity,time,9.81f)-flowing.MouthCenter;
                Assert.That(new Vector2(cross.x,cross.z).magnitude,Is.LessThan(.0001f),"进站后保持原有弹道对口");
                Assert.That(camera.transform.position,Is.EqualTo(cameraPosition));
            }
            File.WriteAllLines(Path.Combine(Output,"entry-checks.txt"),rows);
        }
        // Independent triangle/plane intersections: the oracle reads the imported mesh directly.
        private static float MeshSectionRadius(ContainerVisual binding,float plane,Vector3 center)
        {
            var mesh=binding.InnerCavity.sharedMesh;var vertices=mesh.vertices.Select(binding.InnerCavity.transform.TransformPoint).ToArray();var indices=mesh.triangles;
            float nearest=float.PositiveInfinity;
            for(int i=0;i<indices.Length;i+=3)
            {
                var cuts=new List<Vector2>();
                for(int j=0;j<3;j++)
                {
                    var a=vertices[indices[i+j]];var b=vertices[indices[i+(j+1)%3]];
                    if((a.y-plane)*(b.y-plane)>=0)continue;
                    var p=Vector3.Lerp(a,b,(plane-a.y)/(b.y-a.y))-center;cuts.Add(new Vector2(p.x,p.z));
                }
                if(cuts.Count!=2)continue;
                var edge=cuts[1]-cuts[0];float t=edge.sqrMagnitude<1e-12f?0:Mathf.Clamp01(-Vector2.Dot(cuts[0],edge)/edge.sqrMagnitude);
                nearest=Mathf.Min(nearest,(cuts[0]+t*edge).magnitude);
            }
            return nearest;
        }
        [UnityTest]public IEnumerator ImpactStaysInsideBothJiggerEndsAndGlassesAtShallowLevels()
        {
            yield return Load();var rows=new List<string>();
            var targets=new[]{View("jigger.dual"),View("glass.coupe"),View("glass.highball")};
            foreach(var target in targets)target.enabled=false; // Geometry fixture owns both jigger-end poses.
            var ribbon=session.gameObject.AddComponent<PourRibbon>();
            foreach(var target in targets)
            foreach(var binding in target.GetComponentsInChildren<ContainerVisual>())
            {
                // Both jigger cavities are separately made upright without changing domain state.
                var orientation=target.transform.Find("Orientation");var original=orientation?orientation.rotation:target.transform.rotation;
                if(orientation)orientation.rotation=Quaternion.FromToRotation(binding.transform.up,Vector3.up)*orientation.rotation;
                foreach(double ml in new[]{.05,.2,1,3,15})
                {
                    var shape=binding.Shape;
                    float plane=binding.transform.TransformPoint(new Vector3(shape.Axis.x,binding.Profile.HeightForMl(ml),shape.Axis.y)).y;
                    var mouth=binding.transform.TransformPoint(new Vector3(shape.Axis.x,shape.Top,shape.Axis.y));
                    var input=new PourStreamInput{Outlet=new Pose(new Vector3(mouth.x,mouth.y+.06f,mouth.z),Quaternion.identity),ExitVelocity=Vector3.down*.1f,ConfirmedMlPerSecond=20,LiquidPlaneY=plane,MouthCenter=mouth,MouthRadius=shape.RadiusAt(shape.Top)};
                    PourPose.SetImpactBoundary(ref input,binding);
                    foreach(var other in target.GetComponentsInChildren<ContainerVisual>())if(other.LiquidVolume)other.LiquidVolume.enabled=other==binding;
                    var block=new MaterialPropertyBlock();binding.LiquidVolume.GetPropertyBlock(block);block.SetFloat("_FillHeight",plane);binding.LiquidVolume.SetPropertyBlock(block);
                    var surface=binding.GetComponent<LiquidSurface>();if(!surface)surface=binding.gameObject.AddComponent<LiquidSurface>();surface.Draw(binding.InnerCavity,plane,binding.LiquidVolume.sharedMaterial,true);
                    float actual=MeshSectionRadius(binding,plane,input.ImpactCenter);
                    Assert.That(float.IsInfinity(actual),Is.False,binding.DefinitionId+" mesh section");
                    for(int step=0;step<8;step++)
                    {
                        // Cover the whole expansion phase deterministically, not only one rendered frame.
                        typeof(PourRibbon).GetField("phase",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(ribbon,step/8f);
                        ribbon.Draw(input);var ring=ribbon.GetComponentsInChildren<LineRenderer>().Single();
                        Assert.That(ring.sharedMaterial.GetFloat("_ImpactClip"),Is.EqualTo(1));
                        Assert.That(ring.sharedMaterial.GetFloat("_ImpactRadius"),Is.LessThanOrEqualTo(actual+.00001f));
                        if(ring.enabled)for(int p=0;p<ring.positionCount;p++)
                        {
                            var q=ring.GetPosition(p)-input.ImpactCenter;
                            Assert.That(new Vector2(q.x,q.z).magnitude+ring.startWidth*.5f,Is.LessThanOrEqualTo(actual+.00001f),binding.DefinitionId+" "+ml+" ml ring stroke");
                        }
                    }
                    rows.Add(binding.DefinitionId+","+ml+","+input.ImpactRadius+","+actual);
                    if(ml==.2||ml==15)
                    {
                        var camera=session.Player.ViewCamera;camera.fieldOfView=38;camera.aspect=640f/480;
                        camera.transform.position=mouth+new Vector3(.035f,.18f,-.13f);camera.transform.LookAt(mouth+Vector3.down*.025f);
                        yield return null;Capture(camera,binding.DefinitionId+"-"+ml.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                if(orientation)orientation.rotation=original;else target.transform.rotation=original;
            }
            var body=ribbon.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Core flow parabola");
            Assert.That(body.sharedMaterial.GetFloat("_ImpactClip"),Is.Zero,"水柱和空中水滴不能被接收杯边界裁掉");
            File.WriteAllLines(Path.Combine(Output,"section-checks.csv"),new[]{"container,ml,shapeRadiusMetres,meshRadiusMetres"}.Concat(rows));
        }
        [UnityTest]public IEnumerator ImpactShaderClipsBillboardPixelsOutsideSection()
        {
            yield return Load();var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.transform.position=new Vector3(100,100,100);go.transform.rotation=Quaternion.Euler(90,0,0);go.transform.localScale=Vector3.one*.04f;
            var mat=new Material(Shader.Find("Bar/PourRibbon"));go.GetComponent<MeshRenderer>().sharedMaterial=mat;mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_ImpactClip",1);mat.SetFloat("_ImpactRadius",.005f);mat.SetVector("_ImpactCenter",go.transform.position);
            var camera=new GameObject("Impact pixel test").AddComponent<Camera>();camera.transform.position=go.transform.position+Vector3.up*.1f;camera.transform.LookAt(go.transform.position,Vector3.forward);camera.nearClipPlane=.001f;camera.farClipPlane=1;camera.orthographic=true;camera.orthographicSize=.025f;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
            yield return null;
            var rt=RenderTexture.GetTemporary(256,256,24);var previous=RenderTexture.active;
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;
            var image=new Texture2D(256,256,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
            int outside=0,inside=0;for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float radius=new Vector2((x+.5f-128)*.05f/256,(y+.5f-128)*.05f/256).magnitude;bool lit=image.GetPixel(x,y).maxColorComponent>.1f;
                if(radius>.0054f&&lit)outside++;if(radius<.004f&&lit)inside++;
            }
            File.WriteAllBytes(Path.Combine(Output,"impact-clip-pixels.png"),image.EncodeToPNG());File.WriteAllText(Path.Combine(Output,"pixel-checks.txt"),"outside="+outside+" inside="+inside);
            Assert.That(inside,Is.GreaterThan(300));Assert.That(outside,Is.Zero,"飞溅粒子四边形必须逐像素裁到液面截面内");
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(mat);Object.Destroy(go);Object.Destroy(camera.gameObject);
        }
    }
}
