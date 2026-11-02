using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    public sealed class IceDisplacementTests
    {
        [TestCase(0,0,0)] [TestCase(90,0,0)] [TestCase(37,29,16)]
        public void RotatedBoxHasCorrectHalfVolumeAndEndpoints(float x,float y,float z)
        {
            var slice=new IceVolumeSlice(new Vector3(0,.05f,0),Quaternion.Euler(x,y,z),Vector3.zero,Vector3.one*.04f,64);
            Assert.That(slice.FractionBelow(.05f),Is.EqualTo(.5).Within(1e-5));Assert.That(slice.FractionBelow(0),Is.Zero);Assert.That(slice.FractionBelow(.1f),Is.EqualTo(1));
        }
        [Test] public void OneMlAroundGroundedIceIsNotSixtyFiveMlOfWater()
        {
            var p=ScriptableObject.CreateInstance<VolumeProfile>();p.Heights=new[]{0f,.1f};p.VolumesMl=new[]{0d,400d};
            var ice=new[]{new IceVolumeSlice(new Vector3(0,.02f,0),Quaternion.identity,Vector3.zero,Vector3.one*.04f,64)};
            float h=IceDisplacement.Height(p,1,ice,1);
            Assert.That(h,Is.EqualTo(1f/2400).Within(1e-7));Assert.That(p.VolumeBelowHeight(h)-IceDisplacement.SubmergedMl(h,ice,1),Is.EqualTo(1).Within(.001));
            Assert.That(IceDisplacement.Height(p,0,ice,1),Is.Zero);Assert.That(IceDisplacement.Height(p,100,new IceVolumeSlice[0],0),Is.EqualTo(p.HeightForMl(100)));Object.DestroyImmediate(p);
        }
    }
}
