using NUnit.Framework;
using UnityEngine;
namespace Bar.Unity.Tests
{
    public sealed class BartenderLocomotionTests
    {
        [TestCase(30)][TestCase(60)][TestCase(144)]public void ZoneBlendPreservesVelocityAndCompletes(int fps)
        {
            var p=new BartenderLocomotionSettings();var m=new BartenderLocomotionModel(p);float dt=1f/fps;
            for(int i=0;i<fps;i++)m.Advance(Vector2.up,Quaternion.identity,0,false,dt);
            Assert.That(m.Velocity.z,Is.EqualTo(p.workSpeed).Within(.025f));float previous=m.Velocity.z;
            for(int i=0;i<fps;i++){var v=m.Advance(Vector2.up,Quaternion.identity,1,false,dt);Assert.That(float.IsFinite(v.z));Assert.That(Mathf.Abs(v.z-previous),Is.LessThan(.20f));previous=v.z;}
            Assert.That(m.LoungeBlend,Is.EqualTo(1));Assert.That(m.Velocity.z,Is.EqualTo(p.loungeSpeed).Within(.03f));
        }
        [Test]public void CarryUsesHysteresisAndStrictestScale()
        {
            var p=new BartenderLocomotionSettings();var m=new BartenderLocomotionModel(p);m.SetCareful(.64f);Assert.That(m.Careful,Is.False);m.SetCareful(.7f);Assert.That(m.SpeedScale(false),Is.EqualTo(.7f));m.SetCareful(.62f);Assert.That(m.Careful);Assert.That(m.SpeedScale(true),Is.EqualTo(.2f));m.SetCareful(0);Assert.That(m.Careful,Is.False);
        }
        [Test]public void DiagonalDoesNotOutrunForward()
        {
            var m=new BartenderLocomotionModel(new BartenderLocomotionSettings());for(int i=0;i<120;i++)m.Advance(Vector2.one,Quaternion.identity,1,false,1f/60);Assert.That(m.Velocity.magnitude,Is.LessThanOrEqualTo(1.851f));
        }
        [Test]public void AllZonesUseLoungeSpeedAndNoInputDoesNotCreateMotion()
        {
            var p=new BartenderLocomotionSettings();
            foreach(var input in new[]{Vector2.up,Vector2.right,Vector2.down})
            {
                var work=new BartenderLocomotionModel(p);var lounge=new BartenderLocomotionModel(p);
                for(int i=0;i<120;i++){work.Advance(input,Quaternion.identity,0,false,1f/60);lounge.Advance(input,Quaternion.identity,1,false,1f/60);}
                Assert.That(Vector3.Distance(work.Velocity,lounge.Velocity),Is.LessThan(.0001f));
                for(int i=0;i<180;i++)work.Advance(Vector2.zero,Quaternion.identity,0,false,1f/60);
                Assert.That(work.Velocity.magnitude,Is.LessThan(.00001f));
            }
        }
        [Test]public void StridesComeFromDistanceAndSmallStopIsOneShuffle()
        {
            var p=new BartenderLocomotionSettings();var stride=new BartenderStride();int steps=0,shuffles=0;
            for(int i=0;i<10;i++){stride.Advance(.01f,true,true,.6f,.5f,1f/60,p);steps+=stride.Steps;}
            for(int i=0;i<20;i++){stride.Advance(0,false,true,0,.5f,1f/60,p);shuffles+=stride.Shuffle?1:0;}Assert.That(steps,Is.Zero);Assert.That(shuffles,Is.EqualTo(1));
            for(int i=0;i<60;i++){stride.Advance(.01f,true,true,.6f,.5f,1f/60,p);steps+=stride.Steps;}Assert.That(steps,Is.EqualTo(1));
        }
        [Test]public void BlockedMovementDoesNotMakeFootsteps()
        {var p=new BartenderLocomotionSettings();var s=new BartenderStride();for(int i=0;i<120;i++){s.Advance(0,true,true,0,.5f,1f/60,p);Assert.That(s.Steps,Is.Zero);Assert.That(s.Shuffle,Is.False);}}
        [Test]public void StationLeashIgnoresHeightAndHonoursHorizontalDistance()
        {Assert.That(BartenderLocomotionModel.LeftStation(Vector3.zero,new Vector3(.3f,8,.1f),.45f),Is.False);Assert.That(BartenderLocomotionModel.LeftStation(Vector3.zero,new Vector3(.46f,0,0),.45f));}
        [Test]public void SpringRetuneDoesNotResetValue()
        {var s=new SecondOrderVector(6,1,0,Vector3.zero);for(int i=0;i<10;i++)s.Update(1f/60,Vector3.right);var before=s.Value;s.Reconfigure(4.5f,1.15f,0);Assert.That(s.Value,Is.EqualTo(before));}
        [Test]public void GroundMaterialIsIndependentOfMovementZone()
        {var go=new GameObject();var layout=go.AddComponent<BartenderSpatialLayout>();layout.Frame=go.transform;layout.RubberMat=new Bounds(Vector3.zero,new Vector3(1,.1f,.6f));layout.WorkCorridor=new Bounds(Vector3.zero,new Vector3(3,2,2));Assert.That(layout.OnRubber(Vector3.zero));Assert.That(layout.OnRubber(Vector3.right),Is.False);Assert.That(layout.LoungeWeight(Vector3.right),Is.Zero);Object.DestroyImmediate(go);}
    }
}
