using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CraftingResearch.CoreV2;

namespace Bar.Data
{
    /// <summary>加载物品定义并检查核心原料引用、部件结构和循环引用。</summary>
    public sealed class ItemCatalog
    {
        [DataContract] private sealed class Document { [DataMember(Name = "items")] public ItemDefinition[] Items; }
        private readonly Dictionary<string, ItemDefinition> items;
        private ItemCatalog(ItemDefinition[] definitions, IEnumerable<string> ingredients)
        {
            if (definitions == null || definitions.Length == 0 || ingredients == null) throw new ArgumentException("Items and ingredient IDs required.");
            items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            var known = new HashSet<string>(ingredients, StringComparer.Ordinal);
            foreach (var item in definitions)
            {
                if (item == null) throw new ArgumentException("Null item."); item.Validate();
                if (KindFor(item.Type) != item.Kind) throw new ArgumentException("Runtime type/category mismatch: " + item.Id);
                if (items.ContainsKey(item.Id)) throw new ArgumentException("Duplicate item ID: " + item.Id);
                if (!string.IsNullOrEmpty(item.IngredientId) && !known.Contains(item.IngredientId)) throw new ArgumentException("Unknown core ingredient: " + item.IngredientId);
                items.Add(item.Id, item);
            }
            foreach (var item in definitions)
            {
                foreach (string part in item.Parts.Concat(item.FitsOn)) if (!items.ContainsKey(part)) throw new ArgumentException("Unknown part/parent: " + part);
                Visit(item, new HashSet<string>());
                if (item.Type == "CobblerShaker" && (item.Parts.Count != 1 || items[item.Parts[0]].Type != "ShakerBody")) throw new ArgumentException("Shaker requires one body.");
                if (item.Type == "Jigger" && (item.Parts.Count != 2 || item.Parts.Any(id => items[id].Type != "JiggerCup"))) throw new ArgumentException("Jigger requires two cups.");
                if (item.Type == "Bottle" && (item.Parts.Count > 1 || item.Parts.Any(id => items[id].Type != "BottleCap" || !items[id].FitsOn.Contains(item.Id)) || item.Parts.Count == 1 && item.InitiallyOpen)) throw new ArgumentException("Bottle takes at most one closed cap that fits it: " + item.Id);
            }
        }
        /// <summary>按定义标识查询只读物品；未知标识抛异常。</summary>
        public ItemDefinition this[string id] => items[id];
        /// <summary>全部已经完成校验的物品定义。</summary>
        public IEnumerable<ItemDefinition> Items => items.Values;
        /// <summary>从 JSON 加载并校验物品目录。</summary>
        /// <precondition>JSON 非空，原料标识集合或核心模型非空；部件和父物品引用必须存在。</precondition>
        /// <effects>创建独立目录；拒绝重复标识、结构环、类型不匹配和未知原料，不改核心模型。</effects>
        public static ItemCatalog Load(string json, IEnumerable<string> ingredientIds) => new ItemCatalog(Read<Document>(json).Items, ingredientIds);
        /// <summary>从 JSON 加载并校验物品目录。</summary>
        /// <precondition>JSON 非空，原料标识集合或核心模型非空；部件和父物品引用必须存在。</precondition>
        /// <effects>创建独立目录；拒绝重复标识、结构环、类型不匹配和未知原料，不改核心模型。</effects>
        public static ItemCatalog Load(string json, ModelData model) => Load(json, (model ?? throw new ArgumentNullException(nameof(model))).ingredients.Select(x => x.id));
        /// <summary>读取并校验控制参数 JSON。</summary>
        /// <precondition>JSON 非空且全部必需数值满足 Controls.Validate 的范围。</precondition>
        /// <effects>创建不可变 Controls；非法 JSON 或参数抛异常，不改文件。</effects>
        public static Controls LoadControls(string json) { var value = Read<Controls>(json); value.Validate(); return value; }
        private static T Read<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON required.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private void Visit(ItemDefinition item, HashSet<string> path)
        {
            if (!path.Add(item.Id)) throw new ArgumentException("Cyclic parts.");
            foreach (string part in item.Parts) Visit(items[part], path);
            path.Remove(item.Id);
        }
        private static ItemKind KindFor(string type)
        {
            switch (type)
            {
                case "Glass": case "ShakerBody": case "JiggerCup": return ItemKind.Container;
                case "Bottle": return ItemKind.Bottle;
                case "CobblerShaker": case "Jigger": return ItemKind.Composite;
                case "ShakerStrainer": case "ShakerCap": case "BottleCap": return ItemKind.Attachment;
                case "BarSpoon": case "IceScoop": case "IceTongs": case "Squeezer": return ItemKind.Tool;
                case "LemonHalf": return ItemKind.Produce;
                case "BarCounter": case "IceBin": case "Sink": case "ServeMat": case "CuttingBoard": return ItemKind.Fixture;
                default: throw new ArgumentException("Unregistered runtime item type: " + type);
            }
        }
    }
}
