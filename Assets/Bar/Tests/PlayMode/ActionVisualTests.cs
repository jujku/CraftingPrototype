using System.Collections;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bar.Tests
{
    internal sealed class ActionVisualTests
    {
        private BarSession session;
        private double Now() => session.EventTime(Time.realtimeSinceStartupAsDouble);
        private ItemView View(string id) => session.Views.Where(v => v.DefinitionId == id).OrderBy(v => v.InstanceId).First();
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
            yield return null;
            session = Object.FindFirstObjectByType<BarSession>();
        }
        private void Aim(ItemView view)
        {
            if(view.Item is Bottle){CapSteps.Aim(session,view);return;}
            session.Player.AimAt(view.GetComponent<Collider>().bounds.center);
            Physics.SyncTransforms(); session.Targeting();
            Assert.That(session.Target,Is.SameAs(view));
        }
        private IEnumerator Pick(string id)
        {
            if (View(id).Item is Bottle) yield return CapSteps.Uncap(session, View(id));
            Aim(View(id)); session.RightButton(Now());
            yield return new WaitForSecondsRealtime(.24f);
            Assert.That(session.Hands.SideOf(View(id).Item).HasValue,Is.True);
        }
        private void Start(string action, ItemView actor, ItemView target)
        {
            if (!session.Hands.SideOf(target.Item).HasValue) Aim(target); else session.Targeting();
            int index = session.MenuOptions.ToList().FindIndex(o => o.Action.Id == action && o.Actor == actor.Item &&
                (o.Target == target.Item || o.Target.Parent == target.Item));
            Assert.That(index,Is.GreaterThanOrEqualTo(0),action + " " + session.Clock.Error);
            session.SelectMenu(index,true); session.LeftButton(true,Now());
        }
        private IEnumerator Fill(ItemView target)
        {
            Start("pour.bottle_to_container",View("bottle.gin"),target);
            session.PointerDelta(new Vector2(0,1000),Now()+.001);
            yield return new WaitForSecondsRealtime(.35f);
            session.LeftButton(false,Now());
            yield return new WaitForSecondsRealtime(.23f);
            Assert.That(session.Clock.Error,Is.Null);
        }
        private void Capture(string file, ItemView frame = null)
        {
            var camera = session.Player.ViewCamera;
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            if (frame)
            {
                var renderers = frame.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.name != "InnerCavity" && r.name != "LiquidVolume").ToArray();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                camera.transform.position = bounds.center + new Vector3(-.12f,.10f,-.32f);
                camera.transform.LookAt(bounds.center);
            }
            var rt = RenderTexture.GetTemporary(1000,750,24);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(1000,750,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1000,750),0,0); image.Apply();
                string folder = Path.Combine(Application.dataPath,"../ActionVisualReports"); Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,file+".png"),image.EncodeToPNG()); Object.Destroy(image);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); camera.transform.SetPositionAndRotation(position,rotation); }
        }
        [UnityTest] public IEnumerator JiggerFlipAndPourAnimatePivotPreserveImportedTransformAndTransferOnce()
        {
            yield return Load();
            yield return Pick("jigger.dual");
            var view = View("jigger.dual"); var jigger = (Jigger)view.Item;
            view.JiggerFlipSeconds = .5f;
            var orientation = view.transform.Find("Orientation");
            var imported = orientation.GetComponentsInChildren<Transform>().First(t => t.name == "Visual");
            var originalImportRotation = imported.localRotation; var originalImportScale = imported.localScale;
            var original = orientation.localRotation;
            Start("jigger.flip",view,view); session.LeftButton(false,Now()+.001);
            yield return new WaitUntil(() => jigger.ActiveCup.NominalMl == 30);
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(view.IsAnimating,Is.True);
            Assert.That(Quaternion.Angle(original,orientation.localRotation),Is.InRange(1f,179f));
            Assert.That(session.MenuOptions.Any(o => o.Actor == jigger || o.Target == jigger),Is.False);
            Capture("jigger-flipping");
            yield return new WaitForSecondsRealtime(.55f);
            Assert.That(view.IsAnimating,Is.False);
            var active = view.GetComponentsInChildren<ContainerVisual>().Single(c => c.DefinitionId == jigger.ActiveCup.Definition.Id);
            Assert.That(Vector3.Dot(active.transform.up,view.transform.up),Is.GreaterThan(.99f));
            Assert.That(jigger.CupA.Contents.LiquidMl+jigger.CupB.Contents.LiquidMl,Is.Zero);
            yield return Pick("bottle.gin"); yield return Fill(view);
            double liquid = jigger.ActiveCup.Contents.LiquidMl;
            Assert.That(liquid,Is.GreaterThan(0));
            var upright = orientation.localRotation;
            Start("pour.jigger_to_container",view,View("shaker.cobbler")); session.LeftButton(false,Now()+.001);
            yield return new WaitUntil(() => view.IsAnimating);
            yield return new WaitForSecondsRealtime(.24f);
            yield return new WaitForFixedUpdate(); // Sample the completed previous LateUpdate pose, before the next hand reset.
            Assert.That(Quaternion.Angle(upright,orientation.localRotation),Is.GreaterThan(40));
            Assert.That(Vector3.Distance(view.PourPoint,View("shaker.cobbler").PourPoint),Is.LessThan(.13f));
            Capture("jigger-pouring");
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(view.IsAnimating,Is.False);
            Assert.That(Quaternion.Angle(upright,orientation.localRotation),Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(imported.localRotation,originalImportRotation),Is.LessThan(.05f));
            Assert.That(imported.localScale,Is.EqualTo(originalImportScale));
            Assert.That(jigger.ActiveCup.Contents.LiquidMl,Is.Zero);
            Assert.That(((CobblerShaker)View("shaker.cobbler").Item).Body.Contents.LiquidMl,Is.EqualTo(liquid).Within(1e-7));
            Assert.That(session.Clock.Error,Is.Null);
        }
        [UnityTest] public IEnumerator SpoonEntersHighballAndReturnsToEditedGripOnRelease() => Stir("glass.highball");
        [UnityTest] public IEnumerator SpoonEntersOpenShakerAndReturnsToEditedGripOnRelease() => Stir("shaker.cobbler");
        private IEnumerator Stir(string id)
        {
            yield return Load(); yield return Pick("bottle.gin"); yield return Fill(View(id)); yield return Pick("tool.spoon");
            var spoon = View("tool.spoon"); var target = View(id);
            var tip = spoon.Anchor("Tip"); var grip = spoon.Anchor("Grip_R");
            var edited = grip.localPosition + new Vector3(.002f,.003f,-.001f); grip.localPosition = edited;
            var binding = target.GetComponentsInChildren<ContainerVisual>().Single();
            var container = target.Item is CobblerShaker shaker ? shaker.Body : (Container)target.Item;
            double liquid = container.Contents.LiquidMl;
            Start("spoon.stir",spoon,target);
            // 2026-10-28 (StirMotion): the spoon first moves over the mouth and drops in (enterSeconds 0.32 s), then stirs.
            yield return new WaitForSecondsRealtime(.45f);
            var first = tip.position;
            for (int i = 0; i < 18; i++)
            {
                float angle = i * .5f;
                session.PointerDelta(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*8,Now());
                yield return new WaitForSecondsRealtime(.025f);
                var local = binding.transform.InverseTransformPoint(tip.position);
                float bottom = binding.Profile.HeightForMl(0), rim = binding.Profile.HeightForMl(binding.Profile.CapacityMl);
                Assert.That(local.y,Is.GreaterThan(bottom+.001f).And.LessThan(rim-.001f));
                // The user requested a larger sweep. The old 13mm limit was a visual placeholder;
                // the invariant is that the real spoon bowl stays inside the authored cavity.
                var bowl=spoon.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="SpoonBowl");
                foreach(var vertex in bowl.sharedMesh.vertices)
                {
                    var p=binding.transform.InverseTransformPoint(bowl.transform.TransformPoint(vertex));
                    Assert.That(p.y,Is.GreaterThanOrEqualTo(binding.Shape.Bottom-.0001f));
                    Assert.That(Vector2.Distance(new Vector2(p.x,p.z),binding.Shape.Axis),Is.LessThanOrEqualTo(binding.Shape.RadiusAt(p.y)+.0002f));
                }
                Assert.That(Vector3.Dot((tip.position-grip.position).normalized,binding.transform.up),Is.LessThan(-.99f));
            }
            Assert.That(Vector3.Distance(first,tip.position),Is.GreaterThan(.0001f));
            Capture(id+"-spoon-inserted");
            // It lifts out over the rim before going back to the hand (returnSeconds 0.36 s).
            session.LeftButton(false,Now()); yield return new WaitForSecondsRealtime(.5f);
            Assert.That(grip.localPosition,Is.EqualTo(edited));
            var hand = session.Hands.SideOf(spoon.Item) == HandSide.Left ? session.Player.LeftAnchor : session.Player.RightAnchor;
            Assert.That(Vector3.Distance(grip.position,hand.position),Is.LessThan(.00001f));
            Assert.That(container.Contents.LiquidMl,Is.EqualTo(liquid).Within(1e-8));
            Assert.That(session.Adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));
            Assert.That(session.Clock.Error,Is.Null);
        }
        [UnityTest] public IEnumerator FormalMetalMaterialsRenderWithEnvironmentAndSurviveHover()
        {
            yield return Load();
            foreach (string id in new[]{"jigger.dual","shaker.cobbler"})
            {
                var view = View(id);
                var renderers = view.GetComponentsInChildren<MeshRenderer>();
                var materials = renderers.Select(r => r.sharedMaterials).ToArray();
                view.Highlight(false); view.Highlight(true); view.Highlight(false);
                for (int i = 0; i < renderers.Length; i++) Assert.That(renderers[i].sharedMaterials,Is.EqualTo(materials[i]));
                Capture(id+"-metal",view);
            }
            Assert.That(RenderSettings.customReflectionTexture,Is.Not.Null);
        }
    }
}
