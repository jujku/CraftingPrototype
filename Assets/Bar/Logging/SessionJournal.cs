using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using Bar.Domain;
namespace Bar.Logging
{
    /// <summary>只订阅领域总线并序列化，不读取场景、不访问文件、不推进世界。</summary>
    public sealed class SessionJournal : IDisposable
    {
        private DomainEventStream stream; private readonly Action<string> sink; private long sequence;
        public string OrderId {get;set;}="";
        public int? AttemptIndex {get;set;}
        /// <summary>尝试间换总线但保留会话序号，旧总线解除订阅。</summary>
        public void Connect(DomainEventStream next){stream.Published-=Receive;stream=next;stream.Published+=Receive;}
        /// <summary>创建纯事件订阅器。</summary>
        /// <precondition>stream 与 sink 非 null。</precondition>
        /// <effects>订阅总线；每条观察记录分配连续序号。</effects>
        public SessionJournal(DomainEventStream stream,Action<string> sink)
        { this.stream=stream??throw new ArgumentNullException(nameof(stream));this.sink=sink??throw new ArgumentNullException(nameof(sink));stream.Published+=Receive; }
        private void Receive(DomainEvent e)
        { if(e.Observation is LogRecord row){row.kind=e.Kind;row.sequence=sequence++;row.orderId=OrderId;row.attemptIndex=AttemptIndex;var compact=row.Copy();
                bool boundary=row.kind=="action_selected"||row.kind=="view_inspect"||row.kind=="action_began"||row.kind=="action_ended"||row.kind=="action_cancelled"||row.kind=="action_rejected";
                if(!boundary&&row.served==null){compact.after=null;if(row.operation==null||!(row.operation.type=="StrainTransfer"||row.operation.type=="TransferLiquid"||row.operation.type=="TransferWithIce"))compact.before=null;}
                if(row.kind=="core_operation"||row.kind=="frame_statistics"){compact.cues=null;compact.cueFrame=0;}
                else if(row.kind=="cue_frame"){compact.cueFrame=0;compact.cues=row.cues?.Select(c=>c.SampleCopy()).ToArray();}
                else if(row.kind!="cue_frame")compact.cues=row.cues?.Where(c=>(row.before??Array.Empty<CraftingResearch.CoreV2.VesselState>()).Any(v=>v.id==c.vesselId)||(row.after??Array.Empty<CraftingResearch.CoreV2.VesselState>()).Any(v=>v.id==c.vesselId)).ToArray();
                sink(Encode(compact));} }
        /// <summary>编码一条完整观察记录。</summary>
        /// <precondition>value 含有限数值并满足日志字段类型。</precondition>
        /// <effects>只在内存中生成 JSON 字符串。</effects>
        public static string Encode(LogRecord value)
        { using(var output=new MemoryStream()){new DataContractJsonSerializer(typeof(LogRecord)).WriteObject(output,value);return Encoding.UTF8.GetString(output.ToArray());} }
        /// <summary>解析一条日志 JSON。</summary>
        /// <precondition>json 是当前版本的有效单行 JSON。</precondition>
        /// <effects>只生成 DTO；不读文件或访问世界。</effects>
        public static LogRecord Decode(string json)
        { using(var input=new MemoryStream(Encoding.UTF8.GetBytes(json)))return (LogRecord)new DataContractJsonSerializer(typeof(LogRecord)).ReadObject(input); }
        /// <summary>结束纯日志订阅。</summary>
        /// <precondition>构造已经完成。</precondition>
        /// <effects>解除 Published 订阅，不关闭宿主 sink。</effects>
        public void Dispose() { stream.Published-=Receive; }
    }
}
