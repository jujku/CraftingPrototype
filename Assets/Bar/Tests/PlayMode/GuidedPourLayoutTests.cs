using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bar.Tests
{
    public sealed class GuidedPourLayoutTests
    {
        private static string Output => Path.GetFullPath("GuidedPourLayoutReports");

        [UnityTest]
        public IEnumerator MirroredHandsStayTransverseAndBottleClearsGlass()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");
            yield return null; yield return null;
            var session = Object.FindFirstObjectByType<BarSession>();
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b is PlayerController || b.GetType().Name == "InputRouter") b.enabled = false;
            var rig = session.GetComponent<HoldViewRig>();
            var camera = session.Player.ViewCamera;
            var originalCamera = new Pose(camera.transform.position, camera.transform.rotation);
            var glass = session.Views.Single(v => v.DefinitionId == "glass.highball");
            var spoon = session.Views.Single(v => v.DefinitionId == "tool.spoon");
            var counter = (BarCounter)session.Views.Single(v => v.DefinitionId == "fixture.counter").Item;
            var binding = PourPose.Binding(glass, (Glass)glass.Item);
            var rows = new List<string> { "bottle,hand,fps,yaw,maxPenetrationMetres" };
            int cases = 0, frames = 0;
            Directory.CreateDirectory(Output);

            foreach (string id in new[] { "bottle.tonic", "bottle.soda" })
            foreach (HandSide side in new[] { HandSide.Right, HandSide.Left })
            {
                var bottle = session.Views.Single(v => v.DefinitionId == id);
                session.enabled = true; rig.enabled = true;
                camera.transform.SetPositionAndRotation(originalCamera.position, originalCamera.rotation);
                Assert.That(session.Hands.SetDominant(side));
                ((Bottle)bottle.Item).SetOpen(true);
                Assert.That(session.Hands.TryPickUp(bottle.Item));
                Assert.That(session.Hands.TryPickUp(spoon.Item));
                yield return new WaitForSecondsRealtime(.5f);
                session.enabled = false; rig.enabled = false;
                var bottlePosition = camera.transform.InverseTransformPoint(bottle.transform.position);
                var bottleRotation = Quaternion.Inverse(camera.transform.rotation) * bottle.transform.rotation;
                var spoonPosition = camera.transform.InverseTransformPoint(spoon.transform.position);
                var spoonRotation = Quaternion.Inverse(camera.transform.rotation) * spoon.transform.rotation;
                var shell = bottle.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "GlassShell");
                Assert.That(shell.sharedMesh.isReadable);
                var vertices = shell.sharedMesh.vertices;

                foreach (int fps in new[] { 30, 60, 144 })
                foreach (float yaw in new[] { -40f, 0f, 40f })
                {
                    var mouth = binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x, binding.Shape.Top, binding.Shape.Axis.y));
                    var towardPlayer = Vector3.ProjectOnPlane(originalCamera.position - mouth, Vector3.up).normalized;
                    camera.transform.position = mouth + Quaternion.Euler(0, yaw, 0) * towardPlayer * .55f + Vector3.up * .40f;
                    camera.transform.LookAt(mouth - Vector3.up * .04f);
                    void Home()
                    {
                        bottle.transform.SetPositionAndRotation(camera.transform.TransformPoint(bottlePosition), camera.transform.rotation * bottleRotation);
                        spoon.transform.SetPositionAndRotation(camera.transform.TransformPoint(spoonPosition), camera.transform.rotation * spoonRotation);
                    }
                    Home();
                    var pose = new GuidedPourPose(session, bottle, glass);
                    float penetration = 0;
                    for (int i = 0; i < fps * 4; i++)
                    {
                        float time = (float)i / fps;
                        float angle = time < 1.3f ? 0 : time < 2f ? Mathf.Lerp(0, 125, (time - 1.3f) / .7f) : time < 2.7f ? 125 : Mathf.Lerp(125, 0, (time - 2.7f) / .5f);
                        Home(); pose.Step(1f / fps, angle, 0, (Glass)glass.Item); frames++;
                        foreach (var vertex in vertices)
                        {
                            var local = binding.transform.InverseTransformPoint(shell.transform.TransformPoint(vertex));
                            if (local.y < binding.Shape.Bottom || local.y > binding.Shape.Top) continue;
                            float radius = binding.Shape.RadiusAt(local.y) + .002f;
                            float distance = new Vector2(local.x - binding.Shape.Axis.x, local.z - binding.Shape.Axis.y).magnitude;
                            penetration = Mathf.Max(penetration, radius - distance);
                        }
                        Assert.That(penetration, Is.LessThan(.001f), id + " " + side + " fps=" + fps + " yaw=" + yaw + " t=" + time);
                        if (time >= 1.2f)
                        {
                            var tip = spoon.Anchor("Tip").position;
                            var grip = HoldViewRig.Grip(spoon, side == HandSide.Left ? HandSide.Right : HandSide.Left).position;
                            var rimCrossing = Vector3.Lerp(tip, grip, (mouth.y - tip.y) / (grip.y - tip.y));
                            Assert.That(Vector3.ProjectOnPlane(rimCrossing - mouth, Vector3.up).magnitude,
                                Is.LessThan(binding.Shape.RadiusAt(binding.Shape.Top) - .001f), "Spoon shaft must enter through the opening");
                        }
                        if (i == fps * 2 + fps / 2)
                        {
                            var right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
                            float sign = side == HandSide.Right ? 1 : -1;
                            var center = shell.GetComponent<MeshRenderer>().bounds.center;
                            var grip = HoldViewRig.Grip(spoon, side == HandSide.Left ? HandSide.Right : HandSide.Left).position;
                            Assert.That(Vector3.Dot(center - mouth, right) * sign, Is.GreaterThan(.025f));
                            Assert.That(Vector3.Dot(grip - mouth, right) * sign, Is.LessThan(-.025f));
                            var across = Vector3.ProjectOnPlane(center - grip, Vector3.up).normalized;
                            var look = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                            Assert.That(Mathf.Abs(Vector3.Dot(across, look)), Is.LessThan(.15f), "Hands must be across the view, not in depth");
                        }
                    }
                    rows.Add(id + "," + side + "," + fps + "," + yaw + "," + penetration.ToString("R", CultureInfo.InvariantCulture));
                    cases++;
                }
                Assert.That(session.Hands.TryPlace(session.Hands.SideOf(spoon.Item).Value, counter));
                Assert.That(session.Hands.TryPlace(session.Hands.SideOf(bottle.Item).Value, counter));
            }
            camera.transform.SetPositionAndRotation(originalCamera.position, originalCamera.rotation);
            File.WriteAllLines(Path.Combine(Output, "geometry.csv"), rows);
            File.WriteAllText(Path.Combine(Output, "geometry.json"), "{\"cases\":" + cases + ",\"frames\":" + frames + ",\"bothHands\":true,\"transverseLayout\":true,\"bottleGlassPenetrationBelow1mm\":true}");
        }
    }
}
