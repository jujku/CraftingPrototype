using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Bar.Unity.Editor
{
    public static partial class GinzaLoungeBuilder
    {
        private static string PosterReport => Path.GetFullPath("GinzaPosterUVReports");

        public static void FixPosterUV()
        {
            RequireCache();
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(PosterReport);
            SessionState.SetBool("GinzaPosterUVPending", true);
            EditorApplication.isPlaying = true;
        }

        public static void FixPosterUVAndRender()
        {
            var prints = UnityEngine.Object.FindFirstObjectByType<GinzaEnvironmentTag>()
                .GetComponentsInChildren<MeshFilter>().Where(f => f.name == "Vintage fine art print").ToArray();
            if (prints.Length != 2) throw new InvalidOperationException("Expected exactly two framed print surfaces");
            // First URP render initializes its runtime additional-light data before the lock snapshot.
            RenderPoster(prints[0].transform, "render-warmup");
            var lighting = WorkbenchLock();
            File.WriteAllText(Path.Combine(PosterReport, "workbench-lock-before.txt"), lighting);
            var expected = new[] { Vector2.right, Vector2.zero, Vector2.up, Vector2.one };
            foreach (var print in prints)
            {
                var mesh = print.sharedMesh;
                if (AssetDatabase.GetAssetPath(mesh) != MeshPath || mesh.vertexCount != 4)
                    throw new InvalidOperationException("Unexpected poster mesh binding");
                string label = print.transform.parent.name.Contains("Ginza") ? "Ginza" :
                    print.transform.parent.name.Contains("jazz") ? "Midnight" :
                    throw new InvalidOperationException("Unknown framed print");
                RenderPoster(print.transform, "before-" + label);
                var vertices = mesh.vertices; var normals = mesh.normals; var triangles = mesh.triangles;
                if (vertices[0].x >= 0 || vertices[1].x <= 0 || vertices[2].x <= 0 || vertices[3].x >= 0)
                    throw new InvalidOperationException("Poster quad vertex order changed");
                mesh.uv = expected;
                if (!vertices.SequenceEqual(mesh.vertices) || !normals.SequenceEqual(mesh.normals) || !triangles.SequenceEqual(mesh.triangles))
                    throw new InvalidOperationException("Poster geometry changed while fixing UV");
                EditorUtility.SetDirty(mesh);
                RenderPoster(print.transform, "after-" + label);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(PosterReport, "workbench-lock-after.txt"), WorkbenchLock());
            if (WorkbenchLock() != lighting) throw new InvalidOperationException("Locked workbench lighting changed");
            foreach (var print in prints)
                if (!print.sharedMesh.uv.SequenceEqual(expected)) throw new InvalidOperationException("Poster UV save failed");
            File.WriteAllText(Path.Combine(PosterReport, "verification.json"), "{\"compiled\":true,\"postersFixed\":2,\"onlyUVChanged\":true,\"geometryUnchanged\":true,\"workbenchLightingUnchanged\":true,\"unitTestsRun\":0,\"runtimeScreenshots\":4}");
        }

        private static void RenderPoster(Transform print, string name)
        {
            var main = UnityEngine.Object.FindFirstObjectByType<BarSession>().Player.ViewCamera;
            var go = new GameObject("Ginza_PosterEvidenceCamera");
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(main); camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.transform.position = print.TransformPoint(new Vector3(0, 0, 1.05f));
            camera.transform.LookAt(print.TransformPoint(new Vector3(0, 0, .008f)), print.up);
            camera.fieldOfView = 50; camera.aspect = .75f;
            const int width = 672, height = 896;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            if (!target.IsCreated()) throw new InvalidOperationException("Poster capture render target allocation failed");
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(PosterReport, name + ".png"), pixels.EncodeToPNG());
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [InitializeOnLoad]
    public static class GinzaPosterUVEvidenceDriver
    {
        private static double started = -1;
        static GinzaPosterUVEvidenceDriver()
        {
            if (SessionState.GetBool("GinzaPosterUVPending", false)) EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (started < 0) { started = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - started < 3) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool("GinzaPosterUVPending", false);
            GinzaLoungeBuilder.FixPosterUVAndRender();
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
