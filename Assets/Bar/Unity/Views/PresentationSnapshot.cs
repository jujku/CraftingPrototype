using CraftingResearch.CoreV2;
namespace Bar.Unity
{
    /// <summary>复用显示 DTO，只读核心公开量；不在呈现帧调用会复制数组的 Snapshot。</summary>
    public sealed class PresentationSnapshot
    {
        public VesselState Value {get;}=new VesselState{optical=new double[7],flavor=new double[4],components=new double[5]};
        public VesselState Read(Vessel v,double ethanolDensity)
        {
            var s=Value;s.id=v.id;s.temperature=v.Temperature;s.vesselTemperature=v.VesselTemperature;s.volumeMl=v.volume;s.liquidGrams=v.m;s.iceGrams=v.ice;s.meltGrams=v.melt;s.energy=v.E;s.vesselEnergy=v.vesselEnergy;
            s.dilutionPercent=v.m!=0?100*v.melt/v.m:(double?)null;s.sugarPercent=v.m!=0?100*v.sugar/v.m:(double?)null;s.acidPercent=v.m!=0?100*v.acid/v.m:(double?)null;s.abvPercent=v.volume!=0?100*v.ethanol/ethanolDensity/v.volume:(double?)null;
            s.co2GL=v.volume!=0?1000*v.gas/v.volume:(double?)null;s.co2Retention=v.gasInput!=0?v.gas/v.gasInput:(double?)null;s.sugarAcidRatio=v.acid!=0?v.sugar/v.acid:(double?)null;
            for(int i=0;i<7;i++)s.optical[i]=v.m!=0?v.optical[i]/v.m:0;for(int i=0;i<4;i++)s.flavor[i]=v.m!=0?v.flavor[i]/v.m:0;for(int i=0;i<5;i++)s.components[i]=v.Component(i);
            s.served=v.served;s.iceExchangePaused=v.iceExchangePaused;return s;
        }
    }
}
