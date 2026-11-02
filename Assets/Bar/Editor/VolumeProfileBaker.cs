using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Unity;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>对封闭定向三角网格做水平裁切积分，生成累计 ml→高度资源。</summary>
    public static class VolumeProfileBaker
    {
        private static Vector3[] Vertices(MeshFilter cavity,Transform root)=>cavity.sharedMesh.vertices.Select(v=>root.InverseTransformPoint(cavity.transform.TransformPoint(v))).ToArray();
        /// <summary>烘焙封闭内腔；支持非圆柱和凹形内腔。</summary>
        /// <precondition>网格可读、边流形且定向一致，坐标米制；samples 至少为 3。</precondition>
        /// <effects>返回新建的内存资源；不修改输入网格，不自动写入磁盘。开口或退化网格抛明确错误。</effects>
        public static VolumeProfile Bake(MeshFilter cavity,Transform root,int samples=129)
        {
            if(!cavity||!cavity.sharedMesh||!cavity.sharedMesh.isReadable||!root||samples<3)throw new ArgumentException("InnerCavity 缺失或 Mesh 未启用 Read/Write");
            var vertices=Vertices(cavity,root);var triangles=cavity.sharedMesh.triangles;ValidateClosed(vertices,triangles);
            float bottom=vertices.Min(v=>v.y),top=vertices.Max(v=>v.y);if(top-bottom<1e-6f)throw new ArgumentException("内腔没有有效高度");
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.Heights=new float[samples];profile.VolumesMl=new double[samples];
            for(int i=0;i<samples;i++){float h=Mathf.Lerp(bottom,top,(float)i/(samples-1));profile.Heights[i]=h;profile.VolumesMl[i]=Below(vertices,triangles,h)*1e6;if(i>0&&profile.VolumesMl[i]+1e-5<profile.VolumesMl[i-1])throw new ArgumentException("内腔方向不一致，累计体积不单调");}
            if(profile.CapacityMl<1e-6)throw new ArgumentException("内腔封闭体积为零");return profile;
        }
        private static string Key(Vector3 v)=>Math.Round(v.x,6)+","+Math.Round(v.y,6)+","+Math.Round(v.z,6);
        private static void ValidateClosed(Vector3[] vs,int[] ts)
        {
            var edges=new Dictionary<string,(int count,int direction)>();
            for(int i=0;i<ts.Length;i+=3)for(int j=0;j<3;j++)
            {string a=Key(vs[ts[i+j]]),b=Key(vs[ts[i+(j+1)%3]]);if(a==b)throw new ArgumentException("内腔含退化三角形");bool forward=string.CompareOrdinal(a,b)<0;string key=forward?a+"|"+b:b+"|"+a;edges.TryGetValue(key,out var e);edges[key]=(e.count+1,e.direction+(forward?1:-1));}
            if(edges.Count==0||edges.Values.Any(e=>e.count!=2||e.direction!=0))throw new ArgumentException("InnerCavity 必须封闭且面方向一致：补底面和虚拟顶面，焊接边界并重算法线");
        }
        private static double Below(Vector3[] vs,int[] ts,float height)
        {
            double total=0;
            for(int i=0;i<ts.Length;i+=3)
            {
                var polygon=new List<Vector3>{vs[ts[i]],vs[ts[i+1]],vs[ts[i+2]]};var clipped=new List<Vector3>();
                for(int j=0;j<3;j++){var a=polygon[j];var b=polygon[(j+1)%3];bool ia=a.y<=height,ib=b.y<=height;if(ia)clipped.Add(a);if(ia!=ib)clipped.Add(Vector3.Lerp(a,b,(height-a.y)/(b.y-a.y)));}
                // Origin is on the clipping plane, so the closing cap has zero signed tetrahedron volume.
                for(int j=1;j+1<clipped.Count;j++){var a=clipped[0]-Vector3.up*height;var b=clipped[j]-Vector3.up*height;var c=clipped[j+1]-Vector3.up*height;total+=((double)a.x*(b.y*c.z-b.z*c.y)+(double)a.y*(b.z*c.x-b.x*c.z)+(double)a.z*(b.x*c.y-b.y*c.x))/6;}
            }
            return Math.Abs(total);
        }
        /// <summary>保存并绑定所有容器子节点的烘焙曲线。</summary>
        /// <precondition>root 的容器节点已配置内腔，folder 是 Assets 下的资源目录。</precondition>
        /// <effects>创建或更新曲线资源，保存 Profile 引用；不缩放网格凑容量。</effects>
        public static void BakeAndSave(GameObject root,string folder)
        {
            System.IO.Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            foreach(var visual in root.GetComponentsInChildren<ContainerVisual>(true))
            {var baked=Bake(visual.InnerCavity,visual.transform);string path=folder+"/"+visual.DefinitionId+".asset";var existing=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);if(existing){EditorUtility.CopySerialized(baked,existing);UnityEngine.Object.DestroyImmediate(baked);visual.Profile=existing;}else{AssetDatabase.CreateAsset(baked,path);visual.Profile=baked;}EditorUtility.SetDirty(visual);}
            AssetDatabase.SaveAssets();
        }
    }
}
