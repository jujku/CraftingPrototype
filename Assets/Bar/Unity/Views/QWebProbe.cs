using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>仅临时 WebGL 场景：实际 Unity AudioSource 空间输出和中文字体探针。</summary>
    public sealed class QWebProbe:MonoBehaviour
    {
        private BarSession session;private AudioSource source;private string side="idle";private float stopAt,next;private Font font;
        [Serializable]private sealed class State{public string side,mode,loadState;public float spatialBlend,volume,length;public bool playing,fontChinese,overlayOpaque,sourceEnabled,listenerPaused;public int timeSamples;public double scheduledDsp,engineDsp,contextDsp;}
        private double scheduled;private string mode="idle";
        #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]private static extern void BarQMetrics(string value);
        [DllImport("__Internal")]private static extern double BarQAudioContextTime();
        #endif
        private void Start(){session=GetComponent<BarSession>();font=Resources.Load<Font>("BarUISubsetSC");var go=new GameObject("Q spatial probe");go.transform.SetParent(transform,false);source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.clip=Resources.Load<AudioClip>("BarAudio/Q/ice-0");source.loop=true;source.spatialBlend=BarAudioCatalog.Load().spatialBlend;source.dopplerLevel=0;source.minDistance=.2f;source.maxDistance=6;source.rolloffMode=AudioRolloffMode.Linear;}
        [UnityEngine.Scripting.Preserve]public void ProbeLeft()=>Probe(-1);[UnityEngine.Scripting.Preserve]public void ProbeRight()=>Probe(1);[UnityEngine.Scripting.Preserve]public void ProbeContextSchedule()=>Probe(-1,true);[UnityEngine.Scripting.Preserve]public void ProbeNativeSchedule()=>Probe(-1,true,true);
        private double ContextTime(){
        #if UNITY_WEBGL && !UNITY_EDITOR
        return BarQAudioContextTime();
        #else
        return AudioSettings.dspTime;
        #endif
        }
        private void Probe(int sign,bool schedule=false,bool nativeClock=false){var demo=GetComponent<BStageDemo>();if(demo)demo.AutoRun=false;session.Cues.enabled=false;session.Sound.Pool.Stop();session.Sound.Gate.Unlock();source.Stop();var c=session.Player.ViewCamera.transform;source.transform.position=c.position+c.right*(sign*.6f)+c.forward*.6f;source.volume=.6f;mode=schedule?(nativeClock?"nativeSchedule":"contextSchedule"):"immediate";scheduled=(nativeClock?AudioSettings.dspTime:ContextTime())+.03;if(schedule)source.PlayScheduled(scheduled);else source.Play();side=sign<0?"left":"right";stopAt=Time.unscaledTime+7;}
        private void Update(){if(!source)return;if(Time.unscaledTime>=stopAt&&side!="idle")source.Stop();if(Time.unscaledTime<next)return;next=Time.unscaledTime+.2f;var value=new State{side=side,mode=mode,loadState=source.clip.loadState.ToString(),length=source.clip.length,sourceEnabled=source.enabled&&source.gameObject.activeInHierarchy,listenerPaused=AudioListener.pause,engineDsp=AudioSettings.dspTime,contextDsp=ContextTime(),spatialBlend=source.spatialBlend,volume=source.volume,playing=source.isPlaying,timeSamples=source.timeSamples,fontChinese=font&&font.HasCharacter('调')&&font.HasCharacter('冰'),scheduledDsp=scheduled,overlayOpaque=IceRefractionFeature.OverlayOpaqueValid};
        #if UNITY_WEBGL && !UNITY_EDITOR
        BarQMetrics(JsonUtility.ToJson(value));
        #endif
        }
        private void OnGUI(){if(!font)return;var old=GUI.skin.font;GUI.skin.font=font;GUI.Label(new Rect(12,45,720,32),"Q 验证：中文调酒工作台 · 冰与泡沫 · "+side);GUI.skin.font=old;}
    }
}
