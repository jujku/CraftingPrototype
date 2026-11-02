using System.Collections;
using System.Linq;
using Bar.Domain;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bar.Tests
{
    internal sealed class LabelRotationTests
    {
        [UnityTest] public IEnumerator AuthoredBottleHeadingAndLabelSurviveStartupPickupAndPlacement()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
            var session = Object.FindFirstObjectByType<BarSession>();
            var counter = session.Views.First(v => v.Item is BarCounter);
            var bottle = session.Views.Where(v => v.DefinitionId == "bottle.soda")
                .OrderByDescending(v => Quaternion.Angle(v.transform.rotation,counter.transform.rotation)).First();
            var authored = bottle.transform.rotation;
            Assert.That(Quaternion.Angle(authored,counter.transform.rotation),Is.GreaterThan(150),"覆盖用户已在场景里转过的苏打水瓶");
            var label = bottle.Anchor("OriginalLabel");
            Assert.That(label,Is.Not.Null);
            var localPosition = label.localPosition; var localRotation = label.localRotation;
            var materials = label.GetComponent<MeshRenderer>().sharedMaterials;
            double Now() => session.EventTime(Time.realtimeSinceStartupAsDouble);
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(Quaternion.Angle(bottle.transform.rotation,authored),Is.LessThan(.05f));
            Assert.That(label.gameObject.activeInHierarchy,Is.True);
            Assert.That(label.GetComponent<MeshRenderer>().enabled,Is.True);
            session.Player.AimAt(bottle.GetComponent<Collider>().bounds.center);
            Physics.SyncTransforms(); session.Targeting();
            Assert.That(session.Target,Is.SameAs(bottle));
            session.RightButton(Now());
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(session.Hands.SideOf(bottle.Item).HasValue,Is.True);
            var surface = counter.GetComponent<PlacementSurface>();
            bool found = false;
            for (int x = 0; x < 14 && !found; x++)
            {
                var point = CapSteps.TablePoint(session,-1.1f+x*.16f,.32f);
                if (!surface.CanPlace(point,bottle.Size)) continue;
                if (session.Views.Any(v => v != bottle && v.Item is not Fixture && v.Item.Parent == null &&
                    v.Item.Location.Kind == LocationKind.Surface && Vector2.Distance(new Vector2(point.x,point.z),new Vector2(v.transform.position.x,v.transform.position.z)) < (bottle.Size.x+v.Size.x)*.5f+.012f)) continue;
                session.Player.AimAt(point); Physics.SyncTransforms(); session.Targeting();
                if (session.Target != counter) continue;
                session.RightButton(Now()); found = true;
            }
            Assert.That(found,Is.True,"应能找到空台面放回瓶子");
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(session.Hands.SideOf(bottle.Item).HasValue,Is.False);
            Assert.That(Quaternion.Angle(bottle.transform.rotation,authored),Is.LessThan(.05f));
            Assert.That(label.localPosition,Is.EqualTo(localPosition));
            Assert.That(Quaternion.Angle(label.localRotation,localRotation),Is.LessThan(.05f));
            Assert.That(label.GetComponent<MeshRenderer>().sharedMaterials,Is.EqualTo(materials));
            Assert.That(label.gameObject.activeInHierarchy,Is.True);
            Assert.That(session.Clock.Error,Is.Null);
        }
    }
}
