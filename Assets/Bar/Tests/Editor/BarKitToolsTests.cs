using System.IO;
using System.Linq;
using Bar.Unity;
using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>保护新包接入、几何计量和吧台开口的验收约束。</summary>
    internal sealed class BarKitToolsTests
    {
        [Test] public void PlacementChecksTheWholeFootprintAroundBothOpenings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/fixture.counter.prefab");
            var go = Object.Instantiate(prefab);
            try
            {
                var surface = go.GetComponent<PlacementSurface>();
                Assert.That(surface, Is.Not.Null);
                Assert.That(surface.CanPlace(new Vector3(0,.95f,0), new Vector3(.1f,.2f,.1f)), Is.True);
                foreach (var opening in surface.Excluded)
                {
                    var centre = new Vector3(opening.center.x,.95f,opening.center.y);
                    Assert.That(surface.CanPlace(centre, Vector3.one * .05f), Is.False);
                    centre.x = opening.xMin - .01f;
                    Assert.That(surface.CanPlace(centre, Vector3.one * .05f), Is.False, "中心在开口之外但物品边缘仍与开口相交");
                }
                Assert.That(surface.CanPlace(new Vector3(1.23f,.95f,.2f), Vector3.one * .1f), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }
        /// <summary>开口禁区、设施位置和吧台网格上的真实孔洞必须一致；Unity 导入 FBX 后 X 与 glTF 相反。</summary>
        [Test] public void FixturesSitInTheRealCounterHolesAndTheFaucetStandsOnTheDeck()
        {
            foreach (string scenePath in new[]{"Assets/Bar/Scenes/Bar.unity","Assets/Bar/Scenes/BarPresentation.unity"})
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
                var views = Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None);
                var counter = views.First(v => v.DefinitionId == "fixture.counter");
                var surface = counter.GetComponent<PlacementSurface>();
                var top = counter.transform.Find("Visual/WalnutTop").GetComponent<MeshFilter>();
                var corners = top.sharedMesh.vertices.Select(v => counter.transform.InverseTransformPoint(top.transform.TransformPoint(v))).ToArray();
                float topY = corners.Max(v => v.y);
                var holeCorners = corners.Where(v => Mathf.Abs(v.y - topY) < .0005f).Select(v => new Vector2(v.x, v.z)).ToArray();
                foreach (var opening in surface.Excluded)
                {
                    foreach (var corner in new[]{opening.min, opening.max, new Vector2(opening.xMin, opening.yMax), new Vector2(opening.xMax, opening.yMin)})
                        Assert.That(holeCorners.Min(c => Vector2.Distance(c, corner)), Is.LessThan(.002f), scenePath + " 禁区角点必须落在吧台孔洞角点上 " + corner);
                }
                foreach (var (id, width) in new[]{("fixture.sink", .392f), ("fixture.icebin", .305f)})
                {
                    var fixture = views.First(v => v.DefinitionId == id);
                    Vector3 mouth = counter.transform.InverseTransformPoint(fixture.Anchor("Surface").position);
                    var hole = surface.Excluded.Single(r => r.Contains(new Vector2(mouth.x, mouth.z)));
                    Assert.That(hole.width, Is.EqualTo(width).Within(.002f), scenePath + " " + id + " 必须放进与自身尺寸相符的开口");
                    Assert.That(Mathf.Abs(mouth.x - hole.center.x), Is.LessThan(.005f), scenePath + " " + id);
                    Assert.That(Mathf.Abs(mouth.z - hole.center.y), Is.LessThan(.005f), scenePath + " " + id);
                }
                var sink = views.First(v => v.DefinitionId == "fixture.sink");
                var faucetBase = sink.transform.Find("Visual/FaucetBase").GetComponent<MeshRenderer>().bounds;
                Vector3 low = counter.transform.InverseTransformPoint(faucetBase.min);
                Vector3 high = counter.transform.InverseTransformPoint(faucetBase.max);
                var footprint = Rect.MinMaxRect(Mathf.Min(low.x, high.x), Mathf.Min(low.z, high.z), Mathf.Max(low.x, high.x), Mathf.Max(low.z, high.z));
                Assert.That(surface.Excluded.Any(r => r.Overlaps(footprint)), Is.False, scenePath + " 水龙头底座必须落在台面上，不能悬在开口里");
            }
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        }
        [Test] public void CounterFootRailFacesCustomersAndHandlesFaceTheBartender()
        {
            try
            {
                foreach(string path in new[]{"Assets/Bar/Scenes/Bar.unity","Assets/Bar/Scenes/BarPresentation.unity"})
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                    var counter=Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None).Single(v=>v.DefinitionId=="fixture.counter");
                    var player=Object.FindFirstObjectByType<PlayerController>();
                    var bartender=Vector3.ProjectOnPlane(player.transform.position-counter.transform.position,Vector3.up).normalized;
                    var rail=counter.transform.Find("Visual/BrassFootRail").GetComponent<Renderer>().bounds.center;
                    var handle=counter.transform.Find("Visual/Handle0").GetComponent<Renderer>().bounds.center;
                    Assert.That(Vector3.Dot(rail-counter.transform.position,bartender),Is.LessThan(-.1f),path+" 金色脚踏杆应在顾客侧");
                    Assert.That(Vector3.Dot(handle-counter.transform.position,bartender),Is.GreaterThan(.1f),path+" 柜门把手应在调酒师侧");
                }
            }
            finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        }
        [Test] public void AllModelsAreReadableWithExactAnchorNamesAndDisabledHelpers()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[]{"Assets/Bar/Art"});
            int count = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx")) continue;
                count++;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(t.name, Does.Not.Contain("::"));
                    if (t.name != "InnerCavity" && t.name != "LiquidVolume") continue;
                    Assert.That(t.GetComponent<MeshFilter>().sharedMesh.isReadable, Is.True);
                    Assert.That(t.GetComponent<Renderer>().enabled, Is.False);
                }
            }
            Assert.That(count, Is.EqualTo(21), "18 existing bar models plus the three approved shaking-ice variants");
        }
        [Test] public void FormalPrefabsAreTheOnlyItemPrefabs()
        {
            Assert.That(Directory.Exists("Assets/Bar/Prefabs/Graybox"), Is.False);
            var paths = AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Bar/Prefabs"})
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Assert.That(paths.Length, Is.EqualTo(20));
            foreach (string path in paths)
                Assert.That(path, Does.StartWith("Assets/Bar/Prefabs/Resources/BarPrefabs/"));
        }
    }
}
