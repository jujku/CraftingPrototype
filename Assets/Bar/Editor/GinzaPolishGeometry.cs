using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static float Cross2(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static Vector2[] InsetPolygon(Vector2[] p,float inset)
        {
            return Enumerable.Range(0,p.Length).Select(i=>{var a=(p[i]-p[(i+p.Length-1)%p.Length]).normalized;var b=(p[(i+1)%p.Length]-p[i]).normalized;var na=new Vector2(-a.y,a.x);var nb=new Vector2(-b.y,b.x);var q=p[i]+na*inset;var r=p[i]+nb*inset;return q+a*(Cross2(r-q,b)/Cross2(a,b));}).ToArray();
        }
        private static List<int> TriangulatePolygon(Vector2[] p)
        {
            var pending=Enumerable.Range(0,p.Length).ToList();var result=new List<int>();
            while(pending.Count>3)
            {
                bool clipped=false;
                for(int i=0;i<pending.Count;i++)
                {
                    int a=pending[(i+pending.Count-1)%pending.Count],b=pending[i],c=pending[(i+1)%pending.Count];if(Cross2(p[b]-p[a],p[c]-p[b])<=0)continue;
                    if(pending.Where(j=>j!=a&&j!=b&&j!=c).Any(j=>Cross2(p[b]-p[a],p[j]-p[a])>=0&&Cross2(p[c]-p[b],p[j]-p[b])>=0&&Cross2(p[a]-p[c],p[j]-p[c])>=0))continue;
                    result.AddRange(new[]{a,b,c});pending.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)throw new InvalidOperationException("Invalid U ledge polygon");
            }
            result.AddRange(pending);return result;
        }
        private static Mesh BeveledU(Vector2[] outline,float height,float bevel)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();int n=outline.Length;
            for(int ring=0;ring<10;ring++)
            {
                bool upper=ring>=5;float a=(upper?ring-5:ring)*Mathf.PI/8;float inset=upper?bevel*(1-Mathf.Cos(a)):bevel*(1-Mathf.Sin(a));float y=upper?height-bevel+Mathf.Sin(a)*bevel:bevel*(1-Mathf.Cos(a));
                foreach(var p in InsetPolygon(outline,inset)){v.Add(new Vector3(p.x,y,p.y));uv.Add(p*3);}
                if(ring>0)for(int i=0;i<n;i++)Quad(t,(ring-1)*n+i,ring*n+i,ring*n+(i+1)%n,(ring-1)*n+(i+1)%n);
            }
            var cap=InsetPolygon(outline,bevel);int start=v.Count;foreach(var p in cap){v.Add(new Vector3(p.x,0,p.y));uv.Add(p*3);}foreach(var p in cap){v.Add(new Vector3(p.x,height,p.y));uv.Add(p*3);}var triangles=TriangulatePolygon(cap);
            for(int i=0;i<triangles.Count;i+=3){t.AddRange(new[]{start+triangles[i],start+triangles[i+1],start+triangles[i+2],start+n+triangles[i],start+n+triangles[i+2],start+n+triangles[i+1]});}
            return Mesh(v,t,uv);
        }
    }
}
