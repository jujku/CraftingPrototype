using System.Collections;
using System.IO;
using System.Linq;
using Bar.Unity;
using Bar.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    /// <summary>在相同机位检查基线、Desktop/Web 画质、烘焙探针和实际日志配置，并保存图像证据。</summary>
    internal sealed class PresentationTests
    {
        [System.Serializable]private sealed class CameraProof{public Vector3 position,euler;public float fieldOfView,aspect,m00,m11;public string effectiveTonemapping;}
        private static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../PresentationReports"));
        [UnityTest] public IEnumerator CompareQualityAndCapture()
        {
            Directory.CreateDirectory(Output);
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names,"PC"),true);
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");yield return null;
            var session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;
            var camera=session.Player.ViewCamera;camera.fieldOfView=60;
            yield return Capture(camera,"before");
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;
            session=Object.FindFirstObjectByType<BarSession>();session.enabled=false;camera=session.Player.ViewCamera;camera.fieldOfView=60;
            var rig=Object.FindFirstObjectByType<PresentationRig>();Assert.That(rig,Is.Not.Null);
            var probe=Object.FindFirstObjectByType<ReflectionProbe>();Assert.That(probe.mode,Is.EqualTo(ReflectionProbeMode.Baked));Assert.That(probe.bakedTexture,Is.Not.Null);Assert.That(probe.resolution,Is.InRange(256,512));
            Assert.That(RenderSettings.customReflectionTexture,Is.Null);
            foreach(bool web in new[]{false,true})
            {
                rig.Apply(web);yield return null;yield return null;
                var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                Assert.That(pipeline.supportsCameraOpaqueTexture&&pipeline.supportsCameraDepthTexture&&pipeline.supportsHDR,Is.True);Assert.That(pipeline.msaaSampleCount,Is.EqualTo(4));
                var header=new SessionHeader();PresentationRig.FillHeader(header,camera);Assert.That(header.qualityTier,Is.EqualTo(web?"Web":"Desktop"));Assert.That(header.qualitySettingsSHA256.Length,Is.EqualTo(64));Assert.That(header.postProcessingProfileSHA256.Length,Is.EqualTo(64));Assert.That(header.holdViewSettingsSHA256.Length,Is.EqualTo(64));
                File.WriteAllText(Path.Combine(Output,header.qualityTier+"-settings.json"),JsonUtility.ToJson(header,true));
                foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))foreach(var material in renderer.sharedMaterials)if(material)Assert.That(material.shader.isSupported,Is.True,material.name);
                yield return Capture(camera,web?"web":"desktop");
            }
            rig.Desktop();rig.GetComponent<Volume>().profile.TryGet<Tonemapping>(out var tone);var original=tone.mode.value;string alternate=original==TonemappingMode.Neutral?"aces":"neutral";tone.mode.value=original==TonemappingMode.Neutral?TonemappingMode.ACES:TonemappingMode.Neutral;
            yield return Capture(camera,alternate);tone.mode.value=original;Assert.That(File.ReadAllBytes(Path.Combine(Output,alternate+"-overview.png")),Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(Output,"desktop-overview.png"))),"ACES/Neutral must produce different real postprocessed pixels");
        }
        private static IEnumerator Capture(Camera camera,string prefix)
        {
            var input=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b=>b.GetType().Name=="InputRouter");if(input)input.enabled=false;camera.aspect=640f/480;
            var session=Object.FindFirstObjectByType<BarSession>();
            var highball=session.Views.First(v=>v.DefinitionId=="glass.highball");
            var shaker=session.Views.First(v=>v.DefinitionId=="shaker.cobbler");
            var board=session.Views.First(v=>v.DefinitionId=="fixture.board");
            foreach(string shot in new[]{"overview","wood","metal","glass"})
            {
                if(shot=="overview"){camera.transform.position=new Vector3(0,1.18f,-1.1f);camera.transform.LookAt(new Vector3(0,.62f,.64f));}
                else{var target=shot=="metal"?shaker:shot=="glass"?highball:board;camera.transform.position=target.transform.position+new Vector3(-.11f,.19f,-.33f);camera.transform.LookAt(target.transform.position+Vector3.up*(target.Size.y*.45f));}
                yield return null;yield return null;
                var rt=RenderTexture.GetTemporary(640,480,24);RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;var activeTone=VolumeManager.instance.stack.GetComponent<Tonemapping>();File.WriteAllText(Path.Combine(Output,prefix+"-"+shot+"-camera.json"),JsonUtility.ToJson(new CameraProof{position=camera.transform.position,euler=camera.transform.eulerAngles,fieldOfView=camera.fieldOfView,aspect=camera.aspect,m00=camera.projectionMatrix.m00,m11=camera.projectionMatrix.m11,effectiveTonemapping=activeTone.mode.value.ToString()},true));
                var image=new Texture2D(640,480,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,prefix+"-"+shot+".png"),image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
