using UnityEditor;
using UnityEngine;

// Put this file in Assets/Bar/Editor before importing the supplied FBX models.
// Blender requires globally unique object names; Unity requires exact local names.
namespace Bar.Unity.Editor
{
    /// <summary>BarKit 模型导入规范化，仅处理 Art 下 FBX。</summary>
public sealed class BarKitModelImporter : AssetPostprocessor
{
    bool IsBarKit => assetPath.StartsWith("Assets/Bar/Art/") &&
        assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    void OnPreprocessModel()
    {
        if (!IsBarKit) return;
        var importer = (ModelImporter)assetImporter;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.isReadable = true; // InnerCavity must be readable by VolumeProfileBaker.
    }

    void OnPostprocessModel(GameObject model)
    {
        if (!IsBarKit) return;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            int separator = t.name.LastIndexOf("::", System.StringComparison.Ordinal);
            if (separator >= 0) t.name = t.name.Substring(separator + 2);
            if (t.name.StartsWith("Mark_") && !t.name.EndsWith("ml")) t.name += "ml";
            if (t.name != "InnerCavity" && t.name != "LiquidVolume") continue;
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            foreach (var collider in t.GetComponents<Collider>())
                Object.DestroyImmediate(collider);
        }
    }
}

}
