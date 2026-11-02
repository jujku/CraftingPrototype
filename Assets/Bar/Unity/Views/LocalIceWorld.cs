using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bar.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Bar.Unity
{
    /// <summary>每容器独立的本地 PhysX。只有模型冰量决定块数与缩放，位姿从不写回核心。碰撞墙、出生点和防挤出约束都按 CavityShape 的真实内腔半径曲线生成，任何带 InnerCavity 的容器通用。</summary>
    [DefaultExecutionOrder(90)] public sealed class LocalIceWorld : MonoBehaviour
    {
        // Centre/Half: the drawn ice mesh box per unit cube side, so the collider and containment match what is rendered.
        private sealed class Cube {internal Rigidbody Body;internal BoxCollider Box;internal Transform Display;internal IceContactProbe Probe;internal float Scale=1,Jitter=1,Entry,Quiet,Wet,DeliveryTime;internal IceDeliveryBatch Delivery;internal int DeliveryIndex;internal bool NaturalEntry;internal Quaternion EntryRotation;internal Vector3 From,Centre,Half=Vector3.one*.5f,BeforeVelocity,AppliedAcceleration;}
        private readonly List<Cube> cubes=new List<Cube>();private Scene scene;private PhysicsScene physics;
        private CraftingResearch.CoreV2.Vessel authoritative;private BarSession session;private Container container;private ItemView view;private ContainerVisual visual;private VisualSettings p;
        private IceBoundary boundary;private Bounds bounds;private CavityShape shape;private int capacity;private Rigidbody spoon;private Vector3 last,velocity,acceleration;private double accumulator;private bool initialized;
        public Transform PreviewSpoon;public float PreviewIntensity;
        private int escapes;public int WallEscapes =>escapes+(boundary?boundary.Exits:0);public int DisplayCount=>cubes.Count;
        /// <summary>内腔几何块数上限，也限制新的工具加冰请求；不裁减已经存在的核心冰量。</summary>
        public int Capacity=>capacity;
        public int EnteringCount {get {int count=0;foreach(var cube in cubes)if(cube.Entry>0)count++;return count;}}
        /// <summary>本帧核心冰块数超过形状容量、显示被截断。</summary>
        public bool DisplayCapped {get;private set;}
        /// <summary>冰块物理使用的内腔形状。</summary>
        public CavityShape Shape=>shape;
        public double StepMilliseconds {get;private set;}
        public CollisionDetectionMode Detection=CollisionDetectionMode.Continuous;
        private BPresentationSettings bSettings;private GameObject retention;private float openLid,ceiling,visualTime,restingTime;private bool sealedNow;private Quaternion lastRotation;private double lastIce,lastLiquid;
        public IceImpactStream Impacts {get;private set;}
        public event Action PhysicsStepped;
        public IEnumerable<Rigidbody> Bodies {get {foreach(var cube in cubes)yield return cube.Body;}}
        public IEnumerable<Transform> Displays {get {foreach(var cube in cubes)yield return cube.Display;}}
        private IceVolumeSlice[] displacement=new IceVolumeSlice[0];
        public float LiquidHeight(double liquidMl)
        {
            if(displacement.Length<cubes.Count)displacement=new IceVolumeSlice[cubes.Count];int count=0;
            var core=authoritative;
            double perCube=core.ice/Math.Max(1,Math.Ceiling(core.ice/session.Adapter.World.Data.icePortionGrams))/session.Adapter.World.Data.iceDensity;
            foreach(var cube in cubes)
            {
                if(cube.Entry>0)continue;var box=cube.Box;
                displacement[count++]=new IceVolumeSlice(cube.Body.position,cube.Body.rotation,box.center,box.size,perCube);
            }
            return IceDisplacement.Height(visual.Profile,liquidMl,displacement,count);
        }
        public void Initialize(BarSession s,ItemView v,Container c,ContainerVisual binding,VisualSettings settings)
        {
            session=s;authoritative=s.Adapter.World.GetVessel(c.CoreVesselId);view=v;container=c;visual=binding;p=settings;bounds=CavityShape.BoundsIn(binding.InnerCavity,binding.transform);shape=binding.Shape;
            bSettings=BPresentationSettings.Load();Impacts=new IceImpactStream(bSettings.impacts);lastRotation=transform.rotation;ceiling=shape.Top;
            capacity=shape.CubeCapacity(p.iceSide*(1+Mathf.Abs(p.seedJitter)));
            // A cube wider than the whole mouth (the jigger cones) can only wedge in the rim with its top half outside.
            float lid=shape.LowestFit(p.iceSide*.70710678f,p.iceSide)<shape.Top?bounds.max.y:shape.Top+p.iceSide*1.2f;
            scene=SceneManager.CreateScene("Local ice "+GetInstanceID(),new CreateSceneParameters(LocalPhysicsMode.Physics3D));physics=scene.GetPhysicsScene();
            // Thick boards follow each frustum of the real cavity, so tapered shakers, coupe bowls and jigger cones hold ice like the straight highball.
            foreach(var wall in shape.Walls(p.wallCount,p.wallThickness)){var go=Collider("Thick cup wall",wall.Center,wall.Size);go.transform.rotation=wall.Rotation;}
            // Closed retention plane keeps hand-held display ice inside an abruptly tilted open cup; no numerical spill model is introduced.
            openLid=lid;retention=Collider("Visual retention plane",new Vector3(bounds.center.x,lid+p.wallThickness/2,bounds.center.z),new Vector3(bounds.size.x+p.wallThickness,p.wallThickness,bounds.size.z+p.wallThickness));
            var volume=Collider("Ice boundary trigger",bounds.center,bounds.size+Vector3.one*p.wallThickness*2);volume.GetComponent<BoxCollider>().isTrigger=true;boundary=volume.AddComponent<IceBoundary>();
            var sp=new GameObject("Kinematic spoon proxy");SceneManager.MoveGameObjectToScene(sp,scene);var cap=sp.AddComponent<CapsuleCollider>();cap.radius=p.spoonRadius;cap.height=.12f;spoon=sp.AddComponent<Rigidbody>();spoon.isKinematic=true;spoon.position=Vector3.one*10;
            last=transform.position;initialized=true;
        }
        private GameObject Collider(string name,Vector3 position,Vector3 size)
        {var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=position;var b=go.AddComponent<BoxCollider>();b.size=size;b.contactOffset=p.contactOffset;go.AddComponent<IceCollisionSurface>();return go;}
        private void LateUpdate(){if(initialized)Advance(Time.unscaledDeltaTime);}
        public void Advance(float dt)
        {
            var core=authoritative;bool closed=view.Item is CobblerShaker sh&&sh.State==ShakerState.Sealed;sealedNow=closed;
            int wanted=(int)Math.Ceiling(core.ice/session.Adapter.World.Data.icePortionGrams);DisplayCapped=wanted>capacity;
            int count=Math.Min(Math.Min(p.maxIce,capacity),wanted);
            ceiling=shape.Top;
            if(closed){var slot=view.Anchor("Slot_Strainer");if(slot)ceiling=Mathf.Min(shape.Top,transform.InverseTransformPoint(slot.position).y);}
            retention.transform.position=new Vector3(bounds.center.x,(closed?ceiling:openLid)+p.wallThickness/2,bounds.center.z);
            retention.GetComponent<IceCollisionSurface>().Kind=closed?IceImpactKind.Lid:IceImpactKind.Wall;
            // One receipt belongs to one batch. Reopening a shaker must not replay an old tool origin.
            bool hasEntry=session.IceOrigins.TryGetValue(container.CoreVesselId,out var entryOrigin);
            if(hasEntry)session.IceOrigins.Remove(container.CoreVesselId);
            session.IceDeliveries.TryGetValue(container.CoreVesselId,out var delivery);session.IceDeliveries.Remove(container.CoreVesselId);int deliveryIndex=0;
            bool contentsChanged=count!=cubes.Count||Math.Abs(core.ice-lastIce)>=bSettings.shake.wakeIceGrams||Math.Abs(core.volume-lastLiquid)>=bSettings.shake.wakeLiquidMl;
            if(contentsChanged){lastIce=core.ice;lastLiquid=core.volume;}
            while(cubes.Count>count){var c=cubes[cubes.Count-1];Destroy(c.Body.gameObject);Destroy(c.Display.gameObject);cubes.RemoveAt(cubes.Count-1);}
            while(cubes.Count<count)
            {
                int i=cubes.Count;var go=new GameObject("Local ice proxy");SceneManager.MoveGameObjectToScene(go,scene);var box=go.AddComponent<BoxCollider>();box.size=Vector3.one*p.iceSide;box.contactOffset=p.contactOffset;
                var rb=go.AddComponent<Rigidbody>();rb.useGravity=false;rb.mass=(float)(session.Adapter.World.Data.icePortionGrams/1000);rb.collisionDetectionMode=Detection;rb.maxDepenetrationVelocity=p.depenetration;rb.solverIterations=p.solverIterations;rb.sleepThreshold=p.sleep;
                float half=p.iceSide/2;
                float floor=shape.LowestFit(half*1.4142136f,p.iceSide);rb.position=new Vector3(shape.Axis.x,Mathf.Min(shape.Top-half,floor+half+i*p.iceSide),shape.Axis.y);
                rb.position=new Vector3(Mathf.Clamp(rb.position.x,bounds.min.x+half,bounds.max.x-half),Mathf.Clamp(rb.position.y,bounds.min.y+half,bounds.max.y-half),Mathf.Clamp(rb.position.z,bounds.min.z+half,bounds.max.z-half));
                rb.rotation=hasEntry?Quaternion.Euler(0,i*37,0):Quaternion.Euler(i*17%11,i*37,i*11%9);var display=IceAppearance.Create(i,transform);display.name="Local ice display";var cube=new Cube{Body=rb,Box=box,Display=display.transform,Jitter=1+p.seedJitter*(float)Math.Sin(20261021+i*17.13)};MeasureMesh(cube);box.center=cube.Centre*p.iceSide;box.size=cube.Half*2*p.iceSide;
                if(hasEntry){cube.From=entryOrigin;cube.Entry=p.iceEntrySeconds;cube.Delivery=delivery;cube.DeliveryIndex=deliveryIndex++;rb.isKinematic=true;KeepInside(cube);PrepareEntryLanding(cube);}
                cube.Probe=go.AddComponent<IceContactProbe>();cubes.Add(cube);
            }
            StepMilliseconds=0;if(count==0)return;
            dt=Mathf.Clamp(dt,.0001f,.1f);var next=(transform.position-last)/dt;var a=(next-velocity)/dt;last=transform.position;velocity=next;acceleration=Vector3.Lerp(acceleration,Vector3.ClampMagnitude(a,p.accelerationClamp),1-Mathf.Exp(-p.accelerationFilter*dt));
            bool rotated=Quaternion.Angle(lastRotation,transform.rotation)>bSettings.shake.wakeRotationDegrees;lastRotation=transform.rotation;
            float intensity=Mathf.Max(PreviewIntensity,(float)session.Adapter.Agitation(core.id,.15));float step=intensity>0?p.shakeStep:p.physicsStep;accumulator+=dt;var watch=Stopwatch.StartNew();
            bool moving=contentsChanged||intensity>0||rotated||acceleration.magnitude>bSettings.shake.wakeAcceleration;restingTime=moving?0:restingTime+dt;
            float level=LiquidHeight(core.volume);
            while(accumulator>=step)
            {
                var gravity=transform.InverseTransformDirection(Vector3.down*p.gravity-acceleration);var up=transform.InverseTransformDirection(Vector3.up);
                var active=session.Clock.Active;bool stirring=active!=null&&!active.IsEnded&&active.Action.Id=="spoon.stir"&&active.Target==container;
                stirring|=PreviewSpoon;
                var spoonView=stirring&&active!=null?session.FindViewFor(active.Actor):null;spoon.MovePosition(PreviewSpoon?transform.InverseTransformPoint(PreviewSpoon.position):spoonView?transform.InverseTransformPoint(spoonView.Anchor("Tip").position):Vector3.one*10);
                foreach(var c in cubes)
                {
                    float scale=Mathf.Pow((float)(core.ice/(Math.Max(1,count)*session.Adapter.World.Data.icePortionGrams)),1f/3);c.Scale=Mathf.Min(c.Scale,scale);float side=p.iceSide*c.Scale*c.Jitter;
                    var collider=c.Box;var size=c.Half*2*side;var centre=c.Centre*side;
                    if((collider.size-size).sqrMagnitude>1e-12f)collider.size=size;if((collider.center-centre).sqrMagnitude>1e-12f)collider.center=centre;
                    c.Probe.Contacts.Clear();c.BeforeVelocity=c.Body.linearVelocity;c.AppliedAcceleration=Vector3.zero;
                    if(c.Body.isKinematic)continue;
                    bool wake=moving;
                    if(wake){c.Quiet=0;c.Body.WakeUp();}
                    else if(c.Body.IsSleeping()||restingTime>=bSettings.shake.settleHardSeconds){c.Body.Sleep();continue;}
                    float h=(float)new IceVolumeSlice(c.Body.position,c.Body.rotation,collider.center,collider.size,double.MaxValue).FractionBelow(level);c.Body.linearDamping=Mathf.Lerp(p.dryDrag,p.wetDrag,h);c.Body.angularDamping=Mathf.Lerp(p.dryAngular,p.wetAngular,h);c.Wet=h;
                    c.AppliedAcceleration=gravity+up*(p.gravity*h/.917f);
                    // 2026-10-28: the vortex turns the way the spoon turns (Cross(up, radial) is clockwise seen from above; Spin > 0 is counter-clockwise).
                    if(stirring){var radial=c.Body.position-bounds.center;radial.y=0;var tangent=Vector3.Cross(Vector3.up,radial.normalized)*VortexTurn(container);c.AppliedAcceleration+=(tangent-.15f*radial.normalized)*p.vortex*intensity;}
                    c.Body.AddForce(c.AppliedAcceleration,ForceMode.Acceleration);
                    if(!wake&&c.Body.linearVelocity.magnitude<bSettings.shake.settleSpeed&&c.Body.angularVelocity.magnitude<bSettings.shake.settleAngularSpeed){c.Quiet+=step;if(c.Quiet>=bSettings.shake.settleSeconds)c.Body.Sleep();}else c.Quiet=0;
                }
                physics.Simulate(step);accumulator-=step;visualTime+=step;
                foreach(var c in cubes)
                {
                    if(c.Body.isKinematic)continue;var delta=c.Body.linearVelocity-c.BeforeVelocity-c.AppliedAcceleration*step;
                    foreach(var contact in c.Probe.Contacts)
                    {
                        var otherIce=contact.Other.GetComponent<IceContactProbe>();if(otherIce&&c.Body.GetInstanceID()>contact.Other.attachedRigidbody.GetInstanceID())continue;
                        var surface=contact.Other.GetComponent<IceCollisionSurface>();var kind=otherIce?IceImpactKind.Ice:surface?surface.Kind:IceImpactKind.Wall;
                        // Open vessels: remove the steady support that cancels gravity/buoyancy/inertia each step, so resting contact is not a hit.
                        // The sealed shaker keeps the B estimate: its packed stack pressing the lid is part of the shake sound the user approved.
                        float impulse=Mathf.Max(0,Mathf.Abs(Vector3.Dot(delta,contact.Normal))-(sealedNow?0:Mathf.Abs(Vector3.Dot(c.AppliedAcceleration,contact.Normal))*step))*c.Body.mass;
                        Impacts.Observe(visualTime,impulse,kind,transform.TransformPoint(contact.Point),c.Body.GetInstanceID(),contact.Other.attachedRigidbody?contact.Other.attachedRigidbody.GetInstanceID():contact.Other.GetInstanceID(),c.Wet);
                    }
                }
                foreach(var c in cubes)if(c.Entry<=0)KeepInside(c);
                PhysicsStepped?.Invoke();
                foreach(var c in cubes)if(c.Body.position.x<bounds.min.x-p.wallThickness||c.Body.position.x>bounds.max.x+p.wallThickness||c.Body.position.z<bounds.min.z-p.wallThickness||c.Body.position.z>bounds.max.z+p.wallThickness)escapes++;
            }
            watch.Stop();StepMilliseconds=watch.Elapsed.TotalMilliseconds;
            bool renderInside=!closed||session.DebugVisible&&session.Cues.DebugSeeInside;
            foreach(var c in cubes)
            {
                var rotation=transform.rotation*c.Body.rotation;float entryDt=dt;
                if(c.Delivery!=null)
                {
                    c.DeliveryTime+=dt;float release=c.Delivery.ReleaseAfter(c.DeliveryIndex);
                    var pose=c.Delivery.Sliding(c.DeliveryIndex,Mathf.Min(c.DeliveryTime,release));
                    c.Display.SetPositionAndRotation(pose.position,pose.rotation);rotation=pose.rotation;entryDt=0;
                    if(c.DeliveryTime>=release)
                    {entryDt=Mathf.Min(dt,c.DeliveryTime-release);c.From=c.Delivery.ReleasePosition;c.EntryRotation=pose.rotation;c.NaturalEntry=true;c.Delivery=null;}
                }
                if(c.Entry>0&&c.Delivery==null)
                {
                    c.Entry=Mathf.Max(0,c.Entry-entryDt);float progress=1-c.Entry/p.iceEntrySeconds;
                    c.Display.position=transform.TransformPoint(IceEntryPath.Position(transform.InverseTransformPoint(c.From),c.Body.position,shape.Top,LowestCorner(c),p.iceEntryArc,progress));
                    if(c.NaturalEntry)rotation=Quaternion.Slerp(c.EntryRotation,transform.rotation*c.Body.rotation,IceDeliveryMotion.Ease(Mathf.Min(1,progress*3)));
                    if(c.Entry<=0){c.Body.isKinematic=false;Land(c);}
                }
                else if(c.Entry<=0)c.Display.position=transform.TransformPoint(c.Body.position);
                c.Display.rotation=rotation;var sc=transform.lossyScale;c.Display.gameObject.SetActive(c.Scale>=p.removeScale&&renderInside);c.Display.localScale=new Vector3(p.iceSide*c.Scale*c.Jitter/sc.x,p.iceSide*c.Scale*c.Jitter/sc.y,p.iceSide*c.Scale*c.Jitter/sc.z);
            }
        }
        private Vector3 Corner(Cube cube,int index)
        {return cube.Body.rotation*((cube.Centre+Vector3.Scale(cube.Half,new Vector3((index&1)==0?-1:1,(index&2)==0?-1:1,(index&4)==0?-1:1)))*p.iceSide*cube.Scale*cube.Jitter);}
        private float LowestCorner(Cube cube)
        {float low=float.PositiveInfinity;for(int i=0;i<8;i++)low=Mathf.Min(low,Corner(cube,i).y);return low;}
        /// <summary>the entry path sets each cube down in place, so no contact ever reads as a hit; its arrival is the "ice goes in" sound.
        /// Strength follows the average entry speed (path length over entry time), clamped to a gentle range.</summary>
        private void Land(Cube c)
        {
            float speed=Mathf.Clamp(Vector3.Distance(transform.InverseTransformPoint(c.From),c.Body.position)/Mathf.Max(.01f,p.iceEntrySeconds),.1f,.4f);
            Impacts.Observe(visualTime,speed*c.Body.mass,IceImpactKind.Wall,transform.TransformPoint(c.Body.position),c.Body.GetInstanceID(),0,c.Wet,true);
        }
        private void PrepareEntryLanding(Cube cube)
        {
            float side=p.iceSide*cube.Scale*cube.Jitter;var centre=cube.Body.rotation*(cube.Centre*side);var position=cube.Body.position;
            position.x=shape.Axis.x-centre.x;position.z=shape.Axis.y-centre.z;
            bool Clear(float y)
            {
                for(int i=0;i<8;i++)
                {
                    var corner=Corner(cube,i);float height=y+corner.y;if(height>shape.Top+.0005f)continue;
                    float radial=new Vector2(corner.x-centre.x,corner.z-centre.z).magnitude;
                    if(shape.MinRadius(height,shape.Top)<radial+.0005f)return false;
                }
                return true;
            }
            // Search at the existing geometric slice resolution. If the mouth is too narrow,
            // keep the block above it, preserving the established wedged-in-rim presentation.
            float ceiling=shape.Top-LowestCorner(cube)+.001f,step=Mathf.Max((shape.Top-shape.Bottom)/shape.Samples,.0001f);
            while(!Clear(position.y)&&position.y<ceiling)position.y=Mathf.Min(ceiling,position.y+step);
            cube.Body.position=position;
        }
        private readonly Vector3[] corners=new Vector3[8];
        private void KeepInside(Cube cube)
        {
            // A kinematic spoon can otherwise squeeze a nearly inscribed cube through
            // a static wall. Constrain the complete oriented box against the cavity
            // radius at each corner's own height. This is a visual containment
            // constraint, like the existing retention plane.
            var body=cube.Body;float side=p.iceSide*cube.Scale*cube.Jitter;const float margin=.0005f;
            float low=0,high=0;
            void Measure()
            {
                low=float.MaxValue;high=float.MinValue;
                for(int i=0;i<8;i++){corners[i]=body.rotation*((cube.Centre+Vector3.Scale(cube.Half,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)))*side);low=Mathf.Min(low,corners[i].y);high=Mathf.Max(high,corners[i].y);}
            }
            // Largest distance of the centre from the axis that keeps every corner inside at centre height y.
            // Corners clearly above the rim (by more than the margin) are outside the cup and free; a corner level with the rim is still held.
            // A cube resting wholly on the rim keeps its centre over the mouth, so tilting does not slide it off.
            float Allowed(float y){float a=float.MaxValue;for(int i=0;i<8;i++)if(y+corners[i].y<=shape.Top+margin)a=Mathf.Min(a,shape.RadiusAt(y+corners[i].y)-margin-new Vector2(corners[i].x,corners[i].z).magnitude);return a<float.MaxValue?a:shape.RadiusAt(shape.Top)*.5f;}
            Measure();
            var position=body.position;
            position.y=Mathf.Clamp(position.y,shape.Bottom-low,Mathf.Max(shape.Bottom-low,ceiling-high));
            var delta=new Vector2(position.x-shape.Axis.x,position.z-shape.Axis.y);
            if(Allowed(position.y)<delta.magnitude)
            {
                if(Allowed(position.y)<0)
                {
                    // Too wide at this height: level the cube first, then lift it to where the cavity is wide enough.
                    body.rotation=Quaternion.Euler(0,body.rotation.eulerAngles.y,0);
                    if(!body.isKinematic)body.angularVelocity=new Vector3(0,body.angularVelocity.y,0);Measure();
                    // Search up to the height where the cube clears the rim entirely, so a cube that cannot fit wedges in the mouth.
                    float top=Mathf.Max(shape.Bottom-low,shape.Top+2*margin-low);position.y=Mathf.Max(position.y,shape.Bottom-low);
                    while(Allowed(position.y)<0&&position.y<top)position.y=Mathf.Min(top,position.y+.001f);
                }
                delta=Vector2.ClampMagnitude(delta,Mathf.Max(0,Allowed(position.y)));
                position.x=shape.Axis.x+delta.x;position.z=shape.Axis.y+delta.y;
            }
            var correction=position-body.position;
            if(correction.sqrMagnitude>1e-12f)
            {
                body.position=position;
                if(!body.isKinematic){var normal=correction.normalized;float outward=Vector3.Dot(body.linearVelocity,normal);if(outward<0)body.linearVelocity-=normal*outward;}
            }
        }
        // Bounds of the drawn ice meshes in the display root's own units (the root is scaled to the cube side).
        private static void MeasureMesh(Cube cube)
        {
            bool found=false;var bounds=new Bounds();
            foreach(var filter in cube.Display.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh)continue;var m=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    var q=cube.Display.InverseTransformPoint(filter.transform.TransformPoint(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                    if(found)bounds.Encapsulate(q);else{bounds=new Bounds(q,Vector3.zero);found=true;}
                }
            }
            if(found){cube.Centre=bounds.center;cube.Half=bounds.extents;}
        }
        private void OnDestroy(){foreach(var c in cubes)if(c.Display)Destroy(c.Display.gameObject);if(scene.IsValid())SceneManager.UnloadSceneAsync(scene);}
        private float vortexTurn=1;
        /// <summary>漩涡方向：跟随吧勺转向（StirMotion.Spin），停下时保持上一次的方向。</summary>
        private float VortexTurn(Container container){var stir=session.StirDisplay;if(stir!=null&&stir.Target==container&&Mathf.Abs(stir.Spin)>.3f)vortexTurn=stir.Spin>0?-1:1;return vortexTurn;}
    }
    internal static class VisualLookups
    {
        internal static CraftingResearch.CoreV2.Vessel SingleById(this System.Collections.Generic.IReadOnlyList<CraftingResearch.CoreV2.Vessel> vs,string id){foreach(var v in vs)if(v.id==id)return v;throw new InvalidOperationException(id);}
        internal static ItemView FindViewFor(this BarSession s,Item i){while(i.Parent!=null)i=i.Parent;foreach(var v in s.Views)if(v.Item==i)return v;return null;}
        internal static Transform ViewsWithLoadedIce(this BarSession s){foreach(var v in s.Views)if(v.Item is Tool t&&t.LoadedCubes>0)return v.Anchor("HoldPoint");return null;}
    }
}
