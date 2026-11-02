using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bar.Unity;
using Bar.Unity.Editor;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    public sealed class BOfflineAssetsTests
    {
        [Test] public void OfflineMinnaertWritesPcmAndPadsLoopSources()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../BGeneratedAudio"));BOfflineAudioGenerator.Build(output);
            var p=BarAudioCatalog.Load();Assert.That(Directory.GetFiles(output,"*.wav").Length,Is.EqualTo(p.minnaertVariants+8));
            var water=File.ReadAllBytes(Path.Combine(output,"pourThin-0.wav"));Assert.That(System.Text.Encoding.ASCII.GetString(water,0,4),Is.EqualTo("RIFF"));
            Assert.That(water.Skip(44).Take(p.loopPaddingSamples*2).All(b=>b==0),Is.True);
            Assert.That(water.Skip(44+p.loopPaddingSamples*2).Any(b=>b!=0),Is.True);Assert.That(p.sampleRate,Is.EqualTo(44100));
        }
        [Test] public void GlassKeepsIdenticalMaterialBuffersAndOuterShadows()
        {
            string shader=File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Rendering/Glass.shader"));
            var blocks=Regex.Matches(shader,@"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END",RegexOptions.Singleline);
            Assert.That(blocks.Count,Is.EqualTo(2));Assert.That(blocks[0].Groups[1].Value,Is.EqualTo(blocks[1].Groups[1].Value));Assert.That(shader,Does.Contain("Name \"ShadowCaster\""));Assert.That(shader,Does.Contain("_BarSceneWithIce"));
        }
    }
}
