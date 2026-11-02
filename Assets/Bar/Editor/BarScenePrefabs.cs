using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Bar.Data;
using CraftingResearch.CoreV2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bar.Unity.Editor
{
    /// <summary>把保存的场景迁移成正式预制体实例，保留布局与实例身份，不重建美术资源。</summary>
    public static class BarScenePrefabs
    {
        private const string ScenePath = "Assets/Bar/Scenes/Bar.unity";
        private const string PrefabFolder = "Assets/Bar/Prefabs/Resources/BarPrefabs/";

        /// <summary>显式迁移占位物；已连接正确正式预制体的实例保持原样。</summary>
        /// <precondition>场景已保存，正式预制体与数据完整；不要在有未保存修改的场景中调用。</precondition>
        /// <effects>只保存 Bar.unity 并输出迁移报告，保留原父节点、次序、名称、变换和 InstanceId。</effects>
        [MenuItem("Bar/Use Formal Prefabs In Saved Scene")]
        public static void Apply()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再执行正式预制体迁移。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
            var model = JsonUtility.FromJson<ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text);
            var catalog = ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text, model);
            var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ItemView>(true)).ToArray();
            // Complete the resource/reference checks before changing any scene objects.
            if (views.Select(v => v.InstanceId).Distinct().Count() != views.Length)
                throw new InvalidOperationException("场景物品 InstanceId 重复，迁移中止。");
            var prefabs = new Dictionary<ItemView, GameObject>();
            foreach (var view in views)
            {
                string path = PrefabFolder + catalog[view.DefinitionId].PrefabKey + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab || !prefab.GetComponent<ItemView>() || prefab.GetComponent<ItemView>().DefinitionId != view.DefinitionId)
                    throw new InvalidOperationException("正式预制体缺失或定义不符：" + path);
                var failures = PrefabValidator.Validate(prefab).Where(x => x.StartsWith("失败")).ToArray();
                if (failures.Length > 0) throw new InvalidOperationException(path + "\n" + string.Join("\n", failures));
                prefabs.Add(view, prefab);
            }
            var report = new List<string>();
            bool changed = false;
            foreach (var original in views)
            {
                string path = AssetDatabase.GetAssetPath(prefabs[original]);
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(original.gameObject) == path)
                {
                    report.Add("保留正式实例 " + original.InstanceId + " | " + path);
                    continue;
                }
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[original], scene);
                replacement.transform.SetParent(original.transform.parent, false);
                replacement.transform.localPosition = original.transform.localPosition;
                replacement.transform.localRotation = original.transform.localRotation;
                replacement.transform.localScale = original.transform.localScale;
                replacement.transform.SetSiblingIndex(original.transform.GetSiblingIndex());
                replacement.name = original.name;
                var view = replacement.GetComponent<ItemView>();
                view.InstanceId = original.InstanceId;
                PrefabUtility.RecordPrefabInstancePropertyModifications(replacement);
                PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                report.Add("迁移 " + view.InstanceId + " | " + path + " | position=" + view.transform.position.ToString("F6"));
                UnityEngine.Object.DestroyImmediate(original.gameObject);
                changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("保存正式场景失败。");
            }
            Directory.CreateDirectory("BarKitReports");
            File.WriteAllLines("BarKitReports/scene-prefabs.txt", report);
        }
    }
}
