using System.IO;
using System.Linq;
using Bar.Unity;
using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>重新计算授权杯腔并只读比对已保存的曲线，防止后续模型改动留下过时液面。</summary>
    public sealed class GlassProfileBake
    {
        [Test]
        public void BakedResourcesMatchAuthorizedCavities()
        {
            Assert.That(Application.dataPath.Replace('\\','/'),Does.Contain("/_validation/UnityProject/Assets"));
            foreach(string id in new[]{"glass.highball","glass.coupe"})
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab");
                var visual=root.GetComponent<ContainerVisual>();var baked=VolumeProfileBaker.Bake(visual.InnerCavity,visual.transform);
                var destination=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Bar/Art/VolumeProfiles/"+id+".asset");
                Assert.That(destination.Heights,Is.EqualTo(baked.Heights));
                Assert.That(destination.VolumesMl.Length,Is.EqualTo(baked.VolumesMl.Length));
                for(int i=0;i<baked.VolumesMl.Length;i++)Assert.That(destination.VolumesMl[i],Is.EqualTo(baked.VolumesMl[i]).Within(.000001),id);
                Object.DestroyImmediate(baked);
            }
        }
    }
}
