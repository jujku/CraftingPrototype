using System;
using System.Runtime.Serialization;

namespace Bar.Data
{
    /// <summary>保存并校验流量、倾斜角、量酒器动画与手势控制参数。</summary>
    [DataContract] public sealed class Controls
    {
        /// <summary>linear 保留原有起流规则；geometric 查离线内腔表，没有匹配瓶型时退回 linear。</summary>
        [DataMember(Name = "bottleStartAngleMode")] public string BottleStartAngleMode { get; private set; }
        /// <summary>几何计算的起流曲线，不含物理核心参数。</summary>
        [DataMember(Name = "bottleStartTables")] public BottleStartTable[] BottleStartTables { get; private set; }
        /// <summary>无酒嘴时的最大流量，单位 ml/s。</summary>
        [DataMember(Name = "maxFlowMlPerSecond")] public double MaxFlowMlPerSecond { get; private set; }
        /// <summary>满瓶开始出液的角度，相对直立方向计度。</summary>
        [DataMember(Name = "fullBottleStartDegrees")] public double FullBottleStartDegrees { get; private set; }
        /// <summary>接近空瓶时开始出液的角度，单位度。</summary>
        [DataMember(Name = "emptyBottleStartDegrees")] public double EmptyBottleStartDegrees { get; private set; }
        /// <summary>达到最大流量的倾斜角，单位度。</summary>
        [DataMember(Name = "fullFlowDegrees")] public double FullFlowDegrees { get; private set; }
        /// <summary>起流到全流区间的幂曲线指数。</summary>
        [DataMember(Name = "curveExponent")] public double CurveExponent { get; private set; }
        /// <summary>酒嘴预留的固定流量，单位 ml/s。</summary>
        [DataMember(Name = "spoutFlowMlPerSecond")] public double SpoutFlowMlPerSecond { get; private set; }
        /// <summary>允许的最大倾斜角，单位度。</summary>
        [DataMember(Name = "maximumTiltDegrees")] public double MaximumTiltDegrees { get; private set; }
        /// <summary>量酒器整杯倒出的视图动画时长，单位秒。</summary>
        [DataMember(Name = "jiggerPourSeconds")] public double JiggerPourSeconds { get; private set; }
        /// <summary>每单位归一化鼠标位移对应的倾斜度数。</summary>
        [DataMember(Name = "tiltDegreesPerInput")] public double TiltDegreesPerInput { get; private set; }
        /// <summary>计入一次往复换向所需的最小鼠标行程。</summary>
        [DataMember(Name = "oscillationTravel")] public double OscillationTravel { get; private set; }
        /// <summary>对应最大摇壶强度的每秒换向次数。</summary>
        [DataMember(Name = "maxReversalsPerSecond")] public double MaxReversalsPerSecond { get; private set; }
        /// <summary>计入圆周手势所需的最小鼠标半径。</summary>
        [DataMember(Name = "circleMinRadius")] public double CircleMinRadius { get; private set; }
        /// <summary>对应最大搅拌强度的每秒圈数。</summary>
        [DataMember(Name = "maxCirclesPerSecond")] public double MaxCirclesPerSecond { get; private set; }
        // the circle is measured about a moving centre (Domain.CircleTracker). Older data files without these keys get the defaults.
        [DataMember(Name = "circleCentreSeconds")] private double circleCentreSeconds;
        [DataMember(Name = "circleSmoothingSeconds")] private double circleSmoothingSeconds;
        [DataMember(Name = "circleSampleTravel")] private double circleSampleTravel;
        /// <summary>估计圆心所用的轨迹滑动平均时间常数（秒）。</summary>
        public double CircleCentreSeconds => circleCentreSeconds > 0 ? circleCentreSeconds : .3;
        /// <summary>量圆周角度前对鼠标轨迹的平滑时间常数（秒）。</summary>
        public double CircleSmoothingSeconds => circleSmoothingSeconds > 0 ? circleSmoothingSeconds : .03;
        /// <summary>两次圆周采样之间的最小鼠标行程。</summary>
        public double CircleSampleTravel => circleSampleTravel > 0 ? circleSampleTravel : 5;
        /// <summary>对应完整下压动作的鼠标行程。</summary>
        [DataMember(Name = "pressTravel")] public double PressTravel { get; private set; }
        [DataMember(Name="guidedFlowScale")] public double GuidedFlowScale {get;private set;}
        [DataMember(Name="guidedPrepareSeconds")] public double GuidedPrepareSeconds {get;private set;}
        [DataMember(Name="guidedFollowSeconds")] public double GuidedFollowSeconds {get;private set;}
        public void ValidateGuided(){DataGuard.Positive(GuidedFlowScale);DataGuard.Positive(GuidedPrepareSeconds);DataGuard.Positive(GuidedFollowSeconds);if(GuidedFlowScale>1||GuidedFollowSeconds>=GuidedPrepareSeconds)throw new ArgumentException("Invalid guided pour controls");}
        /// <summary>校验全部控制参数。</summary>
        /// <precondition>流量、时长、行程、曲线指数为有限正数；起流角非负。</precondition>
        /// <effects>不改控制值；不满足满瓶起流角≤空瓶起流角＜全流角≤最大角≤180时抛异常。</effects>
        public void Validate()
        {
            foreach (double x in new[] { MaxFlowMlPerSecond, FullFlowDegrees, CurveExponent, SpoutFlowMlPerSecond, MaximumTiltDegrees, JiggerPourSeconds, TiltDegreesPerInput, OscillationTravel, MaxReversalsPerSecond, CircleMinRadius, MaxCirclesPerSecond, PressTravel }) DataGuard.Positive(x);
            DataGuard.Nonnegative(FullBottleStartDegrees); DataGuard.Nonnegative(EmptyBottleStartDegrees);
            if (FullBottleStartDegrees > EmptyBottleStartDegrees || EmptyBottleStartDegrees >= FullFlowDegrees || FullFlowDegrees > MaximumTiltDegrees || MaximumTiltDegrees > 180) throw new ArgumentException("Inconsistent tilt angles.");
            if(!string.IsNullOrEmpty(BottleStartAngleMode)&&BottleStartAngleMode!="linear"&&BottleStartAngleMode!="geometric")throw new ArgumentException("Unknown bottle start-angle mode.");
            var ids=new System.Collections.Generic.HashSet<string>();
            if(BottleStartTables!=null)foreach(var table in BottleStartTables)
            {
                if(table==null||string.IsNullOrWhiteSpace(table.Id)||!ids.Add(table.Id)||table.Fractions==null||table.Degrees==null||table.Fractions.Length<2||table.Fractions.Length!=table.Degrees.Length)throw new ArgumentException("Invalid bottle geometry table.");
                for(int i=0;i<table.Fractions.Length;i++)
                {
                    DataGuard.Nonnegative(table.Fractions[i]);DataGuard.Nonnegative(table.Degrees[i]);
                    if(table.Fractions[i]>1||table.Degrees[i]>180||i>0&&(table.Fractions[i]<=table.Fractions[i-1]||table.Degrees[i]>table.Degrees[i-1]))throw new ArgumentException("Non-monotone bottle geometry table.");
                }
                if(table.Fractions[0]!=0||table.Fractions[table.Fractions.Length-1]!=1)throw new ArgumentException("Bottle table must cover zero through full stock.");
            }
        }
    }
    /// <summary>单瓶型的剩余比例与起流角表；数据由离线内腔积分产生。</summary>
    [DataContract] public sealed class BottleStartTable
    {
        [DataMember(Name="id")]public string Id {get;private set;}
        [DataMember(Name="fractions")]public double[] Fractions {get;private set;}
        [DataMember(Name="degrees")]public double[] Degrees {get;private set;}
    }
}
