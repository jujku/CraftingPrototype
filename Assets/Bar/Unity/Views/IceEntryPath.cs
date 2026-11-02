using UnityEngine;
namespace Bar.Unity
{
    /// <summary>接收内腔局部空间的入杯路径：原地抬高、在口上方平移、沿口轴下降。整块底角留净空。</summary>
    public static class IceEntryPath
    {
        public static Vector3 Position(Vector3 from,Vector3 landing,float rim,float lowestCorner,float clearance,float progress)
        {
            float height=Mathf.Max(from.y,rim-lowestCorner+Mathf.Max(0,clearance));
            var raised=new Vector3(from.x,height,from.z);var gate=new Vector3(landing.x,height,landing.z);
            float lift=Vector3.Distance(from,raised),cross=Vector3.Distance(raised,gate),drop=Vector3.Distance(gate,landing);
            float distance=(lift+cross+drop)*Mathf.SmoothStep(0,1,Mathf.Clamp01(progress));
            if(distance<lift)return Vector3.Lerp(from,raised,distance/Mathf.Max(lift,1e-7f));
            distance-=lift;if(distance<cross)return Vector3.Lerp(raised,gate,distance/Mathf.Max(cross,1e-7f));
            return Vector3.Lerp(gate,landing,(distance-cross)/Mathf.Max(drop,1e-7f));
        }
    }
}
