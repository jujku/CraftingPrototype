using System;
namespace Bar.Unity
{
    /// <summary>人为设定、未标定的映射参数，全部来自唯一的 BarCues.json。</summary>
    [Serializable] public sealed class CueSettings
    {
        public double sampleInterval=.1,sampleThreshold=.2;
        public VisualSettings visual=new VisualSettings();
        public int version,bubbleMax;public bool uncalibrated;
        public CuePoint[] frost,condensation,bubbleCount,bubbleSpeed,iceSound,dilution;
        public float[] liquidBaseRgba,liquidDilutedRgba;
        public float bubbleRadius,soundHoldSeconds,soundIntervalSeconds,soundMaxDistance,frostOpacity,condensationOpacity;
        /// <summary>对配置曲线分段线性插值。</summary>
        /// <precondition>curve 非空且 x 严格递增，value 有限。</precondition>
        /// <effects>只读数组；越界取端点。</effects>
        public static double Map(CuePoint[] curve,double value)
        {
            if(curve==null||curve.Length==0)throw new ArgumentException("线索映射缺失");
            if(value<=curve[0].x)return curve[0].y;
            for(int i=1;i<curve.Length;i++)if(value<=curve[i].x)return curve[i-1].y+(curve[i].y-curve[i-1].y)*(value-curve[i-1].x)/(curve[i].x-curve[i-1].x);
            return curve[curve.Length-1].y;
        }
    }
    /// <summary>映射曲线中的一对输入与显示输出。</summary>
    [Serializable] public sealed class CuePoint {public double x,y;}
}
