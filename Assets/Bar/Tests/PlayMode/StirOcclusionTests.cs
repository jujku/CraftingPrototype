using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bar.Tests
{
    public sealed class StirOcclusionTests
    {
        [UnityTest] public IEnumerator TableVesselSharesSpoonDepthUntilReturnCompletes()
        {
            foreach (string id in new[] { "shaker.cobbler", "glass.highball" })
            {
                yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/BarPresentation.unity");
                yield return null; yield return null;
                var s = Object.FindFirstObjectByType<BarSession>();
                foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (b is PlayerController || b.GetType().Name == "InputRouter") b.enabled = false;
                var receiver = s.Views.Single(v => v.DefinitionId == id);
                var spoon = s.Views.Single(v => v.Item is BarSpoon);
                var container = receiver.Item is CobblerShaker shaker ? shaker.Body : (Container)receiver.Item;
                s.Adapter.World.Apply(new Operation { type = "PourSource", to = container.CoreVesselId, ingredient = "spirit", ml = 60 });
                s.Adapter.Advance(0);
                var originals = receiver.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.gameObject.layer);
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).ToDictionary(l => l, l => (l.intensity, l.color, l.shadows, l.transform.position, l.transform.rotation));
                var camera = s.Player.ViewCamera;
                var point = receiver.transform.position + Vector3.up * .10f;
                var radial = Vector3.ProjectOnPlane(camera.transform.position - point, Vector3.up).normalized;
                s.Player.transform.position += point + radial * .40f + Vector3.up * .32f - camera.transform.position;
                s.Player.AimAt(point); camera.fieldOfView = 48;
                Assert.That(s.Hands.TryPickUp(spoon.Item), Is.True);
                yield return new WaitForSecondsRealtime(.6f);
                s.Clock.Begin(new ActionResolver().Register(new StirAction(s.GestureControls)).Resolve(receiver.Item, s.Hands).Single());
                var foreground = s.GetComponent<HeldItemRendering>();
                var layers = PlayerEnvironmentSettings.Load();
                float start = Time.realtimeSinceStartup; bool entered = false;
                while (Time.realtimeSinceStartup - start < 1.2f)
                {
                    float phase = (Time.realtimeSinceStartup - start) * 9;
                    s.Clock.SetGesture(80 * Mathf.Cos(phase), 80 * Mathf.Sin(phase), 0);
                    yield return null; foreground.Refresh();
                    if (s.StirDisplay.Target != null) entered = true;
                }
                Assert.That(entered, Is.True);
                string output = Path.GetFullPath("StirOcclusionReports"); Directory.CreateDirectory(output);
                Object.Destroy(PourCaptureTests.Capture(camera, "stir-occlusion-" + id));
                File.Copy(Path.Combine(PourCaptureTests.Output, "stir-occlusion-" + id + ".png"), Path.Combine(output, id + ".png"), true);
                Assert.That(receiver.gameObject.layer, Is.EqualTo(layers.Held), "Table receiver must share spoon depth, not be erased by foreground depth clear");
                foreach (var pair in originals)
                    Assert.That(pair.Key.gameObject.layer, Is.EqualTo(pair.Value == 31 ? layers.Liquid : layers.Held));
                // Render-request shader warmup can make the next real frame longer than the
                // whole return animation. Step release deterministically to check its depth lifetime.
                s.enabled = false; s.Clock.Stop();
                s.StirDisplay.Apply(s, null, null, null, false, 0, 0, 1f / 60); foreground.Refresh();
                Assert.That(s.StirDisplay.StageName, Is.EqualTo("Return"));
                Assert.That(receiver.gameObject.layer, Is.EqualTo(layers.Held), "Keep wall occlusion while lifting the spoon out");
                for (int frame = 0; frame < 240 && s.StirDisplay.Target != null; frame++)
                    s.StirDisplay.Apply(s, null, null, null, false, 0, 0, 1f / 60);
                Assert.That(s.StirDisplay.Target, Is.Null); foreground.Refresh();
                foreach (var pair in originals) Assert.That(pair.Key.gameObject.layer, Is.EqualTo(pair.Value));
                foreach (var pair in lights)
                    Assert.That((pair.Key.intensity, pair.Key.color, pair.Key.shadows, pair.Key.transform.position, pair.Key.transform.rotation), Is.EqualTo(pair.Value));
                Assert.That(spoon.gameObject.layer, Is.EqualTo(layers.Held), "Spoon remains held after release");
            }
        }
    }
}
