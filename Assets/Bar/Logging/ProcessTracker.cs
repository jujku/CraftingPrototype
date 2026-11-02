using System;
using System.Collections.Generic;
using System.Linq;
namespace Bar.Logging
{
    /// <summary>从确认的操作序列追踪出杯祖先及 C2 过程字段，不实现任何物理。</summary>
    public sealed class ProcessTracker
    {
        private readonly List<LogRecord> history=new List<LogRecord>();
        /// <summary>观察一个实际核心操作。</summary>
        /// <precondition>row 非 null；kind 为 core_operation。</precondition>
        /// <effects>只保存成功项供过程统计，不执行物理。</effects>
        public void Observe(LogRecord row){if(row.kind=="core_operation"&&!row.rejected)history.Add(row);}
        /// <summary>按 C2 对照口径提取自由操作的阶段统计。</summary>
        /// <precondition>id 是最终杯 ID，time 为出杯模型秒。</precondition>
        /// <effects>只读已有历史并返回新的过程描述符。</effects>
        public ProcessDescriptors Describe(string id,double time)
        {
            var ancestors=new HashSet<string>{id};
            // Reverse traversal respects transfers that happened before the relevant portion entered the glass.
            var selected=new List<LogRecord>();
            for(int i=history.Count-1;i>=0;i--)
            {
                var r=history[i];var o=r.operation;
                if(IsTransfer(o.type)&&ancestors.Contains(o.to))ancestors.Add(o.from);
                if((o.type=="Shake"||o.type=="Stir")&&ancestors.Contains(o.target))selected.Add(r);
            }
            var primary=selected.Where(r=>r.operation.target!=id).ToArray();var post=selected.Where(r=>r.operation.target==id).ToArray();
            double a=primary.Sum(r=>r.operation.seconds??0),b=post.Sum(r=>r.operation.seconds??0),all=a+b;
            var transfers=history.Where(r=>IsTransfer(r.operation.type)&&r.operation.to==id).ToArray();
            var transfer=transfers.FirstOrDefault(r=>primary.Any(p=>p.operation.target==r.operation.from));
            double preEnd=primary.Count()==0?0:primary.Max(r=>r.time);
            double preRest=transfer==null?0:RestBetween(preEnd,transfer.timeBefore??transfer.time);
            double postEnd=post.Length>0?post.Max(r=>r.time):transfers.LastOrDefault()?.time??time;
            // With a directly built glass and no agitation/transfer, use the first successful liquid addition.
            if(post.Length==0&&transfers.Length==0)postEnd=history.FirstOrDefault(r=>(r.operation.type=="PourSource"||r.operation.type=="TopUp"||r.operation.type=="PourCarbonated")&&r.operation.to==id)?.time??time;
            double hold=RestBetween(postEnd,time),rest=preRest+hold;
            return new ProcessDescriptors{actionSeconds=all,totalSeconds=time,primarySeconds=a,postSeconds=b,
                meanIntensity=all>0?selected.Sum(r=>(r.operation.seconds??0)*(r.operation.intensity??1))/all:0,
                remainingIce=transfer?.before?.FirstOrDefault(v=>v.id==transfer.operation.from)?.iceGrams??0,
                restFraction=all+rest>0?rest/(all+rest):0};
        }
        private double RestBetween(double start,double end)=>history.Where(r=>r.operation.type=="Rest").Sum(r=>Math.Max(0,Math.Min(end,r.time)-Math.Max(start,r.timeBefore??r.time)));
        private static bool IsTransfer(string type)=>type=="TransferLiquid"||type=="StrainTransfer"||type=="TransferWithIce";
    }
}
