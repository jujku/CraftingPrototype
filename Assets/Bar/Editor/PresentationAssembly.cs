using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>在唯一验证副本导出显式材质槽和几何，用于副本烘焙；不保存原场景或美术。</summary>
    public static partial class PresentationAssembly
    {
        [Serializable] public sealed class Export{public MeshRow[] meshes;public MaterialRow[] materials;}
        [Serializable] public sealed class MeshRow
        {
            public string instance,definition,path,meshGuid;public int index;
            public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles,triangleSlots;public float[] matrix;public string[] materials;
        }
        [Serializable] public sealed class MaterialRow{public string guid,path,name;public Color color;public float metallic,smoothness;public bool hasTexture;}
        private static void Guard(){if(!File.Exists(Path.Combine(Application.dataPath,"../.u1-validation-copy")))throw new InvalidOperationException("Validation copy only");}
        public static void ExportSlots()
        {
            Guard();EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");var rows=new List<MeshRow>();var materials=new Dictionary<string,MaterialRow>();
            foreach(var view in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).OrderBy(v=>v.InstanceId))
            foreach(var r in view.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter=r.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh||!r.enabled||!r.gameObject.activeInHierarchy)continue;
                var m=filter.sharedMesh;var ids=new List<string>();
                foreach(var mat in r.sharedMaterials)
                {
                    if(!mat){ids.Add("");continue;}var path=AssetDatabase.GetAssetPath(mat);var guid=AssetDatabase.AssetPathToGUID(path);ids.Add(guid);
                    if(!materials.ContainsKey(guid))materials.Add(guid,new MaterialRow{guid=guid,path=path,name=mat.name,color=mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor"):Color.white,metallic=mat.HasProperty("_Metallic")?mat.GetFloat("_Metallic"):0,smoothness=mat.HasProperty("_Smoothness")?mat.GetFloat("_Smoothness"):0,hasTexture=mat.HasProperty("_BaseMap")&&mat.GetTexture("_BaseMap")});
                }
                var triangles=new List<int>();var slots=new List<int>();for(int slot=0;slot<m.subMeshCount;slot++){var ts=m.GetTriangles(slot);triangles.AddRange(ts);slots.AddRange(Enumerable.Repeat(slot,ts.Length/3));}
                var matrix=new float[16];for(int i=0;i<16;i++)matrix[i]=r.transform.localToWorldMatrix[i];
                rows.Add(new MeshRow{index=rows.Count,instance=view.InstanceId,definition=view.DefinitionId,path=AnimationUtility.CalculateTransformPath(r.transform,view.transform),meshGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m)),vertices=m.vertices,normals=m.normals,uv=m.uv,triangles=triangles.ToArray(),triangleSlots=slots.ToArray(),matrix=matrix,materials=ids.ToArray()});
            }
            Directory.CreateDirectory("PresentationReports");File.WriteAllText("PresentationReports/scene-export.json",JsonUtility.ToJson(new Export{meshes=rows.ToArray(),materials=materials.Values.ToArray()},true));
        }
    }
}
