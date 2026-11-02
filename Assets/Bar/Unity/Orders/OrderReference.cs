using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using CraftingResearch.CoreV2;
namespace Bar.Unity
{
    /// <summary>把数据中的参考做法交给现有核心；用于生成初始区间与验证，不参与玩家步骤评分。</summary>
    public static class OrderReference
    {
        public static Operation Register(Container c)
        {var t=c.Thermal;return new Operation{type="RegisterVessel",id=c.CoreVesselId,definition=new VesselDefinition{id=c.CoreVesselId,kind=c is ShakerBody?"shaker":"glass",capacityMl=c.CapacityMl,massGrams=t.MassGrams,specificHeat=t.SpecificHeat,liquidK=t.LiquidK,environmentK=t.EnvironmentK}};}
        public static Operation[] Plan(ModelData model,string items,OrderDefinition order,string variant="")
        {
            var factory=new ItemFactory(ItemCatalog.Load(items,model),id=>model.ingredients.Single(i=>i.id==id).temperature,()=>.5);
            var shaker=(CobblerShaker)factory.Create("shaker.cobbler","reference-shaker");var glass=(Glass)factory.Create("glass."+order.glass,"reference-glass");
            var ops=new List<Operation>{Register(shaker.Body),Register(glass)};ops.AddRange(Recipe(order,shaker.Body.CoreVesselId,glass.CoreVesselId,variant));return ops.ToArray();
        }
        /// <summary>仅生成参考操作，variant 是验收用故意做偏的做法。</summary>
        public static IEnumerable<Operation> Recipe(OrderDefinition order,string s,string g,string variant="")
        {
            var r=order.reference;bool shaken=r.shakeSeconds>0;string target=shaken?s:g;
            if(r.glassIceGrams>0&&variant!="no-ice")yield return new Operation{type="AddIce",to=g,grams=r.glassIceGrams};
            yield return new Operation{type="PourSource",to=target,ingredient="spirit",ml=r.spiritMl};
            if(r.syrupMl>0&&variant!="no-syrup")yield return new Operation{type="PourSource",to=target,ingredient="syrup",ml=r.syrupMl};
            if(r.lemonMl>0)yield return new Operation{type="PourSource",to=target,ingredient="lemon",ml=r.lemonMl};
            if(r.tonicMl>0)yield return new Operation{type="PourCarbonated",to=g,ingredient="tonic",ml=r.tonicMl,pourMethod="Direct"};
            if(shaken)
            {
                yield return new Operation{type="AddIce",to=s,grams=r.shakerIceGrams};
                yield return new Operation{type="Shake",target=s,seconds=r.shakeSeconds,intensity=r.intensity};
                yield return new Operation{type="StrainTransfer",from=s,to=g};
            }
            if(r.sodaMl>0&&variant!="no-soda")yield return new Operation{type="PourCarbonated",to=g,ingredient="soda",ml=r.sodaMl,pourMethod="Direct"};
            if(r.stirSeconds>0)yield return new Operation{type="Stir",target=g,seconds=r.stirSeconds,intensity=r.intensity};
            if(variant=="no-ice")yield return new Operation{type="Rest",target=g,seconds=60};
            yield return new Operation{type="Serve",from=g};
        }
    }
}
