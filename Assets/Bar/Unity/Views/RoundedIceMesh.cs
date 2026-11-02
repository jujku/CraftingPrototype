using System.Collections.Generic;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>运行时生成倒角方冰网格；BoxCollider 仍是保守碰撞代理，不修改用户模型。</summary>
    public static class RoundedIceMesh
    {
        private static Mesh cached;
        public static Mesh Get()
        {
            if(cached)return cached;var points=new List<Vector3>();var normals=new List<Vector3>();var indices=new List<int>();const int n=6;const float radius=.08f;
            foreach(var face in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
            {
                Vector3 u=Vector3.Cross(face,Vector3.up);if(u.sqrMagnitude<.1f)u=Vector3.right;u.Normalize();var v=Vector3.Cross(face,u);int offset=points.Count;
                for(int y=0;y<=n;y++)for(int x=0;x<=n;x++){var a=face*.5f+u*(x/(float)n-.5f)+v*(y/(float)n-.5f);var c=new Vector3(Mathf.Clamp(a.x,-.5f+radius,.5f-radius),Mathf.Clamp(a.y,-.5f+radius,.5f-radius),Mathf.Clamp(a.z,-.5f+radius,.5f-radius));var normal=(a-c).normalized;points.Add(c+normal*radius);normals.Add(normal);if(x<n&&y<n){int k=offset+y*(n+1)+x;indices.AddRange(new[]{k,k+1,k+n+2,k,k+n+2,k+n+1});}}
            }
            cached=new Mesh{name="Procedural bevel ice"};cached.SetVertices(points);cached.SetNormals(normals);cached.SetTriangles(indices,0);cached.RecalculateBounds();return cached;
        }
    }
}
