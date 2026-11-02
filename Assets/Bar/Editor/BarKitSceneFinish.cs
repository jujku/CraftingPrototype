using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>只调整已导入吧台场景的设施位置和实例，不重建用户模型或材质。</summary>
    public static class BarKitSceneFinish
    {
        /// <summary>将设施嵌入开口、吧台贴地，并在砧板上布置四个独立半柠檬。</summary>
        /// <precondition>正式 BarKit 预制体和 Bar.unity 已存在；只在唯一验收工程显式执行。</precondition>
        /// <effects>只保存场景布局；输出最终预制体验证和坐标报告，不写 Art。</effects>
        public static void Apply()
        {
            EditorSceneManager.OpenScene("Assets/Bar/Scenes/Bar.unity");
            var views = UnityEngine.Object.FindObjectsByType<ItemView>(FindObjectsSortMode.None);
            ItemView View(string id) => views.First(v => v.DefinitionId == id);
            var floor = GameObject.Find("Floor").GetComponent<Renderer>();
            float ground = floor.bounds.max.y;
            var counter = View("fixture.counter");
            float oldY = counter.transform.position.y;
            counter.transform.position = new Vector3(0, ground, .65f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Prefabs/Resources/BarPrefabs/fixture.counter.prefab");
            var measured = UnityEngine.Object.Instantiate(prefab);
            measured.transform.position = counter.transform.position;
            float surface = measured.GetComponent<ItemView>().Anchor("Surface").position.y;
            UnityEngine.Object.DestroyImmediate(measured);
            // Shift existing worktop objects together rather than leaving them floating.
            foreach (var view in views)
            {
                if (view != counter) view.transform.position += Vector3.up * (ground - oldY);
            }
            // Openings measured on the imported counter mesh. Unity's FBX import mirrors X relative to
            // the glTF coordinates, so the ice bin opening is on +X and the sink opening on -X.
            var ice = View("fixture.icebin");
            ice.transform.position = new Vector3(.94f, surface - .25f, .518f);
            var sink = View("fixture.sink");
            sink.transform.position = new Vector3(-.96f, surface - .22f, .528f);
            var board = View("fixture.board");
            board.transform.position = new Vector3(.75f, surface + .002f, .88f);
            View("fixture.servemat").transform.position = new Vector3(-.35f, surface + .002f, .36f);
            View("tool.tongs").transform.position = new Vector3(.53f, surface + .002f, .40f);
            View("shaker.cap").transform.position = new Vector3(-.70f, surface + .002f, .40f);
            var lemons = views.Where(v => v.DefinitionId == "produce.lemon_half").OrderBy(v => v.InstanceId).ToList();
            while (lemons.Count < 4)
            {
                var lemon = UnityEngine.Object.Instantiate(lemons[0].gameObject).GetComponent<ItemView>();
                lemon.InstanceId = "item:produce.lemon_half:" + (lemons.Count + 1);
                lemons.Add(lemon);
            }
            for (int i = 0; i < lemons.Count; i++)
            {
                if (i >= 4)
                {
                    UnityEngine.Object.DestroyImmediate(lemons[i].gameObject);
                    continue;
                }
                lemons[i].name = "半颗柠檬 " + (i + 1);
                lemons[i].transform.position = board.transform.position +
                    new Vector3(i % 2 == 0 ? -.04f : .04f, .0194f + .001f, i / 2 == 0 ? -.04f : .04f);
            }
            var side = GameObject.Find("Side worktop");
            if (side) UnityEngine.Object.DestroyImmediate(side);
            // Camera and fixed hands follow the actual new worktop, preserving their relative pose.
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var cameraPosition = player.ViewCamera.transform.position;
            player.ViewCamera.transform.position = new Vector3(cameraPosition.x, surface + .55f, cameraPosition.z);
            var report = new List<string>();
            foreach (string path in Directory.GetFiles("Assets/Bar/Prefabs/Resources/BarPrefabs", "*.prefab"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                report.Add(Path.GetFileName(path));
                report.AddRange(PrefabValidator.Validate(asset));
            }
            Directory.CreateDirectory("BarKitFinishReports");
            File.WriteAllLines("BarKitFinishReports/prefab-validation.txt", report);
            File.WriteAllText("BarKitFinishReports/layout.txt", "floorTop=" + ground + "\ncounterBottom=" + counter.transform.position.y +
                "\nsurfaceY=" + surface + "\niceRoot=" + ice.transform.position.ToString("F4") +
                "\nsinkRoot=" + sink.transform.position.ToString("F4") + "\ncameraY=" + player.ViewCamera.transform.position.y + "\nlemonCount=4\n");
            if (report.Any(line => line.StartsWith("失败"))) throw new InvalidOperationException("预制体验证失败");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
