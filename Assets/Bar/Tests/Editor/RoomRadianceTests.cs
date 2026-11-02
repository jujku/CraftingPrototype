using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>房间间接光的能量与方向契约，不依赖场景审美阈值。</summary>
    public sealed class RoomRadianceTests
    {
        static Cubemap Cube(Color[] faces)
        {
            var cube=new Cubemap(128,TextureFormat.RGBAFloat,true);
            for(int i=0;i<6;i++){var pixels=new Color[128*128];for(int p=0;p<pixels.Length;p++)pixels[p]=faces[i];cube.SetPixels(pixels,(CubemapFace)i);}
            cube.Apply(true,false);return cube;
        }
        [Test]public void UniformInteriorRadiancePreservesDiffuseEnergyInEveryDirection()
        {
            var colour=new Color(.4f,.2f,.1f,1);var cube=Cube(new[]{colour,colour,colour,colour,colour,colour});
            foreach(var sample in GinzaRoomLightingBake.Integrate(cube)){Assert.That(sample.r,Is.EqualTo(.4f).Within(.002f));Assert.That(sample.g,Is.EqualTo(.2f).Within(.002f));Assert.That(sample.b,Is.EqualTo(.1f).Within(.002f));}
            Object.DestroyImmediate(cube);
        }
        [Test]public void CeilingAndFloorRadianceStayInTheirOwnHemispheres()
        {
            var cube=Cube(new[]{Color.black,Color.black,Color.red,Color.blue,Color.black,Color.black});var sample=GinzaRoomLightingBake.Integrate(cube);
            Assert.That(sample[2].r,Is.GreaterThan(.5f));Assert.That(sample[2].b,Is.Zero);Assert.That(sample[3].b,Is.GreaterThan(.5f));Assert.That(sample[3].r,Is.Zero);
            foreach(int i in new[]{0,1,4,5})Assert.That(sample[i].r,Is.EqualTo(sample[i].b).Within(.0001f));Object.DestroyImmediate(cube);
        }
    }
}
