using System.Collections;
using System.IO;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>诊断场景离线定步长采样；每段4秒视频，表面时间演示加速3倍，非实测FPS/实录声音。</summary>
    public sealed class BVideoCaptureTests
    {
        [UnityTest] public IEnumerator CaptureBoundedBStageClips()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../BVideoFrames"));Directory.CreateDirectory(output);
            foreach(string mode in new[]{"sealed","debug","bubbles","condensation","frost"})
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;var session=Object.FindFirstObjectByType<BarSession>();var demo=session.gameObject.AddComponent<BStageDemo>();demo.AutoRun=false;
                yield return new WaitUntil(()=>demo.Ready);demo.SetMode(mode);session.Cues.enabled=false;
                foreach(var rig in Object.FindObjectsByType<LocalIceWorld>(FindObjectsSortMode.None))rig.enabled=false;
                string folder=Path.Combine(output,mode);Directory.CreateDirectory(folder);int captured=0;float speed=mode=="condensation"||mode=="frost"?3:1;
                for(int frame=0;frame<240;frame++)
                {
                    demo.Step(speed/60,frame*speed/60);
                    foreach(var rig in Object.FindObjectsByType<LocalIceWorld>(FindObjectsSortMode.None).Where(r=>r.gameObject.activeInHierarchy&&r.GetComponentInParent<ItemView>().DefinitionId!="shaker.cobbler"))rig.Advance(speed/60);
                    session.Cues.Present(speed/60);session.GetComponent<HeldItemRendering>().Refresh();
                    if(frame%4!=0)continue;var image=PourCaptureTests.Capture(session.Player.ViewCamera,"B-video-frame");File.WriteAllBytes(Path.Combine(folder,(captured++).ToString("D4")+".png"),image.EncodeToPNG());Object.Destroy(image);
                }
                Assert.That(captured,Is.EqualTo(60));File.WriteAllText(Path.Combine(folder,"capture.txt"),"videoSeconds=4\nframes=60\nfps=15\nsimulationSpeed="+speed+"\nrealtimeFpsMeasurement=false\naudio=separate sample preview, not live mix\n");
            }
        }
    }
}
