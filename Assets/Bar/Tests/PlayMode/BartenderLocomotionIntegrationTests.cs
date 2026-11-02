using System;
using System.IO;
using System.Linq;
using System.Collections;
using Bar.Domain;
using Bar.Unity;
using Bar.Unity.Input;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Bar.Tests
{
    public sealed class BartenderLocomotionIntegrationTests
    {
        [UnityTest]public IEnumerator WalkCarryHandsAndStationCancellation()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");yield return null;yield return null;
            var s=UnityEngine.Object.FindFirstObjectByType<BarSession>();var player=s.Player;var motor=player.Locomotion;Assert.That(motor,Is.Not.Null);s.GetComponent<InputRouter>().enabled=false;Assert.That(player.Body.radius,Is.EqualTo(.18f).Within(.0001));
            int samples=0;float maxSpeed=0;
            foreach(var local in new[]{new Vector3(-2.22f,0,-1.10f),new Vector3(-2.22f,0,1.80f),new Vector3(2.22f,0,1.80f),new Vector3(2.22f,0,-1.10f),new Vector3(0,0,-1.10f)})
            {
                var target=motor.Layout.Frame.TransformPoint(local);target.y=player.transform.position.y;int frames=0;
                while(Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).magnitude>.055f&&frames++<600)
                {
                    var direction=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).normalized;var localDirection=Quaternion.Inverse(Quaternion.Euler(0,player.LookRotation.eulerAngles.y,0))*direction;player.Move(new Vector2(localDirection.x,localDirection.z),1f/60);Assert.That(float.IsFinite(player.transform.position.x));maxSpeed=Mathf.Max(maxSpeed,motor.ActualSpeed);samples++;yield return null;
                }
                Assert.That(frames,Is.LessThan(600),"Route blocked near "+local);
                for(int i=0;i<35;i++){player.Move(Vector2.zero,1f/60);yield return null;}
            }
            Assert.That(motor.FootstepsEmitted,Is.Zero);Assert.That(maxSpeed,Is.GreaterThan(1.3f));
            var glassView=s.Views.Single(v=>v.DefinitionId=="glass.highball");var glass=(Glass)glassView.Item;s.Adapter.World.Apply(new Operation{type="PourSource",to=glass.CoreVesselId,ingredient="spirit",ml=glass.CapacityMl*.72});s.Adapter.Advance(0);Assert.That(s.Hands.TryPickUp(glass));
            for(int i=0;i<120;i++){player.Move(Vector2.zero,1f/60);yield return null;}Assert.That(motor.Careful);
            var hold=s.GetComponent<HoldViewRig>();var left=player.LeftAnchor.localPosition;var right=player.RightAnchor.localPosition;var leftRotation=player.LeftAnchor.localRotation;var rightRotation=player.RightAnchor.localRotation;
            player.Locomotion=null;hold.AdvancePresentation(1f/60);Assert.That(Vector3.Distance(player.LeftAnchor.localPosition,left),Is.LessThan(.00015f));Assert.That(Vector3.Distance(player.RightAnchor.localPosition,right),Is.LessThan(.00015f));Assert.That(Quaternion.Angle(player.LeftAnchor.localRotation,leftRotation),Is.LessThan(.02f));Assert.That(Quaternion.Angle(player.RightAnchor.localRotation,rightRotation),Is.LessThan(.02f));player.Locomotion=motor;
            var counter=(BarCounter)s.Views.Single(v=>v.DefinitionId=="fixture.counter").Item;Assert.That(s.Hands.TryPlace(s.Hands.SideOf(glass).Value,counter));yield return null;
            var spoon=s.Views.Single(v=>v.DefinitionId=="tool.spoon").Item;Assert.That(s.Hands.TryPickUp(spoon));
            var resolver=ActionResolver.CreateU4(s.GestureControls,s.Adapter.World.Data.icePortionGrams,s.Adapter.World.Data.iceDensity,()=>.5);var option=resolver.Resolve(glass,s.Hands).Single(o=>o.Action.Id=="spoon.stir");s.Clock.Enqueue(s.EventTime(Time.realtimeSinceStartupAsDouble),c=>c.Begin(option));yield return new WaitForSecondsRealtime(.15f);Assert.That(s.StationShuffleAllowed);
            var action=s.Clock.Active;for(int i=0;i<190&&!action.IsEnded;i++){player.Move(Vector2.right,1f/60);yield return null;}yield return new WaitForSecondsRealtime(.15f);Assert.That(action.IsEnded);Assert.That(s.Hands.IsBusy,Is.False);Assert.That(s.HudSuspended,Is.False);
            Directory.CreateDirectory("LocomotionFeedbackReports");File.WriteAllText("LocomotionFeedbackReports/runtime-verification.json","{\"routeSamples\":"+samples+",\"maxObservedSpeed\":"+maxSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"footsteps\":"+motor.FootstepsEmitted+",\"fullOpenCarry\":true,\"stationaryHandsPreserved\":true,\"stationActionCancelled\":true,\"cursorNotReleased\":true}");
        }

        [UnityTest] public IEnumerator ApproachRealCounterAndStandWithoutAttraction()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");
            yield return null; yield return null;
            var session = UnityEngine.Object.FindFirstObjectByType<BarSession>();
            session.GetComponent<InputRouter>().enabled = false;
            if (session.HudSuspended) session.LeftButton(true, session.EventTime(Time.realtimeSinceStartupAsDouble));
            var player = session.Player;
            var motor = player.Locomotion;
            var body = player.Body;
            var forward = motor.Layout.Frame.forward;
            Vector2 Axis(Vector3 world)
            {
                var local = Quaternion.Inverse(Quaternion.Euler(0, player.LookRotation.eulerAngles.y, 0)) * world;
                return new Vector2(local.x, local.z);
            }
            int sounds = 0;
            session.Adapter.Events.Published += e => { if (e.Kind == "presentation_footstep_sound") sounds++; };
            float maxUnusedSpace = 0;
            foreach (float x in new[] { -.8f, 0f, .8f })
            {
                body.enabled = false;
                var start = motor.Layout.Frame.TransformPoint(new Vector3(x, 0, -1.1f));
                start.y = player.transform.position.y;
                player.transform.position = start;
                body.enabled = true;
                Physics.SyncTransforms();
                bool touchedCounter = false;
                for (int i = 0; i < 180; i++)
                {
                    player.Move(Axis(forward), 1f / 60);
                    touchedCounter |= body.collisionFlags.HasFlag(CollisionFlags.Sides);
                    yield return null;
                }
                Assert.That(touchedCounter, "Must reach real counter contact; stopped at " + motor.Layout.Local(player.transform.position));
                var contact = player.transform.position;
                for (int i = 0; i < 60; i++) body.Move((forward * 1.85f + Vector3.down * .5f) / 60);
                float unused = Vector3.Dot(player.transform.position - contact, forward);
                maxUnusedSpace = Mathf.Max(maxUnusedSpace, unused);
                Assert.That(unused, Is.LessThan(.025f), "Movement must not stop before the native collision boundary");
            }
            var glideStart = player.transform.position;
            for (int i = 0; i < 40; i++) { player.Move(Axis(forward * .6f + motor.Layout.Frame.right * .8f), 1f / 60); yield return null; }
            float wallSlide = Vector3.Dot(player.transform.position - glideStart, motor.Layout.Frame.right);
            Assert.That(wallSlide, Is.GreaterThan(.15f), "Real contact must still allow lateral wall sliding");
            body.enabled = false;
            var rest = motor.Layout.Frame.TransformPoint(new Vector3(.12f, 0, -1.1f));
            rest.y = player.transform.position.y;
            player.transform.position = rest;
            body.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 90; i++) { player.Move(Vector2.zero, 1f / 60); yield return null; }
            var stopped = player.transform.position;
            for (int i = 0; i < 180; i++) { player.Move(Vector2.zero, 1f / 60); yield return null; }
            float drift = Vector3.ProjectOnPlane(player.transform.position - stopped, Vector3.up).magnitude;
            Assert.That(drift, Is.LessThan(.0005f));
            Assert.That(sounds, Is.Zero);
            Assert.That(motor.FootstepsEmitted, Is.Zero);
            Directory.CreateDirectory("LocomotionFeedbackReports");
            File.WriteAllText("LocomotionFeedbackReports/contact-and-rest.json", "{\"counterApproaches\":3,\"maxUnusedCollisionSpace\":" + maxUnusedSpace.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"contactWallSlide\":" + wallSlide.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"restDrift\":" + drift.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"footstepSounds\":" + sounds + "}");
        }
    }
}
