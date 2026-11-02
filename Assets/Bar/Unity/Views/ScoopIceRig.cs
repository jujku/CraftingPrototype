using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>按真实铲面支撑持冰：取消FBX导入轴差，沿槽放两块并向上堆放，绝不改工具库存或锚点。</summary>
    public sealed class ScoopIceRig:MonoBehaviour
    {
        private readonly List<(Vector3[] vertices,int[] triangles)> metal=new List<(Vector3[],int[])>();
        private readonly Vector3[] polygonA=new Vector3[12],polygonB=new Vector3[12];
        private Transform visual;private Bounds bowl;private bool hasBowl;private Vector3 lastPosition;private Quaternion lastRotation;private float lastSide;
        private Vector3[] positions=new Vector3[0];private Quaternion[] rotations=new Quaternion[0];private int lastCount=-1,seed;private ScoopDisplaySettings settings;
        public bool Ready=>metal.Count>0;
        public Vector3 BowlSize=>bowl.size;
        public void Initialize()
        {
            visual=transform.Find("Visual");settings=BPresentationSettings.Load().scoop;
            foreach(var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh||!filter.sharedMesh.isReadable||filter.name!="ScoopBowl"&&filter.name!="Handle")continue;
                var points=filter.sharedMesh.vertices.Select(p=>transform.InverseTransformPoint(filter.transform.TransformPoint(p))).ToArray();metal.Add((points,filter.sharedMesh.triangles));
                if(filter.name=="ScoopBowl")foreach(var p in points){if(!hasBowl){bowl=new Bounds(p,Vector3.zero);hasBowl=true;}else bowl.Encapsulate(p);}
            }
        }
        /// <summary>只消除Visual的导入轴变换，保留HoldPoint自身的编辑旋转。</summary>
        public Quaternion HoldingRotation(Transform hold)=>hold&&visual?hold.rotation*Quaternion.Inverse(visual.rotation)*transform.rotation:transform.rotation;
        private Bounds DrawnBounds(Transform cube)
        {
            var box=new Bounds();bool found=false;
            foreach(var filter in cube.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.name!="IceSurface")continue;var b=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++){var p=transform.InverseTransformPoint(filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));if(!found){box=new Bounds(p,Vector3.zero);found=true;}else box.Encapsulate(p);}
            }
            return box;
        }
        private static float Distance(Vector3 p,int side,Bounds box)=>side==0?p.x-box.min.x:side==1?box.max.x-p.x:side==2?p.z-box.min.z:box.max.z-p.z;
        private float Support(Bounds box)
        {
            float top=float.NegativeInfinity;
            foreach(var mesh in metal)for(int t=0;t<mesh.triangles.Length;t+=3)
            {
                var a=polygonA;var b=polygonB;int count=3;for(int i=0;i<3;i++)a[i]=mesh.vertices[mesh.triangles[t+i]];
                for(int side=0;side<4&&count>0;side++)
                {
                    int next=0;for(int i=0;i<count;i++){var p=a[i];var q=a[(i+1)%count];float dp=Distance(p,side,box),dq=Distance(q,side,box);if(dp>=0)b[next++]=p;if((dp>=0)!=(dq>=0))b[next++]=Vector3.Lerp(p,q,dp/(dp-dq));}
                    count=next;var swap=a;a=b;b=swap;
                }
                for(int i=0;i<count;i++)top=Mathf.Max(top,a[i].y);
            }
            return top;
        }
        private Vector3 FitShift(Bounds footprint)
        {
            if(!hasBowl)return Vector3.zero;
            float gap=settings.clearanceMetres;
            float Fit(float low,float high,float min,float max)=>high-low<=max-min-2*gap?Mathf.Clamp(0,min+gap-low,max-gap-high):(min+max-low-high)*.5f;
            return new Vector3(Fit(footprint.min.x,footprint.max.x,bowl.min.x,bowl.max.x),0,Fit(footprint.min.z,footprint.max.z,bowl.min.z,bowl.max.z));
        }
        public void Arrange(IceVisual ice,float side)
        {
            var cubes=ice.Cubes.Where(c=>c.gameObject.activeSelf).ToArray();if(cubes.Length==0){lastCount=0;return;}
            var point=transform.InverseTransformPoint(ice.transform.position);var rotation=Quaternion.Inverse(transform.rotation)*ice.transform.rotation;
            bool rebuild=cubes.Length!=lastCount||(point-lastPosition).sqrMagnitude>1e-10f||Quaternion.Angle(rotation,lastRotation)>.001f||Mathf.Abs(lastSide-side)>1e-6f;
            if(rebuild)
            {
                if(lastCount<=0)seed++;int n=cubes.Length;
                lastCount=n;lastPosition=point;lastRotation=rotation;lastSide=side;positions=new Vector3[n];rotations=new Quaternion[n];float width=0,pitch=0;int perRow=1;
                // a scoop holds a single layer along its slot; for this bowl and cube that is two (items.json maxCubes).
                // Each cube settles into the slot with its own small yaw and tilt, and each scoopful differs; angles shrink only if the row would no longer fit.
                foreach(float scale in new[]{1f,.5f,0f})
                {
                    width=0;
                    for(int i=0;i<n;i++){rotations[i]=Quaternion.Euler((Hash(i,seed)-.5f)*4*scale,(Hash(i,seed+1)-.5f)*16*scale,(Hash(i,seed+2)-.5f)*4*scale);cubes[i].localPosition=Vector3.zero;cubes[i].localRotation=rotations[i];width=Mathf.Max(width,DrawnBounds(cubes[i]).size.z);}
                    pitch=width+settings.blockGapMetres;perRow=hasBowl?Mathf.Max(1,Mathf.FloorToInt((bowl.size.z-2*settings.clearanceMetres+settings.blockGapMetres)/pitch)):n;
                    if(perRow>=Mathf.Min(n,2)||scale==0)break;
                }
                // One row along the slot, slightly staggered across it; anything beyond one layer (not reachable with the data) rests on top so nothing intersects.
                int inRow=Mathf.Min(n,perRow);
                for(int i=0;i<n;i++)
                {
                    float z=i<inRow?(i-(inRow-1)*.5f)*pitch:(Hash(i,seed+3)-.5f)*.3f*pitch,x=(Hash(i,seed+4)-.5f)*.16f*width;
                    cubes[i].position=transform.TransformPoint(point+Vector3.forward*z+Vector3.right*x);
                }
                bool row=inRow>1;
                var footprint=DrawnBounds(cubes[0]);if(row)footprint.Encapsulate(DrawnBounds(cubes[1]));var shift=FitShift(footprint);
                var placed=new List<Bounds>();
                foreach(var cube in cubes)
                {
                    cube.position+=transform.TransformVector(shift);cube.position+=transform.TransformVector(new Vector3(FitShift(DrawnBounds(cube)).x,0,0));var box=DrawnBounds(cube);float support=Support(box);
                    foreach(var previous in placed)if(previous.max.x>box.min.x&&previous.min.x<box.max.x&&previous.max.z>box.min.z&&previous.min.z<box.max.z)support=Mathf.Max(support,previous.max.y+settings.blockGapMetres-settings.clearanceMetres);
                    if(!float.IsNegativeInfinity(support))cube.position+=transform.TransformVector(Vector3.up*(support+settings.clearanceMetres-box.min.y));
                    placed.Add(DrawnBounds(cube));positions[placed.Count-1]=cube.localPosition;
                }
            }
            for(int i=0;i<cubes.Length;i++){cubes[i].localPosition=positions[i];cubes[i].localRotation=rotations[i];}
        }
        private static float Hash(int i,int salt)=>Mathf.Repeat(Mathf.Sin(i*12.9898f+salt*78.233f)*43758.5453f,1);
    }
}
