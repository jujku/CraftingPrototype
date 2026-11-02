using UnityEngine;
namespace Bar.Unity
{
    /// <summary>循环采样的有限时长音量包络；静音门禁仍可以立即停止。</summary>
    public sealed class AudioGainEnvelope
    {
        private readonly float attack,release,change;private float previousTarget,releaseRate;
        public float Value {get;private set;}
        public AudioGainEnvelope(float attack,float release,float change){this.attack=attack;this.release=release;this.change=change;}
        public void Reset(){Value=previousTarget=releaseRate=0;}
        public float Step(float target,float dt)
        {
            target=Mathf.Clamp01(target);dt=Mathf.Max(0,dt);
            if(target<=0)
            {
                if(previousTarget>0)releaseRate=Value/Mathf.Max(.001f,release);
                Value=Mathf.MoveTowards(Value,0,releaseRate*dt);
            }
            else Value=Mathf.MoveTowards(Value,target,Mathf.Max(target,Value)*dt/Mathf.Max(.001f,target>Value?attack:change));
            previousTarget=target;return Value;
        }
    }
}
