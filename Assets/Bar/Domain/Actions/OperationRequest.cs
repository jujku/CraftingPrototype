using System;

namespace Bar.Domain
{
    /// <summary>预留库存和容量，保存输入意图以及待核心确认的实际毫升数。</summary>
    public sealed class OperationRequest
    {
        public double Co2LostOnEntryGrams {get;private set;}
        public void CarbonationReceipt(double grams){Guard.Nonnegative(grams);Co2LostOnEntryGrams=grams;}
        public string PourMethod {get;private set;}="None";
        public void GuideCarbonated(){if(PourMethod!="Direct")throw new InvalidOperationException("Guided requires a carbonated bottle request");PourMethod="BarspoonGuided";}
        private readonly Bottle bottle;
        private readonly JiggerCup source;
        private readonly Container target;
        private readonly string command, ingredient;
        private readonly Container commandSource, commandTarget;
        private readonly Action<double> accepted;
        /// <summary>通用操作的来源容器，可以为空。</summary>
        public Container SourceContainer => commandSource ?? source;
        /// <summary>通用操作的目标容器，可以为空。</summary>
        public Container TargetContainer => commandTarget ?? target;
        /// <summary>请求量的单位：ml、g、s 或次。</summary>
        public string AmountUnit => command=="AddIce"||command=="DiscardIce"?"g":command=="Shake"||command=="Stir"?"s":command=="Serve"?"次":"ml";
        /// <summary>核心确认的实际操作量，单位由 AmountUnit 给出。</summary>
        public double? ActualAmount { get; private set; }
        /// <summary>归一化摇搅强度。</summary>
        public double Intensity { get; }
        /// <summary>压汁请求形成时的最大下压程度，范围零到一。</summary>
        public double IntentPressFraction { get; }

