using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bar.Unity;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>仅在唯一验证工程生成瓶子标签材质、同步授权尺寸与锚点，再调用原 BottleCavityBake。</summary>
    public static class BottleRefineBake
    {
        [Serializable]private sealed class CollisionProfiles{public CollisionProfile[] profiles;}
        [Serializable]private sealed class CollisionProfile{public string id,name;public float[] heights,radii,axis;}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/_validation/UnityProject/Assets"))throw new InvalidOperationException("Bake only in the marked validation project");
            var report=new List<string>{"id,widthMm,heightMm,depthMm,capWidthMm,capHeightMm,triangles,bottomY"};string json=File.ReadAllText("Assets/Bar/Data/Json/items.json");
            foreach(string stem in new[]{"gin","syrup","soda","tonic"})
            {
                string name=stem=="gin"?"GinBottle":stem=="syrup"?"SyrupBottle":stem=="soda"?"SodaBottle":"TonicBottle",id="bottle."+stem,path="Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab";
                var imported=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Art/"+name+"/"+name+".fbx");var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform Imported(string n)=>imported.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
                    Transform Target(string n)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
                    foreach(string n in new[]{"Cap","PourLip"})Target(n).localPosition=Imported(n).localPosition;
                    Target("Imported_Grip_R").localPosition=Imported("Grip_R").localPosition;Target("Grip_R").position=Target("Visual").TransformPoint(Imported("Grip_R").localPosition);
                    string folder="Assets/Bar/Art/PresentationLabels/BottleRefine/",matpath=folder+stem+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matpath);
                    if(!material){material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Presentation/Materials/2abb11bad39aabf4ba45e4b969b8e42e.mat"));AssetDatabase.CreateAsset(material,matpath);}
                    var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+stem+"-base.png");var metal=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+stem+"-metal.png");
                    material.name="BottleRefine "+stem+" original paper and foil";material.SetTexture("_BaseMap",tex);material.SetTexture("_MainTex",tex);material.SetTexture("_BumpMap",null);material.DisableKeyword("_NORMALMAP");material.SetTexture("_MetallicGlossMap",metal);material.EnableKeyword("_METALLICSPECGLOSSMAP");material.SetFloat("_Cull",0);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",1);EditorUtility.SetDirty(material);
                    var label=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="FrontLabel"||r.name=="OriginalLabel");label.sharedMaterial=material;
                    Bounds BoundsOf(IEnumerable<MeshFilter> filters)
                    {
                        bool first=true;var b=new Bounds();foreach(var filter in filters)foreach(var v in filter.sharedMesh.vertices){var pt=root.transform.InverseTransformPoint(filter.transform.TransformPoint(v));if(first){b=new Bounds(pt,Vector3.zero);first=false;}else b.Encapsulate(pt);}return b;
                    }
                    var all=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh).ToArray();var bounds=BoundsOf(all);var cap=BoundsOf(Target("Cap").GetComponentsInChildren<MeshFilter>(true));var view=root.GetComponent<ItemView>();view.Size=bounds.size;
                    var collision=JsonUtility.FromJson<CollisionProfiles>(File.ReadAllText("Assets/Bar/Editor/BottleRefineCollisionProfiles.json")).profiles.Single(x=>x.id==stem);var box=root.GetComponent<BoxCollider>();if(box)UnityEngine.Object.DestroyImmediate(box);
                    var collider=root.GetComponent<MeshCollider>();if(!collider)collider=root.AddComponent<MeshCollider>();collider.sharedMesh=CollisionMesh(collision);collider.convex=false;
                    json=Size(json,id,bounds.size);if(stem!="gin")json=Size(json,id+".cap",cap.size);int triangles=all.Sum(f=>f.sharedMesh.triangles.Length/3);
                    report.Add(FormattableString.Invariant($"{id},{bounds.size.x*1000},{bounds.size.y*1000},{bounds.size.z*1000},{cap.size.x*1000},{cap.size.y*1000},{triangles},{bounds.min.y}"));
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllText("Assets/Bar/Data/Json/items.json",json);AssetDatabase.SaveAssets();AssetDatabase.Refresh();Directory.CreateDirectory("BottleRefineReports");File.WriteAllLines("BottleRefineReports/imported-dimensions.csv",report);
            BottleCavityBake.Run();File.WriteAllText("BottleRefineReports/bake-complete.txt","BottleCavityBake completed from updated imported meshes; copy only the declared bottle outputs back to source.");
        }
        private static Mesh CollisionMesh(CollisionProfile p)
        {
            string path="Assets/Bar/Art/"+p.name+"/"+p.name+"-collision.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh{name=p.name+" external collision profile"};AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            const int sides=12;int n=p.heights.Length;var vertices=new Vector3[n*sides+2];var triangles=new List<int>();
            for(int i=0;i<n;i++)for(int j=0;j<sides;j++){float a=2*Mathf.PI*j/sides;vertices[i*sides+j]=new Vector3(p.axis[0]+p.radii[i]*Mathf.Cos(a),p.heights[i],-p.axis[1]+p.radii[i]*Mathf.Sin(a));}
            vertices[n*sides]=new Vector3(p.axis[0],p.heights[0],-p.axis[1]);vertices[n*sides+1]=new Vector3(p.axis[0],p.heights[n-1],-p.axis[1]);
            for(int i=0;i<n-1;i++)for(int j=0;j<sides;j++){int a=i*sides+j,b=i*sides+(j+1)%sides,c=a+sides,d=b+sides;triangles.AddRange(new[]{a,c,b,b,c,d});}
            for(int j=0;j<sides;j++){int b=(j+1)%sides;triangles.AddRange(new[]{n*sides,j,b,n*sides+1,(n-1)*sides+b,(n-1)*sides+j});}
            mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }
        private static string Size(string json,string id,Vector3 size)
        {
            string pattern="(?s)(\"id\": \""+Regex.Escape(id)+"\".*?\"size\": \\{)(.*?)(\\})";int count=0;
            string result=Regex.Replace(json,pattern,m=>{count++;var body=m.Groups[2].Value;foreach(var pair in new[]{("widthCm",size.x*100),("heightCm",size.y*100),("depthCm",size.z*100)})body=Regex.Replace(body,"(\""+pair.Item1+"\": )[0-9.Ee+-]+","${1}"+pair.Item2.ToString("0.######",CultureInfo.InvariantCulture));return m.Groups[1].Value+body+m.Groups[3].Value;});if(count!=1)throw new InvalidOperationException("Missing or duplicate size "+id);return result;
        }
    }
}
