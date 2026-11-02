using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static Transform lounge;private static bool meshFile;private static int serial;
        private static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        private static readonly List<string> parts=new List<string>();
        private static readonly List<Light> lights=new List<Light>();
        private static string MeshPath=AssetRoot+"/GinzaMeshes.asset";
        private static Color Hex(string s){if(!ColorUtility.TryParseHtmlString("#"+s,out var c))throw new ArgumentException(s);return c;}
        private static Transform Group(string name,Transform parent,Vector3 position,Quaternion rotation)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;return go.transform;}
        private static Transform Group(string name,Transform parent,Vector3 position)=>Group(name,parent,position,Quaternion.identity);
        private static Material Mat(string name,string hex,float metal,float smooth,string texture="",float alpha=1,float emission=0)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",new Color(Hex(hex).r,Hex(hex).g,Hex(hex).b,alpha));m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",smooth);
            if(texture!=""){var path=AssetRoot+"/Textures/"+texture+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.wrapMode=TextureWrapMode.Repeat;importer.maxTextureSize=1024;importer.sRGBTexture=true;importer.SaveAndReimport();m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));}
            if(alpha<1){m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",2);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetShaderPassEnabled("ShadowCaster",false);m.renderQueue=3000;}
            if(emission>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(hex)*emission);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;}
            AssetDatabase.CreateAsset(m,AssetRoot+"/Materials/"+name+".mat");materials.Add(name,m);return m;
        }
        private static GameObject Geometry(string name,Transform parent,Mesh mesh,Material mat,Vector3 position,Quaternion rotation)
        {
            mesh.name=(serial++).ToString("D4")+"_"+name;mesh.RecalculateBounds();
            if(!meshFile){AssetDatabase.CreateAsset(mesh,MeshPath);meshFile=true;}else AssetDatabase.AddObjectToAsset(mesh,MeshPath);
            var go=Group(name,parent,position,rotation).gameObject;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);parts.Add(Hierarchy(go.transform));return go;
        }
        private static Mesh Mesh(List<Vector3> v,List<int> t,List<Vector2> uv,List<Vector3> normal=null)
        {var m=new Mesh();if(v.Count>65535)m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;m.SetVertices(v);m.SetTriangles(t,0);m.SetUVs(0,uv);if(normal==null)m.RecalculateNormals();else m.SetNormals(normal);return m;}
        private static void Quad(List<int> t,int a,int b,int c,int d){t.Add(a);t.Add(b);t.Add(c);t.Add(a);t.Add(c);t.Add(d);}
        private static float[] EdgeCoordinates(float h,float r,int cuts)
        {var a=new List<float>();for(int i=0;i<=cuts;i++)a.Add(-h+r*i/cuts);for(int i=0;i<=cuts;i++)a.Add(h-r+r*i/cuts);return a.Distinct().ToArray();}
        private static Mesh RoundedBox(Vector3 size,float radius,int cuts=4)
        {
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();var normals=new List<Vector3>();var half=size*.5f;var inner=half-Vector3.one*radius;
            foreach(var face in new[]{(Vector3.right,Vector3.back,Vector3.up),(Vector3.left,Vector3.forward,Vector3.up),(Vector3.up,Vector3.right,Vector3.back),(Vector3.down,Vector3.right,Vector3.forward),(Vector3.forward,Vector3.right,Vector3.up),(Vector3.back,Vector3.left,Vector3.up)})
            {
                var n=face.Item1;var a=face.Item2;var b=face.Item3;float ah=Vector3.Dot(Abs(a),half),bh=Vector3.Dot(Abs(b),half);var xs=EdgeCoordinates(ah,radius,cuts);var ys=EdgeCoordinates(bh,radius,cuts);int start=v.Count;
                foreach(float y in ys)foreach(float x in xs){var point=n*Vector3.Dot(Abs(n),half)+a*x+b*y;var core=new Vector3(Mathf.Clamp(point.x,-inner.x,inner.x),Mathf.Clamp(point.y,-inner.y,inner.y),Mathf.Clamp(point.z,-inner.z,inner.z));var normal=(point-core).normalized;v.Add(core+normal*radius);normals.Add(normal);uv.Add(new Vector2(x+ah,y+bh)*3);}
                for(int j=0;j<ys.Length-1;j++)for(int i=0;i<xs.Length-1;i++){int q=start+j*xs.Length+i;Quad(t,q,q+1,q+xs.Length+1,q+xs.Length);}
            }
            return Mesh(v,t,uv,normals);
        }
        private static Vector3 Abs(Vector3 a)=>new Vector3(Mathf.Abs(a.x),Mathf.Abs(a.y),Mathf.Abs(a.z));
        private static GameObject Box(string name,Transform parent,Vector3 size,Vector3 center,float bevel,Material m,Quaternion rotation)
        {if(bevel<=0||bevel>Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.5f)throw new ArgumentException("Invalid authored bevel "+name);return Geometry(name,parent,RoundedBox(size,bevel),m,center,rotation);}
        private static GameObject Box(string name,Transform parent,Vector3 size,Vector3 center,float bevel,Material m)=>Box(name,parent,size,center,bevel,m,Quaternion.identity);
        private static Mesh LatheMesh(Vector2[] profile,int segments=48,float rib=0)
        {
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
            for(int j=0;j<profile.Length;j++)for(int i=0;i<=segments;i++){float a=i*2*Mathf.PI/segments,r=profile[j].x+(i%2==0?rib:0);if(profile[j].x==0)r=0;v.Add(new Vector3(Mathf.Cos(a)*r,profile[j].y,Mathf.Sin(a)*r));uv.Add(new Vector2(i/(float)segments,profile[j].y*3));}
            for(int j=0;j<profile.Length-1;j++)for(int i=0;i<segments;i++){int q=j*(segments+1)+i;Quad(t,q,q+segments+1,q+segments+2,q+1);}
            if(rib>0){var split=new List<Vector3>();var splitUv=new List<Vector2>();var splitTriangles=new List<int>();for(int i=0;i<segments;i++){int start=split.Count;for(int j=0;j<profile.Length;j++){int a=j*(segments+1)+i;split.Add(v[a]);split.Add(v[a+1]);splitUv.Add(uv[a]);splitUv.Add(uv[a+1]);}for(int j=0;j<profile.Length-1;j++){int a=start+j*2;Quad(splitTriangles,a,a+2,a+3,a+1);}}return Mesh(split,splitTriangles,splitUv);}
            return Mesh(v,t,uv);
        }
        private static GameObject Turn(string name,Transform parent,Vector2[] profile,Material m,Vector3 center,int segments=48,float rib=0)=>Geometry(name,parent,LatheMesh(profile,segments,rib),m,center,Quaternion.identity);
        private static Vector2 P(float r,float h)=>new Vector2(r,h);
        private static Vector2[] Disc(float r,float h,float edge)=>new[]{P(0,0),P(r-edge,0),P(r-edge*.3f,edge*.3f),P(r,edge),P(r,h-edge),P(r-edge*.3f,h-edge*.3f),P(r-edge,h),P(0,h)};
        private static GameObject Torus(string name,Transform parent,float major,float minor,Vector3 center,Material m,Quaternion rotation)
        {
            var profile=new Vector2[17];for(int i=0;i<=16;i++){float a=i*2*Mathf.PI/16;profile[i]=P(major+Mathf.Cos(a)*minor,Mathf.Sin(a)*minor);}return Geometry(name,parent,LatheMesh(profile,64),m,center,rotation);
        }
        private static GameObject Torus(string name,Transform parent,float major,float minor,Vector3 center,Material m)=>Torus(name,parent,major,minor,center,m,Quaternion.identity);
        private static Vector3[] Bezier(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int n=24)=>Enumerable.Range(0,n+1).Select(i=>{float f=i/(float)n,k=1-f;return k*k*k*a+3*k*k*f*b+3*k*f*f*c+f*f*f*d;}).ToArray();
        private static Mesh Sweep(Vector3[] path,float width,float depth=0,int sides=16)
        {
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();Vector3 previous=Vector3.zero,right=Vector3.zero;float length=0;
            for(int i=0;i<path.Length;i++)
            {
                Vector3 tangent=(path[Mathf.Min(i+1,path.Length-1)]-path[Mathf.Max(0,i-1)]).normalized;
                if(i==0)right=Vector3.Cross(tangent,Mathf.Abs(tangent.y)>.9f?Vector3.right:Vector3.up).normalized;else{right=Quaternion.FromToRotation(previous,tangent)*right;length+=Vector3.Distance(path[i],path[i-1]);}var up=Vector3.Cross(tangent,right);previous=tangent;
                for(int j=0;j<=sides;j++){float a=j*2*Mathf.PI/sides;Vector3 delta;if(depth==0)delta=right*(Mathf.Cos(a)*width*.5f)+up*(Mathf.Sin(a)*width*.5f);else{var rect=RoundRect(width,depth,.001f,3)[j%sides];delta=right*rect.x+up*rect.y;}v.Add(path[i]+delta);uv.Add(new Vector2(j/(float)sides,length*3));}
                if(i>0)for(int j=0;j<sides;j++){int q=(i-1)*(sides+1)+j;Quad(t,q,q+1,q+sides+2,q+sides+1);}
            }
            int first=v.Count;v.Add(path[0]);uv.Add(Vector2.zero);int last=v.Count;v.Add(path[path.Length-1]);uv.Add(Vector2.one);for(int j=0;j<sides;j++){t.Add(first);t.Add(j+1);t.Add(j);int b=(path.Length-1)*(sides+1)+j;t.Add(last);t.Add(b);t.Add(b+1);}return Mesh(v,t,uv);
        }
        private static GameObject Tube(string name,Transform parent,Vector3[] path,float diameter,Material mat,float depth=0)=>Geometry(name,parent,Sweep(path,diameter,depth,depth==0?32:16),mat,Vector3.zero,Quaternion.identity);
        private static Vector2[] RoundRect(float w,float h,float r,int cuts=6)
        {
            var points=new List<Vector2>();for(int corner=0;corner<4;corner++){float cx=corner==0||corner==3?w*.5f-r:-w*.5f+r,cy=corner<2?h*.5f-r:-h*.5f+r;for(int i=0;i<=cuts;i++){float a=(corner*90+i*90f/cuts)*Mathf.Deg2Rad;points.Add(new Vector2(cx+Mathf.Cos(a)*r,cy+Mathf.Sin(a)*r));}}return points.ToArray();
        }
        private static GameObject Frame(string name,Transform parent,float width,float height,float rim,float depth,Vector3 center,Material mat)
        {
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
            var dimensions=new[]{new Vector3(width,height,-depth*.5f),new Vector3(width,height,depth*.5f-.004f),new Vector3(width-.008f,height-.008f,depth*.5f),new Vector3(width-2*rim+.008f,height-2*rim+.008f,depth*.5f),new Vector3(width-2*rim,height-2*rim,depth*.5f-.004f),new Vector3(width-2*rim,height-2*rim,-depth*.5f)};
            foreach(var d in dimensions)foreach(var p in RoundRect(d.x,d.y,.006f)){v.Add(new Vector3(p.x,p.y,d.z));uv.Add(p*3);}int n=v.Count/dimensions.Length;
            for(int j=0;j<dimensions.Length-1;j++)for(int i=0;i<n;i++){int k=(i+1)%n;Quad(t,j*n+i,j*n+k,(j+1)*n+k,(j+1)*n+i);}for(int i=0;i<n;i++){int k=(i+1)%n;Quad(t,i,(dimensions.Length-1)*n+i,(dimensions.Length-1)*n+k,k);}return Geometry(name,parent,Mesh(v,t,uv),mat,center,Quaternion.identity);
        }
        private static GameObject HalfSlat(string name,Transform parent,float height,Material mat,Vector3 center,Quaternion rotation)
        {
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
            var section=Enumerable.Range(0,17).Select(i=>{float a=Mathf.PI*i/16;return new Vector2(Mathf.Cos(a)*.015f,Mathf.Sin(a)*.012f);}).Concat(new[]{new Vector2(-.015f,-.005f),new Vector2(.015f,-.005f)}).ToArray();
            for(int j=0;j<4;j++){float h=j==0?0:j==1?.002f:j==2?height-.002f:height,s=j==0||j==3?.94f:1;for(int i=0;i<section.Length;i++){v.Add(new Vector3(section[i].x*s,h,section[i].y*s));uv.Add(new Vector2(i/(float)section.Length,h*2));}}
            int n=section.Length;for(int j=0;j<3;j++)for(int i=0;i<n;i++)Quad(t,j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i);
            for(int i=1;i<n-1;i++){t.Add(0);t.Add(i+1);t.Add(i);t.Add(3*n);t.Add(3*n+i);t.Add(3*n+i+1);}Flip(t);return Geometry(name,parent,Mesh(v,t,uv),mat,center,rotation);
        }
        private static GameObject ArcPad(string name,Transform parent,float radius,float width,float height,float thick,float start,float end,Vector3 center,Material mat)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();int sections=24;var profile=RoundRect(thick,height,Mathf.Min(.012f,thick*.4f));int n=profile.Length;
            for(int j=0;j<=sections;j++){float a=Mathf.Lerp(start,end,j/(float)sections)*Mathf.Deg2Rad,scale=j==0||j==sections?.8f:1;foreach(var p in profile){float r=radius+p.x*scale;v.Add(new Vector3(Mathf.Sin(a)*r,p.y*scale,Mathf.Cos(a)*r));uv.Add(new Vector2(j/(float)sections,p.y*3));}}
            for(int j=0;j<sections;j++)for(int i=0;i<n;i++)Quad(t,j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i);for(int i=1;i<n-1;i++){t.Add(0);t.Add(i+1);t.Add(i);t.Add(sections*n);t.Add(sections*n+i);t.Add(sections*n+i+1);}Flip(t);return Geometry(name,parent,Mesh(v,t,uv),mat,center,Quaternion.identity);
        }
        private static void Flip(List<int> t){for(int i=0;i<t.Count;i+=3){int a=t[i+1];t[i+1]=t[i+2];t[i+2]=a;}}
        private static void Collider(Transform parent,Vector3 center,Vector3 size){var c=parent.gameObject.AddComponent<BoxCollider>();c.center=center;c.size=size;}
        private static Light Lamp(string name,Transform parent,Vector3 position,Color color,float intensity,float range,bool spot=false,float angle=75,float inner=35,bool shadow=false)
        {var go=Group(name,parent,position);go.localRotation=Quaternion.Euler(90,0,0);var l=go.gameObject.AddComponent<Light>();l.type=spot?LightType.Spot:LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.spotAngle=angle;l.innerSpotAngle=inner;l.shadows=shadow?LightShadows.Soft:LightShadows.None;l.shadowBias=.035f;l.shadowNormalBias=.08f;lights.Add(l);return l;}
    }
}
