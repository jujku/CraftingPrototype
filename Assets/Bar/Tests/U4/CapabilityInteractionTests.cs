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
    /// <summary>验证双向能力配对、所有合规接收方和核心确认守恒。</summary>
    internal sealed class CapabilityInteractionTests
    {
        private ItemFactory factory;
        private WorldAdapter adapter;
        private Hands hands;
        private ActionResolver resolver;
        private int serial;
        [SetUp] public void Setup()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Bar", file));
            var data = JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));
            factory = new ItemFactory(ItemCatalog.Load(Read("Data/Json/items.json"), data),
                id => data.ingredients.Single(i => i.id == id).temperature, () => 1);
            adapter = new WorldAdapter(data);
            hands = new Hands();
            resolver = ActionResolver.CreateU4(ItemCatalog.LoadControls(Read("Data/Json/controls.json")),
                data.icePortionGrams, data.iceDensity, () => .5);
        }
        private T Make<T>(string id) where T : Item
        {
            var item = (T)factory.Create(id, "capability:" + serial++);
            adapter.Register(item);
            return item;
        }
        private void Drain(ActiveAction active)
        {
            foreach (var request in active.Requests.Where(r => r.IsPending)) adapter.Submit(request);
        }
        private void Ledger()
        {
            Assert.That(adapter.World.Ledger().massResidual, Is.EqualTo(0).Within(1e-8));
            Assert.That(adapter.World.Ledger().energyResidual, Is.EqualTo(0).Within(1e-6));
        }
        private (Squeezer tool, LemonHalf lemon) Load(bool reverse = true, bool bothHeld = false)
        {
            var tool = Make<Squeezer>("tool.squeezer");
            var lemon = Make<LemonHalf>("produce.lemon_half");
            if (reverse || bothHeld) hands.TryPickUp(tool);
            if (!reverse || bothHeld) hands.TryPickUp(lemon);
            Item target = reverse ? (Item)lemon : tool;
            var option = resolver.Resolve(target, hands).Single(o => o.Action.Id == "lemon.load");
            Assert.That(option.Actor, Is.SameAs(reverse ? (Item)tool : lemon));
            Assert.That(option.Target, Is.SameAs(target));
            option.Begin(hands);
            Assert.That(tool.Produce, Is.SameAs(lemon));
            Assert.That(lemon.Location.Kind, Is.EqualTo(LocationKind.Attached));
            Assert.That(lemon.Location.OwnerId, Is.EqualTo(tool.InstanceId));
            Assert.That(lemon.Location.Anchor, Is.EqualTo("LoadSlot"));
            Assert.That(hands.SideOf(lemon), Is.Null);
            Assert.That(lemon.JuiceRemainingMl, Is.EqualTo(30));
            if (!hands.SideOf(tool).HasValue) hands.TryPickUp(tool);
            return (tool, lemon);
        }
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void LoadNormalizesBothDirectionsWithIdenticalOwnership(bool reverse, bool bothHeld)
        {
            Load(reverse, bothHeld);
            Assert.That(hands.Left == null || hands.Right == null, Is.True);
            Assert.That(hands.IsBusy, Is.False);
            Ledger();
        }
        private Item Receiver(string id)
        {
            if (id.StartsWith("jigger"))
            {
                var jigger = Make<Jigger>("jigger.dual");
                if (id == "jigger45") jigger.Flip();
                return jigger;
            }
            return Make<Item>(id);
        }
        [TestCase("jigger45")]
        [TestCase("jigger30")]
        [TestCase("shaker.cobbler")]
        [TestCase("glass.highball")]
        [TestCase("glass.coupe")]
        public void PressUsesEveryLegalReceiver(string id)
        {
            var state = Load();
            var target = Receiver(id);
            var receiving = ((ILiquidReceiver)target).ReceivingContainer;
            var active = resolver.Resolve(target, hands).Single(o => o.Action.Id == "lemon.press").Begin(hands);
            active.Update(new ActionInput(pressFraction: 1), .2);
            Assert.That(state.lemon.JuiceRemainingMl, Is.EqualTo(30), "确认前不扣汁");
            Drain(active);
            active.End();
            Assert.That(receiving.Contents.LiquidMl, Is.EqualTo(30).Within(1e-8));
            Assert.That(state.lemon.JuiceRemainingMl, Is.Zero.Within(1e-8));
            if (target is Jigger jigger)
            {
                var down = ReferenceEquals(jigger.ActiveCup, jigger.CupA) ? jigger.CupB : jigger.CupA;
                Assert.That(down.Contents.LiquidMl, Is.Zero);
                Assert.That(((ILiquidReceiver)down).ReceivableMl, Is.Zero);
            }
            Ledger();
        }
        [TestCase("strainer")]
        [TestCase("cap")]
        [TestCase("served")]
        [TestCase("full")]
        [TestCase("down")]
        public void RejectedReceiverDoesNotConsumeJuice(string reason)
        {
            var state = Load();
            Item target;
            if (reason == "strainer" || reason == "cap")
            {
                var shaker = Make<CobblerShaker>("shaker.cobbler");
                shaker.Attach(Make<ShakerStrainer>("shaker.strainer"));
                if (reason == "cap") shaker.Attach(Make<ShakerCap>("shaker.cap"));
                target = shaker;
            }
            else if (reason == "down")
            {
                var jigger = Make<Jigger>("jigger.dual");
                target = jigger.CupB;
            }
            else
            {
                var glass = Make<Glass>("glass.highball");
                if (reason == "served") glass.MarkServed();
                else glass.ApplySnapshot(new ContainerSnapshot(glass.CapacityMl));
                target = glass;
            }
            Assert.That(resolver.Resolve(target, hands).Any(o => o.Action.Id == "lemon.press"), Is.False);
            Assert.Throws<InvalidOperationException>(() => new PressLemonAction().Begin(state.tool, target, hands));
            Assert.That(state.lemon.JuiceRemainingMl, Is.EqualTo(30));
            Assert.That(hands.IsBusy, Is.False);
            Ledger();
        }
        [Test] public void PressToThirtyMarkThenTransferToShakerConservesJuice()
        {
            var state = Load();
            var jigger = Make<Jigger>("jigger.dual");
            hands.TryPickUp(jigger);
            var active = new PressLemonAction().Begin(state.tool, jigger, hands);
            active.Update(new ActionInput(pressFraction: 1), .3);
            Drain(active);
            active.End();
            Assert.That(jigger.ActiveCup.NominalMl, Is.EqualTo(30));
            Assert.That(jigger.ActiveCup.CapacityMl, Is.EqualTo(38));
            Assert.That(jigger.ActiveCup.Contents.LiquidMl, Is.EqualTo(30).Within(1e-8));
            var shaker = Make<CobblerShaker>("shaker.cobbler");
            var pour = resolver.Resolve(shaker, hands).Single(o => o.Action.Id == "pour.jigger_to_container").Begin(hands);
            Drain(pour);
            pour.End();
            Assert.That(jigger.ActiveCup.Contents.LiquidMl, Is.Zero);
            Assert.That(shaker.Body.Contents.LiquidMl, Is.EqualTo(30).Within(1e-8));
            Assert.That(state.lemon.JuiceRemainingMl + shaker.Body.Contents.LiquidMl, Is.EqualTo(30).Within(1e-8));
            Ledger();
        }
        [Test] public void PressureClampsAndRejectedRequestKeepsItsProduce()
        {
            var state = Load();
            var glass = Make<Glass>("glass.coupe");
            adapter.World.Apply(new Operation { type = "PourSource", to = glass.CoreVesselId, ingredient = "spirit", ml = 178 });
            adapter.Advance(0);
            var active = new PressLemonAction().Begin(state.tool, glass, hands);
            active.Update(new ActionInput(pressFraction: 1), .5);
            var request = active.Requests.Single();
            Assert.That(request.IntendedMl, Is.EqualTo(30));
            Assert.That(request.RequestedMl, Is.EqualTo(2).Within(1e-8));
            request.Reject();
            Assert.That(state.lemon.JuiceRemainingMl, Is.EqualTo(30));
            active.Update(new ActionInput(pressFraction: 1), .1);
            Drain(active);
            active.End();
            Assert.That(state.lemon.JuiceRemainingMl, Is.EqualTo(28).Within(1e-8));
            Assert.That(glass.Contents.LiquidMl, Is.EqualTo(180).Within(1e-8));
            Ledger();
        }
    }
}
