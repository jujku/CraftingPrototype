using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Bar.Unity;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>离线从瓶身水平切片建立旋转内腔、体积曲线及起流表；不写 Art、预制体或场景。</summary>
    public static class BottleCavityBake
    {
        public static void Run()
        {
            const string folder="Assets/Bar/Presentation/Resources/BottleGeometry";
            Directory.CreateDirectory(folder);Directory.CreateDirectory("PourReports");
            var report=new List<object>();
            foreach(string id in new[]{"bottle.gin","bottle.syrup","bottle.soda","bottle.tonic"})
            {
                var root=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("BarPrefabs/"+id));
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var body=root.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="BlueGlassShell"||f.name=="GlassShell");
                var mesh=body.sharedMesh;
                var vertices=mesh.vertices.Select(v=>root.transform.InverseTransformPoint(body.transform.TransformPoint(v))).ToArray();
                var normals=mesh.normals.Select(v=>root.transform.InverseTransformDirection(body.transform.TransformDirection(v)).normalized).ToArray();
                var tris=mesh.triangles;float min=vertices.Min(v=>v.y),max=vertices.Max(v=>v.y);
                var lip=root.GetComponentsInChildren<Transform>().First(t=>t.name=="PourLip");
                float top=root.transform.InverseTransformPoint(lip.position).y;
                Vector2 axis=new Vector2((vertices.Min(v=>v.x)+vertices.Max(v=>v.x))*.5f,(vertices.Min(v=>v.z)+vertices.Max(v=>v.z))*.5f);
                int inward=0,outward=0;
                for(int i=0;i<vertices.Length;i++){var radial=new Vector3(vertices[i].x-axis.x,0,vertices[i].z-axis.y);float d=Vector3.Dot(radial.normalized,normals[i]);if(d<-.7f)inward++;if(d>.7f)outward++;}
                bool inner=inward>vertices.Length*.1f;
                var settings=PourSettings.Load();float wall=inner?0:settings.bottleWallMetres;
                var data=new BottleGeometryData{id=id,method=inner?"inner-layer-slicing":"single-shell-inset",wallMetres=wall,axis=axis};
                int n=settings.bottleSlices;data.heights=new float[n];data.radii=new float[n];data.volumesMl=new double[n];
                float innerBottom=inner?vertices.Where((v,i)=>Vector3.Dot(new Vector3(v.x-axis.x,0,v.z-axis.y).normalized,normals[i])<-.25f).Min(v=>v.y):min;
                float bottom=innerBottom+(inner?settings.sliceInsetMetres:wall);
                top=Mathf.Min(top,max-settings.sliceInsetMetres);
                for(int s=0;s<n;s++)
                {
                    float h=Mathf.Lerp(bottom,top,s/(float)(n-1));data.heights[s]=h;
                    var hits=new List<Vector2>();
                    for(int t=0;t<tris.Length;t+=3)
                    {
                        var a=vertices[tris[t]];var b=vertices[tris[t+1]];var c=vertices[tris[t+2]];
                        var centroid=(a+b+c)/3;var normal=(normals[tris[t]]+normals[tris[t+1]]+normals[tris[t+2]]).normalized;
                        if(inner&&Vector3.Dot(new Vector3(centroid.x-axis.x,0,centroid.z-axis.y).normalized,normal)>-.25f)continue;
                        for(int e=0;e<3;e++)
                        {
                            var v=vertices[tris[t+e]];var w=vertices[tris[t+(e+1)%3]];
                            if((v.y<h&&w.y>=h)||(w.y<h&&v.y>=h)){var q=Vector3.Lerp(v,w,(h-v.y)/(w.y-v.y));hits.Add(new Vector2(q.x-axis.x,q.z-axis.y));}
                        }
                    }
                    // Equal-area revolution preserves the sliced cross-sectional volume; it is an explicit geometric approximation.
                    var points=hits.GroupBy(v=>Math.Round(v.x,6)+","+Math.Round(v.y,6)).Select(g=>g.First()).OrderBy(v=>Mathf.Atan2(v.y,v.x)).ToArray();
                    double area=0;for(int j=0;j<points.Length;j++){var a=points[j];var b=points[(j+1)%points.Length];area+=(double)a.x*b.y-(double)b.x*a.y;}
                    float r=Mathf.Sqrt((float)(Math.Abs(area)*.5/Math.PI))-wall;
                    if(r<=0&&s>0)r=data.radii[s-1];
                    if(r<=0)throw new InvalidOperationException(id+" empty slice "+s);
                    data.radii[s]=r;
                    if(s>0){double a=data.radii[s-1],b=r,dy=h-data.heights[s-1];data.volumesMl[s]=data.volumesMl[s-1]+Math.PI*dy*(a*a+a*b+b*b)/3*1e6;}
                }
                int angleSamples=settings.bottleAngleSamples;
                data.fractions=Enumerable.Range(0,angleSamples).Select(i=>i/(double)(angleSamples-1)).ToArray();data.startAngles=new double[angleSamples];
                // Capacity comes from the authored item data, never from a scaled visual mesh.
                var catalogJson=JsonUtility.FromJson<ItemRows>(File.ReadAllText("Assets/Bar/Data/Json/items.json"));
                data.capacityMl=catalogJson.items.First(x=>x.id==id).capacityMl;
                for(int i=0;i<angleSamples;i++)
                {
                    double wanted=data.capacityMl*data.fractions[i],lo=0,hi=180;
                    for(int k=0;k<32;k++){double a=(lo+hi)*.5;double angle=a*Math.PI/180;double plane=top*Math.Cos(angle)-data.radii[n-1]*Math.Sin(angle);if(BottleGeometryMath.VolumeBelow(data,angle,plane)>wanted)lo=a;else hi=a;}
                    data.startAngles[i]=(lo+hi)*.5;
                }
                File.WriteAllText(folder+"/"+id+".json",JsonUtility.ToJson(data,true));
                File.WriteAllText("PourReports/"+id+"-inspection.json",JsonUtility.ToJson(new Inspection{id=id,innerLayer=inner,inwardVertices=inward,outwardVertices=outward,vertices=vertices.Length,wallMetres=wall,cavityMl=data.volumesMl[n-1],requiredMl=data.capacityMl,method=data.method},true));
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.Refresh();
        }
        [Serializable]private sealed class ItemRows{public Row[] items;}
        [Serializable]private sealed class Row{public string id;public double capacityMl;}
        [Serializable]private sealed class Inspection{public string id,method;public bool innerLayer;public int inwardVertices,outwardVertices,vertices;public float wallMetres;public double cavityMl,requiredMl;}
    }
}

