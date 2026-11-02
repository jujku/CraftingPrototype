using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
namespace Bar.Unity.Editor
{
    /// <summary>只剥离 WebGL2 无法使用的内部调试/原生 framebuffer 子通道着色器；不修改包源。</summary>
    public sealed class QWebCompatibility:IPreprocessShaders
    {
        public int callbackOrder=>int.MaxValue;
        public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> variants)
        {
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.WebGL)return;
            if(shader.name=="Hidden/Universal/HDRDebugView"||shader.name=="Hidden/Universal Render Pipeline/StencilDitherMaskSeed"||shader.name=="Hidden/CoreSRP/CoreCopy")variants.Clear();
        }
    }
    /// <summary>表现采样强制加载时解压，无后台流式依赖。</summary>
    public sealed class QAudioImporter:AssetPostprocessor
    {
        private void OnPreprocessAudio(){if(!assetPath.StartsWith("Assets/Bar/Rendering/Resources/BarAudio/"))return;var importer=(AudioImporter)assetImporter;var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.loadInBackground=false;if(assetPath.Contains("/Inspect/micro")){importer.SetOverrideSampleSettings("WebGL",settings);importer.SetOverrideSampleSettings("Standalone",settings);}}
    }
}
