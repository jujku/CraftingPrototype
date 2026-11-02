using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>实际导入瓶形、几何容量、满瓶液位、肩部转角、标签与新起倒角表的一致性。</summary>
    public sealed class BottleRefineTests
    {
        [Serializable]private sealed class Item{public string id;public double capacityMl;}
        [Serializable]private sealed class Items{public Item[] items;}
        [Serializable]private sealed class Table{public string id;public double[] fractions,degrees;}
        [Serializable]private sealed class Controls{public Table[] bottleStartTables;}
        [TestCase("gin",750,800,860,.022f,.200f,.227f,.003f)]
        [TestCase("syrup",500,600,650,.015f,.178f,.207f,.003f)]
        [TestCase("soda",325,345,375,.012f,.168f,.190f,.0028f)]
        [TestCase("tonic",325,345,375,.012f,.168f,.190f,.0028f)]
        public void ImportedBottleMeetsShapeFillLabelAndAngleRequirements(string stem,double capacity,double minMl,double maxMl,float floor,float shoulder,float neck,float wall)
        {
            string id="bottle."+stem;var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab");var g=BottleGeometryData.Load(id);var profile=g.CreateProfile();
            try
            {
                var items=JsonUtility.FromJson<Items>(File.ReadAllText("Assets/Bar/Data/Json/items.json"));Assert.That(items.items.Single(x=>x.id==id).capacityMl,Is.EqualTo(capacity));Assert.That(g.capacityMl,Is.EqualTo(capacity));Assert.That(profile.CapacityMl,Is.InRange(minMl,maxMl));
                Assert.That(profile.Heights[0],Is.InRange(floor-.0001f,floor+.001f));float full=profile.HeightForMl(capacity);Assert.That(full,Is.LessThan(shoulder+.0001f),"满瓶液面必须在肩部以下");
                var controls=JsonUtility.FromJson<Controls>(File.ReadAllText("Assets/Bar/Data/Json/controls.json"));var t=controls.bottleStartTables.Single(x=>x.id==id);Assert.That(t.fractions,Is.EqualTo(g.fractions));
                // GuidedPour reserialized controls.json (e.g. ...25378 -> ...25377).
                // Both literals are the same IEEE double; JsonUtility introduces <= 1.43e-14
                // across the two lexical forms. Geometry hashes and canonical values are unchanged.
                Assert.That(t.degrees,Is.EqualTo(g.startAngles).Within(1e-9));
                Assert.That(g.startAngles.Zip(g.startAngles.Skip(1),(a,b)=>b<=a).All(x=>x),Is.True,"余量减少，起倒角应增大");
                var filters=root.GetComponentsInChildren<MeshFilter>();Assert.That(filters.Sum(f=>f.sharedMesh.triangles.Length/3),Is.LessThanOrEqualTo(12000));
                var collision=root.GetComponent<MeshCollider>();Assert.That(collision,Is.Not.Null);Assert.That(collision.sharedMesh.triangles.Length/3,Is.EqualTo(192));Assert.That(filters.Sum(f=>f.sharedMesh.triangles.Length/3)+collision.sharedMesh.triangles.Length/3,Is.LessThanOrEqualTo(12000),"连碰撞简模也计入12k预算");Assert.That(root.GetComponent<BoxCollider>(),Is.Null,"长颈周围不能有整瓶盒体挡住射线");
                var shell=filters.Single(f=>f.name=="BlueGlassShell"||f.name=="GlassShell");var v=shell.sharedMesh.vertices.Select(x=>root.transform.InverseTransformPoint(shell.transform.TransformPoint(x))).ToArray();var normals=shell.sharedMesh.normals.Select(x=>root.transform.InverseTransformDirection(shell.transform.TransformDirection(x))).ToArray();
                Assert.That(v.Min(x=>x.y),Is.EqualTo(0).Within(.00001f));Assert.That(shell.sharedMesh.colors.Length,Is.EqualTo(v.Length));Assert.That(shell.sharedMesh.colors.All(c=>c.a<.001f),Is.True);
                if(stem=="gin"){Assert.That(v.Max(x=>x.x)-v.Min(x=>x.x),Is.LessThanOrEqualTo(.08001f));Assert.That(root.GetComponent<ItemView>().Size.y,Is.LessThanOrEqualTo(.28001f));}
                var samples=v.Select((x,i)=>new {point=x,normal=normals[i],radial=new Vector3(x.x-g.axis.x,0,x.z-g.axis.y)}).Where(x=>x.point.y>=shoulder-.00001f&&x.point.y<=neck+.00001f&&Vector3.Dot(x.radial,x.normal)>0).OrderBy(x=>x.point.y).ToArray();
                // FBX's -90-degree conversion spreads a horizontal ring by nanometres. Merge by distance,
                // rather than decimal rounding, which can split one ring at a rounding boundary.
                var grouped=new List<List<Vector2>>();foreach(var sample in samples){if(grouped.Count==0||sample.point.y-grouped[grouped.Count-1][0].y>.000001f)grouped.Add(new List<Vector2>());grouped[grouped.Count-1].Add(new Vector2(sample.radial.magnitude,sample.point.y));}
                var rings=grouped.Select(x=>new Vector2(x.Average(a=>a.x),x.Average(a=>a.y))).ToArray();
                var directions=rings.Zip(rings.Skip(1),(a,b)=>Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg).ToArray();Assert.That(rings.Length,Is.GreaterThanOrEqualTo(20));Assert.That(directions.Zip(directions.Skip(1),(a,b)=>Mathf.Abs(Mathf.DeltaAngle(a,b))).Max(),Is.LessThanOrEqualTo(6),"肩部实测最大转角");
                var mid=v.Select((x,i)=>new {point=x,normal=normals[i],r=new Vector3(x.x-g.axis.x,0,x.z-g.axis.y)}).Where(x=>Mathf.Abs(x.point.y-shoulder)<.00001f).ToArray();float outside=mid.Where(x=>Vector3.Dot(x.r,x.normal)>0).Average(x=>x.r.magnitude),inside=mid.Where(x=>Vector3.Dot(x.r,x.normal)<0).Average(x=>x.r.magnitude);Assert.That(outside-inside,Is.EqualTo(wall).Within(.00005f));
                var label=filters.Single(f=>f.name=="FrontLabel"||f.name=="OriginalLabel");float labelTop=label.sharedMesh.vertices.Max(x=>root.transform.InverseTransformPoint(label.transform.TransformPoint(x)).y);Assert.That(labelTop,Is.LessThan(shoulder));if(stem=="syrup")Assert.That(full-labelTop,Is.GreaterThan(.01f),"满瓶糖浆液面在标签上至少10mm");
                var material=label.GetComponent<MeshRenderer>().sharedMaterial;Assert.That(material.GetFloat("_Cull"),Is.Zero);var texture=material.GetTexture("_BaseMap");Assert.That(Mathf.Max(texture.width,texture.height),Is.GreaterThanOrEqualTo(1024));var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));Assert.That(importer.mipmapEnabled,Is.True);Assert.That(importer.filterMode,Is.EqualTo(FilterMode.Trilinear));Assert.That(importer.anisoLevel,Is.GreaterThanOrEqualTo(4));
                if(stem=="soda"||stem=="tonic")
                {
                    var cap=root.GetComponentsInChildren<Transform>().Single(x=>x.name=="Cap");float top=float.MinValue,bottom=float.MaxValue;
                    foreach(var f in cap.GetComponentsInChildren<MeshFilter>())foreach(var x in f.sharedMesh.vertices){float y=root.transform.InverseTransformPoint(f.transform.TransformPoint(x)).y;top=Mathf.Max(top,y);bottom=Mathf.Min(bottom,y);}Assert.That(top-bottom,Is.LessThanOrEqualTo(.01201f));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(profile);}
        }
    }
}
