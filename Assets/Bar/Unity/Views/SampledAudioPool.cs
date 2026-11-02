using System;
using System.Collections.Generic;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>预载的有限采样池；原生循环、DSP 排期、最弱声抢占（按"现在"的响度比较——一次性声音按 stealDecaySeconds 估计衰减，
    /// 所以挤掉的是快响完的旧尾音；新声音比最弱的还小一半以上时直接不放，不去切断正在响的）。无逐帧 LINQ/找组件/时间跳转。</summary>
    public sealed class SampledAudioPool:IDisposable
    {
        private sealed class Voice{internal AudioSource Source;internal string Key,Bank,Vessel,Group;internal bool Loop,Wanted,PendingStart,Busy;internal float Variation,PitchVariation,TargetGain;internal AudioGainEnvelope Envelope;internal double End,Start,LastLog,LastGain,LastPitch;}
        private readonly Voice[] voices;private readonly AudioSource[] sources;private readonly BarAudioCatalog p;private readonly Transform listener;private readonly Dictionary<string,AudioClip[]> clips=new Dictionary<string,AudioClip[]>();
        private readonly Dictionary<string,int> previous=new Dictionary<string,int>();private readonly Dictionary<string,double> cooldown=new Dictionary<string,double>();private readonly System.Random rng=new System.Random(20261026);private readonly Action<string,SoundObservation> observation;
        public int Started{get;private set;}public int Stolen{get;private set;}public int PreloadFailures{get;private set;}
        public int ActiveCount{get{int n=0;foreach(var v in voices)if(v.Busy&&(v.Loop||AudioSettings.dspTime<v.End))n++;return n;}}public IEnumerable<AudioSource> Sources=>sources;
        public SampledAudioPool(Transform owner,Transform listener,BarAudioCatalog p,Action<string,SoundObservation> observation)
        {
            this.p=p;this.listener=listener;this.observation=observation;voices=new Voice[p.poolSize];sources=new AudioSource[p.poolSize];
            foreach(var bank in p.banks){var list=new List<AudioClip>();foreach(var path in bank.paths){var clip=Resources.Load<AudioClip>(path);if(!clip){PreloadFailures++;continue;}if(clip.loadState==AudioDataLoadState.Unloaded)clip.LoadAudioData();if(clip.loadState==AudioDataLoadState.Failed){PreloadFailures++;continue;}list.Add(clip);}clips[bank.id]=list.ToArray();}
            for(int i=0;i<voices.Length;i++){var go=new GameObject("Sample voice "+i);go.transform.SetParent(owner,false);var s=go.AddComponent<AudioSource>();s.playOnAwake=false;s.volume=0;s.spatialBlend=p.spatialBlend;s.dopplerLevel=0;s.minDistance=p.minimumDistance;s.maxDistance=p.audibleDistance;s.rolloffMode=AudioRolloffMode.Linear;sources[i]=s;voices[i]=new Voice{Source=s,Envelope=new AudioGainEnvelope(p.attackSeconds,p.releaseSeconds,p.gainSmoothingSeconds)};}
        }
        private AudioClip Choose(string bank,int variant)
        {if(!clips.TryGetValue(bank,out var c)||c.Length==0)return null;if(variant>=0)return c[variant%c.Length];int n=c.Length==1?0:rng.Next(c.Length-1);if(c.Length>1&&previous.TryGetValue(bank,out int last)&&n>=last)n++;previous[bank]=n;return c[n];}
        public void BeginFrame(){double now=AudioSettings.dspTime;foreach(var v in voices){v.Wanted=false;if(v.Busy&&!v.Loop&&now>=v.End)v.Busy=false;}}
        public bool Play(string key,string bank,string vessel,Vector3 position,float gain,float pitch=1,bool loop=false,string group="other",int variant=-1,double scheduledDsp=0,float phase=-1,bool applyCooldown=true)
        {
            gain*=p.masterGain;if(gain<=.0001f||Vector3.Distance(listener.position,position)>p.audibleDistance)return false;Voice voice=null;double now=AudioSettings.dspTime;bool started=false;
            if(loop)foreach(var v in voices)if(v.Busy&&v.Loop&&v.Key==key){voice=v;break;}
            if(voice==null)
            {
                if(!loop&&applyCooldown&&cooldown.TryGetValue(key,out double last)&&now-last<p.pairCooldown)return false;
                var clip=Choose(bank,variant);if(!clip||clip.loadState!=AudioDataLoadState.Loaded)return false;
                Voice weakest=null;float weakestNow=float.MaxValue;int count=0;foreach(var v in voices){if(v.Busy&&!v.Loop&&now>=v.End)v.Busy=false;if(v.Busy&&v.Group==group&&!v.Loop){count++;float level=v.TargetGain*(float)Math.Exp(-Math.Max(0,now-v.Start)/Math.Max(.01,p.stealDecaySeconds));if(level<weakestNow){weakestNow=level;weakest=v;}}if(!v.Busy&&voice==null)voice=v;}
                int limit=group=="ice"?p.iceConcurrency:p.otherConcurrency;if(!loop&&count>=limit){if(gain<.5f*weakestNow)return false;voice=weakest;}
                if(voice==null){foreach(var v in voices)if(weakest==null||v.TargetGain<weakest.TargetGain)weakest=v;voice=weakest;}
                if(voice.Busy){voice.Source.Stop();Stolen++;}
                voice.Key=key;voice.Bank=bank;voice.Vessel=vessel;voice.Group=group;voice.Loop=loop;voice.Busy=true;voice.Source.clip=clip;voice.Source.loop=loop;voice.PendingStart=loop;voice.Envelope.Reset();
                // Paired bright/dull variants use identical phase and no independent pitch jitter.
                voice.Variation=variant>=0?1:(float)Math.Pow(10,(rng.NextDouble()*2-1)*p.volumeVariationDb/20);voice.PitchVariation=variant>=0?1:1+(float)(rng.NextDouble()*2-1)*p.pitchVariation;
                voice.Source.pitch=pitch*voice.PitchVariation;voice.Source.transform.position=position;voice.Source.volume=loop?0:Mathf.Clamp01(gain*voice.Variation);
                if(loop)voice.Source.time=Mathf.Clamp01(phase>=0?phase:(float)rng.NextDouble())*Mathf.Max(0,clip.length-.001f);else voice.Source.time=0;
                voice.Start=scheduledDsp>0?Math.Max(now+.001,scheduledDsp):now;voice.End=voice.Start+clip.length/Mathf.Max(.01f,voice.Source.pitch);
                if(scheduledDsp>0)voice.Source.PlayScheduled(voice.Start);else voice.Source.Play();if(applyCooldown)cooldown[key]=now;Started++;started=true;
            }
            voice.Wanted=true;voice.Vessel=vessel;voice.Source.transform.position=position;voice.TargetGain=Mathf.Clamp01(gain*voice.Variation);if(loop)voice.Source.pitch=pitch*voice.PitchVariation;else{voice.Source.volume=voice.TargetGain;Record(voice,started);}return true;
        }
        private void Record(Voice v,bool started,bool stopped=false)
        {if(started||stopped||Time.realtimeSinceStartupAsDouble-v.LastLog>=p.observationSeconds&&(Math.Abs(v.Source.volume-v.LastGain)>p.gainChange||Math.Abs(v.Source.pitch-v.LastPitch)>p.pitchChange))
            {observation?.Invoke(v.Vessel,new SoundObservation{category=v.Group,bank=v.Bank,clip=v.Source.clip.name,gain=v.Source.volume,pitch=v.Source.pitch,loop=v.Loop,started=started,scheduledDsp=v.Start});v.LastGain=v.Source.volume;v.LastPitch=v.Source.pitch;v.LastLog=Time.realtimeSinceStartupAsDouble;}}
        public void EndFrame()=>EndFrame(Time.unscaledDeltaTime);
        public void EndFrame(float dt){foreach(var v in voices){if(!v.Busy||!v.Loop)continue;v.Source.volume=v.Envelope.Step(v.Wanted?v.TargetGain:0,dt);bool stopped=!v.Wanted&&v.Source.volume<=0;Record(v,v.PendingStart,stopped);v.PendingStart=false;if(stopped){v.Source.Stop();v.Loop=v.Busy=false;}}}
        public float Gain(string vessel,bool iceOnly){float gain=0;foreach(var v in voices)if(v.Source.isPlaying&&v.Vessel==vessel&&(!iceOnly||v.Group=="ice"))gain=Mathf.Max(gain,v.Source.volume);return gain;}
        public bool Playing(string vessel){foreach(var v in voices)if(v.Source.isPlaying&&v.Vessel==vessel)return true;return false;}
        /// <summary>立即取消本次检视的已排期和正在响的接触声，其他物品/制作声保持播放。</summary>
        public void StopKeyPrefix(string prefix)
        {foreach(var v in voices)if(v.Busy&&v.Key!=null&&v.Key.StartsWith(prefix,StringComparison.Ordinal)){v.Source.Stop();v.Source.volume=0;Record(v,false,true);v.Loop=v.Busy=false;}}
        public void Stop(){foreach(var v in voices){v.Source.Stop();v.Source.volume=0;v.Loop=v.Busy=false;}}
        public void Dispose(){foreach(var v in voices)if(v.Source)UnityEngine.Object.Destroy(v.Source.gameObject);}
    }
}
