using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>近看空高球杯：反射/折射分离截图与平底、长壁法线检查；不修改场景资源。</summary>
    public sealed class GlassNormalTests
    {
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../HighballNormalReports"));
        [UnityTest]public IEnumerator InspectCloseGlassWithReflectionAndRefractionSeparated()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var glass=s.Views.Single(v=>v.DefinitionId=="glass.highball");Assert.That(s.Hands.TryPickUp(glass.Item),Is.True);
            var camera=s.Player.ViewCamera;camera.transform.rotation=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);camera.fieldOfView=32;camera.aspect=4f/3;
            glass.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*.35f-Vector3.up*.075f,Quaternion.identity);
            var foreground=s.GetComponent<HeldItemRendering>();foreground.Refresh();
            var renderers=glass.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Bar/Glass").ToArray();
            var original=renderers.Select(r=>r.sharedMaterials).ToArray();var copies=new List<Material>();
            for(int i=0;i<renderers.Length;i++){var materials=original[i].Select(m=>new Material(m)).ToArray();copies.AddRange(materials);renderers[i].sharedMaterials=materials;}
            var refractions=copies.Select(m=>m.GetFloat("_Refraction")).ToArray();
            var probes=Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);var strengths=probes.Select(p=>p.intensity).ToArray();float environment=RenderSettings.reflectionIntensity;
            string hash=LogReplay.SnapshotHash(s.Adapter.World);Directory.CreateDirectory(Output);
            try
            {
                foreach(string mode in new[]{"regular","no-reflection","no-refraction","neither"})
                {
                    bool reflection=mode=="regular"||mode=="no-refraction",refraction=mode=="regular"||mode=="no-reflection";
                    RenderSettings.reflectionIntensity=reflection?environment:0;for(int i=0;i<probes.Length;i++)probes[i].intensity=reflection?strengths[i]:0;
                    for(int i=0;i<copies.Count;i++)copies[i].SetFloat("_Refraction",refraction?refractions[i]:0);
                    yield return null;
                    Object.Destroy(Capture(camera,"close-"+mode));
                }
                Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                var shell=glass.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Shell");var mesh=shell.sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;var points=vertices.Select(v=>glass.transform.InverseTransformPoint(shell.transform.TransformPoint(v))).ToArray();var ns=normals.Select(n=>glass.transform.InverseTransformDirection(shell.transform.TransformDirection(n)).normalized).ToArray();
                float minY=points.Min(v=>v.y),maxY=points.Max(v=>v.y),bottomWorst=0,wallUp=0;int bottomCorners=0;
                foreach(int i in Enumerable.Range(0,triangles.Length/3))
                {
                    int a=triangles[i*3],b=triangles[i*3+1],c=triangles[i*3+2];var face=Vector3.Cross(points[b]-points[a],points[c]-points[a]);if(face.sqrMagnitude<1e-18f)continue;
                    if(Mathf.Abs(face.normalized.y)>.999f&&Mathf.Max(points[a].y,Mathf.Max(points[b].y,points[c].y))<minY+.004f)
                        foreach(int j in new[]{a,b,c}){bottomWorst=Mathf.Max(bottomWorst,Vector3.Angle(face.normalized,ns[j]));bottomCorners++;}
                    if(Mathf.Max(points[a].y,Mathf.Max(points[b].y,points[c].y))-Mathf.Min(points[a].y,Mathf.Min(points[b].y,points[c].y))>(maxY-minY)*.5f)
                        foreach(int j in new[]{a,b,c})wallUp=Mathf.Max(wallUp,Mathf.Abs(ns[j].y));
                }
                File.WriteAllText(Path.Combine(Output,"normals.json"),"{\"bottomWorstDegrees\":"+bottomWorst.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"bottomCorners\":"+bottomCorners+",\"wallMaximumUpComponent\":"+wallUp.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"height\":"+(maxY-minY).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
                Assert.That(bottomCorners,Is.GreaterThan(0));Assert.That(bottomWorst,Is.LessThan(1),"平底法线不能再向侧面倾斜形成扇形碎片");
                Assert.That(wallUp,Is.LessThan(.1f),"长杯壁法线不能混入杯口的朝上方向");
                Assert.That(maxY-minY,Is.EqualTo(.155f).Within(.00001f),"用户指定厚底新轮廓：总高155mm，保留四块37mm冰的空间");
            }
            finally
            {
                for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].sharedMaterials=original[i];foreach(var m in copies)Object.Destroy(m);
                RenderSettings.reflectionIntensity=environment;for(int i=0;i<probes.Length;i++)if(probes[i])probes[i].intensity=strengths[i];
            }
        }
        private static Texture2D Capture(Camera camera,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());return image;
        }
    }
}
