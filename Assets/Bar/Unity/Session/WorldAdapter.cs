using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Logging;
using CraftingResearch.CoreV2;

namespace Bar.Unity
{
    /// <summary>把领域请求交给唯一物理核心，动态注册容器并回填实际量与权威快照。</summary>
    public sealed class WorldAdapter
    {
        private readonly Dictionary<string, Container> containers = new Dictionary<string, Container>();
        /// <summary>唯一物理世界，调试与测试通过它读取数值。</summary>
        public World World { get; }
        public DomainEventStream Events { get; } = new DomainEventStream();
        public CueFrame[] DisplayedCues { get; set; } = Array.Empty<CueFrame>();
        public long CueFrameNumber { get; set; }
        public bool DebugExposed {get;set;}
        public string CurrentActionId {get;set;}
        public string CurrentActivationId {get;set;}
        public string CurrentHand {get;set;}
        public string CurrentActor {get;set;}
        public string CurrentTarget {get;set;}
        private readonly ProcessTracker process=new ProcessTracker();
        private double directTotal,guidedTotal,entryLostTotal;
        private readonly Dictionary<string,(double time,double intensity)> agitation=new Dictionary<string,(double,double)>();
        public double Agitation(string id,double holdSeconds)
            =>agitation.TryGetValue(id,out var a)&&World.Time-a.time<=holdSeconds?a.intensity:0;
        public VesselState[] States(Item actor=null,Item target=null)
        {
            var ids=new HashSet<string>();
            void Add(Item item){if(item is Container c)ids.Add(c.CoreVesselId);if(item is CompositeItem composite)foreach(var child in composite.Parts)Add(child);}
            Add(actor);Add(target);
            return World.Vessels.Where(v=>ids.Contains(v.id)).Select(v=>v.Snapshot()).ToArray();
        }
        public void Publish(string kind,LogRecord row)
        {row.time=World.Time;row.cues=DisplayedCues;row.cueFrame=CueFrameNumber;row.debugExposed=DebugExposed;Events.Publish(new DomainEvent(kind,row.actorId,row.targetId,row.actionId,observation:row));}
        private object Apply(Operation op)
        {
            var row=new LogRecord{kind="core_operation",operation=op.Copy(),timeBefore=World.Time,actionId=CurrentActionId,activationId=CurrentActivationId,hand=CurrentHand,actorId=CurrentActor,targetId=CurrentTarget};
            if(op.type!="Rest")row.before=World.Vessels.Where(v=>v.id==op.from||v.id==op.to||v.id==op.target).Select(v=>v.Snapshot()).ToArray();
            try
            {
                object result=World.Apply(op);
                if(op.type=="PourCarbonated"){var pour=(PourResult)result;row.carbonatedPourMethod=pour.carbonatedPourMethod;row.acceptedVolumeMl=pour.acceptedMl;row.co2InputGrams=pour.co2InputGrams;row.co2LostOnEntryGrams=pour.co2LostOnEntryGrams;if(pour.carbonatedPourMethod=="Direct")directTotal+=pour.acceptedMl;else guidedTotal+=pour.acceptedMl;entryLostTotal+=pour.co2LostOnEntryGrams;}
                if(op.type=="Shake"||op.type=="Stir")agitation[op.target]=(World.Time,op.intensity??1);
                if(op.type!="Rest")row.after=World.Vessels.Where(v=>v.id==op.from||v.id==op.to||v.id==op.target).Select(v=>v.Snapshot()).ToArray();
                row.time=World.Time;process.Observe(row);
                if(op.type=="Serve"){row.served=World.Vessels.Single(v=>v.id==op.from).Snapshot();row.process=process.Describe(op.from,World.Time);row.attemptDirectPourVolumeMl=directTotal;row.attemptGuidedPourVolumeMl=guidedTotal;row.attemptCo2LostDuringPour=entryLostTotal;}
                Publish("core_operation",row);return result;
            }
            catch(ModelException e){row.rejected=true;row.reason=e.Code;Publish("core_operation",row);throw;}
        }
        /// <summary>最后一个已确认或已拒绝的领域请求。</summary>
        public OperationRequest LastRequest { get; private set; }
        /// <summary>最后一次核心拒绝的错误码，成功提交后清空。</summary>
        public string LastError { get; private set; }
        /// <summary>创建空的动态容器世界。</summary>
        /// <precondition>model 是经过 fixture 验证的核心数据，不能缺少原料或热学参数。</precondition>
        /// <effects>创建不含默认容器的 World；未改动传入模型，也未创建第二套物理。</effects>
        public WorldAdapter(ModelData model) { World = new World(model, new WorldOptions { vessels = Array.Empty<VesselDefinition>(), recording = false }); }
        /// <summary>注册物品及其永久数值容器。</summary>
        /// <precondition>物品非空，所有新容器的 CoreVesselId 尚未注册。</precondition>
        /// <effects>逐个把容量和 VesselThermal 传给核心，保存领域容器映射；复合物品递归注册部件。</effects>
        public void Register(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item is Container c)
            {
                var t = c.Thermal;
                Apply(new Operation {type="RegisterVessel",id=c.CoreVesselId,definition=new VesselDefinition { id = c.CoreVesselId, kind = c is ShakerBody ? "shaker" : c is JiggerCup ? "jigger" : "glass", capacityMl = c.CapacityMl, massGrams = t.MassGrams, specificHeat = t.SpecificHeat, liquidK = t.LiquidK, environmentK = t.EnvironmentK }});
                containers.Add(c.CoreVesselId, c);
            }
            if (item is CompositeItem composite) foreach (var part in composite.Parts) Register(part);
        }
        /// <summary>提交一个预留请求，核心决定实际执行量。</summary>
        /// <precondition>请求仍挂起，目标及容器来源已经 Register；只能由时钟节拍调用。</precondition>
        /// <effects>执行已注册的加料、转移、摇搅、废弃或出杯操作，Confirm 回填实际毫升与快照；核心拒绝时 Reject 释放预留并记录错误码。</effects>
        public void Submit(OperationRequest request)
        {
            if (request == null || !request.IsPending) return;
            LastRequest = request; LastError = null;
            try
            {
                double actual=0;
                var source=request.SourceContainer;var destination=request.TargetContainer;
                switch(request.OperationType)
                {
                    case "PourSource":case "TopUp":
                        if(request.PourMethod!="None"){var receipt=(PourResult)Apply(new Operation{type="PourCarbonated",to=request.TargetVesselId,ingredient=request.IngredientId,ml=request.RequestedMl,temperature=request.SourceTemperatureC,pourMethod=request.PourMethod});actual=receipt.acceptedMl;request.CarbonationReceipt(receipt.co2LostOnEntryGrams);}
                        else actual=((PourResult)Apply(new Operation{type=request.OperationType,to=request.TargetVesselId,ingredient=request.IngredientId,ml=request.RequestedMl,temperature=request.SourceTemperatureC})).acceptedMl;break;
                    case "TransferLiquid":case "StrainTransfer":case "TransferWithIce":
                        var from=World.GetVessel(request.SourceId);var to=World.GetVessel(request.TargetVesselId);
                        double occupied=request.OperationType=="TransferWithIce"?from.OccupiedVolume:from.volume;
                        double fraction=Math.Min(1,Math.Min(from.volume<=0?0:request.RequestedMl/from.volume,occupied<=0?0:Math.Max(0,to.capacity-to.OccupiedVolume)/occupied));
                        actual=from.volume*fraction;Apply(new Operation{type=request.OperationType,from=request.SourceId,to=request.TargetVesselId,fraction=fraction});break;
                    case "AddIce":Apply(new Operation{type="AddIce",to=request.TargetVesselId,grams=request.RequestedMl});actual=request.RequestedMl;break;
                    case "DiscardIce":actual=(double)Apply(new Operation{type="DiscardIce",from=request.SourceId});break;
                    case "DiscardLiquid":actual=World.GetVessel(request.SourceId).volume;Apply(new Operation{type="DiscardLiquid",from=request.SourceId});break;
                    case "Shake":case "Stir":Apply(new Operation{type=request.OperationType,target=request.SourceId,seconds=request.DurationSeconds,intensity=request.Intensity});actual=request.DurationSeconds;break;
                    case "Serve":Apply(new Operation{type="Serve",from=request.SourceId});actual=1;break;
                    default:throw new InvalidOperationException("Unsupported operation "+request.OperationType);
                }
                request.Confirm(actual,destination==null?null:Snapshot(destination.CoreVesselId),source==null?null:Snapshot(source.CoreVesselId));
            }
            catch (ModelException ex) { LastError = ex.Code; request.Reject();throw; }
        }
        /// <summary>推进已完成事件分段的物理时长并刷新领域快照。</summary>
        /// <precondition>seconds 有限且非负，至少有一个已注册且未出杯的容器。</precondition>
        /// <effects>通过 World 的 Rest 推进所有容器，随后从核心同步液体和冰；不冻结全局时间。</effects>
        public void Advance(double seconds)
        {
            var first = World.Vessels.FirstOrDefault(v => !v.served);
            if(first==null)return;
            Apply(new Operation { type = "Rest", target = first.id, seconds = seconds });
            foreach (var c in containers.Values) c.ApplySnapshot(Snapshot(c.CoreVesselId));
        }
        /// <summary>把核心状态转换为领域只读快照。</summary>
        /// <precondition>id 对应已经注册的容器，出杯后仍允许只读观察。</precondition>
        /// <effects>只读取核心体积、冰质量及气体量；冰块数除以核心每块克数，不另算融冰。</effects>
        public ContainerSnapshot Snapshot(string id)
        { var state = World.Vessels.Single(v=>v.id==id); return new ContainerSnapshot(state.volume, state.ice / World.Data.icePortionGrams, state.gas > 0, state.ice/World.Data.iceDensity); }
    }
}
