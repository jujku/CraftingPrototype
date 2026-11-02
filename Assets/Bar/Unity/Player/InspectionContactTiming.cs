using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读现有检视角度曲线，将接触角节点换算为声音时刻；不修改动画或核心。</summary>
    public static class InspectionContactTiming
    {
        private static float Integral(float u,int waves)=>u-Mathf.Sin(waves*2*Mathf.PI*u)/(waves*2*Mathf.PI);
        private static float Smooth(float u)=>u*u*u*(10+u*(-15+6*u));
        /// <summary>无 phase 时沿用 time；角节点采用原动画的单调曲线逆解，单位为秒。</summary>
        public static float Time(HoldInspectionSound cue,HoldInspectSettings j,HoldInspectionProfile p)
        {
            if(string.IsNullOrEmpty(cue.phase))return cue.time;
            if(cue.phase=="spoon.stop")return p.lookSeconds+p.windupSeconds+p.airSeconds;
            if(cue.phase=="jigger.catch")return j.twirlEnd+j.tossAirSeconds;
            float start,duration,total;int mode;
            if(cue.phase=="spoon.roll"){start=0;duration=p.lookSeconds;total=p.lookRollDegrees;mode=1;}
            else if(cue.phase=="spoon.whip"){start=p.lookSeconds+p.windupSeconds;duration=p.airSeconds;total=p.turnDegrees;mode=2;}
            else if(cue.phase=="jigger.roll"){start=j.lookEnd;duration=j.twirlEnd-j.lookEnd;total=j.twirlDegrees;mode=3;}
            else throw new ArgumentException("Unknown inspection contact phase: "+cue.phase);
            float target=Mathf.Clamp01(cue.degrees/Mathf.Max(.001f,total)),lo=0,hi=1;
            for(int i=0;i<24;i++){float mid=(lo+hi)*.5f;float value=mode==1?Integral(mid,1):mode==2?Smooth(mid/(p.spinBias+(1-p.spinBias)*mid)):Integral(mid,2);if(value<target)lo=mid;else hi=mid;}
            return start+duration*(lo+hi)*.5f;
        }
    }
}
