using Bar.Logging;
using NUnit.Framework;
namespace Bar.Tests
{
    public sealed class BAudioSamplingTests
    {
        [Test] public void IndividualSoundRecordsDoNotRetriggerFullCueSetsWithinOneTick()
        {
            var s=new CueSampler{SampleSoundsOnTick=true};
            Assert.That(s.Sample(0,new[]{new CueFrame{vesselId="v"}}),Is.True);
            for(int i=1;i<10;i++)Assert.That(s.Sample(i*.01,new[]{new CueFrame{vesselId="v",soundEmitted=i%2==1,soundPlaying=true,iceSound=i*.1}}),Is.False);
            Assert.That(s.Sample(.1,new[]{new CueFrame{vesselId="v",soundEmitted=true,soundPlaying=true,iceSound=.8}}),Is.True);
            Assert.That(s.Sample(.11,new[]{new CueFrame{vesselId="v",onScreen=true}}),Is.True,"视觉切换仍立即采样");
        }
    }
}
