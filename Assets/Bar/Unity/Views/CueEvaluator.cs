using System;
using Bar.Logging;
using CraftingResearch.CoreV2;
namespace Bar.Unity
{
    /// <summary>只读核心快照到线索值的映射；人为设定、未标定，不反写核心。</summary>
    public sealed class CueEvaluator
    {
        private readonly CueSettings settings;
        /// <summary>保存线索配置。</summary>
        /// <precondition>settings 非 null，曲线已从 JSON 加载。</precondition>
        /// <effects>不接触世界或场景。</effects>
        public CueEvaluator(CueSettings settings){this.settings=settings??throw new ArgumentNullException(nameof(settings));}
        /// <summary>把核心快照和已接受摇搅强度转换为线索。</summary>
        /// <precondition>快照非 null，icePortionGrams 为正，输入有限。</precondition>
        /// <effects>新建输出与颜色数组，不改快照。</effects>
        public CueFrame Evaluate(VesselState snapshot,string kind,double room,double intensity,double icePortionGrams,bool sealedShaker,bool seeInside,bool onScreen,long frame,CueFrame output=null)
        {
            bool liquid=onScreen&&snapshot.volumeMl>1e-8&&(!sealedShaker||seeInside);
            double dilution=CueSettings.Map(settings.dilution,snapshot.dilutionPercent??0);
            var rgba=output?.liquidRgba??new double[4];for(int i=0;i<4;i++)rgba[i]=settings.liquidBaseRgba[i]+(settings.liquidDilutedRgba[i]-settings.liquidBaseRgba[i])*dilution;
            var result=output??new CueFrame();result.vesselId=snapshot.id;result.frame=frame;
            result.vesselId=snapshot.id;
            result.frame=frame;
            result.onScreen=onScreen;
            result.seeInside=seeInside;
            result.liquidVisible=liquid;
            result.iceVisible=onScreen&&snapshot.iceGrams>1e-8&&(!sealedShaker||seeInside);
            result.frost=onScreen&&!seeInside&&kind=="shaker"?CueSettings.Map(settings.frost,snapshot.vesselTemperature??room):0;
            result.condensation=onScreen&&kind=="glass"?CueSettings.Map(settings.condensation,room-(snapshot.vesselTemperature??room)):0;
            result.bubbleCount=liquid?Math.Min(settings.bubbleMax,Math.Round(CueSettings.Map(settings.bubbleCount,snapshot.co2GL??0))):0;
            result.bubbleSpeed=liquid?CueSettings.Map(settings.bubbleSpeed,snapshot.co2GL??0):0;
            result.iceSound=snapshot.iceGrams>0?CueSettings.Map(settings.iceSound,intensity*snapshot.iceGrams/icePortionGrams):0;
            result.dilution=liquid?dilution:0;
            result.liquidRgba=rgba;
            return result;
        }
    }
}
