using System.IO;
using System.Linq;
using Bar.Unity;
using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bar.Tests
{
    internal sealed class ScenePrefabTests
    {
        [Test] public void SavedSceneUsesConnectedFormalPrefabsAndMigrationPreservesOverrides()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
            try
            {
                var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ItemView>(true)).ToArray();
                Assert.That(views.Length, Is.GreaterThanOrEqualTo(23));
                Assert.That(views.Select(v => v.DefinitionId).Distinct().Count(), Is.EqualTo(20));
                Assert.That(views.Select(v => v.InstanceId).Distinct().Count(), Is.EqualTo(views.Length));
                foreach (var view in views)
                {
                    Assert.That(PrefabUtility.GetPrefabInstanceStatus(view), Is.EqualTo(PrefabInstanceStatus.Connected), view.InstanceId);
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject),
                        Is.EqualTo("Assets/Bar/Prefabs/Resources/BarPrefabs/" + view.DefinitionId + ".prefab"));
                    Assert.That(PrefabValidator.Validate(view.gameObject).Where(x => x.StartsWith("失败")), Is.Empty, view.InstanceId);
                }
                var gin = views.First(v => v.DefinitionId == "bottle.gin");
                var grip = gin.Anchor("Grip_R");
                Vector3 adjusted = grip.localPosition + new Vector3(.002f,.003f,.004f);
                grip.localPosition = adjusted;
                PrefabUtility.RecordPrefabInstancePropertyModifications(grip);
                EditorSceneManager.MarkSceneDirty(scene);
                Assert.That(() => BarScenePrefabs.Apply(), Throws.InvalidOperationException.With.Message.Contains("保存"));
                // Discard the temporary edit. Test idempotence on the exact saved scene, without rewriting it.
                scene = EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
                string before = File.ReadAllText("Assets/Bar/Scenes/Bar.unity");
                var ids = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ItemView>(true))
                    .ToDictionary(v => v.InstanceId, v => v.GetInstanceID());
                BarScenePrefabs.Apply();
                var after = Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None);
                foreach (var view in after) Assert.That(view.GetInstanceID(), Is.EqualTo(ids[view.InstanceId]));
                Assert.That(File.ReadAllText("Assets/Bar/Scenes/Bar.unity"), Is.EqualTo(before));
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
