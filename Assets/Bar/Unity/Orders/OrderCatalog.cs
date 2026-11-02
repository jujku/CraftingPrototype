using System;
using System.Linq;
using Bar.Logging;
using CraftingResearch.CoreV2;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>人为设定的订单区间和定性评语；不包含另一套物理。</summary>
    [Serializable] public sealed class OrderCatalog
    {
        public string note,source,satisfiedId,satisfiedText;
        public int maxSentences=2;
        public double strongDeviation=1;
        public OrderDefinition[] orders;
        public FeedbackRule[] feedback;
        public static OrderCatalog Load()
        {
            var result=JsonUtility.FromJson<OrderCatalog>(Resources.Load<OrderDataAsset>("BarOrders").Json.text);result.Validate();return result;
        }
        public void Validate()
        {
            if(maxSentences<1||strongDeviation<=0||orders==null||orders.Length!=3||orders.Select(o=>o.id).Distinct().Count()!=orders.Length)throw new InvalidOperationException("订单配置无效");
            foreach(var o in orders)foreach(var r in o.ranges)
                if(r.scale<=0||double.IsNaN(r.scale)||double.IsInfinity(r.scale)||(r.hasMin&&r.hasMax&&r.min>r.max))throw new InvalidOperationException("订单区间无效："+o.id+"/"+r.dimension);
        }
        public string Text(string id)=>id==satisfiedId?satisfiedText:feedback.Single(f=>f.id==id).text;
        /// <summary>只按出杯描述符判定，按超出区间量/半宽排序，同分按数据次序。</summary>
        public OrderDimension[] Evaluate(OrderDefinition order,VesselState state,string glass)
        {
            return order.ranges.Select(range=>
            {
                // Explicit fields avoid reflection/stripping dependencies in WebGL IL2CPP.
                double? value=range.dimension switch
                {"glass"=>null,"abvPercent"=>state.abvPercent,"sugarAcidRatio"=>state.sugarAcidRatio,"temperature"=>state.temperature,"dilutionPercent"=>state.dilutionPercent,"co2Retention"=>state.co2Retention,_=>throw new InvalidOperationException("未知订单维度："+range.dimension)};
                string direction="inside";double deviation=0;
                if(range.dimension=="glass"){if(glass!=order.glass){direction="wrong";deviation=range.scale;}}
                else if(!value.HasValue||double.IsNaN(value.Value)||double.IsInfinity(value.Value)){direction="missing";deviation=range.scale;}
                else if(range.hasMin&&value.Value<range.min){direction="low";deviation=range.min-value.Value;}
                else if(range.hasMax&&value.Value>range.max){direction="high";deviation=value.Value-range.max;}
                double score=deviation/range.scale;string severity=score>=strongDeviation?"strong":"slight";
                var rule=feedback.FirstOrDefault(f=>f.dimension==range.dimension&&f.direction==direction&&f.severity==severity);
                if(direction!="inside"&&rule==null)throw new InvalidOperationException("缺少反馈规则："+range.dimension+"/"+direction+"/"+severity);
                return new OrderDimension{dimension=range.dimension,value=value,inside=direction=="inside",direction=direction,severity=direction=="inside"?"none":severity,normalizedDeviation=score,feedbackId=rule?.id};
            }).ToArray();
        }
        public string[] Displayed(OrderDimension[] dimensions)
        {var ids=dimensions.Where(d=>!d.inside).OrderByDescending(d=>d.normalizedDeviation).Take(maxSentences).Select(d=>d.feedbackId).ToArray();return ids.Length>0?ids:new[]{satisfiedId};}
    }
    [Serializable] public sealed class OrderDefinition
    {public string id,name,glass,glassLabel,description,targetSource;public OrderRange[] ranges;public ReferenceRecipe reference;}
    [Serializable] public sealed class ReferenceRecipe
    {public double spiritMl,syrupMl,lemonMl,tonicMl,sodaMl,shakerIceGrams,glassIceGrams,shakeSeconds,stirSeconds,intensity;}
    [Serializable] public sealed class OrderRange
    {public string dimension;public bool hasMin,hasMax;public double min,max,scale;}
    [Serializable] public sealed class FeedbackRule
    {public string id,dimension,direction,severity,text;}
}
