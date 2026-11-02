using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>在唯一验证副本内测量并接入更新的冰模型，不保存用户场景。</summary>
    public static class IceModelAssembly
    {
        public static void Install()
        {
            if(!File.Exists(Path.Combine(Application.dataPath,"../.u1-validation-copy")))throw new InvalidOperationException("Validation copy only");
            const string folder="Assets/Bar/Rendering/Resources/Ice";
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var surface=IceMaterial(folder+"/IceSurface.mat",new Color(.87f,.96f,1,.3f),.94f);
            var core=IceMaterial(folder+"/FrostCore.mat",new Color(.93f,.98f,1,.62f),.28f);
            var rows=new List<string>();
            foreach(var id in new[]{"ShakingIce_A","ShakingIce_B","ShakingIce_C"})
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Art/ShakingIce/"+id+"/"+id+".fbx");
                var root=new GameObject(id);var geometry=new GameObject("Normalized geometry");geometry.transform.SetParent(root.transform,false);
                var imported=(GameObject)PrefabUtility.InstantiatePrefab(model);imported.transform.SetParent(geometry.transform,false);
                var renderers=imported.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                // Normalize each axis separately: the user requested a cube rather than the supplied flatter 42x32x42 mm shape.
                imported.transform.localPosition-=bounds.center;
                geometry.transform.localScale=new Vector3(1/bounds.size.x,1/bounds.size.y,1/bounds.size.z);
                double volume=0;int triangles=0;
                foreach(var renderer in renderers)
                {
                    renderer.sharedMaterial=renderer.name=="FrostCore"?core:surface;
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;triangles+=mesh.triangles.Length/3;
                    if(renderer.name!="IceSurface")continue;
                    var vertices=mesh.vertices;var indices=mesh.triangles;double signed=0;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[indices[i]]));
                        var b=root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[indices[i+1]]));
                        var c=root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[indices[i+2]]));
                        signed+=Vector3.Dot(a,Vector3.Cross(b,c))/6d;
                    }
                    volume+=Math.Abs(signed);
                }
                bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                if(Vector3.Distance(bounds.size,Vector3.one)>1e-4||bounds.center.magnitude>1e-4)throw new InvalidOperationException("Ice normalization failed: "+id);
                PrefabUtility.SaveAsPrefabAsset(root,folder+"/"+id+".prefab");
                rows.Add(id+" normalizedBounds="+bounds.size.ToString("F6")+" centre="+bounds.center.ToString("F6")+" triangles="+triangles+" volumeAt37mmMl="+(volume*Math.Pow(.037,3)*1e6).ToString("F4")+" massAtDensity0917="+(volume*Math.Pow(.037,3)*1e6*.917).ToString("F4"));
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();Directory.CreateDirectory(Path.Combine(Application.dataPath,"../IceModelReports"));File.WriteAllLines(Path.Combine(Application.dataPath,"../IceModelReports/install.txt"),rows);
        }
        private static Material IceMaterial(string path,Color color,float smoothness)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetColor("_Color",color);material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);
            material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.One);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha",(float)UnityEngine.Rendering.BlendMode.One);material.SetFloat("_DstBlendAlpha",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0);material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Cull",2);
            material.SetOverrideTag("RenderType","Transparent");material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.EnableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_ALPHATEST_ON");material.renderQueue=3000;
            material.SetShaderPassEnabled("ShadowCaster",false);material.SetShaderPassEnabled("DepthOnly",false);EditorUtility.SetDirty(material);return material;
        }
        public static void Measure()
        {
            if(!File.Exists(Path.Combine(Application.dataPath,"../.u1-validation-copy")))throw new InvalidOperationException("Validation copy only");
            EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");var rows=new List<string>();
            foreach(var v in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).Where(v=>v.DefinitionId=="glass.highball"||v.DefinitionId=="shaker.cobbler"))
            foreach(var c in v.GetComponentsInChildren<ContainerVisual>(true))
            {
                var points=c.InnerCavity.sharedMesh.vertices.Select(x=>v.transform.InverseTransformPoint(c.InnerCavity.transform.TransformPoint(x))).ToArray();
                var centre=new Vector2(points.Average(x=>x.x),points.Average(x=>x.z));
                var rings=points.GroupBy(x=>Mathf.Round(x.y*100000)).Select(g=>new{y=g.Key/100000f,r=g.Select(x=>Vector2.Distance(new Vector2(x.x,x.z),centre)).Where(r=>r>.005f).DefaultIfEmpty(0).Min()}).Where(r=>r.r>0).OrderBy(r=>r.y);
                rows.Add(v.DefinitionId+" height="+(points.Max(x=>x.y)-points.Min(x=>x.y)).ToString("R")+" rings="+string.Join(";",rings.Select(r=>r.y.ToString("R")+":"+(2*r.r).ToString("R"))));
            }
            foreach(var id in new[]{"ShakingIce_A","ShakingIce_B","ShakingIce_C"})
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Art/ShakingIce/"+id+"/"+id+".fbx");var go=(GameObject)PrefabUtility.InstantiatePrefab(model);var renderers=go.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
                rows.Add(id+" bounds="+b+" nodes="+string.Join(",",go.GetComponentsInChildren<Transform>().Select(x=>x.name+":"+x.position)));UnityEngine.Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"../IceModelReports"));File.WriteAllLines(Path.Combine(Application.dataPath,"../IceModelReports/measure.txt"),rows);
        }
    }
}
