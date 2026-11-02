using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>Explicit scene topology in the counter's coordinate frame; no runtime node search.</summary>
    public sealed class BartenderSpatialLayout:MonoBehaviour
    {
        public Transform Frame;public Collider[] Furniture;
        public Bounds WorkCorridor,Lounge,Room,RubberMat;
        public void Validate()
        {
            if(!Frame||Furniture==null||Furniture.Length==0||WorkCorridor.size.x<=0||Room.size.z<=0||Lounge.size.z<=0||RubberMat.size.x<=0)throw new InvalidOperationException("Incomplete bartender spatial layout");
            foreach(var c in Furniture)if(!c)throw new InvalidOperationException("Missing furniture collider");
        }
        public Vector3 Local(Vector3 position)=>Frame.InverseTransformPoint(position);
        private static bool Within(Bounds b,Vector3 p)=>p.x>=b.min.x&&p.x<=b.max.x&&p.z>=b.min.z&&p.z<=b.max.z;
        public float LoungeWeight(Vector3 world)
        {
            var p=Local(world);if(Within(WorkCorridor,p))return 0;if(Within(Lounge,p))return 1;
            float dx=Mathf.Max(Mathf.Max(WorkCorridor.min.x-p.x,0),p.x-WorkCorridor.max.x),dz=Mathf.Max(Mathf.Max(WorkCorridor.min.z-p.z,0),p.z-WorkCorridor.max.z);
            return Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Sqrt(dx*dx+dz*dz)/.65f));
        }
        public bool OnRubber(Vector3 world)=>Within(RubberMat,Local(world));
    }
}
