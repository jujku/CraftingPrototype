using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>仅编辑器离线生成 Minnaert 气泡与水流 PCM WAV；运行时不合成音频。</summary>
    public static class BOfflineAudioGenerator
    {
        [MenuItem("Bar/B/Generate CC0 offline bubble WAVs")]
        public static void BuildAssets(){Build(Path.Combine(Application.dataPath,"Bar/Rendering/Resources/BarAudio"));AssetDatabase.Refresh();}
        public static void Build(string directory)
        {
            Directory.CreateDirectory(directory);var p=BarAudioCatalog.Load();var rng=new System.Random(20261026);
            for(int i=0;i<p.minnaertVariants;i++)
            {
                var signal=new double[(int)(p.sampleRate*p.bubbleSeconds)];double radius=p.bubbleRadiusMin+(p.bubbleRadiusMax-p.bubbleRadiusMin)*i/Math.Max(1,p.minnaertVariants-1);
                Add(signal,0,radius,p,rng);Wave(Path.Combine(directory,"bubble-"+i+".wav"),signal,p,0);
            }
            for(int variant=0;variant<4;variant++)
            {
                var signal=new double[(int)(p.sampleRate*p.waterSeconds)];
                for(int eventIndex=0;eventIndex<(int)(p.waterBubblesPerSecond*p.waterSeconds);eventIndex++)
                {int start=rng.Next(signal.Length);double radius=p.bubbleRadiusMin+rng.NextDouble()*(p.bubbleRadiusMax-p.bubbleRadiusMin);Add(signal,start,radius,p,rng);}
                Wave(Path.Combine(directory,"pourThin-"+variant+".wav"),signal,p,p.loopPaddingSamples);
                var gulp=new double[(int)(p.sampleRate*p.bubbleSeconds*2)];for(int i=0;i<3;i++)Add(gulp,i*p.sampleRate/30,p.bubbleRadiusMax,p,rng);
                Wave(Path.Combine(directory,"gulp-"+variant+".wav"),gulp,p,0);
            }
        }
        private static void Add(double[] signal,int start,double radius,BarAudioCatalog p,System.Random rng)
        {
            // f0=3.26/r (r in metres); damped pulse and modest upward chirp, authored offline only.
            double frequency=3.26/radius;int length=Math.Min(signal.Length-start,(int)(p.sampleRate*p.bubbleSeconds));
            for(int i=0;i<length;i++){double t=(double)i/p.sampleRate,phase=2*Math.PI*frequency*(t+p.bubbleSweep*t*t/p.bubbleSeconds);signal[start+i]+=p.bubbleGain*Math.Exp(-p.bubbleDecay*t)*(Math.Sin(phase)+p.bubbleNoise*(rng.NextDouble()*2-1));}
        }
        private static void Wave(string path,double[] signal,BarAudioCatalog p,int padding)
        {
            double peak=1;foreach(double value in signal)peak=Math.Max(peak,Math.Abs(value));
            using(var writer=new BinaryWriter(File.Create(path)))
            {
                int samples=signal.Length+padding;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(p.sampleRate);writer.Write(p.sampleRate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
                for(int i=0;i<padding;i++)writer.Write((short)0);foreach(double value in signal)writer.Write((short)(value/peak*short.MaxValue));
            }
        }
    }
}
