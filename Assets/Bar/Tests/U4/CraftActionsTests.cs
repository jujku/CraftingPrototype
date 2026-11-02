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
    internal sealed class CraftActionsTests
    {
        private ModelData data;private Controls controls;private ItemFactory factory;private Hands hands;private WorldAdapter adapter;private ActionResolver resolver;private int serial;
        [SetUp] public void Setup(){data=JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));controls=ItemCatalog.LoadControls(Read("Data/Json/controls.json"));factory=new ItemFactory(ItemCatalog.Load(Read("Data/Json/items.json"),data),id=>data.ingredients.Single(i=>i.id==id).temperature,()=>.5);hands=new Hands();adapter=new WorldAdapter(data);resolver=ActionResolver.CreateU4(controls,data.icePortionGrams,data.iceDensity,()=>.5);}
        private static string Read(string file)=>File.ReadAllText(Path.Combine(Application.dataPath,"Bar",file));
        private T Make<T>(string id) where T:Item{var item=(T)factory.Create(id,"u4:"+serial++);adapter.Register(item);return item;}
        private ActiveAction Begin(Item target,string id)=>resolver.Resolve(target,hands).Single(o=>o.Action.Id==id).Begin(hands);
        private void Drain(ActiveAction action){foreach(var r in action.Requests.Where(r=>r.IsPending))adapter.Submit(r);}
        private void Finish(ActiveAction action){Drain(action);if(action.IsEnded)return;action.Update(new ActionInput(),.01);Drain(action);if(!action.IsEnded){action.Update(new ActionInput(),.01);Drain(action);}action.End();}
        private void Fill(Container c,string ingredient,double ml){adapter.World.Apply(new Operation{type="PourSource",to=c.CoreVesselId,ingredient=ingredient,ml=ml});adapter.Advance(0);}
        private void Ledger(){Assert.That(adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));Assert.That(adapter.World.Ledger().energyResidual,Is.EqualTo(0).Within(1e-6));}
        [Test] public void ScoopAndTongsUseDataInventoryAndOnlyCommitIceAfterCoreAcknowledgement()
        {var scoop=Make<IceScoop>("tool.scoop");var bin=Make<IceBin>("fixture.icebin");var glass=Make<Glass>("glass.highball");int stock=bin.StockCubes;hands.TryPickUp(scoop);Finish(Begin(bin,"ice.take"));Assert.That(scoop.LoadedCubes,Is.InRange(scoop.CubesPerScoop.Min,scoop.CubesPerScoop.Max));int taken=scoop.LoadedCubes;Assert.That(bin.StockCubes,Is.EqualTo(stock-taken));var a=Begin(glass,"ice.add");Assert.That(scoop.LoadedCubes,Is.EqualTo(taken));Drain(a);Assert.That(scoop.LoadedCubes,Is.Zero);Assert.That(glass.Contents.IceCubes,Is.EqualTo(taken));a.End();Ledger();}
        [Test] public void CancelledIceRequestKeepsToolInventoryAndReleasesCapacity()
        {var tongs=Make<IceTongs>("tool.tongs");var bin=Make<IceBin>("fixture.icebin");var glass=Make<Glass>("glass.highball");hands.TryPickUp(tongs);Finish(Begin(bin,"ice.take"));Assert.That(tongs.LoadedCubes,Is.EqualTo(1));var a=Begin(glass,"ice.add");a.End(true);Assert.That(tongs.LoadedCubes,Is.EqualTo(1));Assert.That(glass.Contents.IceCubes,Is.Zero);Assert.That(hands.IsBusy,Is.False);Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="ice.add"),Is.True);Ledger();}
        [Test] public void IceOccupancyPreventsOverfillingAndCannotAddThroughASealedLid()
        {var scoop=Make<IceScoop>("tool.scoop");var bin=Make<IceBin>("fixture.icebin");var glass=Make<Glass>("glass.coupe");hands.TryPickUp(scoop);Finish(Begin(bin,"ice.take"));Fill(glass,"spirit",175);Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="ice.add"),Is.False);}
        private (Squeezer tool,LemonHalf lemon,Glass glass) Loaded()
        {var tool=Make<Squeezer>("tool.squeezer");var lemon=Make<LemonHalf>("produce.lemon_half");var glass=Make<Glass>("glass.highball");hands.TryPickUp(tool);hands.TryPickUp(lemon);Finish(Begin(tool,"lemon.load"));Assert.That(tool.LoadedProduce,Is.SameAs(lemon));Assert.That(hands.SideOf(lemon),Is.Null);return(tool,lemon,glass);}
        [Test] public void PressIsStrokeFractionOfRemainingJuiceAndRepeatedSamplesDoNotDuplicateIt()
        {var s=Loaded();double total=s.lemon.JuiceYieldMl;var a=Begin(s.glass,"lemon.press");for(int i=0;i<8;i++){a.Update(new ActionInput(pressFraction:.5),.1);Drain(a);}Assert.That(s.glass.Contents.LiquidMl,Is.EqualTo(total*.5).Within(1e-8));Assert.That(s.lemon.JuiceRemainingMl,Is.EqualTo(total*.5).Within(1e-8));a.End();a=Begin(s.glass,"lemon.press");a.Update(new ActionInput(pressFraction:.5),.1);Drain(a);a.End();Assert.That(s.lemon.JuiceRemainingMl,Is.EqualTo(total*.25).Within(1e-8));Ledger();}
        [Test] public void RejectedJuiceRequestDoesNotSpendLemon()
        {var s=Loaded();var a=Begin(s.glass,"lemon.press");a.Update(new ActionInput(pressFraction:1),.1);a.End(true);Assert.That(s.lemon.JuiceRemainingMl,Is.EqualTo(s.lemon.JuiceYieldMl));Assert.That(s.glass.Contents.LiquidMl,Is.Zero);Ledger();}
        [Test] public void SpentLemonCanBeDiscardedAndSqueezerReused()
        {var s=Loaded();var sink=Make<Sink>("fixture.sink");Assert.That(resolver.Resolve(sink,hands).Any(o=>o.Action.Id=="lemon.discard"),Is.False);var a=Begin(s.glass,"lemon.press");a.Update(new ActionInput(pressFraction:1),.1);Drain(a);a.End();Finish(Begin(sink,"lemon.discard"));Assert.That(s.tool.LoadedProduce,Is.Null);Assert.That(s.lemon.IsDiscarded,Is.True);Assert.That(hands.TryPickUp(s.lemon),Is.False);Ledger();}
        /// <summary>擦干只是展示动作，不发物理操作；需要一只空手拿吧台巾。</summary>
        [Test] public void WipeShakerIsDisplayOnlyAndNeedsAFreeHand()
        {
            var shaker=Make<CobblerShaker>("shaker.cobbler");var glass=Make<Glass>("glass.highball");var coupe=Make<Glass>("glass.coupe");var counter=Make<BarCounter>("fixture.counter");
            var onCounter=resolver.Resolve(shaker,hands).Single(o=>o.Action.Id=="shaker.wipe");Assert.That(onCounter.Actor,Is.Null);
            Assert.That(hands.TryPickUp(glass),Is.True);Assert.That(hands.TryPickUp(coupe),Is.True);
            Assert.That(resolver.Resolve(shaker,hands).Any(o=>o.Action.Id=="shaker.wipe"),Is.False,"双手都满时没有手拿吧台巾");
            Assert.That(hands.TryPlace(hands.SideOf(coupe).Value,counter),Is.True);Assert.That(hands.TryPlace(hands.SideOf(glass).Value,counter),Is.True);
            Assert.That(hands.TryPickUp(shaker),Is.True);var held=resolver.Resolve(shaker,hands).Single(o=>o.Action.Id=="shaker.wipe");Assert.That(held.Actor,Is.SameAs(shaker));
            double inputs=adapter.World.Inputs.m;var action=held.Begin(hands);
            Assert.That(action.IsEnded,Is.True);Assert.That(action.Requests,Is.Empty);Assert.That(action.Events.Any(e=>e.Kind=="action_performed"),Is.True);
            Assert.That(adapter.World.Inputs.m,Is.EqualTo(inputs));Assert.That(hands.SideOf(shaker).HasValue,Is.True);Ledger();
            Assert.That(hands.TryPickUp(glass),Is.True);Assert.That(resolver.Resolve(shaker,hands).Any(o=>o.Action.Id=="shaker.wipe"),Is.False,"另一只手要空着");
        }
        [Test] public void AllFourBottlesRequireDirectUncappingBeforePouring()
        {
            foreach (string id in new[]{"bottle.gin", "bottle.syrup", "bottle.soda", "bottle.tonic"})
            {
                hands = new Hands();
                var bottle = Make<Bottle>(id);
                var glass = Make<Glass>("glass.highball");
                var counter = Make<BarCounter>("fixture.counter");
                Assert.That(bottle.IsOpen, Is.False, id);
                Assert.That(bottle.Cap.Parent, Is.SameAs(bottle), id);
                var surfaceOpen=resolver.Resolve(bottle,hands).Single(o=>o.Action.Id=="bottle.open");
                Assert.That(hands.TryPickUp(bottle), Is.True);
                Assert.Throws<InvalidOperationException>(()=>surfaceOpen.Begin(hands),"失效的台面候选不能操作已拿起的瓶子");
                Assert.That(hands.TryPickUp(glass),Is.True);
                Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="pour.bottle_to_container"),Is.False,id);
                Assert.That(resolver.Resolve(bottle,hands).Any(o=>o.Action.Id=="bottle.open"),Is.False,"双手都满时没有手接住瓶盖");
                Assert.That(hands.TryPlace(hands.SideOf(glass).Value,counter),Is.True);
                double stock=bottle.RemainingMl,inputs=adapter.World.Inputs.m;
                var open=resolver.Resolve(bottle,hands).Single(o=>o.Action.Id=="bottle.open");
                Finish(open.Begin(hands));
                Assert.That(bottle.IsOpen,Is.True,id);
                Assert.That(hands.Get(hands.SideOf(bottle)==HandSide.Left?HandSide.Right:HandSide.Left),Is.SameAs(bottle.Cap),"瓶盖进入另一只手");
                Assert.That(bottle.RemainingMl,Is.EqualTo(stock));Assert.That(adapter.World.Inputs.m,Is.EqualTo(inputs));
                Assert.That(resolver.Resolve(bottle,hands).Any(o=>o.Action.Id=="bottle.open"),Is.False);
                Assert.Throws<InvalidOperationException>(()=>open.Begin(hands),"不能重复开盖");
                var active = Begin(glass, "pour.bottle_to_container");
                active.Update(new ActionInput(120), .1);
                Drain(active);
                active.End();
                Assert.That(glass.Contents.LiquidMl, Is.GreaterThan(0), id);
                Ledger();
            }
        }
        [Test] public void BottleCapCanBeHeldPlacedPickedUpAndPutBackOnItsOwnBottle()
        {
            var gin=Make<Bottle>("bottle.gin");var soda=Make<Bottle>("bottle.soda");var glass=Make<Glass>("glass.highball");var counter=Make<BarCounter>("fixture.counter");
            var cap=gin.Cap;soda.SetOpen(true);
            Assert.That(cap.Location.Kind,Is.EqualTo(LocationKind.Attached));Assert.That(cap.Location.Anchor,Is.EqualTo("Slot_BottleCap"));
            Assert.That(hands.CanPickUp(cap),Is.False,"装着的瓶盖不能单独拿起");
            // Uncap on the counter: the cap goes into the free hand and the bottle stays down.
            Finish(Begin(gin,"bottle.open"));
            Assert.That(hands.Right,Is.SameAs(cap));Assert.That(hands.SideOf(gin),Is.Null);Assert.That(gin.IsOpen,Is.True);Assert.That(cap.Parent,Is.Null);
            Assert.That(resolver.Resolve(soda,hands).Any(o=>o.Action.Id=="bottle.close"),Is.False,"金酒瓶盖不适配苏打水瓶");
            Assert.That(hands.TryPlace(HandSide.Right,counter),Is.True);Assert.That(cap.Location.Kind,Is.EqualTo(LocationKind.Surface));
            Assert.That(resolver.Resolve(gin,hands).Any(o=>o.Action.Id=="bottle.close"),Is.False,"台面上的瓶盖要先拿起");
            // Pour with the cap off, then pick the cap up again and close the held bottle.
            Assert.That(hands.TryPickUp(gin),Is.True);Assert.That(hands.TryPickUp(cap),Is.True);Assert.That(hands.Left,Is.SameAs(cap));
            var pour=Begin(glass,"pour.bottle_to_container");pour.Update(new ActionInput(120),.1);
            Assert.That(gin.CanAttach(cap),Is.False,"倒酒请求未确认时不能盖盖");
            Drain(pour);pour.End();double poured=glass.Contents.LiquidMl;Assert.That(poured,Is.GreaterThan(0));
            Finish(Begin(gin,"bottle.close"));
            Assert.That(gin.IsOpen,Is.False);Assert.That(cap.Parent,Is.SameAs(gin));Assert.That(gin.CapSlot.Content,Is.SameAs(cap));
            Assert.That(hands.SideOf(cap),Is.EqualTo(hands.SideOf(gin)),"盖回后瓶盖随瓶子一起在手里");Assert.That(hands.Get(HandSide.Left),Is.Null);
            Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="pour.bottle_to_container"),Is.False,"盖上后不能再倒");
            Assert.That(glass.Contents.LiquidMl,Is.EqualTo(poured));
            // A capped bottle on the counter can be closed from either hand as well.
            Finish(Begin(gin,"bottle.open"));Assert.That(hands.Get(HandSide.Left),Is.SameAs(cap));
            Assert.That(hands.TryPlace(hands.SideOf(gin).Value,counter),Is.True);
            Finish(Begin(gin,"bottle.close"));Assert.That(gin.IsOpen,Is.False);Assert.That(hands.Left,Is.Null);Assert.That(hands.Right,Is.Null);
            Ledger();
        }
        [Test] public void CatalogRejectsABottleCapThatDoesNotFitItsBottle()
        {
            string json=Read("Data/Json/items.json");
            string wrong=System.Text.RegularExpressions.Regex.Replace(json,@"(""parts"":\s*\[\s*)""bottle\.soda\.cap""","$1\"bottle.gin.cap\"");
            Assert.That(wrong,Is.Not.EqualTo(json));
            Assert.Throws<ArgumentException>(()=>ItemCatalog.Load(wrong,data),"苏打水瓶不能以金酒瓶盖为部件");
        }
        private CobblerShaker Shaker(bool strainer,bool cap)
        {var s=Make<CobblerShaker>("shaker.cobbler");if(strainer)s.Attach(Make<ShakerStrainer>("shaker.strainer"));if(cap)s.Attach(Make<ShakerCap>("shaker.cap"));Fill(s.Body,"spirit",50);adapter.World.AddIce(s.Body.CoreVesselId,data.icePortionGrams*2);adapter.Advance(0);return s;}
        [TestCase(true)] [TestCase(false)] public void ShakerTransferUsesStrainerToChooseIcePolicy(bool strained)
        {var shaker=Shaker(strained,false);var glass=Make<Glass>("glass.highball");hands.TryPickUp(shaker);var a=Begin(glass,"shaker.pour");a.Update(new ActionInput(160),10);Drain(a);a.End();Assert.That(glass.Contents.LiquidMl,Is.EqualTo(50).Within(1e-8));Assert.That(shaker.Body.Contents.IceCubes,Is.EqualTo(strained?2:0).Within(1e-8));Assert.That(glass.Contents.IceCubes,Is.EqualTo(strained?0:2).Within(1e-8));Ledger();}
        [Test] public void ShakeRequiresSealedLidAndAnEmptyOtherHand()
        {var shaker=Shaker(false,false);hands.TryPickUp(shaker);Assert.That(resolver.Resolve(shaker,hands).Any(o=>o.Action.Id=="shaker.shake"),Is.False);shaker.Attach(Make<ShakerStrainer>("shaker.strainer"));shaker.Attach(Make<ShakerCap>("shaker.cap"));Assert.That(resolver.Resolve(shaker,hands).Any(o=>o.Action.Id=="shaker.shake"),Is.True);hands.TryPickUp(Make<Bottle>("bottle.gin"));Assert.That(resolver.Resolve(shaker,hands).Any(o=>o.Action.Id=="shaker.shake"),Is.False);}
        [Test] public void EmptyingCooledGlassAccountsForBothIceAndMeltwater()
        {var glass=Make<Glass>("glass.highball");var sink=Make<Sink>("fixture.sink");adapter.World.AddIce(glass.CoreVesselId,data.icePortionGrams*3);adapter.Advance(2);hands.TryPickUp(glass);Finish(Begin(sink,"sink.empty"));Assert.That(glass.Contents.LiquidMl,Is.Zero);Assert.That(glass.Contents.IceCubes,Is.Zero);Assert.That(adapter.World.Waste.m,Is.GreaterThan(0));Ledger();}
        [Test] public void ServingFreezesGlassAndCannotBePickedUpOrModifiedAgain()
        {var glass=Make<Glass>("glass.highball");var mat=Make<ServeMat>("fixture.servemat");Fill(glass,"tonic",50);hands.TryPickUp(glass);Finish(Begin(mat,"glass.serve"));Assert.That(glass.IsServed,Is.True);Assert.That(glass.IsOpenTop,Is.False);Assert.That(hands.Left,Is.Null);Assert.That(hands.TryPickUp(glass),Is.False);double energy=adapter.World.Vessels.Single().E;adapter.Advance(1);Assert.That(adapter.World.Vessels.Single().E,Is.EqualTo(energy));Ledger();}
        private double[] Timeline(int fps,bool stir)
        {Setup();Container vessel;Item actor;if(stir){vessel=Make<Glass>("glass.highball");Fill(vessel,"tonic",80);actor=Make<BarSpoon>("tool.spoon");}else{var shaker=Shaker(true,true);vessel=shaker.Body;actor=shaker;}hands.TryPickUp(actor);var target=stir?(Item)vessel:actor;var option=resolver.Resolve(target,hands).Single(o=>o.Action.Id==(stir?"spoon.stir":"shaker.shake"));var clock=new SimulationClock(adapter,hands);clock.Enqueue(.013,c=>c.Begin(option));for(int i=0;i<60;i++){double time=.02+i*.025;double angle=i*Math.PI/4;double x=stir?30*Math.Cos(angle):0,y=stir?30*Math.Sin(angle):(i%2==0?30:-30);clock.Enqueue(time,c=>c.SetGesture(x,y,0));}clock.Enqueue(1.54,c=>c.Stop());for(int i=1;i<=fps*2;i++)clock.AdvanceFrame((double)i/fps);Ledger();Assert.That(hands.IsBusy,Is.False);var v=adapter.World.Vessels.Single();return new[]{adapter.World.Time,v.E,v.ice,v.gas};}
        [TestCase(15,false)] [TestCase(144,false)] [TestCase(15,true)] [TestCase(144,true)] public void GestureClockIsIndependentOfFpsAndDoesNotDoubleCountAgitationTime(int fps,bool stir)
        {var expected=Timeline(1000,stir);var actual=Timeline(fps,stir);Assert.That(actual[0],Is.EqualTo(2).Within(1e-10));for(int i=0;i<actual.Length;i++)Assert.That(actual[i],Is.EqualTo(expected[i]).Within(1e-8));}

        [Test] public void ScoopTransfersOnlyOneWholeBlockWhenVolumeHasRoomForOne()
        {
            var scoop=Make<IceScoop>("tool.scoop");var bin=Make<IceBin>("fixture.icebin");var glass=Make<Glass>("glass.highball");
            hands.TryPickUp(scoop);new TakeIceAction(()=>0).Begin(scoop,bin,hands);Assert.That(scoop.LoadedCubes,Is.EqualTo(2));
            Fill(glass,"tonic",glass.CapacityMl-data.icePortionGrams/data.iceDensity*1.5);
            var active=Begin(glass,"ice.add");var request=active.Requests.Single();
            Assert.That(request.IntendedMl,Is.EqualTo(2*data.icePortionGrams));Assert.That(request.RequestedMl,Is.EqualTo(data.icePortionGrams));
            Assert.That(scoop.LoadedCubes,Is.EqualTo(2));Drain(active);active.End();
            Assert.That(scoop.LoadedCubes,Is.EqualTo(1));Assert.That(glass.Contents.IceCubes,Is.EqualTo(1));
            Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="ice.add"),Is.False);Ledger();
        }
        [TestCase(false)] [TestCase(true)] public void CancelledOrRejectedPartialTransferKeepsEveryRemainingBlock(bool reject)
        {
            var scoop=Make<IceScoop>("tool.scoop");var bin=Make<IceBin>("fixture.icebin");var glass=Make<Glass>("glass.highball");hands.TryPickUp(scoop);new TakeIceAction(()=>0).Begin(scoop,bin,hands);
            resolver=ActionResolver.CreateU4(controls,data.icePortionGrams,data.iceDensity,()=>.5,c=>1);
            var active=Begin(glass,"ice.add");Assert.That(active.Requests.Single().RequestedMl,Is.EqualTo(data.icePortionGrams));
            if(reject)active.Requests.Single().Reject();active.End(true);
            Assert.That(scoop.LoadedCubes,Is.EqualTo(2));Assert.That(glass.Contents.IceCubes,Is.Zero);Assert.That(hands.IsBusy,Is.False);Ledger();
        }
        [TestCase("tool.scoop",2)] [TestCase("tool.tongs",1)] public void RawToolIceCanBeDiscardedOnceWithoutEnteringTheCore(string definition,int count)
        {
            var tool=Make<Tool>(definition);var bin=Make<IceBin>("fixture.icebin");var sink=Make<Sink>("fixture.sink");var glass=Make<Glass>("glass.highball");hands.TryPickUp(tool);
            Assert.That(resolver.Resolve(sink,hands).Any(o=>o.Action.Id=="ice.discard"),Is.False);new TakeIceAction(()=>0).Begin(tool,bin,hands);string before=Bar.Logging.LogReplay.SnapshotHash(adapter.World);
            Assert.That(resolver.Resolve(glass,hands).Any(o=>o.Action.Id=="ice.discard"),Is.False);
            var active=Begin(sink,"ice.discard");Assert.That(active.IsEnded,Is.True);var receipt=(ToolIceDiscardReceipt)active.Events.Single(e=>e.Kind=="tool_ice_discarded").Observation;
            Assert.That(receipt.Cubes,Is.EqualTo(count));Assert.That(receipt.Grams,Is.EqualTo(count*data.icePortionGrams));
            Assert.That(tool.LoadedCubes,Is.Zero);Assert.That(sink.DiscardedToolIceCubes,Is.EqualTo(count));Assert.That(Bar.Logging.LogReplay.SnapshotHash(adapter.World),Is.EqualTo(before));
            Assert.That(resolver.Resolve(sink,hands).Any(o=>o.Action.Id=="ice.discard"),Is.False);new TakeIceAction(()=>0).Begin(tool,bin,hands);Assert.That(tool.LoadedCubes,Is.EqualTo(count));Ledger();
        }

        [Test] public void PressRequestPreservesDurationPressureAndIntentBeforeCapacityLimit()
        {var state=Loaded();Fill(state.glass,"tonic",state.glass.CapacityMl-5);var active=Begin(state.glass,"lemon.press");active.Update(new ActionInput(pressFraction:1),.4);var request=active.Requests.Single();Assert.That(request.HeldSeconds,Is.EqualTo(.4));Assert.That(request.IntentPressFraction,Is.EqualTo(1));Assert.That(request.IntendedMl,Is.EqualTo(state.lemon.JuiceYieldMl));Assert.That(request.RequestedMl,Is.EqualTo(5).Within(1e-9));Drain(active);Assert.That(request.ActualAmount,Is.EqualTo(5).Within(1e-9));Assert.That(state.lemon.JuiceRemainingMl,Is.EqualTo(state.lemon.JuiceYieldMl-5).Within(1e-9));active.End();Ledger();}
    }
}

