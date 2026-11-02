using System;
using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>运行时网格副本上的悬臂勺尖余振；握点、FBX与碰撞不变。</summary>
    public sealed class InspectionTipFlex:IDisposable
    {
        private sealed class Part
        {
            public MeshFilter Filter;public Mesh Original,Copy;public Vector3[] Vertices,Normals,Drawn,DrawnNormals,GripVertices,GripNormals;public float[] Weights;
            public Matrix4x4 ToGrip,FromGrip;public Quaternion NormalToGrip,NormalFromGrip;public Bounds OriginalBounds;
        }
        private readonly List<Part> parts=new List<Part>();private readonly float length;
        private readonly SecondOrderVector release=new SecondOrderVector(20,1,0,Vector3.zero);
        private Vector3 angles;private bool returning,disposed;private float returnTime;
        public ItemView Owner{get;}public float MaximumDisplacement{get;private set;}
        public InspectionTipFlex(ItemView owner,Bar.Data.HandSide side,Bounds bounds)
        {
            Owner=owner;length=Mathf.Max(.01f,bounds.max.z);var grip=HoldViewRig.Grip(owner,side);var frame=grip?grip:owner.transform;
            foreach(var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();var mesh=filter.sharedMesh;if(!renderer||!renderer.enabled||!mesh||!mesh.isReadable)continue;
                var vertices=mesh.vertices;var normals=mesh.normals;if(normals.Length!=vertices.Length)continue;
                var copy=UnityEngine.Object.Instantiate(mesh);copy.name=mesh.name+" (inspection-only flex)";copy.MarkDynamic();
                var part=new Part{Filter=filter,Original=mesh,Copy=copy,Vertices=vertices,Normals=normals,Drawn=new Vector3[vertices.Length],DrawnNormals=new Vector3[vertices.Length],ToGrip=frame.worldToLocalMatrix*filter.transform.localToWorldMatrix,NormalToGrip=Quaternion.Inverse(frame.rotation)*filter.transform.rotation};
                part.FromGrip=part.ToGrip.inverse;part.NormalFromGrip=Quaternion.Inverse(part.NormalToGrip);part.OriginalBounds=mesh.bounds;
                part.GripVertices=new Vector3[vertices.Length];part.GripNormals=new Vector3[vertices.Length];part.Weights=new float[vertices.Length];
                for(int i=0;i<vertices.Length;i++){part.GripVertices[i]=part.ToGrip.MultiplyPoint3x4(vertices[i]);part.GripNormals[i]=part.NormalToGrip*normals[i];float s=Mathf.Clamp01(part.GripVertices[i].z/length);part.Weights[i]=s*s;}
                parts.Add(part);filter.sharedMesh=copy;
            }
        }
        public void Apply(Vector3 pitchYawDegrees)
        {
            if(disposed||angles==Vector3.zero&&pitchYawDegrees==Vector3.zero)return;
            angles=pitchYawDegrees;MaximumDisplacement=0;
            if(angles==Vector3.zero){foreach(var p in parts){p.Copy.vertices=p.Vertices;p.Copy.normals=p.Normals;p.Copy.bounds=p.OriginalBounds;}return;}
            foreach(var part in parts)
            {
                for(int i=0;i<part.Vertices.Length;i++)
                {
                    var at=part.GripVertices[i];float weight=part.Weights[i];
                    // theta(s)=theta_tip*s²; integrated deflection theta_tip*L*s³/3.
                    var moved=Quaternion.Euler(angles.x*weight/3,angles.y*weight/3,0)*at;
                    part.Drawn[i]=part.FromGrip.MultiplyPoint3x4(moved);MaximumDisplacement=Mathf.Max(MaximumDisplacement,(moved-at).magnitude);
                    var normal=Quaternion.Euler(angles.x*weight,angles.y*weight,0)*part.GripNormals[i];part.DrawnNormals[i]=part.NormalFromGrip*normal;
                }
                part.Copy.vertices=part.Drawn;part.Copy.normals=part.DrawnNormals;part.Copy.RecalculateBounds();
            }
        }
        public void BeginReturn(){returning=true;returnTime=0;release.Reset(angles);}
        public bool AdvanceReturn(float dt)
        {
            if(!returning)return false;returnTime+=dt;float left=dt;while(left>0){float h=Mathf.Min(left,1f/240);release.Update(h,Vector3.zero);left-=h;}Apply(release.Value);
            return returnTime>=.15f||returnTime>.04f&&angles.sqrMagnitude<.000025f;
        }
        public void Dispose()
        {if(disposed)return;disposed=true;foreach(var p in parts){if(p.Filter&&p.Filter.sharedMesh==p.Copy)p.Filter.sharedMesh=p.Original;if(p.Copy)UnityEngine.Object.Destroy(p.Copy);}parts.Clear();}
    }
}
