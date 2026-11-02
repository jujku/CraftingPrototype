using System;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    public sealed class BartenderFootsteps:MonoBehaviour
    {
        [Serializable]private sealed class Banks{public AudioBank[] banks;}
        public TextAsset AudioBanks;private SampledAudioPool pool;private BartenderLocomotionSettings settings;
        public void Initialize(BarSession session,BartenderLocomotionSettings p)
        {
            if(!AudioBanks)throw new InvalidOperationException("Footstep banks not assigned");settings=p;var banks=JsonUtility.FromJson<Banks>(AudioBanks.text);
            if(banks.banks==null||banks.banks.Length!=4)throw new InvalidOperationException("Expected four footstep banks");foreach(var b in banks.banks)foreach(var path in b.paths)if(!Resources.Load<AudioClip>(path))throw new InvalidOperationException("Missing footstep audio: "+path);
            var audio=BarAudioCatalog.Load();audio.banks=banks.banks;audio.poolSize=4;audio.otherConcurrency=3;audio.spatialBlend=0;audio.pitchVariation=.025f;audio.volumeVariationDb=1.0f;
            pool=new SampledAudioPool(transform,session.Player.ViewCamera.transform,audio,(v,s)=>session.Adapter.Publish("presentation_footstep_sound",new LogRecord{reason=p.version,sound=s}));if(pool.PreloadFailures!=0)throw new InvalidOperationException("Footstep preload failed");
        }
        public void BeginFrame()=>pool.BeginFrame();public void EndFrame()=>pool.EndFrame();
        public void Play(bool rubber,bool shuffle,Vector3 at){string bank=(rubber?"rubber":"wood")+(shuffle?"Shuffle":"Step");pool.Play("locomotion:"+bank,bank,"player",at,shuffle?settings.shuffleGain:settings.stepGain,applyCooldown:false,group:"footsteps");}
        private void OnDestroy(){if(pool!=null)pool.Dispose();}
    }
}
