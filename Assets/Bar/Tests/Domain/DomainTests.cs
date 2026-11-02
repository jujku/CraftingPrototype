using System;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using NUnit.Framework;
using UnityEngine;
using ModelData = CraftingResearch.CoreV2.ModelData;

namespace Bar.Tests
{
    [TestFixture] internal sealed class DomainTests
    {
        private string json, controlJson;
        private ItemCatalog catalog;
        private Controls controls;
        private ItemFactory factory;
        private Hands hands;
        private ActionResolver resolver;
        private int serial;
        private T New<T>(string definition) where T : Item => (T)factory.Create(definition, "test" + (++serial));
        private static string Read(string path) => File.ReadAllText(Path.Combine(Application.dataPath, "Bar", path));
        [SetUp] public void SetUp()
        {
            json = Read("Tests/Domain/Fixtures/legacy-items.json").Replace("\r\n", "\n"); controlJson = Read("Data/Json/controls.json");
            var core = JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));
            catalog = ItemCatalog.Load(json, core); controls = ItemCatalog.LoadControls(controlJson);
            factory = new ItemFactory(catalog, id => core.ingredients.Single(i => i.id == id).temperature, () => 0.5);
            hands = new Hands(); resolver = ActionResolver.CreateU1(controls); serial = 0;
        }
        [Test] public void AllReferenceItemsInstantiateAndHaveDistinctVessels()
        {
            foreach (var definition in catalog.Items) Assert.That(factory.Create(definition.Id, definition.Id), Is.Not.Null);
            Assert.That(catalog.Items.Count(), Is.EqualTo(23));
            var j = New<Jigger>("jigger.dual"); Assert.That(j.CupA.CoreVesselId, Is.Not.EqualTo(j.CupB.CoreVesselId));
            Assert.That(j.CupA.Parent, Is.SameAs(j)); Assert.That(j.CupB.Location.Kind, Is.EqualTo(LocationKind.Attached));
        }
        [Test] public void NoChemistryInBottleDefinitions()
        {
            Assert.That(catalog["bottle.gin"].IngredientId, Is.EqualTo("spirit"));
            Assert.That(New<Bottle>("bottle.gin").ServeTemperatureC, Is.EqualTo(20));
            Assert.That(typeof(Bar.Data.ItemDefinition).GetProperties().Any(p => p.Name == "Abv" || p.Name == "Density"), Is.False);
        }
        [Test] public void PreferredPickupThenFallbackThenFullRejection()
        {
            var a = New<Bottle>("bottle.gin"); var b = New<Bottle>("bottle.syrup"); var c = New<Glass>("glass.coupe");
            Assert.That(hands.TryPickUp(a), Is.True); Assert.That(hands.Right, Is.SameAs(a)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Right));
            Assert.That(hands.TryPickUp(b), Is.True); Assert.That(hands.Left, Is.SameAs(b)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Left));
            Assert.That(hands.TryPickUp(c), Is.False); Assert.That(c.Location.Kind, Is.EqualTo(LocationKind.Surface));
        }
        [Test] public void JiggerAndGlassPreferLeftHand()
        { var j = New<Jigger>("jigger.dual"); hands.TryPickUp(j); Assert.That(hands.Left, Is.SameAs(j)); }
        /// <summary>左手习惯时偏好手镜像，切换时手里的东西换到另一只手，并记录事件。</summary>
        [Test] public void LeftHandednessMirrorsPreferredHandsAndSwapsHeldItems()
        {
            var events = new System.Collections.Generic.List<string>(); hands.Changed += e => events.Add(e.Kind + ":" + e.Detail);
            Assert.That(hands.SetDominant(HandSide.Left), Is.True); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Left));
            var j = New<Jigger>("jigger.dual"); var b = New<Bottle>("bottle.gin"); hands.TryPickUp(j); hands.TryPickUp(b);
            Assert.That(hands.Right, Is.SameAs(j)); Assert.That(hands.Left, Is.SameAs(b)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Left));
            Assert.That(hands.SetDominant(HandSide.Right), Is.True); Assert.That(hands.Left, Is.SameAs(j)); Assert.That(hands.Right, Is.SameAs(b)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Right));
            Assert.That(events, Does.Contain("handedness_changed:Left")); Assert.That(events, Does.Contain("swapped:Right")); Assert.That(events.Last(), Is.EqualTo("handedness_changed:Right"));
            Assert.That(hands.SetDominant(HandSide.Right), Is.True, "已经是该惯用手时不变"); Assert.That(events.Last(), Is.EqualTo("handedness_changed:Right"));
        }
        [Test] public void FixturesAndAttachedPartsCannotBePickedUp()
        { Assert.That(hands.TryPickUp(New<IceBin>("fixture.icebin")), Is.False); var j = New<Jigger>("jigger.dual"); Assert.That(hands.TryPickUp(j.CupA), Is.False); }
        [Test] public void PickUpIsNotDuplicatedForTwoEmptyHands()
        { var options = resolver.Resolve(New<Bottle>("bottle.gin"), hands); Assert.That(options.Count(x => x.Action.Id == "item.pick_up"), Is.EqualTo(1)); }
        [Test] public void SecondHandsCannotStealAnAlreadyHeldItem()
        { var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); Assert.That(new Hands().TryPickUp(b), Is.False); }
        [Test] public void PlaceSetsSurfaceAndFreesCorrectHand()
        {
            var j = New<Jigger>("jigger.dual"); var counter = New<BarCounter>("fixture.counter"); hands.TryPickUp(j);
            var events = 0; hands.Changed += _ => events++;
            Assert.That(hands.TryPlace(HandSide.Left, counter), Is.True); Assert.That(hands.Left, Is.Null); Assert.That(j.Location.OwnerId, Is.EqualTo(counter.InstanceId)); Assert.That(events, Is.EqualTo(1));
            Assert.That(hands.TryPlace(HandSide.Left, counter), Is.False);
        }
        [Test] public void SwapOneItemAndBothItemsPreservesMainItem()
        {
            var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); Assert.That(hands.TrySwap(), Is.True); Assert.That(hands.Left, Is.SameAs(b));
            Assert.That(b.Location.Kind, Is.EqualTo(LocationKind.LeftHand)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Left));
            var j = New<Jigger>("jigger.dual"); hands.TryPickUp(j); Assert.That(hands.TrySwap(), Is.True); Assert.That(hands.Left, Is.SameAs(j)); Assert.That(hands.Right, Is.SameAs(b)); Assert.That(hands.MainHand, Is.EqualTo(HandSide.Left));
        }
        [Test] public void SwapRespectsAllowedHandsAndDoesNotMutateOnFailure()
        {
            var restrictedJson = json.Replace("\"allowedHands\": 3", "\"allowedHands\": 2").Replace("\"preferredHand\": 0", "\"preferredHand\": 1");
            var f = new ItemFactory(ItemCatalog.Load(restrictedJson, new[] { "spirit", "syrup", "lemon", "soda", "tonic" }), _ => 20, () => 0.5);
            var b = f.Create("bottle.gin", "restricted"); hands.TryPickUp(b); Assert.That(hands.TrySwap(), Is.False); Assert.That(hands.Right, Is.SameAs(b));
            Assert.That(hands.TryPickUp(f.Create("bottle.syrup", "second")), Is.False);
        }
        [Test] public void MainHandChoosesBetweenTwoBottleActionsAndMenuKeepsBoth()
        {
            var a = New<Bottle>("bottle.gin"); var b = New<Bottle>("bottle.syrup"); var target = New<Glass>("glass.coupe");
            hands.TryPickUp(a); hands.TryPickUp(b);
            var options = resolver.Resolve(target, hands); Assert.That(options.Count, Is.EqualTo(2)); Assert.That(options[0].Actor, Is.SameAs(b));
            hands.ToggleMain(); Assert.That(resolver.Primary(target, hands).Actor, Is.SameAs(a)); Assert.That(resolver.Resolve(target, hands).Count, Is.EqualTo(2));
        }
        [Test] public void SurfacePlacementMenuListsBothHands()
        {
            hands.TryPickUp(New<Bottle>("bottle.gin")); hands.TryPickUp(New<Jigger>("jigger.dual"));
            var choices = resolver.Resolve(New<BarCounter>("fixture.counter"), hands);
            Assert.That(choices.Count, Is.EqualTo(2)); Assert.That(choices.Select(c => c.Hand).Distinct().Count(), Is.EqualTo(2));
        }
        [Test] public void BottleCanTargetTheOtherHandsJiggerAndEmitsOnlyAProposal()
        {
            var j = New<Jigger>("jigger.dual"); var b = New<Bottle>("bottle.gin"); hands.TryPickUp(j); hands.TryPickUp(b);
            var selected = resolver.Primary(j, hands); Assert.That(selected.Action.InputStyle, Is.EqualTo(InputStyle.HoldTilt));
            var active = selected.Begin(hands); active.Update(new ActionInput(controls.FullFlowDegrees), 0.1);
            var request = active.Requests.Single(); Assert.That(request.TargetVesselId, Is.EqualTo(j.CupA.CoreVesselId)); Assert.That(request.RequestedMl, Is.EqualTo(4).Within(1e-12));
            Assert.That(request.ActualMl, Is.Null); Assert.That(b.RemainingMl, Is.EqualTo(750)); Assert.That(j.CupA.Contents.LiquidMl, Is.Zero); Assert.That(active.Events.Any(e => e.Kind == "operation_requested"), Is.True);
            active.End(true);
        }
        [Test] public void TiltStartDependsOnRemainingVolume()
        {
            var flow = new TiltFlowModel(controls); Assert.That(flow.StartAngle(750, 750), Is.EqualTo(controls.FullBottleStartDegrees));
            Assert.That(flow.StartAngle(0, 750), Is.EqualTo(controls.EmptyBottleStartDegrees));
            double angle = (controls.FullBottleStartDegrees + controls.EmptyBottleStartDegrees) / 2;
            Assert.That(flow.Flow(angle, 750, 750), Is.GreaterThan(0)); Assert.That(flow.Flow(angle, 75, 750), Is.Zero);
        }
        [TestCase(0)] [TestCase(35)] [TestCase(60)] [TestCase(120)] [TestCase(160)]
        public void TiltFlowIsBoundedAndMatchesDataCurve(double angle)
        {
            var flow = new TiltFlowModel(controls); double normalized = Math.Max(0, Math.Min(1, (angle - controls.FullBottleStartDegrees) / (controls.FullFlowDegrees - controls.FullBottleStartDegrees)));
            Assert.That(flow.Flow(angle, 750, 750), Is.EqualTo(controls.MaxFlowMlPerSecond * Math.Pow(normalized, controls.CurveExponent)).Within(1e-12));
        }
        [Test] public void SpoutRateIsFixedOnlyAfterStartAndEmptyStockCannotFlow()
        { var f = new TiltFlowModel(controls); Assert.That(f.Flow(120, 750, 750, 12), Is.EqualTo(12)); Assert.That(f.Flow(0, 750, 750, 12), Is.Zero); Assert.That(f.Flow(120, 0, 750, 12), Is.Zero); }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1)] [TestCase(181)]
        public void TiltRejectsInvalidAngles(double angle) => Assert.Throws<ArgumentOutOfRangeException>(() => new TiltFlowModel(controls).Flow(angle, 750, 750));
        [Test] public void CapacityReservationPreventsOverbookingAndConfirmationRecordsActual()
        {
            var b = New<Bottle>("bottle.gin"); var glass = New<Glass>("glass.coupe"); glass.ApplySnapshot(new ContainerSnapshot(179)); hands.TryPickUp(b);
            var active = resolver.Primary(glass, hands).Begin(hands); active.Update(new ActionInput(120), 1);
            var request = active.Requests.Single(); Assert.That(request.IntendedMl, Is.EqualTo(40)); Assert.That(request.RequestedMl, Is.EqualTo(1)); Assert.That(glass.AvailableMl, Is.Zero);
            active.Update(new ActionInput(120), 1); Assert.That(active.Requests.Count, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => glass.ApplySnapshot(new ContainerSnapshot(180)));
            request.Confirm(0.5, new ContainerSnapshot(179.5)); Assert.That(request.ActualMl, Is.EqualTo(0.5)); Assert.That(b.RemainingMl, Is.EqualTo(749.5)); Assert.That(glass.Contents.LiquidMl, Is.EqualTo(179.5));
            active.End(); Assert.That(hands.IsBusy, Is.False);
        }
        [Test] public void PendingActionLocksHandsUntilEndAndCancelReleasesReservations()
        {
            var b = New<Bottle>("bottle.gin"); var j = New<Jigger>("jigger.dual"); hands.TryPickUp(b);
            var active = resolver.Primary(j, hands).Begin(hands); active.Update(new ActionInput(120), 1);
            Assert.That(hands.TryPlace(HandSide.Right, New<BarCounter>("fixture.counter")), Is.False); Assert.That(hands.TrySwap(), Is.False); Assert.That(resolver.Resolve(j, hands), Is.Empty);
            active.End(true); Assert.That(active.Requests.Single().ActualMl, Is.Zero); Assert.That(j.CupA.AvailableMl, Is.EqualTo(30)); Assert.That(b.RemainingMl, Is.EqualTo(750));
            active.End(true); Assert.Throws<InvalidOperationException>(() => active.Update(new ActionInput(), 0.1));
        }
        [Test] public void InvalidAcknowledgementDoesNotPartiallyMutateOrRelease()
        {
            var b = New<Bottle>("bottle.gin"); var target = New<Glass>("glass.coupe"); hands.TryPickUp(b);
            var active = resolver.Primary(target, hands).Begin(hands); active.Update(new ActionInput(120), 0.1); var request = active.Requests.Single();
            Assert.Throws<ArgumentOutOfRangeException>(() => request.Confirm(5, new ContainerSnapshot(5)));
            Assert.Throws<ArgumentException>(() => request.Confirm(4, new ContainerSnapshot(181)));
            Assert.That(request.IsPending, Is.True); Assert.That(b.RemainingMl, Is.EqualTo(750)); Assert.That(target.Contents.LiquidMl, Is.Zero);
            request.Confirm(4, new ContainerSnapshot(4)); Assert.Throws<InvalidOperationException>(() => request.Confirm(4, new ContainerSnapshot(4))); active.End();
        }
        [Test] public void SmallRemainingStockClampsAndCannotBecomeNegative()
        {
            var b = New<Bottle>("bottle.gin"); var target = New<ShakerBody>("shaker.body"); hands.TryPickUp(b);
            var active = resolver.Primary(target, hands).Begin(hands);
            for (int i = 0; i < 2; i++) { active.Update(new ActionInput(120), 20); var r = active.Requests.Last(); double ml = r.RequestedMl; r.Confirm(ml, new ContainerSnapshot(ml)); target.ApplySnapshot(new ContainerSnapshot()); }
            Assert.That(b.RemainingMl, Is.Zero); active.End(); Assert.That(resolver.Primary(target, hands)?.Action.Id, Is.Not.EqualTo("pour.bottle_to_container"));
        }
        [Test] public void ClosedBottleFullGlassAndSealedShakerCannotReceiveAPour()
        {
            var b = New<Bottle>("bottle.soda"); b.SetOpen(false); var glass = New<Glass>("glass.coupe"); hands.TryPickUp(b);
            var action = new BottlePourAction(controls); Assert.That(action.CanExecute(b, glass, hands), Is.False); b.SetOpen(true); Assert.That(action.CanExecute(b, glass, hands), Is.True);
            glass.ApplySnapshot(new ContainerSnapshot(180)); Assert.That(action.CanExecute(b, glass, hands), Is.False);
            var shaker = New<CobblerShaker>("shaker.cobbler"); shaker.Attach(New<ShakerStrainer>("shaker.strainer")); Assert.That(action.CanExecute(b, shaker, hands), Is.False);
        }
        [Test] public void ServedGlassRejectsInput()
        { var g = New<Glass>("glass.coupe"); g.MarkServed(); var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); Assert.That(new BottlePourAction(controls).CanExecute(b, g, hands), Is.False); }
        [Test] public void ClickJiggerEmitsOneWholeCupTransferAndRequiresWholeCapacity()
        {
            var j = New<Jigger>("jigger.dual"); j.CupA.ApplySnapshot(new ContainerSnapshot(25)); var target = New<Glass>("glass.coupe"); hands.TryPickUp(j);
            var a = new JiggerPourAction(controls); Assert.That(a.CanExecute(j, j, hands), Is.False);
            target.ApplySnapshot(new ContainerSnapshot(160)); Assert.That(a.CanExecute(j, target, hands), Is.False); target.ApplySnapshot(new ContainerSnapshot());
            var active = a.Begin(j, target, hands); var request = active.Requests.Single(); Assert.That(request.RequestedMl, Is.EqualTo(25)); Assert.That(request.DurationSeconds, Is.EqualTo(0.6));
            active.Update(new ActionInput(), 0.6); Assert.That(active.Requests.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => request.Confirm(25, new ContainerSnapshot(25)));
            request.Confirm(25, new ContainerSnapshot(25), new ContainerSnapshot()); active.End(); Assert.That(j.CupA.Contents.LiquidMl, Is.Zero);
        }
        [Test] public void JiggerFlipChecksBothCupsAndTargetsTheActiveEnd()
        {
            var j = New<Jigger>("jigger.dual"); j.Flip(); Assert.That(j.ActiveEnd, Is.EqualTo(JiggerEnd.B)); Assert.That(j.CupA.IsOpenTop, Is.False);
            j.CupB.ApplySnapshot(new ContainerSnapshot(1)); Assert.Throws<InvalidOperationException>(() => j.Flip());
            j.CupB.ApplySnapshot(new ContainerSnapshot()); var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b);
            var active = new BottlePourAction(controls).Begin(b, j, hands); active.Update(new ActionInput(120), 0.1); Assert.That(active.Requests.Single().TargetVesselId, Is.EqualTo(j.CupB.CoreVesselId)); active.End();
        }
        [Test] public void ShakerStateMachineRejectsCapFirstAndStrainerRemovalUnderCap()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); var strainer = New<ShakerStrainer>("shaker.strainer"); var cap = New<ShakerCap>("shaker.cap");
            Assert.That(s.State, Is.EqualTo(ShakerState.Open)); Assert.That(s.IceCanPassOut, Is.True); Assert.That(s.CanShake, Is.False); Assert.Throws<InvalidOperationException>(() => s.Attach(cap));
            s.Attach(strainer); Assert.That(s.State, Is.EqualTo(ShakerState.StrainerOn)); Assert.That(s.CanShake, Is.False); Assert.That(s.IceCanPassOut, Is.False); Assert.That(s.Body.IsOpenTop, Is.False);
            s.Attach(cap); Assert.That(s.CanShake, Is.True); Assert.That(s.State, Is.EqualTo(ShakerState.Sealed)); Assert.Throws<InvalidOperationException>(() => s.Detach(strainer));
            s.Detach(cap); s.Detach(strainer); Assert.That(s.Body.IsOpenTop, Is.True); Assert.That(strainer.Parent, Is.Null);
        }
        [Test] public void AttachedPartCannotHaveTwoParents()
        {
            var a = New<CobblerShaker>("shaker.cobbler"); var b = New<CobblerShaker>("shaker.cobbler"); var part = New<ShakerStrainer>("shaker.strainer"); a.Attach(part);
            Assert.That(b.CanAttach(part), Is.False); Assert.Throws<InvalidOperationException>(() => b.Attach(part)); Assert.That(part.Parent, Is.SameAs(a));
        }
        [Test] public void AssemblyActionsTransferHeldPartsAndRemovalNeedsAFreeHand()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); var part = New<ShakerStrainer>("shaker.strainer"); hands.TryPickUp(s); hands.TryPickUp(part);
            var attach = resolver.Resolve(s, hands).Single(o => o.Action.Id == "shaker.attach_strainer"); attach.Begin(hands);
            Assert.That(hands.Right, Is.Null); Assert.That(part.Parent, Is.SameAs(s));
            var bottle = New<Bottle>("bottle.gin"); hands.TryPickUp(bottle); Assert.That(resolver.Resolve(s, hands).Any(o => o.Action.Id == "shaker.detach_strainer"), Is.False);
            hands.TryPlace(HandSide.Right, New<BarCounter>("fixture.counter")); resolver.Resolve(s, hands).Single(o => o.Action.Id == "shaker.detach_strainer").Begin(hands);
            Assert.That(hands.Right, Is.SameAs(part)); Assert.That(part.Parent, Is.Null); Assert.That(s.State, Is.EqualTo(ShakerState.Open));
        }
        [Test] public void SurfaceShakerHasPickupAndRemovalOptions()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); s.Attach(New<ShakerStrainer>("shaker.strainer")); s.Attach(New<ShakerCap>("shaker.cap"));
            var choices = resolver.Resolve(s, hands); Assert.That(choices.Select(c => c.Action.Id), Does.Contain("shaker.detach_cap")); Assert.That(choices.Select(c => c.Action.Id), Does.Contain("item.pick_up"));
        }
        [Test] public void CapActionsEnforceOrderAndMoveTheCapBackToAHand()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); var cap = New<ShakerCap>("shaker.cap"); hands.TryPickUp(cap);
            Assert.That(new AttachCapAction().CanExecute(cap, s, hands), Is.False);
            s.Attach(New<ShakerStrainer>("shaker.strainer"));
            resolver.Resolve(s, hands).Single(o => o.Action.Id == "shaker.attach_cap").Begin(hands);
            Assert.That(cap.Parent, Is.SameAs(s)); Assert.That(s.CanShake, Is.True);
            resolver.Resolve(s, hands).Single(o => o.Action.Id == "shaker.detach_cap").Begin(hands);
            Assert.That(hands.SideOf(cap), Is.Not.Null); Assert.That(cap.Parent, Is.Null); Assert.That(s.CanShake, Is.False);
        }
        [Test] public void HeldAttachmentMustBeReleasedByTheActionBeforeDirectAssembly()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); var part = New<ShakerStrainer>("shaker.strainer"); hands.TryPickUp(part);
            Assert.Throws<InvalidOperationException>(() => s.Attach(part)); Assert.That(hands.Right, Is.SameAs(part)); Assert.That(s.StrainerSlot.IsEmpty, Is.True);
        }
        [Test] public void AnotherHandsSessionCannotPourIntoAnOwnedTarget()
        {
            var j = New<Jigger>("jigger.dual"); new Hands().TryPickUp(j); var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b);
            Assert.That(new BottlePourAction(controls).CanExecute(b, j, hands), Is.False);
        }
        [Test] public void ShakeRequiresSealedAssemblyAndOtherHandFree()
        {
            var s = New<CobblerShaker>("shaker.cobbler"); hands.TryPickUp(s); s.Attach(New<ShakerStrainer>("shaker.strainer")); s.Attach(New<ShakerCap>("shaker.cap"));
            Assert.That(s.CanShake && hands.CanUse(s, "shaker.shake"), Is.True); hands.TryPickUp(New<Bottle>("bottle.gin")); Assert.That(hands.CanUse(s, "shaker.shake"), Is.False);
        }
        [Test] public void StaleMenuOptionRevalidatesBeforeBegin()
        {
            var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); var target = New<Glass>("glass.coupe"); var option = resolver.Primary(target, hands); b.SetOpen(false);
            Assert.Throws<InvalidOperationException>(() => option.Begin(hands));
        }
        [Test] public void ResolverRejectsDuplicateIdsAndSupportsCustomRegistration()
        {
            Assert.Throws<ArgumentException>(() => resolver.Register(new PickUpAction()));
            var custom = new ActionResolver().Register(new PickUpAction()); Assert.That(custom.Primary(New<Bottle>("bottle.gin"), hands).Action.Id, Is.EqualTo("item.pick_up"));
        }
        [TestCase(-1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidTimeDoesNotGenerateOrReserveRequests(double dt)
        {
            var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); var active = new BottlePourAction(controls).Begin(b, New<Glass>("glass.coupe"), hands);
            Assert.Throws<ArgumentOutOfRangeException>(() => active.Update(new ActionInput(120), dt)); Assert.That(active.Requests, Is.Empty); active.End();
        }
        [Test] public void ZeroTimeOrUprightBottleDoesNotEmitZeroVolumeRequests()
        {
            var b = New<Bottle>("bottle.gin"); hands.TryPickUp(b); var active = new BottlePourAction(controls).Begin(b, New<Glass>("glass.coupe"), hands);
            active.Update(new ActionInput(120), 0); active.Update(new ActionInput(0), 1); Assert.That(active.Requests, Is.Empty); active.End();
        }
        [TestCase(30)] [TestCase(60)] [TestCase(144)]
        public void ConfirmedTiltStreamIsIndependentOfInputSamplingRate(int hz)
        {
            var b = New<Bottle>("bottle.gin"); var glass = New<Glass>("glass.coupe"); hands.TryPickUp(b); var active = new BottlePourAction(controls).Begin(b, glass, hands); double total = 0;
            for (int i = 0; i < hz; i++) { active.Update(new ActionInput(120), 1.0 / hz); var request = active.Requests.Last(); total += request.RequestedMl; request.Confirm(request.RequestedMl, new ContainerSnapshot(total)); }
            active.End(); Assert.That(total, Is.EqualTo(40).Within(1e-10)); Assert.That(b.RemainingMl, Is.EqualTo(710).Within(1e-10));
        }
        [Test] public void SnapshotsAndDataCollectionsCannotBeMutatedByCallers()
        {
            var cup = New<JiggerCup>("jigger.cup30"); Assert.Throws<ArgumentException>(() => cup.ApplySnapshot(new ContainerSnapshot(31)));
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<double>)cup.MarkingsMl)[0] = 99);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerSnapshot(double.NaN));
        }
        [Test] public void DataRejectsUnknownIngredientsBadReferencesAndCycles()
        {
            Assert.Throws<ArgumentException>(() => ItemCatalog.Load(json, new[] { "spirit" }));
            Assert.Throws<ArgumentException>(() => ItemCatalog.Load(json.Replace("\"shaker.body\"", "\"missing.body\"", StringComparison.Ordinal).Replace("\"id\": \"missing.body\"", "\"id\": \"shaker.body\""), new[] { "spirit", "syrup", "lemon", "soda", "tonic" }));
            Assert.Throws<ArgumentException>(() => ItemCatalog.Load(json.Replace("\"parts\": [\n        \"shaker.body\"", "\"parts\": [\n        \"shaker.cobbler\""), new[] { "spirit", "syrup", "lemon", "soda", "tonic" }));
        }
        [Test] public void InvalidControlsAreRejectedBeforeGameplay()
        {
            Assert.Throws<ArgumentException>(() => ItemCatalog.LoadControls(controlJson.Replace("\"fullFlowDegrees\": 120", "\"fullFlowDegrees\": 30")));
            Assert.Throws<ArgumentOutOfRangeException>(() => ItemCatalog.LoadControls(controlJson.Replace("\"maxFlowMlPerSecond\": 40", "\"maxFlowMlPerSecond\": -1")));
        }
        [Test] public void DataRejectsUnknownRuntimeTypesAndWrongCompositeStructure()
        {
            var ingredients = new[] { "spirit", "syrup", "lemon", "soda", "tonic" };
            Assert.Throws<ArgumentException>(() => ItemCatalog.Load(json.Replace("\"type\": \"Bottle\"", "\"type\": \"Unknown\""), ingredients));
            Assert.Throws<ArgumentException>(() => ItemCatalog.Load(json.Replace("\"parts\": [\n        \"shaker.body\"", "\"parts\": [\n        \"shaker.cap\""), ingredients));
        }
        [Test] public void FactoryRejectsDuplicateInstanceIdsAndUnknownDefinitions()
        { factory.Create("bottle.gin", "same"); Assert.Throws<ArgumentException>(() => factory.Create("bottle.gin", "same")); Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => factory.Create("unknown", "new")); }
        [Test] public void LemonYieldIsSampledFromDataOnceAndToolsHaveRequiredState()
        {
            var lemon = New<LemonHalf>("produce.lemon_half"); Assert.That(lemon.JuiceYieldMl, Is.EqualTo(25)); Assert.That(lemon.JuiceRemainingMl, Is.EqualTo(25)); Assert.That(lemon.IsSpent, Is.False);
            Assert.That(New<Squeezer>("tool.squeezer").LoadedProduce, Is.Null); Assert.That(New<IceScoop>("tool.scoop").CubesPerScoop, Is.EqualTo((3, 5))); Assert.That(New<IceTongs>("tool.tongs").CubesPerGrip, Is.EqualTo(1));
        }
        [Test] public void GestureAnalyzerRejectsJitterCountsOscillationAndResets()
        {
            var g = new GestureAnalyzer(controls); for (int i = 0; i < 10; i++) g.Update(0, i % 2, 0.1); Assert.That(g.OscillationIntensity, Is.Zero);
            g.Reset(); g.Update(0, 0, 0.1); g.Update(0, 20, 0.1); g.Update(0, -20, 0.1); g.Update(0, 20, 0.1); Assert.That(g.OscillationIntensity, Is.GreaterThan(0));
            g.Reset(); Assert.That(g.OscillationIntensity, Is.Zero); Assert.That(g.CircleIntensity, Is.Zero);
        }
        // a player presses first and then draws the circle, so the press point lies ON the circle.
        // Measured about a moving centre (CircleTracker) a 1 turn/s circle reads close to 1 turn/s at any frame rate, wherever it is drawn.
        private double Circle(GestureAnalyzer g, int rate, double turnsPerSecond, double seconds, double radius, double driftPerSecond = 0, bool pressOnCircle = true)
        {
            for (int i = 0; i <= seconds * rate; i++)
            {
                double t = i / (double)rate, angle = 2 * Math.PI * turnsPerSecond * t + Math.PI, cx = pressOnCircle ? radius : 0;
                g.Update(cx + radius * Math.Cos(angle) + driftPerSecond * t, radius * Math.Sin(angle), i == 0 ? 1e-12 : 1.0 / rate);
            }
            return g.CircleIntensity * controls.MaxCirclesPerSecond;
        }
        [TestCase(30)] [TestCase(60)] [TestCase(144)]
        public void CircleAndPressMeasurementsAreRateIndependent(int hz)
        {
            var reference = Circle(new GestureAnalyzer(controls), 60, 1, 4, 40);
            Assert.That(Circle(new GestureAnalyzer(controls), hz, 1, 4, 40), Is.EqualTo(reference).Within(.03), "帧率不改变圈速");
            Assert.That(Circle(new GestureAnalyzer(controls), hz, 1, 4, 40, pressOnCircle: false), Is.EqualTo(reference).Within(.03), "圆心在不在按下点，读数都一样");
            var g = new GestureAnalyzer(controls); Assert.That(g.Press(50), Is.EqualTo(0.5)); Assert.That(g.Press(-20), Is.Zero); Assert.That(g.Press(200), Is.EqualTo(1));
        }
        [Test] public void CircleDrawnFromThePressPointReadsTheTrueTurnRate()
        {
            foreach (var turns in new[] { .6, 1.0, 2.0 })
            {
                Assert.That(Circle(new GestureAnalyzer(controls), 60, turns, 4, 40), Is.InRange(turns * .88, turns * 1.01), turns + " 圈/秒，按下点在圆周上");
                Assert.That(Circle(new GestureAnalyzer(controls), 60, turns, 4, 40, driftPerSecond: 40), Is.InRange(turns * .85, turns * 1.01), turns + " 圈/秒，圈慢慢漂移");
            }
            var rng = new System.Random(7); var jitter = new GestureAnalyzer(controls);
            for (int i = 0; i < 240; i++) jitter.Update(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, 1.0 / 60);
            Assert.That(jitter.CircleIntensity, Is.Zero, "原地手抖不算画圈");
            var sweep = new GestureAnalyzer(controls);
            for (int i = 0; i < 240; i++) sweep.Update(150 * Math.Sin(2 * Math.PI * i / 60.0), 0, 1.0 / 60);
            Assert.That(sweep.CircleIntensity, Is.LessThan(.05), "左右直线来回扫不算画圈");
        }
        [Test] public void PureAssembliesHaveNoEngineReferencesAndDependencyDirectionIsDownward()
        {
            foreach (var type in new[] { typeof(Item), typeof(ItemCatalog) }) Assert.That(type.Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEngine") || a.Name.StartsWith("UnityEditor")), Is.False);
            Assert.That(typeof(Item).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Bar.Unity"), Is.False);
            Assert.That(typeof(ItemCatalog).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Bar.Domain"), Is.False);
        }
    }
}
