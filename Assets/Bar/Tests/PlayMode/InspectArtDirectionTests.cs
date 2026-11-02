using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace Bar.Tests
{
    /// <summary>用户2026-10-29最新编排的独立几何、关节、节奏、声音与只读检查。</summary>
    public sealed partial class InspectArtDirectionTests
    {
        private BarSession s;private HoldViewRig rig;private readonly List<LogRecord> records=new List<LogRecord>();
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../InspectArtReports"));
        private ItemView View(string id)=>s.Views.First(v=>v.DefinitionId==id);
        private IEnumerator Load()
        {
            PlayerPrefs.DeleteKey(BarSession.HandednessKey);yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            s=Object.FindFirstObjectByType<BarSession>();rig=s.GetComponent<HoldViewRig>();
            foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(b is PlayerController||b.GetType().Name=="InputRouter")b.enabled=false;
            s.Player.ViewCamera.aspect=4f/3;s.Player.AimAt(CapSteps.TablePoint(s,0,0));records.Clear();s.Adapter.Events.Published+=e=>{if(e.Observation is LogRecord row)records.Add(row);};Directory.CreateDirectory(Output);
        }
        [TearDown]public void Restore()=>PlayerPrefs.DeleteKey(BarSession.HandednessKey);
        private void Step(float dt)
        {
            rig.AdvancePresentation(dt);
            foreach(var view in s.Views)
            {var tongs=view.GetComponent<TongsViewRig>();if(tongs){var ice=view.GetComponentInChildren<IceVisual>(true);if(ice)tongs.Advance(((Tool)view.Item).LoadedCubes>0,ice.transform,dt);}view.GetComponent<SqueezerViewRig>()?.AdvanceInspectionReturn(dt);}
            foreach(var ice in s.Views.SelectMany(v=>v.GetComponentsInChildren<LocalIceWorld>()))ice.Advance(dt);
            s.Cues.Present(dt);
        }
        private void Freeze(){s.enabled=false;rig.enabled=false;foreach(var ice in s.Views.SelectMany(v=>v.GetComponentsInChildren<LocalIceWorld>()))ice.enabled=false;for(int i=0;i<120;i++)Step(1f/120);}
        private void Start(){s.Inspect(s.Clock.Ticks*.1);s.Clock.AdvanceFrame((s.Clock.Ticks+1)*.1);}
        private HandSide Side(ItemView view)=>s.Hands.SideOf(view.Item).Value;
        private Transform Anchor(ItemView view)=>Side(view)==HandSide.Left?s.Player.LeftAnchor:s.Player.RightAnchor;
        private void Fill(ItemView v,string mode)
        {
            if(v.Item is Squeezer sq&&mode=="peek"){sq.Load((ILoadableProduce)s.Views.First(x=>x.Item is LemonHalf).Item,s.Hands);return;}
            if(v.Item is Bottle b){b.SetOpen(true);return;}
            var c=InspectionPolicy.Container(v.Item);if(c==null||mode=="empty")return;
            if(mode.Contains("ice"))s.Adapter.World.Apply(new Operation{type="AddIce",to=c.CoreVesselId,grams=80});
            s.Adapter.World.Pour(c.CoreVesselId,mode=="gas"?"soda":"spirit",mode=="full"?c.CapacityMl:mode=="nearfull"||mode=="strainerfull"?c.CapacityMl*.98:75);s.Adapter.Advance(0);
            if(mode=="strainerfull")((CobblerShaker)v.Item).Attach((ShakerStrainer)s.Views.First(x=>x.Item is ShakerStrainer).Item);
            if(mode.StartsWith("sealed")){var sh=(CobblerShaker)v.Item;sh.Attach((ShakerStrainer)s.Views.First(x=>x.Item is ShakerStrainer).Item);sh.Attach((ShakerCap)s.Views.First(x=>x.Item is ShakerCap).Item);}
        }
        [UnityTest]public IEnumerator SpoonHasDepthWhipMovingPivotAndIndependentCantileverAt144Fps()
        {
            yield return Load();var v=View("tool.spoon");Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();
            var originals=v.GetComponentsInChildren<MeshFilter>().ToDictionary(f=>f,f=>f.sharedMesh);var vertices=originals.ToDictionary(x=>x.Key,x=>x.Value.vertices);Start();string hash=LogReplay.SnapshotHash(s.Adapter.World);
            var grip=HoldViewRig.Grip(v,Side(v));var p=HoldViewSettings.Load().inspectAll.For(v.DefinitionId);var previous=v.transform.rotation;float peakLook=0,peakWhip=0,tip=0,angle=0,depth=0,minZ=10,maxZ=-10;Quaternion ringStart=Quaternion.identity;bool ringStarted=false;float ringRootTurn=0;
            for(int i=1;i<=430;i++)
            {
                Step(1f/144);float t=i/144f,w=Quaternion.Angle(previous,v.transform.rotation)*144;previous=v.transform.rotation;
                if(t>.4f&&t<1.1f){peakLook=Mathf.Max(peakLook,w);var direction=s.Player.ViewCamera.transform.InverseTransformDirection(grip.forward);angle=Mathf.Atan2(Mathf.Abs(direction.y),Mathf.Abs(direction.x))*Mathf.Rad2Deg;depth=-direction.z;}
                if(t>1.3f&&t<1.95f){peakWhip=Mathf.Max(peakWhip,w);float z=s.Player.ViewCamera.transform.InverseTransformPoint(grip.position).z;minZ=Mathf.Min(minZ,z);maxZ=Mathf.Max(maxZ,z);}
                if(t>1.965f&&t<2.20f){if(!ringStarted){ringStart=v.transform.rotation;ringStarted=true;}ringRootTurn=Mathf.Max(ringRootTurn,Quaternion.Angle(ringStart,v.transform.rotation));tip=Mathf.Max(tip,rig.InspectionTipDisplacement(Side(v)));}
            }
            Assert.That(angle,Is.EqualTo(35).Within(1));Assert.That(depth,Is.GreaterThan(.35f),"长轴有25°近远透视，不能只是屏幕斜摆");Assert.That(peakWhip,Is.GreaterThan(1100));Assert.That(peakWhip,Is.GreaterThan(peakLook*2.5f));Assert.That(maxZ-minZ,Is.GreaterThan(.015f),"移动转轴带来深度轨迹");
            Assert.That(ringRootTurn,Is.LessThan(.2f),"收住后根朝向稳定，余振发生在勺尖网格");Assert.That(tip,Is.InRange(.0004f,.015f));
            foreach(var pair in originals){Assert.That(pair.Key.sharedMesh,Is.SameAs(pair.Value));Assert.That(pair.Value.vertices,Is.EqualTo(vertices[pair.Key]),"还原运行时副本，不改原网格");}
            Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            File.WriteAllText(Path.Combine(Output,"spoon-structure.txt"),$"diagonal={angle}\nnearDepth={depth}\nlookPeak={peakLook}\nwhipPeak={peakWhip}\npivotDepthRange={maxZ-minZ}\nringRootTurn={ringRootTurn}\ntipDisplacement={tip}\n");
            File.WriteAllLines(Path.Combine(Output,"spoon-meshes.txt"),originals.Select(x=>x.Key.name+" "+x.Value.bounds));
        }
        [UnityTest]public IEnumerator LoadedPeekOnlyOpensUpperLeverAndKeepsLemonAndCore()
        {
            yield return Load();var v=View("tool.squeezer");Fill(v,"peek");Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();var sq=(Squeezer)v.Item;var fruit=sq.Produce;double ml=fruit.RemainingMl;
            var upper=v.GetComponentsInChildren<Transform>().First(t=>t.name.EndsWith("UpperPivot"));var middle=v.GetComponentsInChildren<Transform>().First(t=>t.name.EndsWith("MiddlePivot"));var upperRest=upper.localRotation;var middleRest=middle.localRotation;
            Assert.That(HudText.Inspectable(v.Item),Is.True);Start();string hash=LogReplay.SnapshotHash(s.Adapter.World);float high=0,middleMove=0;Assert.That(rig.InspectionDuration(v),Is.EqualTo(1.8f));
            for(int i=0;i<240;i++){Step(1f/120);high=Mathf.Max(high,Quaternion.Angle(upperRest,upper.localRotation));middleMove=Mathf.Max(middleMove,Quaternion.Angle(middleRest,middle.localRotation));}
            Assert.That(high,Is.EqualTo(28).Within(.15));Assert.That(middleMove,Is.LessThan(.1f));Assert.That(sq.Produce,Is.SameAs(fruit));Assert.That(fruit.RemainingMl,Is.EqualTo(ml));Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));Assert.That(Quaternion.Angle(upperRest,upper.localRotation),Is.LessThan(.1f));
            var start=s.Journal.ReadLines().Select(SessionJournal.Decode).Last(r=>r.kind=="view_inspect");Assert.That(start.inspection.produceId,Is.EqualTo(fruit.ProduceItem.InstanceId));Assert.That(start.inspection.produceRemainingMl,Is.EqualTo(ml));
        }
        [UnityTest]public IEnumerator SpillLimitedAlternativesKeepMovingAndNeverCrossTheRim()
        {
            foreach(var pair in new[]{("bottle.gin","full"),("glass.highball","gas"),("glass.coupe","full"),("shaker.cobbler","nearfull"),("shaker.cobbler","strainerfull")})
            {
                yield return Load();var v=View(pair.Item1);Fill(v,pair.Item2);Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();Start();string hash=LogReplay.SnapshotHash(s.Adapter.World);var p=HoldViewSettings.Load().inspectAll.For(v.DefinitionId);var centre=HoldViewRig.GripBounds(v,Side(v),Quaternion.identity).center;var grip=HoldViewRig.Grip(v,Side(v));var previous=grip.TransformPoint(centre);var previousQ=v.transform.rotation;float still=0,longest=0;
                for(int i=1;i<=Mathf.CeilToInt(rig.InspectionDuration(v)*120);i++)
                {
                    Step(1f/120);float t=i/120f;var now=grip.TransformPoint(centre);float velocity=(now-previous).magnitude*120,omega=Quaternion.Angle(previousQ,v.transform.rotation)*120;previous=now;previousQ=v.transform.rotation;
                    if(t>.35f&&t<p.lookSeconds+p.playSeconds){still=velocity<.002f&&omega<.5f?still+1f/120:0;longest=Mathf.Max(longest,still);}CheckRim(v);
                }
                Assert.That(longest,Is.LessThan(.5f),pair.Item1+" 安全角受限也不得静止超过0.5秒");Assert.That(LogReplay.SnapshotHash(s.Adapter.World),Is.EqualTo(hash));
            }
        }
        private void CheckRim(ItemView v)
        {
            if(v.Item is Bottle b){float limit=Mathf.Max(0,(float)new TiltFlowModel(s.GestureControls).StartAngle(b.RemainingMl,b.CapacityMl,b.Definition.Id)-10);Assert.That(Vector3.Angle(v.transform.up,Vector3.up),Is.LessThanOrEqualTo(limit+.05f));return;}
            var c=InspectionPolicy.Container(v.Item);if(c==null||v.Item is CobblerShaker sh&&sh.State==ShakerState.Sealed||v.Item is not CobblerShaker&&!c.IsOpenTop)return;var visual=InspectionPolicy.Binding(v);var material=visual.LiquidVolume.sharedMaterial;var n=(Vector3)material.GetVector("_PlaneNormal");var pivot=(Vector3)material.GetVector("_PlanePivot");float h=material.GetFloat("_FillHeight");var shape=visual.Shape;float radius=shape.RadiusAt(shape.Top);
            for(int i=0;i<96;i++){float a=i*Mathf.PI*2/96;var point=visual.transform.TransformPoint(new Vector3(shape.Axis.x+radius*Mathf.Cos(a),shape.Top,shape.Axis.y+radius*Mathf.Sin(a)));Assert.That(Vector3.Dot(n,point-pivot)-n.y*(h-pivot.y),Is.GreaterThanOrEqualTo(-.00015f),v.DefinitionId+" 实际渲染平面越口沿");}
        }
        // 冰夹两次空夹落在钳口完全合拢的 .245/.56 s（真实不锈钢夹子录音），抛接时只有掌心接物与钳口轻碰。
        [UnityTest]public IEnumerator TongsClicksLandOnClosedJawsAndInterruptDoesNotTriggerFutureCatches()
        {
            yield return Load();var v=View("tool.tongs");Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();Start();for(int i=0;i<360;i++)Step(1f/120);
            var catalog=BarAudioCatalog.Load();var sounds=records.Where(r=>r.kind=="presentation_sound"&&r.sound.category=="inspect"&&r.sound.started).ToArray();
            Assert.That(sounds.Select(r=>r.sound.bank),Is.EqualTo(new[]{"inspectTongs","inspectTongs","inspectSwish","inspectCatch","inspectTongs"}));
            Assert.That(sounds[0].sound.pitch,Is.GreaterThan(sounds[1].sound.pitch));Assert.That(sounds[0].sound.gain,Is.LessThan(sounds[1].sound.gain),"先轻夹试探，再完整一夹");
            Assert.That(sounds[1].sound.scheduledDsp-sounds[0].sound.scheduledDsp,Is.EqualTo(.315).Within(.002));
            Assert.That(sounds.All(r=>r.sound.gain<catalog.iceLandingGain*catalog.masterGain),Is.True,"检视声低于冰块落杯，保留克制声量");
            records.Clear();Start();for(int i=0;i<54;i++)Step(1f/120);rig.InterruptInspections("test.cancel");for(int i=0;i<240;i++)Step(1f/120);Assert.That(records.Count(r=>r.kind=="presentation_sound"&&r.sound.category=="inspect"&&r.sound.started),Is.EqualTo(1));
        }
        [UnityTest]public IEnumerator InterruptRestoresCantileverMeshAndLoadedPeekWithin150ms()
        {
            foreach(var pair in new[]{("tool.spoon",1.98f),("tool.squeezer",.8f)})
            {
                yield return Load();var v=View(pair.Item1);if(v.Item is Squeezer)Fill(v,"peek");Assert.That(s.Hands.TryPickUp(v.Item),Is.True);yield return new WaitForSecondsRealtime(.8f);Freeze();var anchor=Anchor(v);var rest=new Pose(anchor.localPosition,anchor.localRotation);var meshes=v.GetComponentsInChildren<MeshFilter>().ToDictionary(m=>m,m=>m.sharedMesh);var upper=v.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.EndsWith("UpperPivot"));var upperRest=upper?upper.localRotation:Quaternion.identity;
                Start();for(int i=0;i<Mathf.RoundToInt(pair.Item2*120);i++)Step(1f/120);float error=Vector3.Distance(rest.position,anchor.localPosition);s.Cancel(s.Clock.Ticks*.1);Assert.That(rig.Inspecting(Side(v)),Is.False);for(int i=0;i<18;i++)Step(1f/120);
                Assert.That(Vector3.Distance(rest.position,anchor.localPosition),Is.LessThanOrEqualTo(Mathf.Max(.001f,error*.05f)));if(upper)Assert.That(Quaternion.Angle(upper.localRotation,upperRest),Is.LessThan(1.4f));foreach(var mesh in meshes)Assert.That(mesh.Key.sharedMesh,Is.SameAs(mesh.Value));
            }
        }
    }
}
