using System;
using System.IO;
using System.Linq;
using Bar.Data;
using CraftingResearch.CoreV2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>为已有灰盒添加真实内腔、锚点、曲线和可替换预制体，不更改物理核心。</summary>
    public static class U4SceneSetup
    {
        /// <summary>升级保存的灰盒并烘焙每种容器。</summary>
        /// <precondition>只在唯一验证工程或用户主动调用的 Editor 执行；U3 场景和 JSON 已存在。</precondition>
        /// <effects>更新场景容器节点、保存网格/曲线/预制体，输出灰盒验证清单。</effects>
        [MenuItem("Bar/Upgrade Graybox To U4")]
        public static void Install()
        {
            if (File.Exists("Assets/Bar/Prefabs/BarKit-installed.txt"))
                throw new InvalidOperationException("BarKit 已接入，为保护用户美术调整，禁止灰盒生成器覆盖正式资源。");
            EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");Directory.CreateDirectory("Assets/Bar/Art/VolumeProfiles");Directory.CreateDirectory("Assets/Bar/Prefabs/Resources/BarPrefabs");
            var model=JsonUtility.FromJson<ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text);
            var catalog=ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text,model);
            string materialPath="Assets/Bar/Art/VolumeProfiles/Liquid.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(!material){material=new Material(Shader.Find("Bar/LiquidFill"));AssetDatabase.CreateAsset(material,materialPath);}material.SetColor("_BaseColor",new Color(.56f,.82f,.7f));
            foreach(var view in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None))
            {
                var definition=catalog[view.DefinitionId];AddAnchors(view.transform,definition);
                for(int legacy=1;legacy<=3;legacy++){var oldMark=view.transform.Find("Marking"+legacy);if(oldMark)UnityEngine.Object.DestroyImmediate(oldMark.gameObject);}
                if(view.transform.Find("LiquidVolume"))UnityEngine.Object.DestroyImmediate(view.transform.Find("LiquidVolume").gameObject);
                string[] containerIds=definition.Kind==ItemKind.Container?new[]{definition.Id}:definition.Parts.Where(id=>catalog[id].Kind==ItemKind.Container).ToArray();
                for(int i=0;i<containerIds.Length;i++)
                {
                    var d=catalog[containerIds[i]];string nodeName=definition.Type=="Jigger"?(i==0?"CupA":"CupB"):definition.Type=="CobblerShaker"?"Body":"VesselVisual";
                    var node=view.transform.Find(nodeName);if(!node){node=new GameObject(nodeName).transform;node.SetParent(view.transform,false);}node.localPosition=definition.Type=="Jigger"&&i==1?new Vector3(0,.04f,0):Vector3.zero;
                    AddAnchors(node,d);var cv=node.GetComponent<ContainerVisual>();if(!cv)cv=node.gameObject.AddComponent<ContainerVisual>();cv.DefinitionId=d.Id;
                    float w=(float)d.Size.WidthCm*.0086f,depth=(float)d.Size.DepthCm*.0086f;
                    string meshPath="Assets/Bar/Art/VolumeProfiles/"+d.Id+"-cavity.asset";
                    var shape=GameObject.CreatePrimitive(PrimitiveType.Cylinder);var mesh=UnityEngine.Object.Instantiate(shape.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(shape);
                    var vertices=mesh.vertices;var bounds=mesh.bounds;for(int k=0;k<vertices.Length;k++)vertices[k]=new Vector3(vertices[k].x/bounds.size.x,vertices[k].y/bounds.size.y,vertices[k].z/bounds.size.z);
                    double unitVolume=0;var triangles=mesh.triangles;for(int k=0;k<triangles.Length;k+=3)unitVolume+=Vector3.Dot(vertices[triangles[k]],Vector3.Cross(vertices[triangles[k+1]],vertices[triangles[k+2]]))/6.0;
                    float h=(float)(d.CapacityMl*1e-6/(Math.Abs(unitVolume)*w*depth)),bottom=(float)d.Size.HeightCm*.0095f-h;if(bottom<0)throw new InvalidOperationException(d.Id+" 内腔不能容纳定义容量");
                    for(int k=0;k<vertices.Length;k++)vertices[k]=Vector3.Scale(vertices[k],new Vector3(w,h,depth))+Vector3.up*(bottom+h/2);mesh.vertices=vertices;mesh.RecalculateBounds();
                    var oldMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(oldMesh){EditorUtility.CopySerialized(mesh,oldMesh);UnityEngine.Object.DestroyImmediate(mesh);mesh=oldMesh;}else AssetDatabase.CreateAsset(mesh,meshPath);
                    var cavity=node.Find("InnerCavity");var mf=cavity.GetComponent<MeshFilter>();if(!mf)mf=cavity.gameObject.AddComponent<MeshFilter>();mf.sharedMesh=mesh;var renderer=cavity.GetComponent<MeshRenderer>();if(renderer)renderer.enabled=false;cv.InnerCavity=mf;
                    var liquid=node.Find("LiquidVolume");var lmf=liquid.GetComponent<MeshFilter>();if(!lmf)lmf=liquid.gameObject.AddComponent<MeshFilter>();lmf.sharedMesh=mesh;var lr=liquid.GetComponent<MeshRenderer>();if(!lr)lr=liquid.gameObject.AddComponent<MeshRenderer>();lr.sharedMaterial=material;cv.LiquidVolume=lr;
                    var baked=VolumeProfileBaker.Bake(mf,node);foreach(double mark in d.MarkingsMl){var anchor=FindOrCreate(node,"Mark_"+mark.ToString(System.Globalization.CultureInfo.InvariantCulture)+"ml");anchor.localPosition=Vector3.up*baked.HeightForMl(mark);
                        var ringTransform=FindOrCreate(anchor,"MarkingRing");var ring=ringTransform.GetComponent<LineRenderer>();if(!ring)ring=ringTransform.gameObject.AddComponent<LineRenderer>();ring.useWorldSpace=false;ring.loop=true;ring.positionCount=48;ring.widthMultiplier=.0006f;ring.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Art/Graybox/Charcoal.mat");
                        for(int k=0;k<48;k++){float angle=k*Mathf.PI*2/48;ring.SetPosition(k,new Vector3(Mathf.Cos(angle)*view.Size.x*.505f,0,Mathf.Sin(angle)*view.Size.z*.505f));}}UnityEngine.Object.DestroyImmediate(baked);
                }
                VolumeProfileBaker.BakeAndSave(view.gameObject,"Assets/Bar/Art/VolumeProfiles");
                PrefabUtility.SaveAsPrefabAsset(view.gameObject,"Assets/Bar/Prefabs/Resources/BarPrefabs/"+definition.PrefabKey+".prefab");
            }
            var log=new System.Collections.Generic.List<string>();foreach(var view in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None)){log.Add(view.DefinitionId);log.AddRange(PrefabValidator.Validate(view.gameObject));}
            Directory.CreateDirectory("U4Reports");File.WriteAllLines("U4Reports/prefab-validation.txt",log);
            if(log.Any(line=>line.StartsWith("失败")))throw new InvalidOperationException("灰盒预制体验证未通过，见 U4Reports/prefab-validation.txt");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        private static Transform FindOrCreate(Transform root,string name){var t=root.Find(name);if(!t){t=new GameObject(name).transform;t.SetParent(root,false);}return t;}
        private static void AddAnchors(Transform root,ItemDefinition definition)
        {
            foreach(string name in definition.Anchors)
            {var t=FindOrCreate(root,name);if(name=="PourLip"||name=="Surface"||name=="Outlet"||name=="Slot_Strainer"||name=="Slot_Cap")t.localPosition=Vector3.up*(float)definition.Size.HeightCm*.01f;}
        }
    }
}
