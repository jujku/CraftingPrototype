using System;
using System.Linq;
namespace Bar.Logging
{
    /// <summary>模拟节拍采样与阈值变化触发。最近呈现帧仍由宿主单独缓存。</summary>
    public sealed class CueSampler
    {
        private long tick=-1;private CueFrame[] previous;private double sum,max,window;private int frames;
        public double Interval=.1,Threshold=.2;
        /// <summary>声音另有逐次事件记录时，不让短音脉冲触发整组线索；节拍仍保存实际声音状态。</summary>
        public bool SampleSoundsOnTick;
        public bool Sample(double time,CueFrame[] values)
        {
            long now=(long)Math.Floor((time+1e-8)/Interval);bool change=false;
            if(previous!=null&&previous.Length!=values.Length)change=true;
            if(previous!=null&&previous.Length==values.Length)for(int i=0;i<values.Length;i++)
            {var a=previous[i];var b=values[i];change|=a.vesselId!=b.vesselId||a.onScreen!=b.onScreen||a.liquidVisible!=b.liquidVisible||a.iceVisible!=b.iceVisible||a.seeInside!=b.seeInside||!SampleSoundsOnTick&&(a.soundEmitted!=b.soundEmitted||a.soundPlaying!=b.soundPlaying||Math.Abs(a.iceSound-b.iceSound)>Threshold)||Math.Abs(a.foam-b.foam)>.0005||Math.Abs(a.frost-b.frost)>Threshold||Math.Abs(a.condensation-b.condensation)>Threshold||Math.Abs(a.dilution-b.dilution)>Threshold||Math.Abs(a.bubbleSpeed-b.bubbleSpeed)>Threshold||Math.Abs(a.bubbleCount-b.bubbleCount)/50>Threshold;
                if(a.liquidRgba!=null&&b.liquidRgba!=null)for(int k=0;k<Math.Min(a.liquidRgba.Length,b.liquidRgba.Length);k++)change|=Math.Abs(a.liquidRgba[k]-b.liquidRgba[k])>Threshold;}
            if(tick==now&&!change)return false;tick=now;previous=new CueFrame[values.Length];for(int i=0;i<values.Length;i++)previous[i]=values[i].SampleCopy();return true;
        }
        public LogRecord FrameStatistics(double time,double dt)
        {
            sum+=dt;max=Math.Max(max,dt);frames++;if(time-window<1-1e-8)return null;
            var r=new LogRecord{meanFrameSeconds=sum/frames,maxFrameSeconds=max,renderedFrames=frames};window=Math.Floor(time);sum=max=0;frames=0;return r;
        }
    }
}
