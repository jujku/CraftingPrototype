using System;
using UnityEngine;
namespace Bar.Unity
{
    [Serializable] public sealed class BartenderLocomotionSettings
    {
        public string version="ginza-locomotion-2";
        public bool footstepsEnabled=false;
        public float workSpeed=1.85f,loungeSpeed=1.85f,workStrafe=.82f,loungeStrafe=.82f,workBack=.70f,loungeBack=.70f,zoneBlendSeconds=.35f;
        public float workFrequency=4.8f,loungeFrequency=4.8f,workZeta=.90f,loungeZeta=.90f;
        public float gazeDistance=1.8f,gazePitch=12,gazeBobScale=.15f,cameraVertical=.009f,handSide=.006f,handVertical=.012f;
        public float fullEnter=.65f,fullExit=.60f,carryScale=.70f,carryStopSecondsScale=1.4f,carryZeta=1.15f,actionScale=.20f,actionLeash=.45f;
        public float workStride=.50f,loungeStride=.65f,shuffleDistance=.28f,stopSpeed=.025f,stopDelay=.12f,stepGain=.30f,shuffleGain=.14f;
        public void Validate()
        {
            foreach(var f in GetType().GetFields())if(f.FieldType==typeof(float)){float v=(float)f.GetValue(this);if(float.IsNaN(v)||float.IsInfinity(v)||v<=0)throw new ArgumentException("Invalid locomotion setting: "+f.Name);}
            if(string.IsNullOrWhiteSpace(version)||fullExit>=fullEnter||fullEnter>=1||carryScale>1||actionScale>1||gazeBobScale>1)throw new ArgumentException("Invalid locomotion parameter relationship");
        }
    }
    /// <summary>Movement mathematics only; never advances or edits vessel simulation.</summary>
    public sealed class BartenderLocomotionModel
    {
        private readonly BartenderLocomotionSettings p;private readonly SecondOrderVector speed;
        private bool zoneReady;private float zone;public bool Careful{get;private set;}public float LoungeBlend=>zone;
        public Vector3 Velocity=>speed.Value;
        public BartenderLocomotionModel(BartenderLocomotionSettings settings){p=settings;p.Validate();speed=new SecondOrderVector(p.workFrequency,p.workZeta,0,Vector3.zero);}
        public void SetCareful(float openFill){if(float.IsNaN(openFill)||float.IsInfinity(openFill)||openFill<0||openFill>1.001f)throw new ArgumentException("Invalid open-vessel fill");Careful=Careful?openFill>p.fullExit:openFill>=p.fullEnter;}
        public float SpeedScale(bool stationAction)=>stationAction?p.actionScale:Careful?p.carryScale:1;
        public Vector3 Advance(Vector2 input,Quaternion heading,float zoneTarget,bool stationAction,float dt)
        {
            if(!float.IsFinite(dt)||dt<=0||!float.IsFinite(zoneTarget)||zoneTarget<0||zoneTarget>1||!float.IsFinite(input.x)||!float.IsFinite(input.y))throw new ArgumentException("Invalid movement input");
            if(!zoneReady){zone=zoneTarget;zoneReady=true;}else zone=Mathf.MoveTowards(zone,zoneTarget,dt/p.zoneBlendSeconds);
            var axis=Vector2.ClampMagnitude(input,1);float forward=Mathf.Lerp(p.workSpeed,p.loungeSpeed,zone),strafe=Mathf.Lerp(p.workStrafe,p.loungeStrafe,zone),back=Mathf.Lerp(p.workBack,p.loungeBack,zone);
            var target=heading*new Vector3(axis.x*strafe,0,axis.y*(axis.y<0?back:1))*forward*SpeedScale(stationAction);
            float frequency=Mathf.Lerp(p.workFrequency,p.loungeFrequency,zone);if(Careful&&axis.sqrMagnitude<.0001f)frequency/=p.carryStopSecondsScale;
            speed.Reconfigure(frequency,Mathf.Lerp(p.workZeta,p.loungeZeta,zone),0);return speed.Update(dt,target);
        }
        public void CollisionVelocity(Vector3 actual)=>speed.Reset(actual);
        public void Stop()=>speed.Reset(Vector3.zero);
        public static bool LeftStation(Vector3 start,Vector3 current,float leash){var d=current-start;d.y=0;return d.sqrMagnitude>leash*leash;}
    }
    public sealed class BartenderStride
    {
        private float progress,episode,stopped;private bool active;public float Phase{get;private set;}public int Steps{get;private set;}public bool Shuffle{get;private set;}
        public void Advance(float distance,bool intentional,bool grounded,float speed,float stride,float dt,BartenderLocomotionSettings p)
        {
            if(distance<0||!float.IsFinite(distance)||stride<=0||dt<=0)throw new ArgumentException("Invalid stride integration");Steps=0;Shuffle=false;
            if(!grounded){Clear();return;}
            if(intentional){active=true;stopped=0;}
            if(active&&speed>p.stopSpeed){episode+=distance;float cycles=distance/stride;progress+=cycles;Phase=Mathf.Repeat(Phase+Mathf.PI*cycles,Mathf.PI*2);while(progress>=1){progress-=1;Steps++;}stopped=0;}
            else if(active){stopped+=dt;if(stopped>=p.stopDelay){Shuffle=episode>0&&episode<p.shuffleDistance;Clear();}}
        }
        public void Clear(){active=false;episode=progress=stopped=0;}
    }
}
