using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>把倾斜角与瓶内剩余比例转换成毫升每秒的流量。</summary>
    public sealed class TiltFlowModel
    {
        private readonly Controls controls;
        /// <summary>创建TiltFlowModel。</summary>
        /// <precondition>Controls 非空且所有流量、角度、时长与手势参数通过校验。</precondition>
        /// <effects>保存只读控制依赖；创建流量模型，不改变任何库存。</effects>
        public TiltFlowModel(Controls controls) { this.controls = controls ?? throw new ArgumentNullException(nameof(controls)); controls.Validate(); }
        /// <summary>按瓶内剩余比例求起流角。</summary>
        /// <precondition>容量为有限正数，剩余量有限且在零与容量之间。</precondition>
        /// <effects>返回满瓶与空瓶起流角的线性插值，不扣库存。</effects>
        public double StartAngle(double remainingMl, double capacityMl, string bottleDefinitionId = null)
        {
            Guard.Nonnegative(remainingMl); Guard.Nonnegative(capacityMl); if (capacityMl == 0 || remainingMl > capacityMl) throw new ArgumentException("Invalid fill fraction.");
            double fraction=remainingMl/capacityMl;
            if(controls.BottleStartAngleMode=="geometric"&&bottleDefinitionId!=null&&controls.BottleStartTables!=null)
                foreach(var table in controls.BottleStartTables)
                    if(table.Id==bottleDefinitionId&&table.Fractions!=null&&table.Degrees!=null&&table.Fractions.Length==table.Degrees.Length&&table.Fractions.Length>1)
                    {
                        if(fraction<=table.Fractions[0])return table.Degrees[0];
                        for(int i=1;i<table.Fractions.Length;i++)if(fraction<=table.Fractions[i])return table.Degrees[i-1]+(table.Degrees[i]-table.Degrees[i-1])*(fraction-table.Fractions[i-1])/(table.Fractions[i]-table.Fractions[i-1]);
                        return table.Degrees[table.Degrees.Length-1];
                    }
            return controls.EmptyBottleStartDegrees + (controls.FullBottleStartDegrees - controls.EmptyBottleStartDegrees) * remainingMl / capacityMl;
        }
        /// <summary>计算当前倾角对应的毫升每秒流量。</summary>
        /// <precondition>倾角在零与 MaximumTiltDegrees 之间；剩余量与容量有效；可选酒嘴流量非负有限。</precondition>
        /// <effects>空瓶或未到起流角返回零；酒嘴返回固定流量；其余返回被最大流量限幅的幂曲线值。</effects>
        public double Flow(double angleDegrees, double remainingMl, double capacityMl, double? spoutFlowMlPerSecond = null,string bottleDefinitionId=null)
        {
            Guard.Range(angleDegrees, controls.MaximumTiltDegrees);
            double start = StartAngle(remainingMl, capacityMl,bottleDefinitionId);
            if (spoutFlowMlPerSecond.HasValue) Guard.Nonnegative(spoutFlowMlPerSecond.Value);
            if (remainingMl == 0 || angleDegrees <= start) return 0;
            if (spoutFlowMlPerSecond.HasValue) return spoutFlowMlPerSecond.Value;
            double normalized = Math.Min(1, (angleDegrees - start) / Math.Max(1e-12,controls.FullFlowDegrees - start));
            return controls.MaxFlowMlPerSecond * Math.Pow(normalized, controls.CurveExponent);
        }
    }
}
