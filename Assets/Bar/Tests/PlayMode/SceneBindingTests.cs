using System.Collections;
using System.Linq;
using Bar.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bar.Tests
{
    internal sealed class SceneBindingTests
    {
        [UnityTest] public IEnumerator SessionBindsExistingObjectsAndRetainsEditedGrip()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Bar/Scenes/Bar.unity");
            yield return null;
            var previous = Object.FindFirstObjectByType<BarSession>();
            previous.enabled = false;
            previous.GetComponent<Bar.Unity.Input.InputRouter>().enabled = false;
            var root = new GameObject("Binding regression");
            root.SetActive(false);
            // Start a fresh inactive setup, as a saved scene does before its first Awake.
            // Rebinding already running liquid/ice helper renderers is a different lifecycle.
            foreach (var view in previous.Views) view.gameObject.SetActive(false);
            var template = Resources.Load<GameObject>("BarPrefabs/bottle.gin");
            var edited = Object.Instantiate(template, root.transform).GetComponent<ItemView>();
            edited.InstanceId = "test:edited-gin";
            var second = Object.Instantiate(template, root.transform).GetComponent<ItemView>();
            second.InstanceId = "test:second-gin";
            var grip = edited.Anchor("Grip_R");
            var adjusted = grip.localPosition + new Vector3(.002f,.003f,.004f);
            grip.localPosition = adjusted;
            var renderer = edited.GetComponentInChildren<MeshRenderer>();
            var materials = renderer.sharedMaterials;
            var session = root.AddComponent<BarSession>();
            session.ModelJson = previous.ModelJson;
            session.ItemsJson = previous.ItemsJson;
            session.ControlsJson = previous.ControlsJson;
            session.Player = previous.Player;
            var existing = root.GetComponentsInChildren<ItemView>(true).ToDictionary(v => v.InstanceId, v => v);
            try
            {
                root.SetActive(true);
                yield return null;
                Assert.That(session.Views.Single(v => v.InstanceId == "test:edited-gin"), Is.SameAs(edited));
                foreach (var pair in existing)
                    Assert.That(session.Views.Single(v => v.InstanceId == pair.Key), Is.SameAs(pair.Value), pair.Key);
                Assert.That(grip.localPosition, Is.EqualTo(adjusted));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(materials));
                Assert.That(edited.Item.InstanceId, Is.EqualTo("test:edited-gin"));
            }
            finally { Object.Destroy(root); }
        }
    }
}
