using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    public sealed partial class InspectArtDirectionTests
    {
        [Serializable]public sealed class PreviewCue
        {public float time,gain,pitch;public string bank,clip,path;}
        [Serializable]public sealed class PreviewSequence
        {public string id,item,mode,hand;public float duration,frameRate=60,fov;public int frames;public PreviewCue[] cues;}
        private void NativeFrame(string folder,int frame)
        {
            Directory.CreateDirectory(folder);s.GetComponent<HeldItemRendering>().Refresh();var camera=s.Player.ViewCamera;var rt=RenderTexture.GetTemporary(960,720,24);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);File.WriteAllBytes(Path.Combine(folder,frame.ToString("D4")+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
        }
        [UnityTest,Timeout(600000)]public IEnumerator NativeVideoFramesForEveryArtBranch()
        {
            var cases=new[]{("tool.spoon","empty","right"),("tool.spoon","empty","left"),("tool.tongs","empty","right"),("tool.scoop","empty","right"),("shaker.cobbler","empty","right"),("shaker.cobbler","sealedice","right"),("shaker.cobbler","openice","right"),("bottle.gin","full","right"),("bottle.syrup","full","right"),("bottle.soda","full","right"),("bottle.tonic","full","right"),("glass.highball","liquid","right"),("glass.highball","gas","right"),("glass.coupe","full","right"),("glass.coupe","liquid","right"),("tool.squeezer","empty","right"),("tool.squeezer","peek","right"),("jigger.dual","empty","right"),("glass.highball","ice","right"),("shaker.cobbler","sealedliquid","right")};
            foreach(var item in cases)
            {
                yield return Load();var view=View(item.Item1);Fill(view,item.Item2);Assert.That(s.Hands.TryPickUp(view.Item),Is.True);
                if(view.Item is BarSpoon&&Side(view).ToString().ToLowerInvariant()!=item.Item3)Assert.That(s.Hands.TrySwap(),Is.True);
                yield return new WaitForSecondsRealtime(.8f);Freeze();
                var p=HoldViewSettings.Load().inspectAll.For(view.DefinitionId);string id=item.Item1+"-"+item.Item2+"-"+item.Item3,folder=Path.Combine(Output,"sequences",id);int frame=0;
                NativeFrame(folder,frame++);Start();string hash=LogReplay.SnapshotHash(s.Adapter.World);float duration=rig.InspectionDuration(view);float fov=s.Player.ViewCamera.fieldOfView;
                var elapsed=new Dictionary<LogRecord,float>();
                for(int i=1;i<=Mathf.CeilToInt((duration+.30f)*120);i++){Step(1f/120);foreach(var r in records)if(!elapsed.ContainsKey(r))elapsed[r]=rig.InspectionElapsed(Side(view));if(i%2==0)NativeFrame(folder,frame++);}
                var catalog=BarAudioCatalog.Load();var list=view.Item is Jigger?HoldViewSettings.Load().inspect.sounds:p.sounds;var sounds=records.Where(r=>r.kind=="presentation_sound"&&r.sound.category=="inspect"&&r.sound.started).ToArray();var cues=new List<PreviewCue>();
                foreach(var sound in sounds)
                {
                    // 增益会按冰量/液量缩放，改用库名、变体和触发时刻对回配置里的 cue。
                    var r=sound.sound;var configured=list.Where(c=>c.bank==r.bank&&r.clip==c.bank+"-"+c.variant&&InspectionContactTiming.Time(c,HoldViewSettings.Load().inspect,p)<=elapsed[sound]+.01f).OrderBy(c=>elapsed[sound]-InspectionContactTiming.Time(c,HoldViewSettings.Load().inspect,p)).First();
                    var bank=catalog.banks.First(b=>b.id==r.bank);string path=bank.paths.First(x=>Path.GetFileName(x)==r.clip);
                    cues.Add(new PreviewCue{time=InspectionContactTiming.Time(configured,HoldViewSettings.Load().inspect,p),gain=(float)r.gain,pitch=(float)r.pitch,bank=r.bank,clip=r.clip,path=path});
                }
                var sequence=new PreviewSequence{id=id,item=view.DefinitionId,mode=item.Item2,hand=Side(view).ToString(),duration=frame/60f,frames=frame,fov=fov,cues=cues.ToArray()};File.WriteAllText(Path.Combine(folder,"sequence.json"),JsonUtility.ToJson(sequence,true));
                Assert.That(s.Player.ViewCamera.fieldOfView,Is.EqualTo(fov));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));Assert.That(records.Last(r=>r.kind=="view_inspect_ended").outcome,Is.EqualTo("completed"));
            }
        }
    }
}
