using System;
using System.Collections.Generic;
using System.Linq;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>按定义创建实例与永久部件，注入批次温度和随机数并防止重复实例标识。</summary>
    public sealed class ItemFactory
    {
        private readonly ItemCatalog catalog;
        private readonly Func<string, double> ingredientTemperature;
        private readonly Func<double> random;
        private readonly HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>创建ItemFactory。</summary>
        /// <precondition>目录、批次温度函数与随机数函数均非空。</precondition>
        /// <effects>保存注入依赖并建立空的会话实例标识集合。</effects>
        public ItemFactory(ItemCatalog catalog, Func<string, double> ingredientTemperature, Func<double> random)
        { this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.ingredientTemperature = ingredientTemperature ?? throw new ArgumentNullException(nameof(ingredientTemperature)); this.random = random ?? throw new ArgumentNullException(nameof(random)); }
        /// <summary>创建定义对应的物品及永久部件。</summary>
        /// <precondition>定义标识存在；实例标识非空且在本工厂尚未使用；部件定义结构正确。</precondition>
        /// <effects>保留实例标识，创建永久部件；异常时撤回本次标识预留，已创建物品不加入外部世界。</effects>
        public Item Create(string definitionId, string instanceId)
        {
            var before = new HashSet<string>(ids);
            try { return CreatePart(catalog[definitionId], instanceId); }
            catch { ids.RemoveWhere(id => !before.Contains(id)); throw; }
        }
        private Item CreatePart(ItemDefinition d, string id)
        {
            Guard.Text(id); if (!ids.Add(id)) throw new ArgumentException("Duplicate instance ID: " + id);
            switch (d.Type)
            {
                case "Glass": return new Glass(id, d, d.Id == "glass.coupe" ? GlassType.Coupe : GlassType.Highball);
                case "ShakerBody": return new ShakerBody(id, d);
                case "JiggerCup": return new JiggerCup(id, d);
                case "Bottle": return new Bottle(id, d, ingredientTemperature(d.IngredientId), d.Parts.Count == 0 ? null : (BottleCap)CreatePart(catalog[d.Parts.Single()], id + "/cap"));
                case "CobblerShaker": return new CobblerShaker(id, d, (ShakerBody)CreatePart(catalog[d.Parts.Single()], id + "/body"));
                case "Jigger": return new Jigger(id, d, (JiggerCup)CreatePart(catalog[d.Parts[0]], id + "/a"), (JiggerCup)CreatePart(catalog[d.Parts[1]], id + "/b"));
                case "ShakerStrainer": return new ShakerStrainer(id, d);
                case "ShakerCap": return new ShakerCap(id, d);
                case "BottleCap": return new BottleCap(id, d);
                case "BarSpoon": return new BarSpoon(id, d);
                case "IceScoop": return new IceScoop(id, d);
                case "IceTongs": return new IceTongs(id, d);
                case "Squeezer": return new Squeezer(id, d);
                case "LemonHalf": return new LemonHalf(id, d, random());
                case "IceBin": return new IceBin(id, d);
                case "Sink": return new Sink(id, d);
                case "ServeMat": return new ServeMat(id, d);
                case "BarCounter": return new BarCounter(id, d);
                case "CuttingBoard": return new CuttingBoard(id, d);
                default: throw new ArgumentException("Unregistered item type: " + d.Type);
            }
        }
    }
}
