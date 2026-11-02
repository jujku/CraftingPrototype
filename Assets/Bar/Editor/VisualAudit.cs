using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using Bar.Data;
using CraftingResearch.CoreV2;
using UnityEditor;
using UnityEngine;
namespace Bar.Unity.Editor
{
    /// <summary>A0：用原核心和场景物品参数测量参考操作，不启动订单系统。</summary>
    public static class VisualAudit
    {
        [Serializable] public sealed class Range {public string recipe,vessel;public double time;public double? wall,liquid,co2,dilution;}
        public static void Ranges()
        {
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../VisualReports"));Directory.CreateDirectory(root);
            var model=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));
            var catalog=ItemCatalog.Load(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Data/Json/items.json")),model);
            var rows=new List<string>();var physical=new StringBuilder();
            foreach(string recipe in new[]{"Gin Sour","G&T","Tom Collins","Ice cup"})
            {
                var adapter=new WorldAdapter(model);var factory=new Bar.Domain.ItemFactory(catalog,id=>model.ingredients.Single(i=>i.id==id).temperature,()=>.5);
                var shaker=(Bar.Domain.CobblerShaker)factory.Create("shaker.cobbler","shaker");var glass=(Bar.Domain.Glass)factory.Create(recipe=="Gin Sour"?"glass.coupe":"glass.highball","glass");adapter.Register(shaker);adapter.Register(glass);var w=adapter.World;string s=shaker.Body.CoreVesselId,g=glass.CoreVesselId;
                void Sample()
                {
                    foreach(var v in w.Vessels){var snap=v.Snapshot();var row=new Range{recipe=recipe,vessel=v.kind,time=w.Time,wall=snap.vesselTemperature,liquid=snap.temperature,co2=snap.co2GL,dilution=snap.dilutionPercent};using(var m=new MemoryStream()){new DataContractJsonSerializer(typeof(Range)).WriteObject(m,row);rows.Add(Encoding.UTF8.GetString(m.ToArray()));}}
                    // Explicit legacy numerical projection: added passive fields never enter this byte comparison.
                    var numbers=new List<double>{w.Time,w.EnvironmentEnergy,w.MinimumInputTemperature,w.MaximumInputTemperature,w.MinimumLiquidus};
                    foreach(var v in w.Vessels){numbers.AddRange(new[]{v.m,v.ice,v.E,v.water,v.ethanol,v.sugar,v.acid,v.gas,v.melt,v.volume,v.gasInput,v.vesselEnergy});numbers.AddRange(v.flavor);}
                    var l=w.Ledger();numbers.AddRange(new[]{l.massResidual,l.energyResidual,l.compositionResidual});numbers.AddRange(l.components);numbers.AddRange(l.flavorResidual);
                    physical.AppendLine(string.Join(",",numbers.Select(n=>n.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
                }
                void Op(Operation o){w.Apply(o);Sample();}
                void Advance(string target,string mode,double seconds){for(int i=0;i<(int)Math.Round(seconds*10);i++)Op(new Operation{type=mode,target=target,seconds=.1,intensity=.8});}
                Sample();
                if(recipe=="Ice cup"){Op(new Operation{type="AddIce",to=g,grams=60});Advance(g,"Rest",20);}
                else if(recipe=="G&T"){Op(new Operation{type="AddIce",to=g,grams=100});Op(new Operation{type="PourSource",to=g,ingredient="spirit",ml=45});Op(new Operation{type="PourSource",to=g,ingredient="tonic",ml=120});Advance(g,"Stir",3);Advance(g,"Rest",10);}
                else {foreach(var pair in new[]{("spirit",45d),("syrup",20d),("lemon",25d)})Op(new Operation{type="PourSource",to=s,ingredient=pair.Item1,ml=pair.Item2});Op(new Operation{type="AddIce",to=s,grams=100});Advance(s,"Shake",12);Op(new Operation{type="StrainTransfer",from=s,to=g});if(recipe=="Tom Collins"){Op(new Operation{type="AddIce",to=g,grams=60});Op(new Operation{type="TopUp",to=g,ingredient="soda",ml=60});}Advance(g,"Rest",10);}
            }
            File.WriteAllLines(Path.Combine(root,"ranges.jsonl"),rows,new UTF8Encoding(false));File.WriteAllText(Path.Combine(root,"physical.txt"),physical.ToString(),new UTF8Encoding(false));
        }
    }
}
