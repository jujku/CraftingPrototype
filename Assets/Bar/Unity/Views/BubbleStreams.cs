using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>36 个固定点共用一个 Shuriken 系统。停发射时让存量飞完；碰壁滑升，冰外侧发泡。</summary>
    public sealed class BubbleStreams:MonoBehaviour
    {
        public sealed class Site{public ParticleSystem System;public string Kind;public Vector3 LocalPosition;internal float Credit,Rate;}
        private readonly List<Site> sites=new List<Site>();private readonly List<Transform> iceRoots=new List<Transform>();private readonly List<MeshRenderer> iceRenderers=new List<MeshRenderer>();
        private Transform frame;private CavityShape shape;private LocalIceWorld ice;private BubbleDisplaySettings p;private Material material;private ParticleSystem system;private ParticleSystemRenderer particleRenderer;private Camera camera;
        private ParticleSystem.Particle[] particles;private float burst,lastCharge;private int previousIce=-1;
        public IReadOnlyList<Site> Sites=>sites;public float Burst=>burst;public int ActiveCount{get;private set;}public float Charge{get;private set;}public int EmittedCount{get;private set;}public int PopCount{get;private set;}
        public void Initialize(Transform frame,CavityShape shape,LocalIceWorld ice=null)
        {
            this.frame=frame;this.shape=shape;this.ice=ice;p=BPresentationSettings.Load().bubbles;camera=Camera.main;material=new Material(Shader.Find("Bar/Bubble"));
            var go=new GameObject("CO2 shared nucleation");go.transform.SetParent(frame,false);system=go.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=system.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.maxParticles=p.maxParticlesPerSource;main.startSpeed=0;main.startLifetime=10;main.startColor=new Color(.85f,.95f,1,.75f);
            var emission=system.emission;emission.enabled=false;var volume=system.shape;volume.enabled=false;system.useAutoRandomSeed=false;system.randomSeed=20261026;system.Pause();particleRenderer=system.GetComponent<ParticleSystemRenderer>();particleRenderer.sharedMaterial=material;particleRenderer.renderMode=ParticleSystemRenderMode.Billboard;
            particles=new ParticleSystem.Particle[p.maxParticlesPerSource];var rng=new System.Random(20261026);
            for(int i=0;i<p.sourceCount;i++){double u=Mathf.Max(.0001f,(float)rng.NextDouble()),v=rng.NextDouble();float rate=Mathf.Exp((float)(System.Math.Sqrt(-2*System.Math.Log(u))*System.Math.Cos(6.283185*v))*p.rateLogSigma);sites.Add(new Site{System=system,Kind=i%3==0?"bottom":i%3==1?"wall":"ice",Rate=rate});}
        }
        public void Present(float co2,float temperature,float height,Vector3 normal,float agitation,bool visible,float dt,Material liquid=null)
        {
            Charge=Mathf.Clamp01((co2-p.criticalCo2)/(p.fullCo2-p.criticalCo2));if(Charge>lastCharge+.02f||agitation>0)burst=1;lastCharge=Charge;burst*=Mathf.Exp(-dt/p.burstSeconds);
            float supersaturation=Charge*Mathf.Pow(p.temperatureFactor,(temperature-p.referenceTemperature)/10);float rate=p.rate*Mathf.Pow(Charge,1.5f)*(1+burst*p.burstGain);
            if(liquid){liquid.SetFloat(BShaderIds.BubbleStrength,visible?Charge*p.microStrength:0);liquid.SetFloat(BShaderIds.BubbleTiling,p.microTiling);liquid.SetVector(BShaderIds.BubbleAxis,new Vector4(shape.Axis.x,shape.Axis.y,0,0));}
            material.SetFloat(BShaderIds.FillHeight,height);material.SetVector(BShaderIds.PlaneNormal,normal);material.SetVector(BShaderIds.PlanePivot,frame.position);
            float minimum=0;if(camera){float distance=Vector3.Distance(camera.transform.position,frame.position);minimum=2*distance*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)/Mathf.Max(1,camera.pixelHeight)*p.minimumPixelSize;}material.SetFloat(BShaderIds.MinimumDiameter,minimum);
            if(ice&&previousIce!=ice.DisplayCount){iceRoots.Clear();iceRenderers.Clear();foreach(var r in ice.Displays){iceRoots.Add(r);foreach(var mr in r.GetComponentsInChildren<MeshRenderer>(true))if(mr.name=="IceSurface")iceRenderers.Add(mr);}previousIce=ice.DisplayCount;}
            float level=frame.InverseTransformPoint(new Vector3(frame.position.x,height,frame.position.z)).y;var upward=frame.InverseTransformDirection(Vector3.up);
            bool emitVisible=visible;
            for(int i=0;i<sites.Count;i++)
            {
                var site=sites[i];float angle=i*2.399963f;float y=site.Kind=="bottom"?shape.Bottom+p.largeSize:Mathf.Lerp(shape.Bottom,shape.Top,.08f+(i%7)*.12f);
                float radius=Mathf.Max(0,shape.RadiusAt(y)-p.largeSize)*(site.Kind=="bottom"?.2f+.6f*((i%5)/5f):1);var point=new Vector3(shape.Axis.x+Mathf.Cos(angle)*radius,y,shape.Axis.y+Mathf.Sin(angle)*radius);
                bool hasIce=site.Kind!="ice"||iceRoots.Count>0;
                if(site.Kind=="ice"&&iceRoots.Count>0){int k=(i/3)%iceRoots.Count;var r=iceRenderers[k];var bounds=r.localBounds;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));direction/=Mathf.Max(Mathf.Abs(direction.x),Mathf.Abs(direction.y));var local=bounds.center+new Vector3(direction.x*bounds.extents.x*1.04f,bounds.extents.y*(-.5f+(i%5)*.25f),direction.y*bounds.extents.z*1.04f);point=frame.InverseTransformPoint(r.transform.TransformPoint(local));}
                site.LocalPosition=point;
                if(!emitVisible||Charge<=0||point.y>=level-p.smallSize||!hasIce){site.Credit=0;continue;}
                site.Credit+=rate*site.Rate*dt/sites.Count;int count=Mathf.FloorToInt(site.Credit);site.Credit-=count;
                for(int j=0;j<count;j++){float size=Mathf.Lerp(p.smallSize,p.largeSize,Mathf.Abs(Mathf.Sin((EmittedCount+1)*137.5f)));system.Emit(new ParticleSystem.EmitParams{position=point,velocity=upward*p.initialSpeed,startSize=size,startLifetime=10,randomSeed=(uint)(EmittedCount+1)},1);EmittedCount++;}
            }
            system.Simulate(dt,false,false,false);system.Pause();int alive=system.GetParticles(particles);
            for(int i=0;i<alive;i++)
            {
                var particle=particles[i];var world=frame.TransformPoint(particle.position);float plane=Vector3.Dot(world-new Vector3(frame.position.x,height,frame.position.z),normal);
                if(plane>=0){particle.remainingLifetime=0;PopCount++;}
                else{float physical=Mathf.Min(.0015f,Mathf.Lerp(p.smallSize,p.largeSize,Mathf.Abs(Mathf.Sin(particle.randomSeed*137.5f)))+p.growthPerSecond*supersaturation*(particle.startLifetime-particle.remainingLifetime));var local=OutsideIce(particle.position,physical);float radius=Mathf.Max(0,shape.RadiusAt(local.y)-physical*.5f);var radial=new Vector2(local.x-shape.Axis.x,local.z-shape.Axis.y);if(radial.magnitude>radius){radial=radial.normalized*radius;local.x=shape.Axis.x+radial.x;local.z=shape.Axis.y+radial.y;}particle.position=local;
                    particle.startSize=Mathf.Max(physical,minimum);float alpha=.8f*Mathf.Sqrt(physical/particle.startSize);particle.startColor=new Color(.85f,.95f,1,alpha);particle.velocity=upward*(p.initialSpeed+physical*30);if(local.y<shape.Bottom)particle.remainingLifetime=0;}
                particles[i]=particle;
            }
            system.SetParticles(particles,alive);ActiveCount=system.particleCount;
        }
        private Vector3 OutsideIce(Vector3 point,float diameter)
        {
            var world=frame.TransformPoint(point);foreach(var r in iceRenderers){if(!r)continue;var p=r.transform.InverseTransformPoint(world);var b=r.localBounds;if(!b.Contains(p))continue;float x=b.extents.x-Mathf.Abs(p.x-b.center.x),z=b.extents.z-Mathf.Abs(p.z-b.center.z);float gap=diameter/Mathf.Max(.0001f,Mathf.Abs(r.transform.lossyScale.x));if(x<z)p.x=b.center.x+Mathf.Sign(p.x-b.center.x)*(b.extents.x+gap);else p.z=b.center.z+Mathf.Sign(p.z-b.center.z)*(b.extents.z+gap);world=r.transform.TransformPoint(p);}return frame.InverseTransformPoint(world);
        }
        private void OnDestroy(){if(material)Destroy(material);if(system)Destroy(system.gameObject);}
    }
}
