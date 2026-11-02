using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Unity;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>在每个实际呈现帧末检查流线，独立于协程的物理节拍采样。</summary>
    [DefaultExecutionOrder(500)]public sealed class PourFrameVerifier:MonoBehaviour
    {
        public ItemView Source;public BarSession Session;public int VisibleFrames;
        public readonly List<string> Errors=new List<string>();
        private void LateUpdate()
        {
            foreach(var ribbon in Session.GetComponents<PourRibbon>().Where(r=>r.Visible))
            {
                VisibleFrames++;var input=ribbon.LastInput;var points=ribbon.Centerline;if(points.Length<2){Errors.Add("no centreline");continue;}
                if(Vector3.Distance(points[0],Source.PourPoint)>=.001f)Errors.Add("mouth > 1mm");
                if(Mathf.Abs(points.Last().y-input.LiquidPlaneY)>=.002f)Errors.Add("impact > 2mm");
                bool descending=false;for(int i=1;i<points.Length;i++){if(points[i].y<points[i-1].y)descending=true;else if(descending&&points[i].y>points[i-1].y+.000001f)Errors.Add("rises after apex");}
                float g=PourSettings.Load().gravity;
                if(!PourTrajectory.PlaneTime(points[0],input.ExitVelocity,input.MouthCenter.y,g,out float t))Errors.Add("no opening intersection");
                else{var offset=PourTrajectory.Point(points[0],input.ExitVelocity,t,g)-input.MouthCenter;if(new Vector2(offset.x,offset.z).magnitude>=input.MouthRadius)Errors.Add("wall entry");}
            }
        }
    }
}
