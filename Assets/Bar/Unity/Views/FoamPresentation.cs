using System;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只订阅已确认 Domain 事件；含气量是未标定显示量，不是新增核心物理。</summary>
    public sealed class FoamPresentation:IDisposable
    {
        private sealed class State{internal double Time,Air,Fizz;}
        private readonly Dictionary<string,State> states=new Dictionary<string,State>();private readonly WorldAdapter adapter;private readonly FoamDisplaySettings p;
        public FoamPresentation(WorldAdapter adapter,FoamDisplaySettings p){this.adapter=adapter;this.p=p;foreach(var v in adapter.World.Vessels)states[v.id]=new State{Time=adapter.World.Time};adapter.Events.Published+=Observe;}
        private State At(string id,double time){if(!states.TryGetValue(id,out var s))states[id]=s=new State{Time=time};s.Air*=Math.Exp(-Math.Max(0,time-s.Time)/p.decaySeconds);s.Fizz*=Math.Exp(-Math.Max(0,time-s.Time)/PourSettings.Load().guided.foamDecaySeconds);s.Time=time;return s;}
        private void Observe(DomainEvent e)
        {
            if(e.Kind!="core_operation"||e.Observation is not LogRecord r||r.rejected||r.operation==null)return;var op=r.operation;
            if(op.type=="PourCarbonated"){var state=At(op.to,r.time);var g=PourSettings.Load().guided;state.Fizz=Math.Min(g.directFoamMetres,state.Fizz+g.directFoamMetres*r.co2LostOnEntryGrams.Value/g.foamLossGrams);}
            if(op.type=="Shake"){var s=At(op.target,r.time);s.Air=1-(1-s.Air)*Math.Exp(-(op.seconds??0)*(op.intensity??1)/p.shakeBuildSeconds);}
            if(op.type=="DiscardLiquid"&&op.from!=null){At(op.from,r.time).Air=0;At(op.from,r.time).Fizz=0;}
            if(op.to==null||r.before==null||r.after==null)return;
            double beforeTarget=0,afterTarget=0;foreach(var v in r.before)if(v.id==op.to)beforeTarget=v.volumeMl;foreach(var v in r.after)if(v.id==op.to)afterTarget=v.volumeMl;
            double added=Math.Max(0,afterTarget-beforeTarget);if(added<=0||afterTarget<=0)return;double incoming=op.from==null?0:At(op.from,r.time).Air;var to=At(op.to,r.time);to.Air=(to.Air*beforeTarget+incoming*p.transferRetention*added)/afterTarget;
        }
        // τ is normalised by a sour-like turbidity (tauForFullFoam), otherwise a shaken Sour (τ≈0.1) only showed a 0.6 mm line.
        public float Height(string id,double tau)=>Mathf.Max((float)At(id,adapter.World.Time).Fizz,Mathf.Clamp01((float)At(id,adapter.World.Time).Air)*Mathf.Clamp01((float)tau/Mathf.Max(.0001f,p.tauForFullFoam))*p.maximumHeightMetres);
        public float Aeration(string id)=>Mathf.Clamp01((float)At(id,adapter.World.Time).Air);
        public void Dispose()=>adapter.Events.Published-=Observe;
    }
}
