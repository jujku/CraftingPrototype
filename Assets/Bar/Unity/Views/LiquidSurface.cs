using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>缓存内腔拓扑、截面缓冲与属性块。局部液面超过 0.05mm/法线阈值才重建。</summary>
    public sealed class LiquidSurface:MonoBehaviour
    {
        private Mesh mesh,source;private MeshRenderer rendererCache;private Vector3[] vertices;private int[] triangles;private Matrix4x4 cachedRelative;
        private readonly List<(Vector3 a,Vector3 b)> segments=new List<(Vector3,Vector3)>(512);private readonly List<Vector3> loop=new List<Vector3>(256),output=new List<Vector3>(512),normals=new List<Vector3>(512);
        private readonly List<int> indices=new List<int>(1536),remaining=new List<int>(256);private MaterialPropertyBlock block;
        private Vector3 lastNormal,basisX,basisY;private float lastPlane=float.NaN;public int RebuildCount {get;private set;}public MeshRenderer Renderer=>rendererCache;
        public void Draw(MeshFilter cavity,float height,Material material,bool visible,Vector3 normal=default)
        {
            if(!rendererCache){var go=new GameObject("Liquid surface (baked level)");go.layer=31;go.transform.SetParent(transform,false);rendererCache=go.AddComponent<MeshRenderer>();mesh=new Mesh{name="Horizontal liquid section"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;}
            rendererCache.enabled=visible;if(!visible)return;rendererCache.sharedMaterial=material;block??=new MaterialPropertyBlock();block.SetFloat(BShaderIds.FillHeight,height+.00001f);rendererCache.SetPropertyBlock(block);
            if(normal.sqrMagnitude<.1f)normal=Vector3.up;
            var relative=transform.worldToLocalMatrix*cavity.transform.localToWorldMatrix;bool topology=source!=cavity.sharedMesh;
            if(topology){source=cavity.sharedMesh;vertices=source.vertices;triangles=source.triangles;}
            var localNormal=transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;var point=transform.InverseTransformPoint(new Vector3(transform.position.x,height,transform.position.z));float level=Vector3.Dot(localNormal,point);
            if(!topology&&Mathf.Abs(level-lastPlane)<.00005f&&(localNormal-lastNormal).sqrMagnitude<.00000025f&&relative==cachedRelative)return;
            lastPlane=level;lastNormal=localNormal;cachedRelative=relative;RebuildCount++;
            basisX=Vector3.Cross(localNormal,Mathf.Abs(localNormal.y)>.9f?Vector3.right:Vector3.up).normalized;basisY=Vector3.Cross(localNormal,basisX);
            segments.Clear();output.Clear();indices.Clear();normals.Clear();
            for(int t=0;t<triangles.Length;t+=3)
            {
                int count=0;Vector3 first=default,second=default;
                for(int j=0;j<3;j++){var a=relative.MultiplyPoint3x4(vertices[triangles[t+j]]);var b=relative.MultiplyPoint3x4(vertices[triangles[t+(j+1)%3]]);float da=Vector3.Dot(a,localNormal)-level,db=Vector3.Dot(b,localNormal)-level;
                    if((da<0&&db>=0)||(db<0&&da>=0)){var hit=Vector3.LerpUnclamped(a,b,-da/(db-da));if(count==0){first=hit;count=1;}else if((hit-first).sqrMagnitude>1e-12f){second=hit;count=2;}}}
                if(count==2)segments.Add((first,second));
            }
            while(segments.Count>0)
            {
                var edge=segments[segments.Count-1];segments.RemoveAt(segments.Count-1);loop.Clear();loop.Add(edge.a);loop.Add(edge.b);
                while((loop[loop.Count-1]-loop[0]).sqrMagnitude>1e-10f)
                {var end=loop[loop.Count-1];int next=-1;for(int n=0;n<segments.Count;n++)if((segments[n].a-end).sqrMagnitude<1e-10f||(segments[n].b-end).sqrMagnitude<1e-10f){next=n;break;}if(next<0)break;var e=segments[next];segments.RemoveAt(next);loop.Add((e.a-end).sqrMagnitude<1e-10f?e.b:e.a);}
                if(loop.Count<4||(loop[loop.Count-1]-loop[0]).sqrMagnitude>1e-10f)continue;loop.RemoveAt(loop.Count-1);
                for(int i=loop.Count-1;i>=0&&loop.Count>3;i--)if(Mathf.Abs(Cross(loop[(i+loop.Count-1)%loop.Count],loop[i],loop[(i+1)%loop.Count]))<1e-9f)loop.RemoveAt(i);
                int offset=output.Count;output.AddRange(loop);for(int i=0;i<loop.Count;i++)normals.Add(localNormal);Triangulate(offset);
            }
            mesh.Clear();mesh.SetVertices(output);mesh.SetNormals(normals);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
        private float Cross(Vector3 a,Vector3 b,Vector3 c){var ab=b-a;var ac=c-a;return Vector3.Dot(ab,basisX)*Vector3.Dot(ac,basisY)-Vector3.Dot(ab,basisY)*Vector3.Dot(ac,basisX);}
        private void Triangulate(int offset)
        {
            float area=0;for(int i=0;i<loop.Count;i++)area+=Cross(Vector3.zero,loop[i],loop[(i+1)%loop.Count]);float sign=Mathf.Sign(area);remaining.Clear();for(int i=0;i<loop.Count;i++)remaining.Add(i);int guard=loop.Count*loop.Count;
            while(remaining.Count>=3&&guard-->0){bool clipped=false;for(int j=0;j<remaining.Count;j++){int a=remaining[(j+remaining.Count-1)%remaining.Count],b=remaining[j],c=remaining[(j+1)%remaining.Count];if(Cross(loop[a],loop[b],loop[c])*sign<=1e-10f)continue;bool contains=false;
                foreach(int q in remaining)if(q!=a&&q!=b&&q!=c&&Cross(loop[a],loop[b],loop[q])*sign>=-1e-10f&&Cross(loop[b],loop[c],loop[q])*sign>=-1e-10f&&Cross(loop[c],loop[a],loop[q])*sign>=-1e-10f){contains=true;break;}
                if(contains)continue;indices.Add(offset+a);indices.Add(offset+b);indices.Add(offset+c);remaining.RemoveAt(j);clipped=true;break;}if(!clipped)break;}
        }
        private void OnDestroy(){if(mesh){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}}
    }
}
