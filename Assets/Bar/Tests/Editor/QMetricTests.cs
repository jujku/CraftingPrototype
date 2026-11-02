using System;
using System.IO;
using System.Linq;
using Bar.Logging;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Bar.Tests
{
    public sealed class QMetricTests
    {
        [Test] public void ThinFrostAndCondensationMeetReferenceMetricBounds()
        {
            var p=BPresentationSettings.Load().surface;Assert.That(p.frostCap,Is.EqualTo(1f));Assert.That(p.frostGrowth,Is.InRange(.25f,.5f));Assert.That(p.frostGrowthTiling,Is.InRange(3f,5f));Assert.That(p.frostNormalStrength,Is.InRange(.15f,.25f));Assert.That(p.mistBlurPixels,Is.InRange(6f,10f));Assert.That(p.dropRefraction,Is.InRange(.02f,.03f));
            var fogOnly=new SurfaceEvolution(p);for(int i=0;i<100;i++)fogOnly.Step(12,false,false,.1f);Assert.That(fogOnly.Mist,Is.GreaterThan(.4f),"露点以下先起雾");Assert.That(fogOnly.Frost,Is.Zero,"起霜之前只有雾");
            var metal=new SurfaceEvolution(p);for(int i=0;i<1000;i++)metal.Step(-6,false,false,.1f);Assert.That(metal.Frost,Is.EqualTo(1f).Within(.0001));metal.Step(-6,false,true,.1f);metal.Step(-6,false,false,6);Assert.That(metal.Wipe,Is.GreaterThan(.5));
            var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath,"Bar/Rendering/Resources/BTextures/water-mask.png")));var pixels=image.GetPixels32();int drops=0,neutral=0;foreach(var c in pixels){if(c.b>25)drops++;if(Math.Abs(c.r-128)<3&&Math.Abs(c.g-128)<3)neutral++;}
            Assert.That((double)drops/pixels.Length,Is.InRange(.12,.35),"密集小珠覆盖率，禁止只在水道中有珠");Assert.That((double)neutral/pixels.Length,Is.GreaterThan(.5),"干处必须近中性法线");UnityEngine.Object.DestroyImmediate(image);
            string frost=File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Rendering/FrostMetal.shader"));Assert.That(frost,Does.Contain("float outward="),"只在外表面结霜");Assert.That(frost,Does.Contain("s.metallic=lerp(lerp(_Metallic,_Metallic*.85,fog),.1,dense)"),"未结霜处沿用原材质的金属度");Assert.That(frost,Does.Not.Contain("voronoi"),"薄霜不画可见冰晶");Assert.That(frost,Does.Contain("DepthNormals"));
        }
        [Test] public void ReadonlyPresentationSnapshotReusesBuffersWithoutAllocating()
        {
            var data=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));var world=new World(data);var v=world.Vessels.First();world.Pour(v.id,"lemon",25);var cache=new PresentationSnapshot();var expected=v.Snapshot();cache.Read(v,data.ethanolDensity);
            long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)cache.Read(v,data.ethanolDensity);long allocated=GC.GetAllocatedBytesForCurrentThread()-start;Assert.That(allocated,Is.Zero);
            Assert.That(cache.Value.optical,Is.EqualTo(expected.optical));Assert.That(cache.Value.co2GL,Is.EqualTo(expected.co2GL));Assert.That(cache.Value.temperature,Is.EqualTo(expected.temperature));Assert.That(cache.Value.volumeMl,Is.EqualTo(expected.volumeMl));
        }
        [Test] public void AudioLoopBanksUseLoadedPcmNativeLoopsAndCalibratedImpacts()
        {
            var p=BarAudioCatalog.Load();Assert.That(p.spatialBlend,Is.InRange(.8f,1));Assert.That(p.iceMassFullGrams,Is.EqualTo(150));Assert.That(p.impactFull,Is.EqualTo(.015f));Assert.That(BSoundDirector.ImpactGain(p.impactMinimum,p.impactMinimum,p.impactFull),Is.EqualTo(Mathf.Pow(10,-1.5f)).Within(.0001));Assert.That(BSoundDirector.ImpactGain(p.impactFull,p.impactMinimum,p.impactFull),Is.EqualTo(1).Within(.0001));
            foreach(var bank in p.banks.Where(b=>b.paths.Any(s=>s.StartsWith("BarAudio/Q/"))))foreach(string path in bank.paths)
            {var clip=Resources.Load<AudioClip>(path);Assert.That(clip,Is.Not.Null);var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));Assert.That(importer.defaultSampleSettings.loadType,Is.EqualTo(AudioClipLoadType.DecompressOnLoad));if(bank.loop){Assert.That(bank.loopStartSample,Is.Zero);var bytes=File.ReadAllBytes(AssetDatabase.GetAssetPath(clip));Assert.That(System.Text.Encoding.ASCII.GetString(bytes),Does.Contain("smpl"));}}
            foreach(string name in new[]{"press","pourThin"})Assert.That(Resources.LoadAll<AudioClip>("BarAudio").Any(c=>c.name.StartsWith(name+"-")),Is.False);
        }
        [Test] public void IngredientTopupDilutesAerationWithoutChangingCore()
        {
            var data=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));var catalog=Bar.Data.ItemCatalog.Load(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Data/Json/items.json")),data);var factory=new Bar.Domain.ItemFactory(catalog,_=>20,()=>.5);var shaker=(Bar.Domain.CobblerShaker)factory.Create("shaker.cobbler","q-shaker");var glass=(Bar.Domain.Glass)factory.Create("glass.coupe","q-glass");var adapter=new WorldAdapter(data);adapter.Register(shaker);adapter.Register(glass);using var foam=new FoamPresentation(adapter,BPresentationSettings.Load().foam);
            void Apply(Operation op){var before=adapter.World.Vessels.Select(v=>v.Snapshot()).ToArray();adapter.World.Apply(op);var after=adapter.World.Vessels.Select(v=>v.Snapshot()).ToArray();adapter.Events.Publish(new Bar.Domain.DomainEvent("core_operation",null,null,null,observation:new LogRecord{time=adapter.World.Time,operation=op,before=before,after=after}));}
            Apply(new Operation{type="PourSource",to=shaker.Body.CoreVesselId,ingredient="lemon",ml=25});Apply(new Operation{type="Shake",target=shaker.Body.CoreVesselId,seconds=12,intensity=.8});Apply(new Operation{type="StrainTransfer",from=shaker.Body.CoreVesselId,to=glass.CoreVesselId,fraction=1});var v=adapter.World.GetVessel(glass.CoreVesselId);double oldVolume=v.volume,tau=v.Snapshot().optical[3];float height=foam.Height(v.id,tau);Assert.That(height,Is.GreaterThan(0));float air=foam.Aeration(v.id);Apply(new Operation{type="PourSource",to=v.id,ingredient="spirit",ml=10});double nextTau=v.Snapshot().optical[3];Assert.That(foam.Aeration(v.id),Is.EqualTo(air*oldVolume/v.volume).Within(1e-6),"补原料按体积稀释含气量（泡沫高度对 τ 有满档截断，改为直接检查含气量）");string hash=LogReplay.SnapshotHash(adapter.World);foam.Height(v.id,nextTau);Assert.That(LogReplay.SnapshotHash(adapter.World),Is.EqualTo(hash));
        }
        [Test] public void FoamSurvivesSamplingAndUsesNewBackwardCompatibleSchema()
        {
            var cue=new CueFrame{foam=.0008,liquidRgba=new double[]{1,1,1,1}};var copy=cue.SampleCopy();cue.liquidRgba[0]=0;Assert.That(copy.liquidRgba[0],Is.EqualTo(1));Assert.That(copy.foam,Is.EqualTo(.0008));var row=new LogRecord{cues=new[]{copy},header=new SessionHeader()};var decoded=SessionJournal.Decode(SessionJournal.Encode(row));Assert.That(decoded.header.schemaVersion,Is.EqualTo(row.header.schemaVersion));Assert.That(decoded.cues[0].foam,Is.EqualTo(.0008));
            var p=BPresentationSettings.Load().foam;Assert.That(p.note,Does.Contain("不是物理"));
        }
        /// <summary>冰声只留"放进敞口容器时的落定"和"封壶真正在摇时的撞击"。</summary>
        [Test] public void IceSoundsAreOnlyLandingsAndTheShakeItself()
        {
            var p=BarAudioCatalog.Load();Assert.That(p.openIceImpactSounds,Is.False);
            Assert.That(BSoundDirector.IceAudible(p,false,false,true),Is.True,"冰块放进杯里");Assert.That(BSoundDirector.IceAudible(p,false,false,false),Is.False,"杯里冰块互相压着、端着走");
            Assert.That(BSoundDirector.IceAudible(p,true,true,false),Is.True,"封壶在摇");Assert.That(BSoundDirector.IceAudible(p,true,false,false),Is.False,"盖上盖子、封壶静止或端着走");
        }
    }
}
