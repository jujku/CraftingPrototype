using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Serialization.Json;
using CraftingResearch.CoreV2;
namespace Bar.Logging
{
    /// <summary>把记录的操作逐个交还原核心并校验出杯数值，绝不从意图重算物理。</summary>
    public sealed class LogReplay
    {
        public int ServedCount {get;private set;}
        public double MaximumError {get;private set;}
        public int AttemptCount {get;private set;}
        /// <summary>计算完整世界快照的 SHA-256。</summary>
        /// <precondition>world 已创建且可只读 Snapshot。</precondition>
        /// <effects>内存序列化快照，不修改世界。</effects>
        public static string SnapshotHash(World world)
        {
            using(var bytes=new MemoryStream())using(var sha=SHA256.Create())
            {new DataContractJsonSerializer(typeof(WorldState)).WriteObject(bytes,world.Snapshot());return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();}
        }
        /// <summary>把准确核心序列交回原 World 并核对出杯。</summary>
        /// <precondition>首行日志头有效、序号连续；成功 Serve 有描述符。</precondition>
        /// <effects>创建独立回放世界并更新比较计数；失败抛异常。</effects>
        public void Run(IEnumerable<string> lines,double tolerance=1e-9)
        {
            World world=null;SessionHeader header=null;string order="";int? attempt=null;var process=new ProcessTracker();long expected=0;double direct=0,guided=0,lost=0;ServedCount=0;MaximumError=0;AttemptCount=0;
            foreach(var line in lines.Where(s=>!string.IsNullOrWhiteSpace(s)))
            {
                var r=SessionJournal.Decode(line);if(r.sequence!=expected++)throw new InvalidOperationException("日志序列不连续");
                if(r.kind=="session_header"){if(world!=null||(r.header.schemaVersion!="bar-log-1.0"&&r.header.schemaVersion!="bar-log-1.1"&&r.header.schemaVersion!="bar-log-1.2"&&r.header.schemaVersion!="bar-log-1.3"&&r.header.schemaVersion!="bar-log-1.4"))throw new InvalidOperationException("日志头无效");header=r.header;world=new World(header.model,new WorldOptions{vessels=Array.Empty<VesselDefinition>(),recording=false,profile=header.profile});continue;}
                if(world==null)throw new InvalidOperationException("缺少日志头");
                if(r.kind=="order_started")
                {
                    if(string.IsNullOrEmpty(r.orderId)||!r.attemptIndex.HasValue||string.IsNullOrEmpty(r.orderDataSHA256))throw new InvalidOperationException("尝试标记缺失");
                    order=r.orderId;attempt=r.attemptIndex;AttemptCount++;direct=guided=lost=0;world=new World(header.model,new WorldOptions{vessels=Array.Empty<VesselDefinition>(),recording=false,profile=header.profile});process=new ProcessTracker();continue;
                }
                if(attempt.HasValue&&(r.orderId!=order||r.attemptIndex!=attempt))throw new InvalidOperationException("跨尝试记录");
                if(r.kind!="core_operation")continue;
                Compare(world.Time,r.timeBefore??world.Time,tolerance,"timeBefore");
                string error=null;object receipt=null;try{receipt=world.Apply(r.operation);}catch(ModelException ex){error=ex.Code;}
                if(r.rejected!=(error!=null)||error!=r.reason)throw new InvalidOperationException("拒绝原因不一致: "+error+" / "+r.reason);
                if(header.schemaVersion=="bar-log-1.4"&&error==null&&r.operation.type=="PourCarbonated"){var p=(PourResult)receipt;if(p.carbonatedPourMethod!=r.carbonatedPourMethod)throw new InvalidOperationException("Receipt method mismatch");Compare(p.acceptedMl,r.acceptedVolumeMl,tolerance,"acceptedVolumeMl");Compare(p.co2InputGrams,r.co2InputGrams,tolerance,"co2InputGrams");Compare(p.co2LostOnEntryGrams,r.co2LostOnEntryGrams,tolerance,"co2LostOnEntryGrams");if(p.carbonatedPourMethod=="Direct")direct+=p.acceptedMl;else guided+=p.acceptedMl;lost+=p.co2LostOnEntryGrams;}
                Compare(world.Time,r.time,tolerance,"time");process.Observe(r);
                if(r.operation.type=="Serve"&&!r.rejected&&(r.served==null||r.process==null))throw new InvalidOperationException("出杯缺少描述符");
                if(r.served!=null)
                {
                    var s=world.Vessels.Single(v=>v.id==r.operation.from).Snapshot();var t=r.served;
                    Compare(s.temperature,t.temperature,tolerance,"temperature");Compare(s.vesselTemperature,t.vesselTemperature,tolerance,"vesselTemperature");
                    Compare(s.abvPercent,t.abvPercent,tolerance,"abvPercent");Compare(s.dilutionPercent,t.dilutionPercent,tolerance,"dilutionPercent");
                    Compare(s.sugarPercent,t.sugarPercent,tolerance,"sugarPercent");Compare(s.acidPercent,t.acidPercent,tolerance,"acidPercent");Compare(s.sugarAcidRatio,t.sugarAcidRatio,tolerance,"sugarAcidRatio");
                    if(header.schemaVersion=="bar-log-1.4"){Compare(direct,r.attemptDirectPourVolumeMl,tolerance,"attemptDirectPourVolumeMl");Compare(guided,r.attemptGuidedPourVolumeMl,tolerance,"attemptGuidedPourVolumeMl");Compare(lost,r.attemptCo2LostDuringPour,tolerance,"attemptCo2LostDuringPour");Compare(s.directPourVolumeMl,t.directPourVolumeMl,tolerance,"directPourVolumeMl");Compare(s.guidedPourVolumeMl,t.guidedPourVolumeMl,tolerance,"guidedPourVolumeMl");Compare(s.co2LostDuringPour,t.co2LostDuringPour,tolerance,"co2LostDuringPour");if(s.carbonatedPourMethod!=t.carbonatedPourMethod)throw new InvalidOperationException("Pour lineage method mismatch");}
                    Compare(s.co2GL,t.co2GL,tolerance,"co2GL");Compare(s.co2Retention,t.co2Retention,tolerance,"co2Retention");Compare(s.volumeMl,t.volumeMl,tolerance,"volumeMl");Compare(s.iceGrams,t.iceGrams,tolerance,"iceGrams");
                    Compare(s.liquidGrams,t.liquidGrams,tolerance,"liquidGrams");Compare(s.meltGrams,t.meltGrams,tolerance,"meltGrams");Compare(s.energy,t.energy,tolerance,"energy");Compare(s.vesselEnergy,t.vesselEnergy,tolerance,"vesselEnergy");
                    for(int i=0;i<4;i++)Compare(s.flavor[i],t.flavor[i],tolerance,"flavor"+i);
                    var actual=process.Describe(r.operation.from,r.time);
                    foreach(var f in typeof(ProcessDescriptors).GetFields())Compare((double)f.GetValue(actual),(double)f.GetValue(r.process),tolerance,f.Name);
                    ServedCount++;
                }
            }
            if(world==null)throw new InvalidOperationException("空日志");
        }
        private void Compare(double? a,double? b,double tolerance,string name)
        {if(a.HasValue!=b.HasValue)throw new InvalidOperationException(name+" 空值不一致");if(!a.HasValue)return;double e=Math.Abs(a.Value-b.Value);if(double.IsNaN(e)||double.IsInfinity(e)||e>tolerance)throw new InvalidOperationException(name+" 差值 "+e);MaximumError=Math.Max(MaximumError,e);}
    }
}
