using UnityEngine;
using CraftingResearch.CoreV2;
namespace Bar.Unity
{
    /// <summary>带阻尼的二维弹簧与 Beer–Lambert 参数，不回写核心。</summary>
    public sealed class LiquidMotion
    {
        public Vector3 Normal {get;private set;}=Vector3.up;private Quaternion rotation;
        private float scatteringGain;private Vector3 last,velocity;private Vector2 spring,rate;private bool initialized;
        public void Apply(Material material,Transform frame,VesselState snapshot,VisualSettings p,float dt,float agitation,float? maximumTilt=null)
        {
            if(scatteringGain<=0)scatteringGain=BPresentationSettings.Load().surface.scatteringGain;
            if(!initialized){last=frame.position;rotation=frame.rotation;initialized=true;}
            dt=Mathf.Clamp(dt,.0001f,.05f);var next=(frame.position-last)/dt;var acceleration=(next-velocity)/dt;last=frame.position;velocity=next;
            var angular=(frame.rotation*Quaternion.Inverse(rotation)).eulerAngles;rotation=frame.rotation;
            var tilt=new Vector2(Mathf.DeltaAngle(0,angular.z),-Mathf.DeltaAngle(0,angular.x))*Mathf.Deg2Rad;
            var forcing=tilt*p.tiltGain+new Vector2(-acceleration.x,-acceleration.z)*p.accelerationGain;
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.008f));float h=dt/steps;
            float zeta=Mathf.Lerp(p.damping,p.syrupDamping,Mathf.Clamp01((float)(snapshot.sugarPercent??0)/30));
            float limit=Mathf.Tan(Mathf.Min(p.maxTilt,maximumTilt??p.maxTilt)*Mathf.Deg2Rad);
            for(int i=0;i<steps;i++){rate+=(forcing-2*zeta*p.springFrequency*rate-p.springFrequency*p.springFrequency*spring)*h;spring+=rate*h;spring=Vector2.ClampMagnitude(spring,limit);if(limit==0)rate=Vector2.zero;}
            var normal=new Vector3(spring.x,1,spring.y).normalized;
            Normal=normal;material.SetMatrix(BShaderIds.ContainerToWorld,frame.localToWorldMatrix);material.SetVector(BShaderIds.PlaneNormal,normal);material.SetVector(BShaderIds.PlanePivot,frame.position);material.SetMatrix(BShaderIds.WorldToContainer,frame.worldToLocalMatrix);
            material.SetFloat(BShaderIds.Meniscus,p.meniscus);material.SetFloat(BShaderIds.FoamBand,p.foamBand);material.SetFloat(BShaderIds.Agitation,agitation);
            material.SetFloat(BShaderIds.MinPath,p.minimumPathCm);material.SetFloat(BShaderIds.MaxPath,p.maximumPathCm);material.SetFloat(BShaderIds.ScatterPath,p.scatteringDistanceCm);material.SetFloat(BShaderIds.ScatterGain,scatteringGain);
            var a=snapshot.optical;
            material.SetVector(BShaderIds.Absorption,a==null?Vector4.zero:new Vector4((float)a[0],(float)a[1],(float)a[2],0));
            float tau=a==null?0:Mathf.Clamp01((float)a[3]);material.SetFloat(BShaderIds.Turbidity,tau);
            material.SetColor(BShaderIds.ScatterColor,a==null||tau<.0001?Color.white:new Color((float)a[4]/tau,(float)a[5]/tau,(float)a[6]/tau));
            material.SetFloat(BShaderIds.CO2,(float)(snapshot.co2GL??0));
        }
    }
}
