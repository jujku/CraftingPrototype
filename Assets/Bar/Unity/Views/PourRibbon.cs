using UnityEngine;
namespace Bar.Unity
{
    /// <summary>IPourStream 的解析重力管线实现；不反求速度，半径由确认流量和局部速度守恒计算。</summary>
    public sealed class PourRibbon : MonoBehaviour,IPourStream
    {
        private Mesh mesh;private MeshRenderer rendererCache;private Material material,impactMaterial;private ParticleSystem splash,drops;private LineRenderer ripple;private float phase,dropBudget;private int lastFrame=-1;private VisualSettings visual;
        public Vector3[] Centerline {get;private set;}=System.Array.Empty<Vector3>();
        public PourStreamInput LastInput {get;private set;}
        private readonly Transform[] renderRoots=new Transform[4];
        public Transform[] RenderRoots{get{renderRoots[0]=rendererCache?rendererCache.transform:null;renderRoots[1]=splash?splash.transform:null;renderRoots[2]=drops?drops.transform:null;renderRoots[3]=ripple?ripple.transform:null;return renderRoots;}}
        public bool Visible=>rendererCache&&rendererCache.enabled||drops&&drops.particleCount>0;
        public Color Tint {get;set;}=new Color(.85f,.94f,1);
        public void Hide(){if(rendererCache)rendererCache.enabled=false;if(splash)splash.Stop();if(drops)drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);if(ripple)ripple.enabled=false;dropBudget=0;Centerline=System.Array.Empty<Vector3>();}
        private ParticleSystem Particles(string name,float size,float lifetime)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);var ps=go.AddComponent<ParticleSystem>();var main=ps.main;main.playOnAwake=false;main.startLifetime=lifetime;main.startSpeed=0;main.startSize=size;main.maxParticles=PourSettings.Load().maxStreamParticles;main.simulationSpace=ParticleSystemSimulationSpace.World;ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;return ps;
        }
        private void Prepare()
        {
            if(mesh)return;var go=new GameObject("Core flow parabola");go.transform.SetParent(transform,false);mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;rendererCache=go.AddComponent<MeshRenderer>();
            material=new Material(Shader.Find("Bar/PourRibbon"));material.SetTexture("_NoiseTex",Resources.Load<Texture2D>("PourNoise"));rendererCache.sharedMaterial=material;
            impactMaterial=new Material(material);impactMaterial.SetFloat("_ImpactClip",1);
            var p=PourSettings.Load();visual=JsonUtility.FromJson<CueSettings>(Resources.Load<CueSettingsAsset>("BarCueSettings").Json.text).visual;
            splash=Particles("Ballistic impact splash",p.splashSize,p.splashLifetimeSeconds);var main=splash.main;main.startSpeed=p.splashSpeed;var shape=splash.shape;shape.shapeType=ParticleSystemShapeType.Hemisphere;shape.radius=p.splashRadius;
            splash.GetComponent<ParticleSystemRenderer>().sharedMaterial=impactMaterial;
            splash.transform.rotation=Quaternion.FromToRotation(Vector3.forward,Vector3.up);
            drops=Particles("Low flow droplets",p.dropletDiameter,1);var ds=drops.shape;ds.enabled=false;
            ripple=new GameObject("Ballistic impact ripple").AddComponent<LineRenderer>();ripple.transform.SetParent(transform,false);ripple.sharedMaterial=impactMaterial;ripple.positionCount=33;ripple.startWidth=ripple.endWidth=p.rippleLineWidth;ripple.loop=true;ripple.alignment=LineAlignment.TransformZ;ripple.transform.rotation=Quaternion.FromToRotation(Vector3.forward,Vector3.up);
        }
        public void Draw(PourStreamInput input)
        {
            LastInput=input;var p=PourSettings.Load();
            if(input.ConfirmedMlPerSecond<=0||!PourTrajectory.PlaneTime(input.Outlet.position,input.ExitVelocity,input.LiquidPlaneY,p.gravity,out float duration)){Hide();return;}
            Prepare();material.SetColor("_BaseColor",Tint);impactMaterial.SetColor("_BaseColor",Tint);
            float delta=lastFrame==Time.frameCount?0:Time.unscaledDeltaTime;lastFrame=Time.frameCount;
            int n=p.streamSegments,sides=p.streamSides;Centerline=new Vector3[n+1];var vertices=new Vector3[(n+1)*sides];var triangles=new int[n*sides*6];var uv=new Vector2[vertices.Length];
            float contactTime=0,dropTime=0;
            if(input.Guided){if(!PourTrajectory.PlaneTime(input.Outlet.position,input.ExitVelocity,input.GuideContact.y,p.gravity,out contactTime))throw new System.InvalidOperationException("Guided stream must reach contact from above");if(input.GuideEnd.y<input.LiquidPlaneY-.00001f)throw new System.InvalidOperationException("Guided film endpoint is below liquid");if(input.GuideEnd.y>input.LiquidPlaneY&&!PourTrajectory.PlaneTime(input.GuideEnd,Vector3.down*.15f,input.LiquidPlaneY,p.gravity,out dropTime))throw new System.InvalidOperationException("Guided film cannot reach liquid plane");}
            for(int i=0;i<=n;i++)
            {
                float time=duration*i/n;var point=PourTrajectory.Point(input.Outlet.position,input.ExitVelocity,time,p.gravity);Centerline[i]=point;
                var velocity=PourTrajectory.Velocity(input.ExitVelocity,time,p.gravity);
                if(input.Guided)
                {
                    float f=i/(float)n;
                    if(f<=.3f){float t=contactTime*f/.3f;point=PourTrajectory.Point(input.Outlet.position,input.ExitVelocity,t,p.gravity);velocity=PourTrajectory.Velocity(input.ExitVelocity,t,p.gravity);}
                    else if(f<=.78f){float u=(f-.3f)/.48f;var axis=(input.GuideEnd-input.GuideContact).normalized;var rightFilm=Vector3.Cross(axis,Vector3.right).normalized;point=Vector3.Lerp(input.GuideContact,input.GuideEnd,u)+rightFilm*(Mathf.Sin(u*4*Mathf.PI)*p.guided.filmRadius*Mathf.Sin(Mathf.PI*u));velocity=axis;}
                    else {float u=(f-.78f)/.22f;point=PourTrajectory.Point(input.GuideEnd,Vector3.down*.15f,dropTime*u,p.gravity);velocity=PourTrajectory.Velocity(Vector3.down*.15f,dropTime*u,p.gravity);}
                    Centerline[i]=point;
                }
                var tangent=velocity.normalized;
                var right=Vector3.Cross(tangent,Vector3.forward).normalized;if(right.sqrMagnitude<.01)right=Vector3.right;var up=Vector3.Cross(tangent,right);
                float radius=PourTrajectory.Radius(input.ConfirmedMlPerSecond,velocity.magnitude,p.minimumStreamRadius);
                // A surface film is a thin sheet; free-flight sections retain the round jet.
                float thickness=input.Guided&&i/(float)n>.3f&&i/(float)n<.78f?p.guided.filmRadius:radius;
                for(int j=0;j<sides;j++)
                {float a=j*2*Mathf.PI/sides;uv[i*sides+j]=new Vector2(j/(float)sides,i/(float)n);vertices[i*sides+j]=transform.InverseTransformPoint(point+right*Mathf.Cos(a)*radius+up*Mathf.Sin(a)*thickness);if(i<n){int k=(i*sides+j)*6,b=i*sides+j,c=i*sides+(j+1)%sides;triangles[k]=b;triangles[k+1]=c+sides;triangles[k+2]=c;triangles[k+3]=b;triangles[k+4]=b+sides;triangles[k+5]=c+sides;}}
            }
            bool drip=!input.Guided&&input.ConfirmedMlPerSecond<p.dropletThresholdMlPerSecond;rendererCache.enabled=!drip;
            if(drip)
            {
                var main=drops.main;main.gravityModifier=p.gravity/Physics.gravity.magnitude;var emission=drops.emission;emission.enabled=false;
                if(!drops.isPlaying)drops.Play();
                float volumeMl=Mathf.PI/6*p.dropletDiameter*p.dropletDiameter*p.dropletDiameter*1e6f;
                dropBudget+=input.ConfirmedMlPerSecond/volumeMl*delta;int count=Mathf.FloorToInt(dropBudget);dropBudget-=count;
                if(count>0){var emit=new ParticleSystem.EmitParams{position=input.Outlet.position,velocity=input.ExitVelocity,startColor=Tint,startLifetime=duration};drops.Emit(emit,Mathf.Min(count,p.maxStreamParticles));}
            }
            else if(drops.particleCount>0)drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();
            Vector3 end=Centerline[n];
            var center=input.HasImpactBoundary?input.ImpactCenter:new Vector3(input.MouthCenter.x,input.LiquidPlaneY,input.MouthCenter.z);
            float impactRadius=Mathf.Max(0,(input.HasImpactBoundary?input.ImpactRadius:input.MouthRadius)-p.impactRimInset);
            impactMaterial.SetVector("_ImpactCenter",center);impactMaterial.SetFloat("_ImpactRadius",impactRadius);
            float offset=new Vector2(end.x-center.x,end.z-center.z).magnitude;
            float available=impactRadius-offset;
            // Include the ring stroke and the splash billboard itself, not just its centre.
            phase=Mathf.Repeat(phase+delta/visual.rippleSeconds,1);
            ripple.enabled=available>p.rippleLineWidth;
            if(ripple.enabled)
            {
                float ringRadius=Mathf.Min(p.rippleInnerRadius+p.rippleGrowth*phase,available-p.rippleLineWidth*.5f);
                for(int i=0;i<33;i++){float a=i*2*Mathf.PI/32;ripple.SetPosition(i,end+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*ringRadius);}
            }
            if(available>p.splashSize*.5f)
            {
                splash.transform.position=end;var shape=splash.shape;shape.radius=Mathf.Min(p.splashRadius,available-p.splashSize*.5f);
                var e=splash.emission;e.rateOverTime=visual.splashRate*Mathf.Clamp01(input.ConfirmedMlPerSecond/p.splashReferenceFlowMlPerSecond);if(!splash.isPlaying)splash.Play();
            }
            else splash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void OnDestroy(){if(rendererCache)Destroy(rendererCache.gameObject);if(splash)Destroy(splash.gameObject);if(drops)Destroy(drops.gameObject);if(ripple)Destroy(ripple.gameObject);if(material)Destroy(material);if(impactMaterial)Destroy(impactMaterial);if(mesh)Destroy(mesh);}
    }
}
