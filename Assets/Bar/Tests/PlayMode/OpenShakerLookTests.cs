using System;
using System.Collections;
using System.IO;
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
        [UnityTest]public IEnumerator BareShakerLookRevealsBottomAndRetainsLoadedRimLimit()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../OpenShakerLookReports"));Directory.CreateDirectory(output);
            foreach(var mode in new[]{"empty","openice","nearfull","strainerfull","sealedliquid"})
            {
                yield return Load();yield return Resources.UnloadUnusedAssets();GC.Collect();yield return null;var v=View("shaker.cobbler");Fill(v,mode);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();
                var p=HoldViewSettings.Load().inspectAll.For(v.DefinitionId);float configured=p.openBodyViewDegrees;
                float expectedDuration=p.Duration+(((CobblerShaker)v.Item).State==ShakerState.Open?Mathf.Max(0,p.openBodyLookSeconds-p.lookSeconds):0);
                Assert.That(rig.InspectionDuration(v),Is.EqualTo(expectedDuration).Within(.001f));
                Quaternion before=Quaternion.identity;
                try
                {
                    foreach(bool enabled in new[]{false,true})
                    {
                        p.openBodyViewDegrees=enabled?configured:0;records.Clear();Start();string hash=Bar.Logging.LogReplay.SnapshotHash(s.Adapter.World);int frames=Mathf.RoundToInt((((CobblerShaker)v.Item).State==ShakerState.Open&&p.openBodyLookSeconds>p.lookSeconds?p.openBodyLookSeconds:p.lookSeconds)*.5f*120);for(int i=0;i<frames;i++)Step(1f/120);
                        if(enabled)
                        {
                            if(mode=="empty")
                            {
                                var visual=InspectionPolicy.Binding(v);var shape=visual.Shape;Vector3 bottom=visual.transform.TransformPoint(new Vector3(shape.Axis.x,shape.Bottom+.002f,shape.Axis.y));
                                var mouth=visual.transform.TransformPoint(new Vector3(shape.Axis.x,shape.Top,shape.Axis.y));var toEye=(s.Player.ViewCamera.transform.position-bottom).normalized;
                                float depth=Vector3.Dot(mouth-bottom,visual.transform.up),cos=Vector3.Dot(toEye,visual.transform.up);Assert.That(cos,Is.GreaterThan(.8f));
                                var atMouth=bottom+toEye*(depth/cos);float radius=Vector3.ProjectOnPlane(atMouth-mouth,visual.transform.up).magnitude;
                                Assert.That(radius,Is.LessThan(shape.RadiusAt(shape.Top)*.9f),"壶底中心到眼睛的视线经过杯口内部");
                                NativeFrame(Path.Combine(output,"after"),0);
                                File.WriteAllText(Path.Combine(output,"bottom-line.txt"),$"mouthRadius={shape.RadiusAt(shape.Top)}\nsightlineRadius={radius}\n");
                            }
                            if(mode=="strainerfull"||mode=="sealedliquid")Assert.That(Quaternion.Angle(before,v.transform.rotation),Is.LessThan(.01f),"滤网/封盖分支姿态不变");
                        }
                        else {before=v.transform.rotation;if(mode=="empty")NativeFrame(Path.Combine(output,"before"),0);}
                        if(mode!="sealedliquid")CheckRim(v);
                        rig.InterruptInspections("test.look.reset");for(int i=0;i<120;i++)Step(1f/120);
                        Assert.That(Bar.Logging.LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
                    }
                }
                finally{p.openBodyViewDegrees=configured;}
            }
        }
    }
}
