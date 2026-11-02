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
    /// <summary>同一真实厚底，不换模型/环境：对照原最近面和恢复内底界面，保留三机位木台/暗背景原生图。</summary>
    public sealed class ShamReadabilityTests
    {
        [UnityTest]public IEnumerator ExistingHeavyShamRemainsClearAndHasAnInnerInterface()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=Object.FindFirstObjectByType<BarSession>();s.enabled=false;s.Hud.enabled=false;s.GetComponent<OrderOverlay>().enabled=false;
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            var view=s.Views.Single(v=>v.DefinitionId=="glass.highball");var binding=view.GetComponent<ContainerVisual>();
            var floor=binding.Shape.Bottom;Assert.That(floor,Is.EqualTo(.020f).Within(.00003f));
            var shell=view.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Shell");var vertices=shell.sharedMesh.vertices.ToArray();
            Assert.That(s.Hands.TryPickUp(view.Item),Is.True);var camera=s.Player.ViewCamera;camera.fieldOfView=32;camera.aspect=4f/3;
            var home=view.transform.position;var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ShamReadabilityReports"));Directory.CreateDirectory(output);
            string hash=LogReplay.SnapshotHash(s.Adapter.World);var rows=new List<string>{"background,angle,changedPixels,meanDifference"};
            foreach(string background in new[]{"wood","black"})foreach(float angle in new[]{15f,35,55})
            {
                view.transform.SetPositionAndRotation(home+(background=="black"?Vector3.up*.65f:Vector3.zero),Quaternion.identity);
                var centre=view.transform.position+Vector3.up*.065f;var offset=Quaternion.AngleAxis(angle,Vector3.right)*new Vector3(0,0,-.38f);
                camera.transform.position=centre+offset;camera.transform.LookAt(centre);s.GetComponent<HeldItemRendering>().Refresh();yield return null;
                var material=shell.GetComponent<MeshRenderer>().sharedMaterial;var optics=material.GetVector("_ShamOptics");Assert.That(optics.x,Is.EqualTo(1));
                var shape=material.GetVector("_ShamShape");Assert.That(shape.x,Is.EqualTo(floor));Assert.That(shape.y,Is.GreaterThan(.02f));
                material.SetVector("_ShamOptics",new Vector4(0,optics.y,optics.z,optics.w));var before=Capture(camera,output,background+"-"+angle+"-before");
                material.SetVector("_ShamOptics",optics);var after=Capture(camera,output,background+"-"+angle+"-after");
                // Separate floor-interface restoration from tint reduction; the
                // interface must still be visible in a same-frame ablation.
                material.SetVector("_ShamOptics",new Vector4(1,optics.y,optics.z,0));var noFloor=Capture(camera,output,background+"-"+angle+"-no-interface");material.SetVector("_ShamOptics",optics);
                int changed=0,interfacePixels=0;float sum=0;var a=before.GetPixels32();var b=after.GetPixels32();var c=noFloor.GetPixels32();
                for(int i=0;i<a.Length;i++)
                {
                    int d=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b);if(d>6)changed++;sum+=d/(255f*3*a.Length);
                    if(Mathf.Abs(c[i].r-b[i].r)+Mathf.Abs(c[i].g-b[i].g)+Mathf.Abs(c[i].b-b[i].b)>3)interfacePixels++;
                }
                rows.Add(background+","+angle+","+changed+","+sum+",interface="+interfacePixels);
                Assert.That(changed,Is.GreaterThan(50));if(angle>=35)Assert.That(interfacePixels,Is.GreaterThan(20),"不能仅淡化厚底，却继续擦掉内底界面");
                Object.Destroy(before);Object.Destroy(after);Object.Destroy(noFloor);
            }
            Assert.That(shell.sharedMesh.vertices,Is.EqualTo(vertices));Assert.That(binding.Shape.Bottom,Is.EqualTo(floor));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            File.WriteAllLines(Path.Combine(output,"comparison.csv"),rows);
        }
        private static Texture2D Capture(Camera camera,string output,string name)
        {
            var rt=RenderTexture.GetTemporary(960,720,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return image;
        }
    }
}
