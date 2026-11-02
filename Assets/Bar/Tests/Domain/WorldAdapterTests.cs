using System;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;

namespace Bar.Tests
{
    [TestFixture] internal sealed class WorldAdapterTests
    {
        private static string Read(string p)=>File.ReadAllText(Path.Combine(Application.dataPath,"Bar",p));
        private (WorldAdapter adapter,Hands hands,SimulationClock clock,Bottle bottle,Jigger jigger,ActionResolver resolver) Setup()
        {
            var data=JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));var controls=ItemCatalog.LoadControls(Read("Data/Json/controls.json"));var catalog=ItemCatalog.Load(Read("Tests/Domain/Fixtures/legacy-items.json"),data);
            var factory=new ItemFactory(catalog,id=>data.ingredients.Single(i=>i.id==id).temperature,()=>.5);var bottle=(Bottle)factory.Create("bottle.gin","gin");var jigger=(Jigger)factory.Create("jigger.dual","measure");jigger.Flip();
            var adapter=new WorldAdapter(data);adapter.Register(jigger);var hands=new Hands();hands.TryPickUp(jigger);hands.TryPickUp(bottle);return(adapter,hands,new SimulationClock(adapter,hands),bottle,jigger,ActionResolver.CreateU1(controls));
        }
        [Test] public void InputsWaitForTickAndActualVolumeComesFromCore()
        {
            var s=Setup();s.clock.Enqueue(.023,c=>c.Begin(s.resolver.Primary(s.jigger,s.hands)));s.clock.Enqueue(.041,c=>c.SetTilt(120));s.clock.Enqueue(.079,c=>c.Stop());
            s.clock.AdvanceFrame(.099);Assert.That(s.bottle.RemainingMl,Is.EqualTo(750));Assert.That(s.adapter.World.Time,Is.Zero);
            s.clock.AdvanceFrame(.1);Assert.That(s.adapter.World.Time,Is.EqualTo(.1).Within(1e-12));Assert.That(s.jigger.CupB.Contents.LiquidMl,Is.EqualTo(40*(.079-.041)).Within(1e-9));Assert.That(s.adapter.LastRequest.ActualMl,Is.EqualTo(s.adapter.LastRequest.RequestedMl));Assert.That(s.hands.IsBusy,Is.False);
        }
        private double[] Timeline(int fps)
        {
            var s=Setup();s.clock.Enqueue(.023,c=>c.Begin(s.resolver.Primary(s.jigger,s.hands)));s.clock.Enqueue(.041,c=>c.SetTilt(68));s.clock.Enqueue(.247,c=>c.SetTilt(100));s.clock.Enqueue(.513,c=>c.SetTilt(125));s.clock.Enqueue(.879,c=>c.Stop());
            for(int frame=1;frame<=fps*2;frame++)s.clock.AdvanceFrame((double)frame/fps);
            var v=s.adapter.World.GetVessel(s.jigger.CupB.CoreVesselId);return new[]{s.adapter.World.Time,v.m,v.E,v.volume,s.bottle.RemainingMl,s.adapter.World.EnvironmentEnergy};
        }
        [TestCase(15)] [TestCase(30)] [TestCase(60)] [TestCase(144)] public void EventTimelineIsIndependentOfFrameRate(int fps)
        {var reference=Timeline(1000);var actual=Timeline(fps);for(int i=0;i<actual.Length;i++)Assert.That(actual[i],Is.EqualTo(reference[i]).Within(1e-9),"field "+i);}
        [Test] public void ClockFillsTheFortyFiveMlEndAndDoesNotLoseReleaseTail()
        {
            var s=Setup();s.clock.Enqueue(.013,c=>c.Begin(s.resolver.Primary(s.jigger,s.hands)));s.clock.Enqueue(.017,c=>c.SetTilt(120));s.clock.Enqueue(1.743,c=>c.Stop());
            s.clock.AdvanceFrame(2);Assert.That(s.jigger.CupB.Contents.LiquidMl,Is.EqualTo(45).Within(1e-9));Assert.That(s.bottle.RemainingMl,Is.EqualTo(705).Within(1e-9));Assert.That(s.hands.IsBusy,Is.False);
        }
        [Test] public void JiggerClickTransfersThroughCoreAndEndsItsAnimationLock()
        {
            var s=Setup();var core=s.adapter.World;core.Pour(s.jigger.CupB.CoreVesselId,"lemon",25);s.jigger.CupB.ApplySnapshot(s.adapter.Snapshot(s.jigger.CupB.CoreVesselId));
            var model=JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));var catalog=ItemCatalog.Load(Read("Data/Json/items.json"),model);var factory=new ItemFactory(catalog,_=>20,()=>.5);var shaker=(CobblerShaker)factory.Create("shaker.cobbler","shaker");s.adapter.Register(shaker);
            s.hands.SelectMain(HandSide.Left);s.clock.Enqueue(.03,c=>c.Begin(s.resolver.Primary(shaker,s.hands)));s.clock.AdvanceFrame(1);
            Assert.That(shaker.Body.Contents.LiquidMl,Is.EqualTo(25).Within(1e-9));Assert.That(s.jigger.CupB.Contents.LiquidMl,Is.Zero);Assert.That(s.hands.IsBusy,Is.False);Assert.That(core.Ledger().massResidual,Is.EqualTo(0).Within(1e-9));
        }
    }
}
