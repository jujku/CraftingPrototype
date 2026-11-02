using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>检查实际导入的厚底/曲面杯腔及重新烘焙的液面，防止只改外观而液体仍落在旧杯底。</summary>
    public sealed class GlassProfileTests
    {
        [TestCase("glass.highball",.020f,.155f,.037f,false)]
        [TestCase("glass.coupe",.088f,.126f,.048f,true)]
        public void ImportedCavityLiquidAndOpticalThicknessAgree(string id,float floor,float height,float radius,bool workingFill)
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+id+".prefab");
            var visual=root.GetComponent<ContainerVisual>();var shape=CavityShape.FromCavity(visual.InnerCavity,visual.transform);
            var shell=root.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Shell");var bounds=CavityShape.BoundsIn(shell,root.transform);
            Assert.That(shape.Bottom,Is.EqualTo(floor).Within(.00003f));Assert.That(bounds.max.y,Is.EqualTo(height).Within(.00003f));
            Assert.That(bounds.extents.x,Is.InRange(radius-.00005f,radius+.003f),"杯肚允许在内收杯口外略微鼓出");
            float rim=shell.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(shell.transform.TransformPoint(v))).Where(v=>Mathf.Abs(v.y-(height-.0011f))<.00001f).Max(v=>new Vector2(v.x,v.z).magnitude);
            Assert.That(rim,Is.EqualTo(radius).Within(.00005f));Assert.That(visual.NominalCapacityIsWorkingFill,Is.EqualTo(workingFill));
            Assert.That(visual.Profile.Heights[0],Is.EqualTo(shape.Bottom).Within(.00001f));
            Assert.That(visual.Profile.CapacityMl,Is.EqualTo(shape.VolumeMl).Within(shape.VolumeMl*.015));
            foreach(double ml in new[]{.1,1,5,20,80,160})
                Assert.That(visual.Profile.HeightForMl(ml),Is.InRange(shape.Bottom+.000001f,shape.Top),id+" 液面不能进厚玻璃底座");
            var liquid=visual.LiquidVolume.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(liquid.vertices,Is.EqualTo(visual.InnerCavity.sharedMesh.vertices));Assert.That(liquid.triangles,Is.EqualTo(visual.InnerCavity.sharedMesh.triangles));
            Assert.That(shell.sharedMesh.colors.Length,Is.EqualTo(shell.sharedMesh.vertexCount));
            Assert.That(shell.sharedMesh.colors.Max(c=>c.r)*32,Is.GreaterThanOrEqualTo(id=="glass.highball"?19.9f:5.4f),"厚底光学元数据");
            Assert.That(shell.sharedMesh.colors.All(c=>c.a<.01f),Is.True,"仅新杯型启用专用光学");
            if(id=="glass.highball")Assert.That(shape.CubeCapacity(.037f),Is.EqualTo(4),"保留已有两铲四块冰流程");
            Assert.That(root.GetComponent<ItemView>().Size.y,Is.EqualTo(height).Within(.00001f));
            Assert.That(root.GetComponent<BoxCollider>().size.y,Is.EqualTo(height).Within(.00001f));
        }
    }
}
