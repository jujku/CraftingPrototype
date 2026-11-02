using System.Collections.Generic;
using System.IO;
using Bar.Unity;
using UnityEngine;
using UnityEngine.Rendering;
namespace Bar.Tests
{
    /// <summary>所有表现 LateUpdate 后，从真实玩家相机采样。只用于回归，不插值或重绘动画帧。</summary>
    [DefaultExecutionOrder(250)]public sealed class IceDeliveryFrameRecorder:MonoBehaviour
    {
        private Camera camera;private ItemView source;private string output;private float started,last=-100;private bool recording;
        private readonly List<string> rows=new List<string>{"frame,seconds,x,y,z,qx,qy,qz,qw"};public int Count{get;private set;}
        public void Prepare(Camera view,ItemView tool,string directory)
        {camera=view;source=tool;output=directory;Directory.CreateDirectory(output);RenderFrame();RenderFrame();}
        public void StartRecording(){started=Time.realtimeSinceStartup;last=-100;recording=true;}
        private void LateUpdate()
        {
            if(!recording||Time.realtimeSinceStartup-last<1f/30)return;last=Time.realtimeSinceStartup;
            File.WriteAllBytes(Path.Combine(output,Count.ToString("D4")+".png"),RenderFrame());
            var p=source.transform.position;var q=source.transform.rotation;rows.Add($"{Count},{last-started:R},{p.x:R},{p.y:R},{p.z:R},{q.x:R},{q.y:R},{q.z:R},{q.w:R}");Count++;
        }
        private byte[] RenderFrame()
        {
            var rt=RenderTexture.GetTemporary(960,600,24);var previous=RenderTexture.active;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;
            var t=new Texture2D(960,600,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,960,600),0,0);t.Apply();var bytes=t.EncodeToPNG();Destroy(t);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return bytes;
        }
        public void Finish(){recording=false;File.WriteAllLines(Path.Combine(output,"frames.csv"),rows);}
    }
}
