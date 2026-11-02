using System.Linq;
using Bar.Data;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bar.Tests
{
    internal sealed class VisualMaterialTests
    {
        private static void ColorNear(Color actual, Color expected) => Assert.That(Vector4.Distance(actual,expected),Is.LessThan(1e-6f));
        [Test] public void HighlightPreservesEachSlotAlphaBlocksLiquidAndLiveMaterialEdits()
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/jigger.dual.prefab"));
            var a = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var b = new Material(a);
            try
            {
                var model = JsonUtility.FromJson<ModelData>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Core/Data/model-v2.json").text);
                var catalog = ItemCatalog.Load(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Bar/Data/Json/items.json").text, model);
                var factory = new Bar.Domain.ItemFactory(catalog, id => 20, () => .5);
                var view = go.GetComponent<ItemView>();
                view.Bind(factory.Create("jigger.dual","test:jigger"),model.icePortionGrams,model.iceDensity);
                var renderer = go.GetComponentsInChildren<MeshRenderer>().First(r => r.enabled && r.name != "LiquidVolume" && r.name != "InnerCavity");
                var ca = new Color(.2f,.6f,.3f,.4f);
                var cb = new Color(.8f,.2f,.1f,.7f);
                var overrideColor = new Color(.1f,.3f,.8f,.5f);
                a.SetColor("_BaseColor",ca); b.SetColor("_BaseColor",cb);
                renderer.sharedMaterials = new[]{a,b};
                var global = new MaterialPropertyBlock(); global.SetFloat("_FillHeight",.123f); renderer.SetPropertyBlock(global);
                var second = new MaterialPropertyBlock(); second.SetFloat("_FillHeight",.456f); second.SetColor("_BaseColor",overrideColor); renderer.SetPropertyBlock(second,1);
                var liquid = go.GetComponentInChildren<ContainerVisual>().LiquidVolume;
                var liquidBlock = new MaterialPropertyBlock(); liquidBlock.SetFloat("_FillHeight",.789f); liquid.SetPropertyBlock(liquidBlock);
                view.Highlight(true);
                var read = new MaterialPropertyBlock(); renderer.GetPropertyBlock(read,0);
                ColorNear(read.GetColor("_BaseColor"),Color.Lerp(ca,new Color(1,.72f,.2f,ca.a),.18f));
                Assert.That(read.GetFloat("_FillHeight"),Is.EqualTo(.123f));
                renderer.GetPropertyBlock(read,1);
                ColorNear(read.GetColor("_BaseColor"),Color.Lerp(overrideColor,new Color(1,.72f,.2f,overrideColor.a),.18f));
                Assert.That(read.GetFloat("_FillHeight"),Is.EqualTo(.456f));
                view.Highlight(false);
                renderer.GetPropertyBlock(read,0); Assert.That(read.isEmpty,Is.True);
                renderer.GetPropertyBlock(read,1); ColorNear(read.GetColor("_BaseColor"),overrideColor);
                Assert.That(read.GetFloat("_FillHeight"),Is.EqualTo(.456f));
                liquid.GetPropertyBlock(read); Assert.That(read.GetFloat("_FillHeight"),Is.EqualTo(.789f));
                Assert.That(read.HasProperty(Shader.PropertyToID("_BaseColor")),Is.False);
                a.SetColor("_BaseColor",cb);
                view.Highlight(false); view.Highlight(true);
                renderer.GetPropertyBlock(read,0);
                ColorNear(read.GetColor("_BaseColor"),Color.Lerp(cb,new Color(1,.72f,.2f,cb.a),.18f));
                view.Highlight(false);
                ColorNear(a.GetColor("_BaseColor"),cb);
                ColorNear(b.GetColor("_BaseColor"),cb);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
        [Test] public void SavedSceneProvidesNonBlackNeutralReflectionForMetalMaterials()
        {
            EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
            try
            {
                Assert.That(RenderSettings.defaultReflectionMode,Is.EqualTo(DefaultReflectionMode.Custom));
                var cube = RenderSettings.customReflectionTexture as Cubemap;
                Assert.That(cube,Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(cube),Is.EqualTo("Assets/Bar/Rendering/BarStudioReflection.cubemap"));
                Assert.That(cube.mipmapCount,Is.GreaterThan(1));
                Assert.That(cube.GetPixel(CubemapFace.PositiveY,32,32).grayscale,Is.GreaterThan(.4f));
                Assert.That(cube.GetPixel(CubemapFace.PositiveY,32,32).grayscale,Is.GreaterThan(cube.GetPixel(CubemapFace.NegativeY,32,32).grayscale+ .2f));
                var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Art/BarKitMaterials/Polished stainless edges.mat");
                foreach (string id in new[]{"jigger.dual","shaker.strainer","shaker.cap"})
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab");
                    var polished = prefab.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                        .Where(m => AssetDatabase.GetAssetPath(m) == "Assets/Bar/Rendering/PolishedStainless.mat").ToArray();
                    Assert.That(polished,Is.Not.Empty,id);
                    foreach (var material in polished)
                    {
                        Assert.That(material.GetFloat("_Metallic"),Is.GreaterThan(.8f));
                        Assert.That(material.GetFloat("_Smoothness"),Is.GreaterThan(.8f));
                        ColorNear(material.GetColor("_BaseColor"),source.GetColor("_BaseColor"));
                    }
                }
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); }
        }
    }
}
