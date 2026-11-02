using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Bar.Unity.Editor
{
    /// <summary>只在显式运行时检查并组装 BarKit，导入后不覆盖用户调整。</summary>
    public static class BarKitAssembly
    {
        internal static Transform Node(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }
        internal static Bounds VisibleBounds(Transform root)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r.name == "InnerCavity" || r.name == "LiquidVolume") continue;
                var b = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var v = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1,
                        (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    v = root.InverseTransformPoint(r.transform.TransformPoint(v));
                    if (first) bounds = new Bounds(v, Vector3.zero);
                    else bounds.Encapsulate(v);
                    first = false;
                }
            }
            return bounds;
        }
        private static Bounds WorldBounds(Transform node)
        {
            var root = new GameObject("BoundsRoot");
            var old = node.parent;
            node.SetParent(root.transform, true);
            var bounds = VisibleBounds(root.transform);
            node.SetParent(old, true);
            UnityEngine.Object.DestroyImmediate(root);
            return bounds;
        }
        public static void Inspect()
        {
            Directory.CreateDirectory("BarKitReports");
            var lines = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[]{"Assets/Bar/Art"}))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx")) continue;
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var wrapper = new GameObject("MetricRoot");
                go.transform.SetParent(wrapper.transform, true);
                var b = VisibleBounds(wrapper.transform);
                lines.Add(go.name.Replace("(Clone)", "") + "|" + b.size.ToString("F7") + "|" + b.center.ToString("F7"));
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "ShakerBody" || t.name == "ShakerStrainer" || t.name == "ShakerCap" ||
                        t.name == "CupA" || t.name == "CupB" || t.name == "WalnutTop")
                        lines.Add("  " + t.name + "|" + VisibleBounds(t).size.ToString("F7") + " worldBounds=" + WorldBounds(t).ToString("F7") + "|position=" + t.position);
                }
                File.WriteAllLines("BarKitReports/"+go.name.Replace("(Clone)","")+"-nodes.txt",
                    go.GetComponentsInChildren<Transform>(true).Select(t => t.name + " pos="+t.localPosition+" scale="+t.localScale+" rot="+t.localEulerAngles));
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            File.WriteAllLines("BarKitReports/dimensions.txt", lines);
        }
        private const string Prefabs = "Assets/Bar/Prefabs/Resources/BarPrefabs/";
        private const string Materials = "Assets/Bar/Art/BarKitMaterials/";
        private static readonly Dictionary<string,string> Models = new Dictionary<string,string>
        {
            {"bottle.gin","GinBottle"}, {"bottle.syrup","SyrupBottle"},
            {"bottle.soda","SodaBottle"}, {"bottle.tonic","TonicBottle"},
            {"shaker.cobbler","CobblerShaker"}, {"shaker.strainer","CobblerShaker"},
            {"shaker.cap","CobblerShaker"}, {"jigger.dual","Jigger"},
            {"glass.coupe","Coupe"}, {"glass.highball","Highball"},
            {"tool.spoon","BarSpoon"}, {"tool.scoop","IceScoop"}, {"tool.tongs","IceTongs"},
            {"tool.squeezer","Squeezer"}, {"produce.lemon_half","LemonHalf"},
            {"fixture.icebin","IceBin"}, {"fixture.sink","Sink"},
            {"fixture.servemat","ServeMat"}, {"fixture.board","CuttingBoard"}, {"fixture.counter","BarCounter"}
        };
        private static Transform Anchor(Transform root, string name, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(root, false);
            node.localPosition = position;
            return node;
        }
        private static Material Material(string name, Color color, float metal = 0, bool transparent = false)
        {
            string path = Materials + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Smoothness", metal > 0 ? .8f : .45f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static void RemapMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "InnerCavity" || r.name == "LiquidVolume") continue;
                r.sharedMaterials = r.sharedMaterials.Select(original =>
                {
                    string name = original ? original.name : "Steel";
                    string lower = name.ToLowerInvariant();
                    bool glass = lower.Contains("glass");
                    Color color = original && original.HasProperty("_Color") ? original.color : new Color(.65f,.68f,.7f);
                    if (lower.Contains("steel") || lower.Contains("metal")) color = new Color(.55f,.59f,.62f);
                    if (glass) color = lower.Contains("blue") ? new Color(.1f,.5f,.85f,.26f) : new Color(.82f,.94f,.95f,.18f);
                    return Material(name, color, lower.Contains("steel") || lower.Contains("metal") || lower.Contains("brass") ? .75f : 0, glass);
                }).ToArray();
            }
        }
        private static void ContainerBinding(Transform root, string definitionId, Material liquid)
        {
            var binding = root.gameObject.AddComponent<ContainerVisual>();
            binding.DefinitionId = definitionId;
            binding.InnerCavity = Node(root, "InnerCavity").GetComponent<MeshFilter>();
            binding.LiquidVolume = Node(root, "LiquidVolume").GetComponent<MeshRenderer>();
            binding.LiquidVolume.sharedMaterial = liquid;
            binding.LiquidVolume.enabled = false;
            var profile = VolumeProfileBaker.Bake(binding.InnerCavity, root);
            var marks = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Mark_") && t.name.EndsWith("ml")).ToArray();
            foreach (var mark in marks)
            {
                string name = mark.name;
                double ml = double.Parse(name.Substring(5, name.Length - 7), System.Globalization.CultureInfo.InvariantCulture);
                mark.name = "Geometry_" + name;
                var point = Anchor(root, name, Vector3.up * profile.HeightForMl(ml));
                mark.SetParent(point, true);
            }
            var nominal = Node(root, "NominalFillLevel");
            if (nominal) UnityEngine.Object.DestroyImmediate(nominal.gameObject);
            UnityEngine.Object.DestroyImmediate(profile);
        }
        /// <summary>显式一次性替换灰盒，并验证所有最终预制体；不在导入回调或运行时重建。</summary>
        public static void Install()
        {
            if (File.Exists("Assets/Bar/Prefabs/BarKit-installed.txt"))
                throw new InvalidOperationException("BarKit 已安装。为保护用户调整，拒绝再次自动组装。");
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var model = JsonUtility.FromJson<CraftingResearch.CoreV2.ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text);
            var catalog = Bar.Data.ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text, model);
            var liquid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Art/VolumeProfiles/Liquid.mat");
            var report = new List<string>();
            foreach (var pair in Models)
            {
                var definition = catalog[pair.Key];
                var root = new GameObject(pair.Key);
                var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Art/"+pair.Value+"/"+pair.Value+".fbx"));
                source.name = "Visual";
                source.transform.SetParent(root.transform, true);
                if (pair.Key.StartsWith("shaker."))
                {
                    string part = pair.Key == "shaker.cobbler" ? "ShakerBody" : pair.Key == "shaker.strainer" ? "ShakerStrainer" : "ShakerCap";
                    var selected = Node(source.transform, part);
                    selected.SetParent(root.transform, true);
                    UnityEngine.Object.DestroyImmediate(source);
                    source = selected.gameObject;
                    source.name = pair.Key == "shaker.cobbler" ? "BodyMesh" : "Visual";
                    var bounds = VisibleBounds(root.transform);
                    source.transform.position -= Vector3.up * bounds.min.y;
                    if (pair.Key == "shaker.cobbler")
                    {
                        var body = Anchor(root.transform, "Body", Vector3.zero);
                        source.transform.SetParent(body, true);
                        ContainerBinding(body, "shaker.body", liquid);
                        // Independent strainer's origin is its bottom, 10 mm below its insertion seat.
                        var slot = Node(root.transform, "Slot_Strainer");
                        slot.position -= Vector3.up * .01f;
                    }
                }
                if (pair.Key == "jigger.dual")
                {
                    var orientation = Anchor(root.transform, "Orientation", Vector3.up * .07f);
                    source.transform.SetParent(orientation, true);
                    foreach (string name in new[]{"CupA", "CupB"})
                    {
                        var cup = Node(source.transform, name);
                        var bindingRoot = Anchor(orientation, name, Vector3.up * (name == "CupA" ? .001f : -.001f));
                        if (name == "CupB") bindingRoot.localRotation = Quaternion.Euler(180,0,0);
                        cup.SetParent(bindingRoot, true);
                        cup.name = "ImportedCup";
                        ContainerBinding(bindingRoot, name == "CupA" ? "jigger.cup45" : "jigger.cup30", liquid);
                    }
                }
                if (pair.Key.StartsWith("glass.")) ContainerBinding(root.transform, pair.Key, liquid);
                foreach (string name in definition.Anchors)
                {
                    var existing = Node(root.transform, name);
                    if (name.StartsWith("Grip_"))
                    {
                        Vector3 pos = existing ? root.transform.InverseTransformPoint(existing.position) : Vector3.up * (float)definition.Size.HeightCm * .005f;
                        if (existing) existing.name = "Imported_" + name;
                        Anchor(root.transform, name, pos);
                    }
                    else if (!existing) Anchor(root.transform, name, Vector3.up * (float)definition.Size.HeightCm * .01f);
                }
                if (pair.Key == "shaker.cobbler")
                {
                    // Distinct upper and lower grips for a sealed shaker held with both hands.
                    root.transform.Find("Grip_L").localPosition = new Vector3(0,.195f,.012f);
                    root.transform.Find("Grip_R").localPosition = new Vector3(0,.07f,.012f);
                    Anchor(root.transform.Find("Body"), "Grip_L", new Vector3(0,.08f,.012f));
                    Anchor(root.transform.Find("Body"), "Grip_R", new Vector3(0,.08f,.012f));
                }
                var view = root.AddComponent<ItemView>();
                view.DefinitionId = pair.Key;
                view.InstanceId = "prefab:" + pair.Key;
                var visible = VisibleBounds(root.transform);
                view.Size = visible.size;
                var collider = root.AddComponent<BoxCollider>();
                collider.center = visible.center;
                collider.size = visible.size;
                if (pair.Key == "fixture.counter")
                {
                    var placement = root.AddComponent<PlacementSurface>();
                    placement.LocalBounds = new Rect(-1.24f,-.432f,2.48f,.8f);
                    placement.Excluded = new[]{new Rect(.7875f,-.2845f,.305f,.305f),new Rect(-1.156f,-.283f,.392f,.322f)}; // Unity mesh space: X is mirrored from glTF.
                }
                if (pair.Key == "tool.squeezer")
                {
                    var rig = root.AddComponent<SqueezerViewRig>();
                    rig.Mode = SqueezerViewRig.FruitMode.Lemon;
                    rig.OpenAmount = 1;
                }
                RemapMaterials(root);
                VolumeProfileBaker.BakeAndSave(root, "Assets/Bar/Art/VolumeProfiles");
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + definition.PrefabKey + ".prefab");
                report.Add(pair.Key);
                report.AddRange(PrefabValidator.Validate(root));
                UnityEngine.Object.DestroyImmediate(root);
            }
            FinalizeMarkings();
            NormalizeSlots();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
            foreach (var view in UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None))
            {
                if (view.DefinitionId == "fixture.counter") view.transform.position = new Vector3(0,0,.65f);
            }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            BarKitSceneFinish.Apply();
            BarScenePrefabs.Apply();
            Directory.CreateDirectory("BarKitReports");
            File.WriteAllLines("BarKitReports/prefab-validation.txt", report);
            AssetDatabase.SaveAssets();
            if (report.Any(line => line.StartsWith("失败"))) throw new InvalidOperationException("BarKit 预制体验证失败，见报告");
            File.WriteAllText("Assets/Bar/Prefabs/BarKit-installed.txt", "2026-10-23 显式安装完成。后续 Art 和 Prefabs 由用户调整；自动组装拒绝覆盖。\n");
            AssetDatabase.Refresh();
        }
        /// <summary>把项目可见刻线组件绑定到新量酒器的实际内腔截面。</summary>
        private static void FinalizeMarkings()
        {
            string path = Prefabs + "jigger.dual.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var visual in root.GetComponentsInChildren<ContainerVisual>(true))
                {
                    foreach (var point in visual.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Mark_")).ToArray())
                    {
                        float height = point.localPosition.y;
                        float radius = 0;
                        var mesh = visual.InnerCavity.sharedMesh;
                        var vertices = mesh.vertices.Select(v => visual.transform.InverseTransformPoint(visual.InnerCavity.transform.TransformPoint(v))).ToArray();
                        var indices = mesh.triangles;
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            for (int k = 0; k < 3; k++)
                            {
                                var a = vertices[indices[i + k]];
                                var b = vertices[indices[i + (k + 1) % 3]];
                                if ((a.y < height) == (b.y < height)) continue;
                                var v = Vector3.Lerp(a, b, (height - a.y) / (b.y - a.y));
                                radius = Mathf.Max(radius, new Vector2(v.x, v.z).magnitude);
                            }
                        }
                        var ring = point.GetComponent<LineRenderer>();
                        if (!ring) ring = point.gameObject.AddComponent<LineRenderer>();
                        ring.useWorldSpace = false;
                        ring.loop = true;
                        ring.positionCount = 64;
                        ring.widthMultiplier = .0002f;
                        ring.sharedMaterial = Material("MarkInk", new Color(.15f,.12f,.05f));
                        for (int i = 0; i < 64; i++)
                        {
                            float angle = i * Mathf.PI * 2 / 64;
                            ring.SetPosition(i, new Vector3(Mathf.Cos(angle)*radius, 0, Mathf.Sin(angle)*radius));
                        }
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
        /// <summary>在预制体层统一安装锚点的米制朝向，不改变 FBX 或可见几何。</summary>
        private static void NormalizeSlots()
        {
            foreach (string id in new[]{"shaker.cobbler", "shaker.strainer", "tool.squeezer"})
            {
                string path = Prefabs + id + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var anchor in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (anchor.name.StartsWith("Slot_") || anchor.name.StartsWith("LoadSlot"))
                            anchor.rotation = root.transform.rotation;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
