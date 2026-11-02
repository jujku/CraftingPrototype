using System.IO;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>内腔形状按真实网格切片：口径与 glTF 实测一致、体积与离线烘焙一致、半径不比内壁宽、碰撞墙不侵入内腔。</summary>
    internal sealed class CavityShapeTests
    {
        private static ContainerVisual[] Bindings(string prefab)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/"+prefab+".prefab").GetComponentsInChildren<ContainerVisual>(true);
        // Mouth and base radii (cm) measured from the BarKit glTF cavities; the base of a tapered cavity is narrower than the mouth.
        [TestCase("glass.highball",3.48f,2.60f)]
        [TestCase("glass.coupe",4.58f,.10f)]
        [TestCase("shaker.cobbler",3.86f,2.69f)]
        public void ShapeFollowsTheRealCavity(string prefab,float mouthCm,float baseCm)
        {
            var binding=Bindings(prefab).Single();var shape=CavityShape.FromCavity(binding.InnerCavity,binding.transform);
            Assert.That(shape.FromMesh,Is.True,"内腔网格必须可读");
            Assert.That(shape.RadiusAt(shape.Top)*100,Is.EqualTo(mouthCm).Within(.15f),"口径");
            Assert.That(shape.RadiusAt(shape.Bottom)*100,Is.LessThanOrEqualTo(baseCm+.05f),"底部");
            Assert.That(shape.VolumeMl,Is.EqualTo(binding.Profile.CapacityMl).Within(binding.Profile.CapacityMl*.04),"切片体积与离线烘焙体积一致");
            AssertConservativeAndWallsOutside(binding,shape);
        }
        [Test] public void BothJiggerCupsOpenUpwardInTheirOwnFrame()
        {
            foreach(var binding in Bindings("jigger.dual"))
            {
                var shape=CavityShape.FromCavity(binding.InnerCavity,binding.transform);
                Assert.That(shape.RadiusAt(shape.Top),Is.GreaterThan(shape.RadiusAt(shape.Bottom)*2),binding.DefinitionId+" 锥形口朝上");
                Assert.That(shape.VolumeMl,Is.EqualTo(binding.Profile.CapacityMl).Within(binding.Profile.CapacityMl*.04),binding.DefinitionId);
                AssertConservativeAndWallsOutside(binding,shape);
            }
        }
        [Test] public void CapacityReportsHowManyWholeCubesFit()
        {
            float side=.037f;var report="";
            foreach(var prefab in new[]{"glass.highball","glass.coupe","shaker.cobbler","jigger.dual"})
                foreach(var binding in Bindings(prefab))
                {
                    var shape=CavityShape.FromCavity(binding.InnerCavity,binding.transform);int n=shape.CubeCapacity(side);
                    Assert.That(n,Is.GreaterThanOrEqualTo(1));report+=binding.DefinitionId+" "+n+" cubes, volume "+shape.VolumeMl.ToString("F1")+" ml, segments "+shape.Segments().Count+"\n";
                }
            var highball=Bindings("glass.highball").Single();
            Assert.That(CavityShape.FromCavity(highball.InnerCavity,highball.transform).CubeCapacity(side),Is.GreaterThanOrEqualTo(3),"highball 能竖着叠三块以上");
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"../VisualReports"));File.WriteAllText(Path.Combine(Application.dataPath,"../VisualReports/cavity-capacity.txt"),report);
        }
        private static void AssertConservativeAndWallsOutside(ContainerVisual binding,CavityShape shape)
        {
            // The radius never exceeds the wall: every side-wall vertex lies at or outside it.
            foreach(var v in binding.InnerCavity.sharedMesh.vertices)
            {
                var p=binding.transform.InverseTransformPoint(binding.InnerCavity.transform.TransformPoint(v));
                if(p.y<shape.Bottom+.001f||p.y>shape.Top-.001f)continue;
                Assert.That(new Vector2(p.x-shape.Axis.x,p.z-shape.Axis.y).magnitude,Is.GreaterThanOrEqualTo(shape.RadiusAt(p.y)-.0005f),binding.DefinitionId+" y="+p.y);
            }
            // Wall boards sit behind the cavity surface; joints may overlap by at most 1.5 mm.
            foreach(var wall in shape.Walls(16,.03f).Take(shape.Walls(16,.03f).Count-1))
                for(int i=0;i<4;i++)
                {
                    var p=wall.Center+wall.Rotation*new Vector3(((i&1)==0?-.5f:.5f)*wall.Size.x,((i&2)==0?-.5f:.5f)*wall.Size.y,-.5f*wall.Size.z);
                    if(p.y<shape.Bottom||p.y>shape.Top)continue;
                    Assert.That(new Vector2(p.x-shape.Axis.x,p.z-shape.Axis.y).magnitude,Is.GreaterThanOrEqualTo(shape.RadiusAt(p.y)-.0015f),binding.DefinitionId+" 墙板侵入内腔 y="+p.y);
                }
        }
    }
}