        /// <summary>为宿主确认和日志关联生成的唯一请求标识。</summary>
        public string RequestId { get; } = Guid.NewGuid().ToString("N");
        /// <summary>待核心适配器提交的操作类型。</summary>
        public string OperationType => command ?? (bottle != null ? "PourSource" : "TransferLiquid");
        /// <summary>来源酒瓶的实例标识或来源容器的核心标识。</summary>
        public string SourceId => bottle != null ? bottle.InstanceId : SourceContainer?.CoreVesselId ?? "";
        /// <summary>目标容器在核心中的标识。</summary>
        public string TargetVesselId => TargetContainer?.CoreVesselId;
        /// <summary>现有核心原料标识，不复制其成分或密度。</summary>
        public string IngredientId => ingredient ?? bottle?.IngredientId;
        /// <summary>来源原料的批次温度，单位摄氏度。</summary>
        public double? SourceTemperatureC => bottle?.ServeTemperatureC;
        /// <summary>容量与库存限幅之前的输入意图量，单位毫升。</summary>
        public double IntendedMl { get; }
        /// <summary>库存与空余容量限幅后的提案量，单位毫升。</summary>
        public double RequestedMl { get; }
        /// <summary>核心确认的实际量，确认前为空，拒绝后为零，单位毫升。</summary>
        public double? ActualMl { get; private set; }
        /// <summary>提出请求时的输入倾角，单位度。</summary>
        public double IntentTiltDegrees { get; }
        /// <summary>提出请求时累计按住时长，单位秒。</summary>
        public double HeldSeconds { get; }
        /// <summary>本次输入片段或量酒器动画的时长，单位秒。</summary>
        public double DurationSeconds { get; }
        /// <summary>库存和容量预留是否仍在等待宿主确认。</summary>
        public bool IsPending { get; private set; } = true;
        /// <summary>请求确认或拒绝后通知订阅者，并携带最终请求状态。</summary>
        public event Action<DomainEvent> Resolved;
        internal OperationRequest(Bottle bottle, JiggerCup source, Container target, double intendedMl, double angle, double heldSeconds, double duration)
        {
            if ((bottle == null) == (source == null) || target == null || ReferenceEquals(source, target)) throw new ArgumentException("Invalid transfer endpoints.");
            Guard.Nonnegative(intendedMl); Guard.Nonnegative(angle); Guard.Nonnegative(heldSeconds); Guard.Nonnegative(duration);
            if (target.HasPending || bottle != null && bottle.HasPending || source != null && source.HasPending) throw new InvalidOperationException("Await the existing request acknowledgement.");
            if(bottle!=null&&(bottle.IngredientId=="soda"||bottle.IngredientId=="tonic"))PourMethod="Direct";
            this.bottle = bottle; this.source = source; this.target = target;
            IntendedMl = intendedMl; IntentTiltDegrees = angle; HeldSeconds = heldSeconds; DurationSeconds = duration;
            RequestedMl = Math.Min(intendedMl, Math.Min(bottle != null ? bottle.AvailableMl : source.PourableMl, target.AvailableMl));
            if (RequestedMl <= 0) throw new InvalidOperationException("No stock or capacity.");
            target.ReserveIn(RequestedMl); if (bottle != null) bottle.Reserve(RequestedMl); else source.ReserveOut(RequestedMl);
        }
        internal OperationRequest(string operation,Container from,Container to,double amount,double seconds=0,double intensity=0,double angle=0,string ingredient=null,Action<double> accepted=null,double heldSeconds=0,double pressFraction=0,double? intendedAmount=null)
        {
            Guard.Text(operation);Guard.Nonnegative(heldSeconds);Guard.Range(pressFraction,1);Guard.Nonnegative(amount);Guard.Nonnegative(seconds);Guard.Range(intensity,1);Guard.Nonnegative(angle);
            if(from==null&&to==null||ReferenceEquals(from,to))throw new ArgumentException("Operation endpoints required");
            if(from?.HasPending==true||to?.HasPending==true)throw new InvalidOperationException("Await acknowledgement");
            command=operation;commandSource=from;commandTarget=to;this.ingredient=ingredient;this.accepted=accepted;
            IntendedMl=intendedAmount??amount;Guard.Nonnegative(IntendedMl);if(IntendedMl<amount)throw new ArgumentException("Intent must cover requested amount");RequestedMl=amount;HeldSeconds=heldSeconds;IntentPressFraction=pressFraction;DurationSeconds=seconds;Intensity=intensity;IntentTiltDegrees=angle;
            from?.ReserveOperation();to?.ReserveOperation();
        }
        /// <summary>接受核心回执并完成请求。</summary>
        /// <precondition>请求仍挂起；实际毫升数在零与 RequestedMl 之间；目标快照不超容量；量酒器转移还须有来源快照。</precondition>
        /// <effects>先完成全部校验，再释放预留、更新权威快照、扣除瓶内实际量、填写 ActualMl，触发 operation_confirmed。</effects>
        public void Confirm(double actualMl, ContainerSnapshot targetSnapshot, ContainerSnapshot sourceSnapshot = null)
        {
            if (!IsPending) throw new InvalidOperationException("Request already resolved."); if(command!=null&&actualMl>RequestedMl&&actualMl-RequestedMl<1e-8)actualMl=RequestedMl; Guard.Range(actualMl, RequestedMl);
            if(command!=null)
            {
                commandTarget?.ValidateSnapshot(targetSnapshot);commandSource?.ValidateSnapshot(sourceSnapshot);
                Release();if(commandTarget!=null)commandTarget.AcceptSnapshot(targetSnapshot);if(commandSource!=null)commandSource.AcceptSnapshot(sourceSnapshot);
                ActualAmount=actualMl;ActualMl=AmountUnit=="ml"?actualMl:(double?)null;IsPending=false;accepted?.Invoke(actualMl);
                Resolved?.Invoke(new DomainEvent("operation_confirmed",SourceId,commandTarget?.InstanceId,OperationType,this));return;
            }
            target.ValidateSnapshot(targetSnapshot);
            if (source != null) source.ValidateSnapshot(sourceSnapshot);
            Release(); target.AcceptSnapshot(targetSnapshot);
            if (bottle != null) bottle.Consume(actualMl); else source.AcceptSnapshot(sourceSnapshot);
            ActualAmount = ActualMl = actualMl; IsPending = false; Resolved?.Invoke(new DomainEvent("operation_confirmed", SourceId, target.InstanceId, OperationType, this));
        }
        /// <summary>拒绝或取消尚未确认的请求。</summary>
        /// <precondition>已完成请求重复拒绝时直接返回。</precondition>
        /// <effects>释放输入和输出预留；ActualMl=0；不扣瓶内库存，不改快照，触发 operation_rejected。</effects>
        public void Reject()
        {
            if (!IsPending) return;
            Release(); ActualAmount = ActualMl = 0; IsPending = false; Resolved?.Invoke(new DomainEvent("operation_rejected", SourceId, TargetContainer?.InstanceId, OperationType, this));
        }
        private void Release() { if(command!=null){commandSource?.ReleaseOperation();commandTarget?.ReleaseOperation();return;} target.ReleaseIn(RequestedMl); if (bottle != null) bottle.Release(RequestedMl); else source.ReleaseOut(RequestedMl); }
    }
}
