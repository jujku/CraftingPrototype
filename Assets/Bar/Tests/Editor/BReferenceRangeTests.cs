using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bar.Data;
using Bar.Domain;
using Bar.Unity;
using CraftingResearch.CoreV2;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Tests
{
    public sealed class BReferenceRangeTests
    {
        [Test] public void NormalSourTraceReachesVisibleFrostWithoutChangingThermalModel()
        {
            string Read(string file)=>File.ReadAllText(Path.Combine(Application.dataPath,"Bar",file));var data=JsonUtility.FromJson<ModelData>(Read("Core/Data/model-v2.json"));
            var factory=new ItemFactory(ItemCatalog.Load(Read("Data/Json/items.json"),data),id=>data.ingredients.Single(i=>i.id==id).temperature,()=>0);var shaker=(CobblerShaker)factory.Create("shaker.cobbler","B:sour");var adapter=new WorldAdapter(data);adapter.Register(shaker);
            foreach(var pair in new[]{("spirit",45d),("syrup",20d),("lemon",25d)})adapter.World.Apply(new Operation{type="PourSource",to=shaker.Body.CoreVesselId,ingredient=pair.Item1,ml=pair.Item2});adapter.World.Apply(new Operation{type="AddIce",to=shaker.Body.CoreVesselId,grams=120});
            var surface=new SurfaceEvolution(BPresentationSettings.Load().surface);var rows=new List<string>{"time,wallC,liquidC,frost,dilutionPercent"};double minimum=25;
            for(int i=0;i<120;i++){adapter.World.Apply(new Operation{type="Shake",target=shaker.Body.CoreVesselId,seconds=.1,intensity=.8});var s=adapter.World.Vessels.Single().Snapshot();minimum=System.Math.Min(minimum,s.vesselTemperature.Value);surface.Step((float)s.vesselTemperature.Value,false,false,.1f,.8f);rows.Add(adapter.World.Time+","+s.vesselTemperature+","+s.temperature+","+surface.Frost+","+s.dilutionPercent);}
            // 2026-10-28 (core 2.2, third frost pass): the shaken wall goes below 0 °C and a 12 s shake at 0.8 shows frost that is still growing.
            Assert.That(surface.Frost,Is.InRange(.15f,.85f));Assert.That(minimum,Is.LessThan(BPresentationSettings.Load().surface.metalFrostOnset));Assert.That(adapter.World.Ledger().massResidual,Is.EqualTo(0).Within(1e-8));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"normal-sour-range.csv"),rows);
        }
        /// <summary>
        /// on the core 2.2 wall trace frost needs a wall below 0 °C,
        /// grows as an accumulator whose speed is set by coldness and shake strength (about 9 s bare to full even at full strength and cold),
        /// a wipe keeps it off while the tin still holds ice until the tin is shaken hard again, and after the ice is dumped nothing comes back.
        /// Round: shake 12 s, strain at 15 s, wipe at 17 s with the ice still in, shake again 19–25 s, dump the ice at 28 s, wipe at 30 s, watch to 90 s.
        /// </summary>
        [Test] public void FrostBuildsOverSecondsOnlyBelowZeroAndReturnsSlowlyAfterWiping()
        {
            var data=JsonUtility.FromJson<ModelData>(File.ReadAllText(Path.Combine(Application.dataPath,"Bar/Core/Data/model-v2.json")));var world=new World(data);
            world.Pour("shaker","spirit",45,20);world.Pour("shaker","lemon",22.5,20);world.Pour("shaker","syrup",22.5,20);world.AddIce("shaker",160);
            var p=BPresentationSettings.Load().surface;var surface=new SurfaceEvolution(p);var rows=new List<string>{"time,wallC,mist,frost,wet"};
            double first=-1,atStop=-1,lockedMist=0,lockedFrost=0,reshaken=-1,afterDump=0;
            for(int i=0;i<900;i++)
            {
                double t=i*.1;bool shaking=t<12||t>=19&&t<25;
                if(i==150)world.Transfer("shaker","glass",1);
                if(i==170)surface.WipeDry();
                if(i==280)world.DiscardIce("shaker");
                if(i==300){Assert.That(world.GetVessel("shaker").VesselTemperature.Value,Is.LessThan(p.metalFrostOnset),"擦干时壶壁仍在 0 °C 以下");surface.WipeDry();}
                world.Advance(.1,"shaker",shaking?"shake":"rest",1);float wall=(float)world.GetVessel("shaker").VesselTemperature.Value;
                float before=surface.Frost;surface.Step(wall,false,true,.1f,shaking?1:0,world.GetVessel("shaker").ice>.5);
                if(wall>=p.metalFrostOnset&&surface.Frost>before+1e-6f)Assert.Fail("frost grew on a wall at "+wall+" °C");
                if(first<0&&surface.Frost>.05f)first=t;if(i==119)atStop=surface.Frost;
                if(i>=175&&i<190){lockedMist=System.Math.Max(lockedMist,surface.Mist);lockedFrost=System.Math.Max(lockedFrost,surface.Frost);}
                if(i==249)reshaken=surface.Frost;if(i>=305)afterDump=System.Math.Max(afterDump,surface.Frost+surface.Mist);
                rows.Add(t.ToString("F1")+","+wall.ToString("F2")+","+surface.Mist.ToString("F3")+","+surface.Frost.ToString("F3")+","+surface.Wet.ToString("F3"));
            }
            Assert.That(first,Is.GreaterThan(4),"摇壶最初几秒壶壁还在 0 °C 以上，不应有霜");Assert.That(atStop,Is.InRange(.3f,.85f),"12 秒摇壶结束时霜在长、还没满");
            Assert.That(lockedFrost,Is.LessThan(.001f),"壶里还有冰，但擦干后不再摇就不长霜");Assert.That(lockedMist,Is.LessThan(.02f),"锁住时只极慢地起一点雾");
            Assert.That(reshaken,Is.GreaterThan(.2f),"重新用力摇壶后霜长回来");Assert.That(afterDump,Is.LessThan(.01f),"倒冰擦干后不再起雾结霜");
            // Bare to full on a wall held at -6 °C: never faster than about 8 s even at full strength; at rest it takes minutes.
            var hard=new SurfaceEvolution(p);int steps=0;while(hard.Frost<.9f&&steps<10000){hard.Step(-6,false,true,.1f,1,true);steps++;}Assert.That(steps*.1,Is.InRange(7.5,12),"全力摇从无到满霜 7.5–12 秒");
            var still=new SurfaceEvolution(p);for(int i=0;i<600;i++)still.Step(-6,false,false,.1f,0,true);Assert.That(still.Frost,Is.LessThan(.8f),"不摇时一分钟也长不满");
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../BReports"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"wall-frost-timeline.csv"),rows);
        }
    }
}
