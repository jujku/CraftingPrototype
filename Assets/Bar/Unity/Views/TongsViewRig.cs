using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>冰夹的只读显示开合；按实际持冰包围盒张开夹臂及夹齿，保持所有用户锚点不动。</summary>
    public sealed class TongsViewRig : MonoBehaviour
    {
        [Header("表现参数（人为设定、未标定）")]
        [Min(.02f)] public float OpenSeconds=.14f;
        [Min(.02f)] public float CloseSeconds=.08f;
        [Min(0)] public float ClearanceMeters=.00025f;
        [Range(0,10)] public float ReleaseExtraDegrees=2;
        /// <summary>模型是否包含可驱动的左右夹臂、弹簧及持冰点。</summary>
        public bool Ready {get;private set;}
        /// <summary>当前两夹臂的额外张开角度之和。</summary>
        public float OpeningDegrees=>angle.x+angle.y;
        /// <summary>本帧冰块是否已经获得足够空间而可见。</summary>
        public bool IceVisible {get;private set;}
        private sealed class Part
        {
            public Transform node;public Vector3 position;public Quaternion rotation;
            public Vector3[] vertices;public int[] triangles;public int side;
        }
        private readonly List<Part> parts=new List<Part>();
        private readonly Vector3[] polyA=new Vector3[12],polyB=new Vector3[12];
        private Transform hold;
        private Vector3 pivot,opening,reach,normal;
        private Vector2 angle,fit,start,goal;
        private Bounds iceBox,lastBox;
        private bool wasLoaded,hasBox;
        private float elapsed;
        private int phase;
        private float? inspection;
        private readonly SecondOrderFloat inspectionSpring=new SecondOrderFloat(18,.8f,0,0);
        /// <summary>空夹检视的独立开合弹簧；释放后恢复原有夹冰动画。</summary>
        public void SetInspection(float? degrees)
        {
            if(degrees.HasValue&&!inspection.HasValue)inspectionSpring.Reset(angle.x);
            if(!degrees.HasValue&&inspection.HasValue)Begin(4,Vector2.zero);
            inspection=degrees;
        }
        private Transform Node(string name)
        {foreach(var t in GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
        private Vector3 Frame(Vector3 p)=>new Vector3(Vector3.Dot(p,opening),Vector3.Dot(p,reach),Vector3.Dot(p,normal));
        /// <summary>缓存当前模型姿态和铰点，不重设 HoldPoint、Grip 或原始网格。</summary>
        /// <precondition>冰夹带 Arm1、Arm-1、Spring 和 HoldPoint 节点；根为米制尺寸。</precondition>
        /// <effects>仅缓存显示几何，后续不重新设父节点、不修改网格或物理数量。</effects>
        public void Initialize(Transform holdPoint)
        {
            if(Ready)return;hold=holdPoint;var spring=Node("Spring");var armA=Node("Arm1");var armB=Node("Arm-1");
            if(!hold||!spring||!armA||!armB)return;
            var springMesh=spring.GetComponent<MeshFilter>();if(!springMesh)return;
            pivot=transform.InverseTransformPoint(spring.TransformPoint(springMesh.sharedMesh.bounds.center));
            opening=transform.InverseTransformDirection(armA.parent.right).normalized;
            reach=Vector3.ProjectOnPlane(transform.InverseTransformPoint(hold.position)-pivot,opening).normalized;
            normal=Vector3.Cross(reach,opening).normalized;
            if(normal.sqrMagnitude<.9f)return;
            foreach(var mesh in GetComponentsInChildren<MeshFilter>(true))
            {
                if(mesh.name!="Arm1"&&mesh.name!="Arm-1"&&!mesh.name.StartsWith("Tooth"))continue;
                var vertices=mesh.sharedMesh.vertices;var triangles=mesh.sharedMesh.triangles;
                if(mesh.name.StartsWith("Tooth"))
                {
                    // A tooth's box conservatively contains its small rounded mesh and avoids scanning duplicate cap vertices.
                    var b=mesh.sharedMesh.bounds;vertices=new Vector3[8];
                    for(int i=0;i<8;i++)vertices[i]=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    triangles=new[]{0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5};
                }
                float side=0;
                for(int i=0;i<vertices.Length;i++){vertices[i]=transform.InverseTransformPoint(mesh.transform.TransformPoint(vertices[i]));side+=Vector3.Dot(vertices[i]-pivot,opening);}
                parts.Add(new Part{node=mesh.transform,position=transform.InverseTransformPoint(mesh.transform.position),rotation=Quaternion.Inverse(transform.rotation)*mesh.transform.rotation,vertices=vertices,triangles=triangles,side=side>=0?1:-1});
            }
            Ready=parts.Count>=2;
        }
        private int Clip(Vector3[] input,int count,Vector3[] output,int axis,float limit,bool keepGreater)
        {
            if(count==0)return 0;int n=0;var previous=input[count-1];float pd=(previous[axis]-limit)*(keepGreater?1:-1);
            for(int i=0;i<count;i++)
            {
                var point=input[i];float d=(point[axis]-limit)*(keepGreater?1:-1);
                if((d>=0)!=(pd>=0))output[n++]=Vector3.LerpUnclamped(previous,point,pd/(pd-d));
                if(d>=0)output[n++]=point;previous=point;pd=d;
            }
            return n;
        }
        private bool Fits(int side,float degrees)
        {
            var q=Quaternion.AngleAxis(side*degrees,normal);float limit=side>0?iceBox.max.x+ClearanceMeters:iceBox.min.x-ClearanceMeters;
            foreach(var part in parts)
            {
                if(part.side!=side)continue;
                for(int i=0;i<part.triangles.Length;i+=3)
                {
                    for(int j=0;j<3;j++)polyA[j]=Frame(pivot+q*(part.vertices[part.triangles[i+j]]-pivot));
                    int count=Clip(polyA,3,polyB,1,iceBox.min.y,true);
                    count=Clip(polyB,count,polyA,1,iceBox.max.y,false);
                    count=Clip(polyA,count,polyB,2,iceBox.min.z,true);
                    count=Clip(polyB,count,polyA,2,iceBox.max.z,false);
                    for(int j=0;j<count;j++)if(side*(polyA[j].x-limit)<-1e-7f)return false;
                }
            }
            return true;
        }
        private float FitAngle(int side)
        {
            if(Fits(side,0))return 0;
            for(float high=2;high<=36;high+=2)
            {
                if(!Fits(side,high))continue;
                float low=high-2;
                for(int i=0;i<12;i++){float middle=(low+high)/2;if(Fits(side,middle))high=middle;else low=middle;}
                return high+.002f;
            }
            return 36;
        }
        private Bounds BoundsOfIce(Transform ice)
        {
            var result=new Bounds();bool first=true;
            foreach(var mesh in ice.GetComponentsInChildren<MeshFilter>(true))
            {
                if(mesh.name!="IceSurface"&&mesh.name!="Test ice")continue;
                var b=mesh.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    var point=Frame(transform.InverseTransformPoint(mesh.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)))));
                    if(first){result=new Bounds(point,Vector3.zero);first=false;}else result.Encapsulate(point);
                }
            }
            return result;
        }
        private void Begin(int stage,Vector2 target){phase=stage;start=angle;goal=target;elapsed=0;}
        private bool Tween(float seconds,float dt)
        {elapsed+=Mathf.Max(0,dt);float t=Mathf.Clamp01(elapsed/Mathf.Max(.02f,seconds));angle=Vector2.Lerp(start,goal,Mathf.SmoothStep(0,1,t));return t>=1;}
        private void Apply()
        {
            foreach(var part in parts)
            {
                var q=Quaternion.AngleAxis(part.side*(part.side<0?angle.x:angle.y),normal);
                part.node.SetPositionAndRotation(transform.TransformPoint(pivot+q*(part.position-pivot)),transform.rotation*q*part.rotation);
            }
        }
        /// <summary>只读工具载冰状态，先张开再夹住；成功放冰后张开并回到原始空夹姿态。</summary>
        /// <precondition>ShowCentered 已更新冰块网格；dt 非负。</precondition>
        /// <effects>仅控制夹臂、夹齿及冰显示时机，始终保留持冰点与核心冰量。</effects>
        public void Advance(bool loaded,Transform ice,float dt)
        {
            if(!Ready||!ice)return;
            if(inspection.HasValue&&!loaded)
            {
                float remaining=dt,value=inspectionSpring.Value;
                while(remaining>0){float h=Mathf.Min(remaining,1f/240);value=inspectionSpring.Update(h,inspection.Value);remaining-=h;}
                angle=Vector2.one*value;IceVisible=false;ice.gameObject.SetActive(true);Apply();return;
            }
            if(loaded)
            {
                iceBox=BoundsOfIce(ice);
                bool changed=!hasBox||(lastBox.center-iceBox.center).sqrMagnitude>1e-10f||(lastBox.size-iceBox.size).sqrMagnitude>1e-10f;
                if(changed){fit=new Vector2(FitAngle(-1),FitAngle(1));lastBox=iceBox;hasBox=true;}
                if(!wasLoaded)Begin(1,fit+Vector2.one*ReleaseExtraDegrees);
                else if(changed){angle=fit;Begin(2,fit);elapsed=CloseSeconds;}
                if(phase==1&&Tween(OpenSeconds,dt))Begin(2,fit);
                else if(phase==2)Tween(CloseSeconds,dt);
                IceVisible=Fits(-1,angle.x)&&Fits(1,angle.y);
                ice.gameObject.SetActive(IceVisible);
            }
            else
            {
                if(wasLoaded){Begin(3,angle+Vector2.one*ReleaseExtraDegrees);hasBox=false;}
                if(phase==3&&Tween(CloseSeconds,dt))Begin(4,Vector2.zero);
                else if(phase==4&&Tween(OpenSeconds,dt)){angle=Vector2.zero;phase=0;}
                IceVisible=false;ice.gameObject.SetActive(true);
            }
            wasLoaded=loaded;Apply();
        }
    }
}
