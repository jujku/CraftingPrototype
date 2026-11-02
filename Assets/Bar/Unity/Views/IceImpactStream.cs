using System;
using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    public enum IceImpactKind {Wall,Ice,Lid}
    /// <summary>只用于视听表现的冲量事件；时间是独立显示时钟，位置是世界坐标。</summary>
    public readonly struct IceImpact
    {
        /// <summary>这块冰刚放进容器后的第一次落定（声音只给它）。</summary>
        public readonly bool Landing;
        public readonly float Time,Impulse,Strength,Submerged;public readonly IceImpactKind Kind;public readonly Vector3 Position;public readonly int Cube,Other;
        public IceImpact(float time,float impulse,float strength,IceImpactKind kind,Vector3 position,int cube,int other,float submerged=0,bool landing=false)
        {Landing=landing;Time=time;Impulse=impulse;Strength=strength;Submerged=submerged;Kind=kind;Position=position;Cube=cube;Other=other;}
    }
    /// <summary>冲量门限、物体对冷却和每秒限额；不访问物理核心。</summary>
    public sealed class IceImpactStream
    {
        private readonly IceImpactSettings settings;private readonly Dictionary<long,float> pairs=new Dictionary<long,float>();
        private readonly Queue<float> times=new Queue<float>();private readonly Queue<IceImpact> history=new Queue<IceImpact>();
        public event Action<IceImpact> Emitted;public IEnumerable<IceImpact> Recent=>history;public int Count {get;private set;}
        public IceImpactStream(IceImpactSettings settings){this.settings=settings;}
        public bool Observe(float time,float impulse,IceImpactKind kind,Vector3 position,int cube,int other,float submerged=0,bool landing=false)
        {
            if(impulse<settings.minimumImpulse)return false;int a=Math.Min(cube,other),b=Math.Max(cube,other);long key=((long)(uint)a<<32)|(uint)b;
            if(pairs.TryGetValue(key,out float last)&&time-last<settings.pairCooldown)return false;
            while(times.Count>0&&time-times.Peek()>=1)times.Dequeue();if(times.Count>=settings.eventsPerSecond)return false;
            pairs[key]=time;times.Enqueue(time);var impact=new IceImpact(time,impulse,Mathf.Clamp01(impulse/settings.fullImpulse),kind,position,cube,other,submerged,landing);
            history.Enqueue(impact);while(history.Count>settings.historyCount)history.Dequeue();Count++;Emitted?.Invoke(impact);return true;
        }
    }
}
