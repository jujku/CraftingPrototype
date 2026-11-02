using System;
using System.Collections;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    public sealed partial class InspectArtDirectionTests
    {
        [UnityTest]public IEnumerator SpinMicroFoleyFollowsAngleCheckpointsAndStopsOnInterrupt()
        {
            foreach(var id in new[]{"tool.spoon","jigger.dual"})
            {
                yield return Load();var v=View(id);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();
                var settings=HoldViewSettings.Load();var p=settings.inspectAll.For(id);var cues=v.Item is Jigger?settings.inspect.sounds:p.sounds;
                Assert.That(cues.Length,Is.LessThanOrEqualTo(31));Assert.That(cues.Any(c=>c.bank=="inspectSwish"),Is.False);
                // Invert the real animation curves independently at 30 / 60 / 144 fps.
                foreach(var cue in cues)
                {
                    float t=InspectionContactTiming.Time(cue,settings.inspect,p),u=0,phase=0;
                    if(cue.phase=="spoon.roll"){u=t/p.lookSeconds;phase=(u-Mathf.Sin(2*Mathf.PI*u)/(2*Mathf.PI))*p.lookRollDegrees;}
                    if(cue.phase=="spoon.whip"){u=(t-p.lookSeconds-p.windupSeconds)/p.airSeconds;u=u/(p.spinBias+(1-p.spinBias)*u);phase=u*u*u*(10+u*(-15+6*u))*p.turnDegrees;}
                    if(cue.phase=="jigger.roll"){u=(t-settings.inspect.lookEnd)/(settings.inspect.twirlEnd-settings.inspect.lookEnd);phase=(u-Mathf.Sin(4*Mathf.PI*u)/(4*Mathf.PI))*settings.inspect.twirlDegrees;}
                    if(cue.phase.EndsWith("roll")||cue.phase.EndsWith("whip"))Assert.That(phase,Is.EqualTo(cue.degrees).Within(.002f));
                    foreach(int fps in new[]{30,60,144})Assert.That(Math.Ceiling(t*fps)/fps-t,Is.InRange(-.00001,1.0/fps+.00001));
                }
                Start();string hash=Bar.Logging.LogReplay.SnapshotHash(s.Adapter.World);
                float end=v.Item is Jigger?2.88f:1.98f;
                while(rig.InspectionElapsed(Side(v))<end){Step(Mathf.Min(Time.unscaledDeltaTime,.04f));yield return null;}
                var sounds=records.Where(r=>r.kind=="presentation_sound"&&r.sound.category=="inspect"&&r.sound.started).ToArray();
                Assert.That(sounds.Count(r=>r.sound.bank=="microKnuckle"),Is.EqualTo(v.Item is Jigger?0:3));
                Assert.That(sounds.Count(r=>r.sound.bank=="microCone"),Is.EqualTo(v.Item is Jigger?3:0));
                Assert.That(sounds.Any(r=>r.sound.bank==(v.Item is Jigger?"microPalm":"microSpoonStop")),Is.True);
                Assert.That(sounds.All(r=>r.sound.bank.StartsWith("micro")),Is.True);
                int count=sounds.Length;rig.InterruptInspections("test.microFoley.cancel");
                Assert.That(s.Sound.Pool.Sources.Any(a=>a.isPlaying&&a.clip&&a.clip.name.StartsWith("micro")),Is.False,"当前响声和排期立即停止");
                for(int i=0;i<60;i++)Step(1f/120);
                Assert.That(records.Count(r=>r.kind=="presentation_sound"&&r.sound.category=="inspect"&&r.sound.started),Is.EqualTo(count));
                Assert.That(Bar.Logging.LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            }
        }
    }
}
