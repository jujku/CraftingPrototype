namespace Bar.Domain
{
    /// <summary>把冰铲/冰夹的剩余整块冰丢入水槽，记录尚未进入物理核心的库存废弃。</summary>
    public sealed class DiscardToolIceAction:CraftAction
    {
        private readonly double grams;
        public override string Id=>"ice.discard";
        public override string Label=>"倒掉工具上的冰";
        public DiscardToolIceAction(double grams){Guard.Nonnegative(grams);if(grams==0)throw new System.ArgumentException();this.grams=grams;}
        public override bool CanExecute(Item actor,Item target,Hands hands)=>Usable(actor,hands,Id)&&(actor is IceScoop||actor is IceTongs)&&((Tool)actor).LoadedCubes>0&&target is Sink&&InteractionAccess.Reachable(target,hands);
        protected override ActiveAction Create(Item actor,Item target,Hands hands)=>new DiscardSession(this,(Tool)actor,(Sink)target,hands,grams);
        private sealed class DiscardSession:ActiveAction
        {
            internal DiscardSession(IBarAction action,Tool tool,Sink sink,Hands hands,double grams):base(action,tool,sink,hands,false)
            {
                int count=tool.LoadedCubes;tool.LoadedCubes=0;sink.DiscardedToolIceCubes+=count;
                Emit(new DomainEvent("tool_ice_discarded",tool.InstanceId,sink.InstanceId,action.Id,observation:new ToolIceDiscardReceipt(count,count*grams)));End();
            }
            public override void Update(ActionInput input,double dt){ValidateUpdate(input,dt);}
        }
    }
    public sealed class ToolIceDiscardReceipt
    {
        public int Cubes {get;}
        public double Grams {get;}
        public ToolIceDiscardReceipt(int cubes,double grams){Cubes=cubes;Grams=grams;}
    }
}
