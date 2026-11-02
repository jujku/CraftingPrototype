using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bar.Unity.Editor
{
    /// <summary>显式添加中性环境反射，让已有金属材质在正式场景中可见；不改用户材质。</summary>
    public static class BarMetalLighting
    {
        /// <summary>按显式材质槽引用另存 PBR 修正版；不按物体名字分类，不覆盖原材质。</summary>
        public static Material CreateSlotVariant(Material original,string destination,float metallic,float smoothness)
        {
            if(AssetDatabase.LoadAssetAtPath<Material>(destination))throw new InvalidOperationException("Variant already exists: "+destination);
            var material=new Material(original){name=original.name+" (presentation copy)"};material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smoothness);AssetDatabase.CreateAsset(material,destination);return material;
        }
        private const string Path = "Assets/Bar/Rendering/BarStudioReflection.cubemap";
        /// <summary>输出正式金属的材质槽、着色器和资源来源，不修改资源。</summary>
        public static void InspectMaterials()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (string id in new[]{"jigger.dual","shaker.cobbler","shaker.strainer","shaker.cap"})
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab");
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        lines.Add(id+" | "+renderer.name+" | "+material.name+" | "+material.shader.name+" | "+AssetDatabase.GetAssetPath(material));
            }
            Directory.CreateDirectory("BarKitReports");
            File.WriteAllLines("BarKitReports/metal-materials.txt",lines);
        }
        /// <summary>保留用户原材质，将误设成非金属的抛光钢克隆成修正版并仅替换原材质的引用。</summary>
        /// <precondition>显式在验收副本执行；原抛光材质和正式预制体存在。</precondition>
        /// <effects>写入 Rendering 下修正版和环境贴图，仅更新量酒器、隔冰器、上盖的对应材质槽。</effects>
        public static void RepairMaterials()
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Art/BarKitMaterials/Polished stainless edges.mat");
            const string correctedPath = "Assets/Bar/Rendering/PolishedStainless.mat";
            var corrected = AssetDatabase.LoadAssetAtPath<Material>(correctedPath);
            if (!corrected)
            {
                corrected = new Material(original) { name = "Polished stainless edges (corrected PBR)" };
                corrected.SetFloat("_Metallic",.9f); corrected.SetFloat("_Smoothness",.85f);
                AssetDatabase.CreateAsset(corrected,correctedPath);
            }
            foreach (string id in new[]{"jigger.dual","shaker.strainer","shaker.cap"})
            {
                string path = "Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++)
                            if (materials[i] == original) { materials[i] = corrected; changed = true; }
                        renderer.sharedMaterials = materials;
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Path);
            Populate(cube);
            EditorUtility.SetDirty(cube);
            AssetDatabase.SaveAssets();
            InspectMaterials();
        }
        private static void Populate(Cubemap cube)
        {
            int size = cube.width;
            for (int face = 0; face < 6; face++)
            {
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    var direction = face == 0 ? new Vector3(1,-v,-u) : face == 1 ? new Vector3(-1,-v,u) :
                        face == 2 ? new Vector3(u,1,v) : face == 3 ? new Vector3(u,-1,-v) :
                        face == 4 ? new Vector3(u,-v,1) : new Vector3(-u,-v,-1);
                    direction.Normalize();
                    float ceiling = Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.55f,.8f,direction.y));
                    var color = Color.Lerp(new Color(.18f,.20f,.22f), new Color(.65f,.69f,.73f), ceiling);
                    float key = Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,new Vector3(-.7f,.6f,-.3f).normalized)),40);
                    float fill = Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,new Vector3(.8f,.2f,.5f).normalized)),16);
                    pixels[y*size+x] = color + new Color(1.3f,1.2f,1.1f)*key + new Color(.5f,.6f,.7f)*fill;
                }
                cube.SetPixels(pixels,(CubemapFace)face);
            }
            cube.Apply(true,false);
        }
        /// <summary>创建一次环境贴图并仅更新保存场景的反射设置。</summary>
        /// <precondition>在唯一验收工程显式执行；当前没有未保存场景。</precondition>
        /// <effects>新增 Rendering 下的反射 Cubemap，保留已有用户反射与所有场景对象、材质、模型。</effects>
        public static void Apply()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存场景再更新反射设置。");
            var scene = EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
            if (RenderSettings.customReflectionTexture && AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture) != Path)
                throw new InvalidOperationException("已有用户自定义反射，保留现状，停止自动设置。");
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Path);
            if (!cube)
            {
                Directory.CreateDirectory("Assets/Bar/Rendering");
                AssetDatabase.Refresh();
                const int size = 64;
                cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Bar studio reflection", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                Populate(cube);
                AssetDatabase.CreateAsset(cube,Path);
                AssetDatabase.SaveAssets();
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 1;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("保存场景反射设置失败。");
        }
    }
}
